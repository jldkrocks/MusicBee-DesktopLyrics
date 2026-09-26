using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    // A regular, movable and resizable window. The original transparent desktop
    // overlay remains available when CompactWindow is disabled.
    internal sealed class FrmLyricsWindow : Form, IDesktopLyricsView
    {
        private const int BarCount = 48;
        private const float TransitionMs = 320f;
        private readonly Plugin.MusicBeeApiInterface _musicBee;
        private readonly float[] _fft = new float[4096];
        private readonly float[] _bars = new float[BarCount];
        private readonly float[] _levels = new float[BarCount];
        private readonly float[] _targets = new float[BarCount];
        private readonly System.Threading.Timer _animationTimer;
        private SettingsObj _settings;
        private string _line1 = "", _line2, _nextLine;
        private string _previousLine1, _previousLine2, _previousNextLine;
        private long _transitionStarted;
        private float _gain = 6f;
        private int _framePending;
        private long _lastFrameTimestamp;
        private long _lastSpectrumSample;
        private bool _useArtworkColors;
        private string _artworkTrackUrl;
        private int _artworkRequestId;
        private ArtworkPalette _palette = ArtworkPalette.Default;
        private ArtworkPalette _paletteFrom, _paletteTo;
        private long _paletteStarted;
        private bool _loaded;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        public Form Form => this;

        public FrmLyricsWindow(SettingsObj settings, Plugin.MusicBeeApiInterface musicBee)
        {
            _settings = settings;
            _musicBee = musicBee;
            _useArtworkColors = settings.UseArtworkColors;
            Text = "Desktop Lyrics";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            BackColor = Color.FromArgb(13, 18, 32);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(420, 190);
            Size = new Size(Math.Max(420, settings.WindowWidth), Math.Max(190, settings.WindowHeight));
            TopMost = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            var workArea = Screen.PrimaryScreen.WorkingArea;
            Location = settings.WindowPosX < 0 || settings.WindowPosY < 0
                ? new Point(workArea.Left + (workArea.Width - Width) / 2,
                            workArea.Bottom - Height - 70)
                : new Point(settings.WindowPosX, settings.WindowPosY);
            if (!IsVisibleOnAnyScreen())
                Location = new Point(workArea.Left + (workArea.Width - Width) / 2,
                                     workArea.Bottom - Height - 70);

            _animationTimer = new System.Threading.Timer(AnimationClockTick, null,
                Timeout.Infinite, Timeout.Infinite);
            Shown += (sender, args) =>
            {
                _loaded = true;
                StartAnimation();
                RefreshArtwork(true);
            };
            VisibleChanged += (sender, args) =>
            {
                if (Visible && _loaded) StartAnimation();
                else _animationTimer.Change(Timeout.Infinite, Timeout.Infinite);
            };
            LocationChanged += (sender, args) => SaveBounds();
            SizeChanged += (sender, args) => SaveBounds();
        }

        private bool IsVisibleOnAnyScreen()
        {
            foreach (var screen in Screen.AllScreens)
            {
                var intersection = Rectangle.Intersect(Bounds, screen.WorkingArea);
                if (intersection.Width >= 80 && intersection.Height >= 40) return true;
            }
            return false;
        }

        private void SaveBounds()
        {
            if (!_loaded || WindowState != FormWindowState.Normal) return;
            _settings.WindowPosX = Left;
            _settings.WindowPosY = Top;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }

        private void StartAnimation()
        {
            _lastFrameTimestamp = 0;
            _lastSpectrumSample = 0;
            // An 8 ms target gives the UI up to 120 frames per second. Slow
            // paints drop frames instead of building up a queue of old frames.
            _animationTimer.Change(0, 8);
        }

        private void AnimationClockTick(object state)
        {
            if (Interlocked.CompareExchange(ref _framePending, 1, 0) != 0) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (IsDisposed || !Visible) return;
                        var now = Stopwatch.GetTimestamp();
                        var elapsedMs = _lastFrameTimestamp == 0 ? 8.0 : Math.Min(50.0,
                            (now - _lastFrameTimestamp) * 1000.0 / Stopwatch.Frequency);
                        _lastFrameTimestamp = now;
                        if (_lastSpectrumSample == 0 ||
                            (now - _lastSpectrumSample) * 1000.0 / Stopwatch.Frequency >= 30)
                        {
                            SampleSpectrum();
                            _lastSpectrumSample = now;
                        }
                        StepSpectrum(elapsedMs);
                        AdvancePalette();
                        Invalidate();
                        Update();
                    }
                    finally { Interlocked.Exchange(ref _framePending, 0); }
                }));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref _framePending, 0); }
        }

        public void UpdateFromSettings(SettingsObj settings)
        {
            _settings = settings;
            if (_useArtworkColors != settings.UseArtworkColors)
            {
                _useArtworkColors = settings.UseArtworkColors;
                if (_useArtworkColors) RefreshArtwork(true);
                else
                {
                    Interlocked.Increment(ref _artworkRequestId);
                    _artworkTrackUrl = null;
                    SetPalette(ArtworkPalette.Default);
                }
            }
            Invalidate();
        }

        public void UpdateLyrics(string line1, string line2, string nextLine)
        {
            RefreshArtwork(false);
            line1 = line1 ?? "";
            if (_line1 == line1 && _line2 == line2 && _nextLine == nextLine) return;
            _previousLine1 = _line1;
            _previousLine2 = _line2;
            _previousNextLine = _nextLine;
            _line1 = line1 ?? "";
            _line2 = line2;
            _nextLine = nextLine;
            _transitionStarted = Stopwatch.GetTimestamp();
            Invalidate();
        }

        public void Clear()
        {
            UpdateLyrics("", null, null);
        }

        public void RefreshArtwork(bool force)
        {
            if (!_useArtworkColors || IsDisposed || !IsHandleCreated) return;
            string trackUrl;
            try { trackUrl = _musicBee.NowPlaying_GetFileUrl(); }
            catch (Exception) { return; }
            if (!force && trackUrl == _artworkTrackUrl) return;
            _artworkTrackUrl = trackUrl;
            var request = Interlocked.Increment(ref _artworkRequestId);

            // MusicBee's artwork callbacks may use its UI context. Fetch the
            // source there, then decode and sample the image off the UI thread.
            byte[] imageData = null;
            string artworkUrl = null;
            string artwork = null;
            try
            {
                Plugin.PictureLocations locations;
                string pictureUrl;
                byte[] bytes;
                if (!string.IsNullOrEmpty(trackUrl) && _musicBee.Library_GetArtworkEx != null &&
                    _musicBee.Library_GetArtworkEx(trackUrl, 0, true,
                        out locations, out pictureUrl, out bytes))
                {
                    imageData = bytes;
                    artworkUrl = pictureUrl;
                }
            }
            catch (Exception) { /* Some tracks do not belong to the library. */ }
            if (imageData == null || imageData.Length == 0)
            {
                try
                {
                    if (_musicBee.NowPlaying_GetArtworkUrl != null)
                    {
                        var url = _musicBee.NowPlaying_GetArtworkUrl();
                        if (!string.IsNullOrWhiteSpace(url)) artworkUrl = url;
                    }
                }
                catch (Exception) { }
                try
                {
                    if (_musicBee.NowPlaying_GetArtwork != null)
                        artwork = _musicBee.NowPlaying_GetArtwork();
                }
                catch (Exception) { }
            }
            ThreadPool.QueueUserWorkItem(state =>
            {
                ArtworkPalette palette;
                if (!ArtworkPalette.TryLoad(imageData, out palette) &&
                    !ArtworkPalette.TryLoad(artworkUrl, out palette) &&
                    !ArtworkPalette.TryLoad(artwork, out palette))
                    palette = ArtworkPalette.Default;
                if (IsDisposed || !IsHandleCreated) return;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (request == _artworkRequestId && _useArtworkColors)
                            SetPalette(palette);
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private void SetPalette(ArtworkPalette target)
        {
            _paletteFrom = _palette;
            _paletteTo = target;
            _paletteStarted = Stopwatch.GetTimestamp();
        }

        private void AdvancePalette()
        {
            if (_paletteStarted == 0) return;
            var progress = Math.Min(1f, (float)((Stopwatch.GetTimestamp() - _paletteStarted) *
                1000.0 / Stopwatch.Frequency / 550.0));
            _palette = ArtworkPalette.Blend(_paletteFrom, _paletteTo, progress);
            if (progress >= 1f) _paletteStarted = 0;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Match the native title bar to the dark lyrics surface while
            // retaining Windows' normal drag, close and resize controls.
            try
            {
                var dark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private void SampleSpectrum()
        {
            var count = 0;
            try
            {
                if (_musicBee.Player_GetPlayState() == Plugin.PlayState.Playing &&
                    _musicBee.NowPlaying_GetSpectrumData != null)
                    count = _musicBee.NowPlaying_GetSpectrumData(_fft);
            }
            catch (Exception)
            {
                // A missing audio stream should leave the lyrics window usable.
            }

            var validCount = Math.Min(Math.Max(0, count), _fft.Length);
            var upperBin = Math.Min(validCount / 2, 1024);
            var total = 0f;
            for (var bar = 0; bar < BarCount; bar++)
            {
                var level = 0f;
                if (upperBin > 2)
                {
                    var start = Math.Max(2, (int)(2 * Math.Pow(upperBin / 2.0, (double)bar / BarCount)));
                    var end = Math.Max(start + 1, (int)(2 * Math.Pow(upperBin / 2.0, (double)(bar + 1) / BarCount)));
                    for (var bin = start; bin < Math.Min(end, upperBin); bin++)
                    {
                        // MusicBee has returned both normal and mirrored / centred
                        // spectrum layouts. Fold the halves so either orientation
                        // produces a balanced low-to-high display.
                        level = Math.Max(level, SafeMagnitude(_fft[bin]));
                        level = Math.Max(level, SafeMagnitude(_fft[validCount - 1 - bin]));
                        level = Math.Max(level, SafeMagnitude(_fft[validCount / 2 + bin]));
                        level = Math.Max(level, SafeMagnitude(_fft[validCount / 2 - 1 - bin]));
                    }
                }
                _levels[bar] = level;
                total += level;
            }

            var targetGain = Math.Max(1.2f, Math.Min(28f, 0.18f / (total / BarCount + 0.005f)));
            _gain += (targetGain - _gain) * 0.12f;
            for (var bar = 0; bar < BarCount; bar++)
            {
                var level = _levels[bar] * 0.7f;
                if (bar > 0) level += _levels[bar - 1] * 0.15f;
                if (bar + 1 < BarCount) level += _levels[bar + 1] * 0.15f;
                _targets[bar] = (float)Math.Min(1.0, Math.Sqrt(level * _gain) * 0.92);
            }
        }

        private void StepSpectrum(double elapsedMs)
        {
            for (var bar = 0; bar < BarCount; bar++)
            {
                var speed = _targets[bar] > _bars[bar] ? 0.42f : 0.10f;
                var fraction = 1 - Math.Pow(1 - speed, elapsedMs / 16.0);
                _bars[bar] += (float)((_targets[bar] - _bars[bar]) * fraction);
            }
        }

        private static float SafeMagnitude(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Abs(value);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            using (var background = new LinearGradientBrush(bounds,
                       _palette.Left, _palette.Right,
                       LinearGradientMode.Horizontal))
                g.FillRectangle(background, bounds);

            DrawSpectrum(g, bounds);

            var progress = 1f;
            if (_transitionStarted != 0)
            {
                progress = Math.Min(1f, (float)((Stopwatch.GetTimestamp() - _transitionStarted) *
                    1000.0 / Stopwatch.Frequency / TransitionMs));
                if (progress >= 1f)
                {
                    _transitionStarted = 0;
                    _previousLine1 = _previousLine2 = _previousNextLine = null;
                }
            }
            if (string.IsNullOrWhiteSpace(_line1) &&
                string.IsNullOrWhiteSpace(_line2) &&
                string.IsNullOrWhiteSpace(_nextLine) &&
                string.IsNullOrWhiteSpace(_previousLine1) &&
                string.IsNullOrWhiteSpace(_previousLine2) &&
                string.IsNullOrWhiteSpace(_previousNextLine)) return;

            var scale = (float)Math.Max(0.75, Math.Min(2.6,
                Math.Sqrt((double)bounds.Width / 760 * (bounds.Height + 28.0) / 230)));
            var content = new RectangleF(22, 20, Math.Max(1, bounds.Width - 44),
                                         Math.Max(1, bounds.Height - 33));
            var gap = 6f * scale;
            var mainHeight = 58f * scale;
            var subHeight = 38f * scale;
            var subCount = Math.Max(CountSubLines(_line2, _nextLine),
                                    CountSubLines(_previousLine2, _previousNextLine));
            var groupHeight = mainHeight + subCount * (subHeight + gap);
            if (groupHeight > content.Height)
            {
                var fit = content.Height / groupHeight;
                mainHeight *= fit;
                subHeight *= fit;
                gap *= fit;
                groupHeight = content.Height;
            }
            var y = content.Top + (content.Height - groupHeight) / 2;

            using (var panelPath = RoundedRectangle(new Rectangle(13, (int)(y - 9),
                       bounds.Width - 26, (int)(groupHeight + 18)), 14))
            using (var shade = new SolidBrush(Color.FromArgb(128, 10, 13, 27)))
            using (var outline = new Pen(Color.FromArgb(44, _palette.Border)))
            {
                g.FillPath(shade, panelPath);
                g.DrawPath(outline, panelPath);
            }

            var eased = progress * progress * (3 - 2 * progress);
            var promotePreview = progress < 1f && !string.IsNullOrEmpty(_previousNextLine) &&
                                 _previousNextLine == _line1;
            if (progress < 1f)
                DrawLyricGroup(g, content, mainHeight, subHeight, gap, scale,
                    _previousLine1, _previousLine2, _previousNextLine,
                    1 - eased, -18f * scale * eased, true, !promotePreview);
            DrawLyricGroup(g, content, mainHeight, subHeight, gap, scale,
                _line1, _line2, _nextLine, eased, 18f * scale * (1 - eased),
                !promotePreview, true);
            if (promotePreview)
            {
                var oldCount = CountSubLines(_previousLine2, _previousNextLine);
                var newCount = CountSubLines(_line2, _nextLine);
                var oldTop = content.Top + (content.Height -
                    (mainHeight + oldCount * (subHeight + gap))) / 2;
                var newTop = content.Top + (content.Height -
                    (mainHeight + newCount * (subHeight + gap))) / 2;
                var oldPreviewY = oldTop + mainHeight + gap +
                    (string.IsNullOrWhiteSpace(_previousLine2) ? 0 : subHeight + gap);
                var start = new RectangleF(content.Left, oldPreviewY, content.Width, subHeight);
                var end = new RectangleF(content.Left, newTop, content.Width, mainHeight);
                var traveling = new RectangleF(content.Left,
                    start.Top + (end.Top - start.Top) * eased, content.Width,
                    start.Height + (end.Height - start.Height) * eased);
                var fontSize = (_settings.Font ?? SystemFonts.DefaultFont).SizeInPoints * scale;
                DrawLine(g, _line1, traveling, fontSize * (0.63f + 0.37f * eased),
                    (int)(145 + 110 * eased));
            }

        }

        private static int CountSubLines(string translation, string preview)
        {
            return (string.IsNullOrWhiteSpace(translation) ? 0 : 1) +
                   (string.IsNullOrWhiteSpace(preview) ? 0 : 1);
        }

        private void DrawLyricGroup(Graphics g, RectangleF content, float mainHeight,
            float subHeight, float gap, float scale, string line1, string line2,
            string nextLine, float opacity, float offsetY, bool drawMain, bool drawPreview)
        {
            if (opacity <= 0) return;
            var subCount = CountSubLines(line2, nextLine);
            var groupHeight = mainHeight + subCount * (subHeight + gap);
            var y = content.Top + (content.Height - groupHeight) / 2 + offsetY;
            var fontSize = (_settings.Font ?? SystemFonts.DefaultFont).SizeInPoints * scale;

            if (drawMain)
                DrawLine(g, line1, new RectangleF(content.Left, y, content.Width, mainHeight),
                    fontSize, (int)(255 * opacity));
            y += mainHeight + gap;
            if (!string.IsNullOrWhiteSpace(line2))
            {
                DrawLine(g, line2, new RectangleF(content.Left, y, content.Width, subHeight),
                    fontSize * 0.68f, (int)(225 * opacity));
                y += subHeight + gap;
            }
            if (drawPreview && !string.IsNullOrWhiteSpace(nextLine))
                DrawLine(g, nextLine, new RectangleF(content.Left, y, content.Width, subHeight),
                    fontSize * 0.63f, (int)(145 * opacity));
        }

        private void DrawSpectrum(Graphics g, Rectangle area)
        {
            var usableWidth = area.Width - 46;
            var barSpacing = (float)usableWidth / BarCount;
            var barWidth = Math.Max(2, barSpacing - 3);
            var floor = area.Bottom - 10;
            var maxHeight = Math.Max(1, area.Height - 28);
            using (var brush = new LinearGradientBrush(
                       new Point(0, 16), new Point(0, floor),
                       Color.FromArgb(124, _palette.BarTop),
                       Color.FromArgb(165, _palette.BarBottom)))
            {
                for (var i = 0; i < BarCount; i++)
                {
                    var height = Math.Max(2f, _bars[i] * maxHeight);
                    g.FillRectangle(brush, 23 + i * barSpacing, floor - height, barWidth, height);
                }
            }
        }

        private void DrawLine(Graphics g, string lyric, RectangleF area, float desiredPoints, int alpha)
        {
            if (string.IsNullOrEmpty(lyric) || alpha <= 0) return;
            var selected = _settings.Font ?? SystemFonts.DefaultFont;
            var size = Math.Max(10f, Math.Min(desiredPoints, area.Height * 0.74f));
            using (var format = new StringFormat(StringFormatFlags.NoWrap)
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center,
                       Trimming = StringTrimming.EllipsisCharacter
                   })
            {
                Font fitted = new Font(selected.FontFamily, size, selected.Style, GraphicsUnit.Point);
                var measuredWidth = g.MeasureString(lyric, fitted).Width;
                if (measuredWidth > area.Width - 8)
                {
                    size = Math.Max(10f, size * (area.Width - 8) / measuredWidth);
                    fitted.Dispose();
                    fitted = new Font(selected.FontFamily, size, selected.Style, GraphicsUnit.Point);
                }
                using (fitted)
                using (var shadow = new SolidBrush(Color.FromArgb(alpha * 2 / 3, 0, 0, 0)))
                using (var foreground = CreateTextBrush(area, alpha))
                {
                    var shadowArea = area;
                    shadowArea.Offset(1, 2);
                    g.DrawString(lyric, fitted, shadow, shadowArea, format);
                    g.DrawString(lyric, fitted, foreground, area, format);
                }
            }
        }

        private Brush CreateTextBrush(RectangleF area, int alpha)
        {
            var first = Color.FromArgb(alpha, _settings.Color1);
            var second = Color.FromArgb(alpha, _settings.Color2);
            if (_settings.GradientType == (int)GradientType.NoGradient || area.Height < 2)
                return new SolidBrush(first);
            var triple = _settings.GradientType == (int)GradientType.TripleColor;
            var gradientArea = triple
                ? new RectangleF(area.X, area.Y, area.Width, area.Height / 2f)
                : area;
            return new LinearGradientBrush(gradientArea, first, second, LinearGradientMode.Vertical)
            {
                WrapMode = triple ? WrapMode.TileFlipY : WrapMode.Tile
            };
        }

        private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animationTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
