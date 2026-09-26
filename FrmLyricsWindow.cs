using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    // A regular, movable and resizable window. The original transparent desktop
    // overlay remains available when CompactWindow is disabled.
    internal sealed class FrmLyricsWindow : Form, IDesktopLyricsView
    {
        private const int BarCount = 48;
        private readonly Plugin.MusicBeeApiInterface _musicBee;
        private readonly float[] _fft = new float[4096];
        private readonly float[] _bars = new float[BarCount];
        private readonly Timer _animationTimer;
        private SettingsObj _settings;
        private string _line1 = "", _line2, _nextLine;
        private bool _loaded;

        public Form Form => this;

        public FrmLyricsWindow(SettingsObj settings, Plugin.MusicBeeApiInterface musicBee)
        {
            _settings = settings;
            _musicBee = musicBee;
            Text = "Desktop Lyrics";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
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

            _animationTimer = new Timer { Interval = 40 };
            _animationTimer.Tick += (sender, args) =>
            {
                UpdateSpectrum();
                Invalidate();
            };
            Shown += (sender, args) => { _loaded = true; _animationTimer.Start(); };
            VisibleChanged += (sender, args) =>
            {
                if (Visible) _animationTimer.Start();
                else _animationTimer.Stop();
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

        public void UpdateFromSettings(SettingsObj settings)
        {
            _settings = settings;
            Invalidate();
        }

        public void UpdateLyrics(string line1, string line2, string nextLine)
        {
            _line1 = line1 ?? "";
            _line2 = line2;
            _nextLine = nextLine;
            Invalidate();
        }

        public void Clear()
        {
            UpdateLyrics("", null, null);
        }

        private void UpdateSpectrum()
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

            var upperBin = Math.Min(Math.Min(Math.Max(0, count), _fft.Length) / 2, 1024);
            for (var bar = 0; bar < BarCount; bar++)
            {
                var level = 0f;
                if (upperBin > 2)
                {
                    // Logarithmic bands give bass and vocals room without losing
                    // the upper frequencies in a compact display.
                    var start = Math.Max(2, (int)Math.Pow(upperBin, (double)bar / BarCount));
                    var end = Math.Max(start + 1, (int)Math.Pow(upperBin, (double)(bar + 1) / BarCount));
                    for (var bin = start; bin < Math.Min(end, upperBin); bin++)
                    {
                        var sample = Math.Abs(_fft[bin]);
                        if (!float.IsNaN(sample) && !float.IsInfinity(sample))
                            level = Math.Max(level, sample);
                    }
                }

                var target = (float)Math.Min(1.0, Math.Sqrt(level) * 1.5);
                var speed = target > _bars[bar] ? 0.56f : 0.14f;
                _bars[bar] += (target - _bars[bar]) * speed;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;

            using (var background = new LinearGradientBrush(bounds,
                       Color.FromArgb(13, 18, 32), Color.FromArgb(31, 21, 51),
                       LinearGradientMode.Horizontal))
                g.FillRectangle(background, bounds);

            DrawSpectrum(g, bounds);

            var hasTranslation = !string.IsNullOrWhiteSpace(_line2);
            var hasPreview = !string.IsNullOrWhiteSpace(_nextLine);
            var subCount = (hasTranslation ? 1 : 0) + (hasPreview ? 1 : 0);
            var content = new Rectangle(22, 46, Math.Max(1, bounds.Width - 44),
                                        Math.Max(1, bounds.Height - 57));
            var gap = 5;
            var mainHeight = Math.Min(50, Math.Max(34, content.Height - subCount * 32 - subCount * gap));
            var subHeight = Math.Min(32, Math.Max(22, (content.Height - mainHeight - subCount * gap) / Math.Max(1, subCount)));
            var groupHeight = mainHeight + subCount * (subHeight + gap);
            var y = content.Top + (content.Height - groupHeight) / 2;

            using (var panelPath = RoundedRectangle(new Rectangle(13, y - 8,
                       bounds.Width - 26, groupHeight + 16), 14))
            using (var shade = new SolidBrush(Color.FromArgb(128, 10, 13, 27)))
            using (var outline = new Pen(Color.FromArgb(44, 180, 180, 235)))
            {
                g.FillPath(shade, panelPath);
                g.DrawPath(outline, panelPath);
            }

            DrawLine(g, _line1, new RectangleF(content.Left, y, content.Width, mainHeight), 30, 255);
            y += mainHeight + gap;
            if (hasTranslation)
            {
                DrawLine(g, _line2, new RectangleF(content.Left, y, content.Width, subHeight), 20, 225);
                y += subHeight + gap;
            }
            if (hasPreview)
                DrawLine(g, _nextLine, new RectangleF(content.Left, y, content.Width, subHeight), 19, 145);

            using (var labelFont = new Font(FontFamily.GenericSansSerif, 8f, FontStyle.Bold))
            using (var labelBrush = new SolidBrush(Color.FromArgb(172, 202, 210, 235)))
            using (var border = new Pen(Color.FromArgb(55, 116, 191, 237)))
            using (var dot = new SolidBrush(Color.FromArgb(124, 219, 245)))
            {
                g.FillEllipse(dot, 22, 20, 7, 7);
                g.DrawString("MUSICBEE  /  DESKTOP LYRICS", labelFont, labelBrush, 37, 15);
                var rightLabel = "LIVE SPECTRUM";
                var rightWidth = g.MeasureString(rightLabel, labelFont).Width;
                if (bounds.Width > 500)
                    g.DrawString(rightLabel, labelFont, labelBrush, bounds.Width - rightWidth - 22, 15);
                g.DrawLine(border, 20, 40, bounds.Width - 20, 40);
            }
        }

        private void DrawSpectrum(Graphics g, Rectangle area)
        {
            var usableWidth = area.Width - 46;
            var barSpacing = (float)usableWidth / BarCount;
            var barWidth = Math.Max(2, barSpacing - 3);
            var floor = area.Bottom - 10;
            var maxHeight = Math.Max(1, area.Height - 63);
            using (var brush = new LinearGradientBrush(
                       new Point(0, 45), new Point(0, floor),
                       Color.FromArgb(84, 135, 110, 242),
                       Color.FromArgb(110, 57, 193, 221)))
            {
                for (var i = 0; i < BarCount; i++)
                {
                    var height = Math.Max(2f, _bars[i] * maxHeight);
                    g.FillRectangle(brush, 23 + i * barSpacing, floor - height, barWidth, height);
                }
            }
        }

        private void DrawLine(Graphics g, string lyric, RectangleF area, float maxPoints, int alpha)
        {
            if (string.IsNullOrEmpty(lyric)) return;
            var selected = _settings.Font ?? SystemFonts.DefaultFont;
            var size = Math.Min(selected.SizeInPoints, maxPoints);
            size = Math.Min(size, area.Height * 0.72f);
            using (var format = new StringFormat(StringFormatFlags.NoWrap)
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center,
                       Trimming = StringTrimming.EllipsisCharacter
                   })
            {
                Font fitted;
                while (true)
                {
                    fitted = new Font(selected.FontFamily, Math.Max(10, size), selected.Style, GraphicsUnit.Point);
                    if (size <= 10 || g.MeasureString(lyric, fitted).Width <= area.Width - 8) break;
                    fitted.Dispose();
                    size -= 1;
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
