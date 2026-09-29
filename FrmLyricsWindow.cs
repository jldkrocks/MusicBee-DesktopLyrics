using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    // A movable and resizable window with optional transparent canvas.
    internal sealed class FrmLyricsWindow : Form, IDesktopLyricsView
    {
        private const int BarCount = 48;
        private const float TransitionMs = 300f;
        private const float QueueNoticeMs = 3200f;
        private readonly Plugin.MusicBeeApiInterface _musicBee;
        private readonly PlaybackHistory _history;
        private readonly Action<SettingsObj> _settingsChanged;
        private readonly Action _openSettings;
        private readonly Action<string, string> _previewTiming, _savedTiming;
        private readonly Action<string> _cancelTiming;
        private readonly EnglishTranslationStore _englishStore;
        private readonly PartyTempoStore _partyTempoStore;
        private readonly Action<string> _englishSaved;
        private readonly ContextMenuStrip _flyoutMenu;
        private readonly float[] _fft = new float[4096];
        private readonly float[] _bars = new float[BarCount];
        private readonly float[] _levels = new float[BarCount];
        private readonly float[] _targets = new float[BarCount];
        private readonly System.Windows.Forms.Timer _animationTimer;
        private SettingsObj _settings;
        private string _line1 = "", _line2, _nextLine;
        private string _previousLine1, _previousLine2, _previousNextLine;
        private long _transitionStarted;
        private float _gain = 6f;
        private long _lastFrameTimestamp;
        private long _lastPaintRequest;
        private long _lastSpectrumSample;
        private long _lastQueueCheck;
        private volatile bool _movingOrResizing;
        private bool _useArtworkColors;
        private string _artworkTrackUrl;
        private int _artworkRequestId;
        private ArtworkPalette _palette = ArtworkPalette.Default;
        private ArtworkPalette _paletteFrom, _paletteTo;
        private long _paletteStarted;
        private bool _loaded;
        private bool _animationDisposed;
        private Bitmap _albumArtwork;
        private Bitmap _backgroundCache;
        private ArtworkPalette _cachedBackgroundPalette;
        private string _songTitle = "", _songArtist = "";
        private Plugin.PlayState _playState = Plugin.PlayState.Undefined;
        private long _lastPlayStateCheck;
        private Rectangle _previousButton, _playButton, _nextButton, _menuButton,
            _timingButton, _lrcButton, _backgroundButton, _partyButton, _resizeGrip;
        private PartyDancerWindow _leftDancer, _rightDancer;
        private readonly PartyBeatTracker _partyBeat = new PartyBeatTracker();
        private readonly PartyPlaybackClock _partyClock = new PartyPlaybackClock();
        private double _partyBpm, _partyTagBpm;
        private int _partyOriginMs;
        // A dialog preview overrides drawing only; saved/live tempo stays intact.
        private string _partyPreviewTrackUrl;
        private double _partyPreviewBpm;
        private int _partyPreviewOriginMs;
        private PartyTempoSource _partyTempoSource;
        private string _partyApiKey, _songAlbum = "", _lastOnlineAttemptTrack,
            _partyLookupError, _partyLookupDetail;
        private CancellationTokenSource _partyLookupCancellation;
        private PartyOnlineStatus _partyOnlineStatus;
        private int _partySpectrumMisses;
        private long _lastPartyUpdate;
        private Rectangle _partyLayoutWindow, _leftPartyBounds, _rightPartyBounds;
        private Size _partyLayoutClientSize;
        private FormWindowState _partyLayoutState;
        private bool _partyLayoutValid;
        private string _hoverButton;
        private long _queueNoticeStarted;
        private string _queueNoticeText = "That's the end of the queue ♪";
        private bool _queueEnded;
        private FrmTimingEditor _timingEditor;
        private FrmTimingCreator _timingCreator;
        private FrmLrcLibPicker _lrcPicker;
        private FrmEnglishImport _englishImporter;
        private List<UpcomingQueue.Track> _queueTracks = new List<UpcomingQueue.Track>();
        private readonly List<QueueHit> _queueHits = new List<QueueHit>();
        private Rectangle _queueCard, _queueTab, _queueUpButton, _queueDownButton;
        private int _queueScroll, _queueVisibleCount;
        private bool _queueExpanded;
        private bool _queueExhausted;
        private int _lastFutureOffset;
        private string _hoverQueue;
        private readonly List<TextGeometry> _textGeometries = new List<TextGeometry>();
        private static readonly Color ClearKey = Color.Fuchsia;

        private enum PartyTempoSource { None, Tag, Saved, Manual, Online }
        private enum PartyOnlineStatus { None, Searching, NoMatch, Error }

        private sealed class QueueHit
        {
            public Rectangle Area;
            public UpcomingQueue.Track Track;
        }

        private sealed class TextGeometry : IDisposable
        {
            public string Text, FontFamily;
            public FontStyle FontStyle;
            public float DesiredPoints, Width, Height, DpiY, FittedPoints;
            public GraphicsPath Path;
            public RectangleF Bounds;

            public void Dispose() { Path?.Dispose(); }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message,
            IntPtr wParam, IntPtr lParam);

        public Form Form => this;
        public bool IsAtEndOfQueue => _queueEnded;
        public bool HasQueueNotice => _queueNoticeStarted != 0 &&
            (Stopwatch.GetTimestamp() - _queueNoticeStarted) * 1000.0 /
                Stopwatch.Frequency < QueueNoticeMs;

        public FrmLyricsWindow(SettingsObj settings, Plugin.MusicBeeApiInterface musicBee,
            PlaybackHistory history,
            Action<SettingsObj> settingsChanged, Action openSettings,
            Action<string, string> previewTiming, Action<string> cancelTiming,
            Action<string, string> savedTiming, EnglishTranslationStore englishStore,
            Action<string> englishSaved, PartyTempoStore partyTempoStore)
        {
            _settings = settings;
            _musicBee = musicBee;
            _history = history;
            _settingsChanged = settingsChanged;
            _openSettings = openSettings;
            _previewTiming = previewTiming;
            _cancelTiming = cancelTiming;
            _savedTiming = savedTiming;
            _englishStore = englishStore;
            _partyTempoStore = partyTempoStore;
            _partyApiKey = _partyTempoStore.LoadApiKey();
            _englishSaved = englishSaved;
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
            _flyoutMenu = CreateFlyoutMenu();
            ContextMenuStrip = _flyoutMenu;

            var workArea = Screen.PrimaryScreen.WorkingArea;
            Location = settings.WindowPosX < 0 || settings.WindowPosY < 0
                ? new Point(workArea.Left + (workArea.Width - Width) / 2,
                            workArea.Bottom - Height - 70)
                : new Point(settings.WindowPosX, settings.WindowPosY);
            if (!IsVisibleOnAnyScreen())
                Location = new Point(workArea.Left + (workArea.Width - Width) / 2,
                                     workArea.Bottom - Height - 70);

            _animationTimer = new System.Windows.Forms.Timer { Interval = 8 };
            _animationTimer.Tick += AnimationClockTick;
            ApplyTransparency();
            Shown += (sender, args) =>
            {
                _loaded = true;
                StartAnimation();
                RefreshArtwork(true);
                RefreshQueue();
                UpdatePartyDancers();
            };
            VisibleChanged += (sender, args) =>
            {
                if (_animationDisposed) return;
                if (Visible && _loaded) StartAnimation();
                else _animationTimer.Stop();
                UpdatePartyDancers();
            };
            LocationChanged += (sender, args) => { SaveBounds(); UpdatePartyDancers(); };
            SizeChanged += (sender, args) => { SaveBounds(); UpdatePartyDancers(); };
            ResizeBegin += (sender, args) =>
            {
                _movingOrResizing = true;
            };
            ResizeEnd += (sender, args) =>
            {
                _movingOrResizing = false;
                Invalidate();
            };
            Resize += (sender, args) =>
            {
                // Size changes can outpace posted timer callbacks in the native
                // sizing loop; paint the new complete client area immediately.
                if (_loaded && _movingOrResizing && WindowState == FormWindowState.Normal)
                {
                    Invalidate();
                    Update();
                }
            };
        }

        private void ApplyTransparency()
        {
            // A borderless colour-key canvas keeps only the drawn UI visible.
            // The visible cards must be fully opaque so their edges do not
            // blend with the key colour and acquire purple fringes.
            var clientSize = ClientSize;
            FormBorderStyle = _settings.TransparentCanvas ? FormBorderStyle.None :
                FormBorderStyle.SizableToolWindow;
            ClientSize = clientSize;
            TransparencyKey = _settings.TransparentCanvas ? ClearKey : Color.Empty;
            BackColor = _settings.TransparentCanvas ? ClearKey : Color.FromArgb(13, 18, 32);
            _backgroundCache?.Dispose();
            _backgroundCache = null;
            Invalidate();
            if (_loaded && Visible) StartAnimation();
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
            _lastPaintRequest = 0;
            _lastSpectrumSample = 0;
            // An 8 ms target gives the UI up to 120 frames per second. Slow
            // paints drop frames instead of building up a queue of old frames.
            _animationTimer.Start();
        }

        private void AnimationClockTick(object sender, EventArgs args)
        {
            if (IsDisposed || !Visible) return;
            var now = Stopwatch.GetTimestamp();
            var elapsedMs = _lastFrameTimestamp == 0 ? 8.0 : Math.Min(50.0,
                (now - _lastFrameTimestamp) * 1000.0 / Stopwatch.Frequency);
            _lastFrameTimestamp = now;
            if (_lastSpectrumSample == 0 ||
                (now - _lastSpectrumSample) * 1000.0 / Stopwatch.Frequency >= 30)
            {
                if ((_settings.ShowVisualizer && !_settings.TransparentCanvas) ||
                    (_settings.PartyMode && _partyBpm == 0 &&
                     _partyBeat.Bpm == 0 && !PartyOnlineEnabled))
                    SampleSpectrum();
                _lastSpectrumSample = now;
            }
            if (_lastPlayStateCheck == 0 ||
                (now - _lastPlayStateCheck) * 1000.0 / Stopwatch.Frequency >= 100)
            {
                RefreshPlayState();
                _lastPlayStateCheck = now;
            }
            if (_settings.PartyMode && (_lastPartyUpdate == 0 ||
                (now - _lastPartyUpdate) * 1000.0 / Stopwatch.Frequency >= 15))
            {
                UpdatePartyDancers();
                _lastPartyUpdate = now;
            }
            if (_settings.ShowSongQueue &&
                (_lastQueueCheck == 0 ||
                 (now - _lastQueueCheck) * 1000.0 / Stopwatch.Frequency >= 12000))
                RefreshQueue();
            StepSpectrum(elapsedMs);
            AdvancePalette();
            var barsMoving = false;
            if (_settings.ShowVisualizer && !_settings.TransparentCanvas)
                for (var i = 0; i < BarCount; i++)
                    if (_bars[i] > 0.003f || _targets[i] > 0.003f)
                    { barsMoving = true; break; }
            // Keep the fade to zero at full speed, even after pausing.
            if (!_movingOrResizing && _playState != Plugin.PlayState.Playing &&
                !barsMoving && _transitionStarted == 0 && _paletteStarted == 0 &&
                !HasQueueNotice && _lastPaintRequest != 0 &&
                (now - _lastPaintRequest) * 1000.0 / Stopwatch.Frequency < 40)
                return;
            _lastPaintRequest = now;
            Invalidate();
            if (_movingOrResizing) Update();
        }

        public void UpdateFromSettings(SettingsObj settings)
        {
            var transparentChanged = _settings.TransparentCanvas != settings.TransparentCanvas ||
                TransparencyKey != (settings.TransparentCanvas ? ClearKey : Color.Empty);
            _settings = settings;
            ClearTextGeometries();
            if (transparentChanged) ApplyTransparency();
            UpdatePartyDancers();
            if (_useArtworkColors != settings.UseArtworkColors)
            {
                _useArtworkColors = settings.UseArtworkColors;
                if (!_useArtworkColors) SetPalette(ArtworkPalette.Default);
                RefreshArtwork(true);
            }
            Invalidate();
        }

        private ContextMenuStrip CreateFlyoutMenu()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(27, 29, 41),
                ForeColor = Color.FromArgb(234, 234, 241),
                ShowCheckMargin = true,
                Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors())
            };
            AddToggle(menu, "Show song title", () => _settings.ShowSongTitle,
                value => _settings.ShowSongTitle = value);
            AddToggle(menu, "Show album artwork", () => _settings.ShowAlbumArt,
                value => _settings.ShowAlbumArt = value);
            AddToggle(menu, "Show playback controls", () => _settings.ShowTransportControls,
                value => _settings.ShowTransportControls = value);
            AddToggle(menu, "Show visualizer", () => _settings.ShowVisualizer,
                value => _settings.ShowVisualizer = value);
            AddToggle(menu, "Show queue and history", () => _settings.ShowSongQueue,
                value => _settings.ShowSongQueue = value);
            AddToggle(menu, "Match album artwork colours", () => _settings.UseArtworkColors,
                value => _settings.UseArtworkColors = value);
            AddToggle(menu, "Preview next lyric", () => _settings.NextLineWhenNoTranslation,
                value => _settings.NextLineWhenNoTranslation = value);
            AddToggle(menu, "Show English / translation", () => _settings.ShowTranslation,
                value => _settings.ShowTranslation = value);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Add English meaning from Genius…", null, (sender, args) =>
                BeginInvoke(new Action(OpenEnglishImporter)));
            var timingAction = menu.Items.Add("Edit lyric timing…", null, (sender, args) =>
                BeginInvoke(new Action(OpenTimingEditor)));
            var lrcAction = menu.Items.Add("Find lyrics on LRCLIB…", null, (sender, args) =>
                BeginInvoke(new Action(OpenLrcLibPicker)));
            var tempoAction = menu.Items.Add("Adjust Party BPM…", null, (sender, args) =>
                BeginInvoke(new Action(OpenPartyTempoEditor)));
            menu.Items.Add("Online Party BPM…", null, (sender, args) =>
                BeginInvoke(new Action(OpenPartyOnlineSettings)));
            var retryOnline = menu.Items.Add("Retry online BPM for this song", null,
                (sender, args) => BeginInvoke(new Action(() => StartPartyOnlineLookup(true))));
            menu.Opening += (sender, args) =>
            {
                timingAction.Visible = _timingButton.IsEmpty;
                lrcAction.Visible = _lrcButton.IsEmpty;
                tempoAction.Enabled = !string.IsNullOrWhiteSpace(_artworkTrackUrl);
                var savedTempo = !string.IsNullOrWhiteSpace(_artworkTrackUrl) ?
                    _partyTempoStore.Load(_artworkTrackUrl) : null;
                retryOnline.Enabled = _settings.PartyMode &&
                    !string.IsNullOrWhiteSpace(_partyApiKey) &&
                    !string.IsNullOrWhiteSpace(_artworkTrackUrl) &&
                    _partyTagBpm == 0 && (savedTempo == null ||
                    (!savedTempo.Manual && !savedTempo.Online));
            };
            menu.Items.Add("More settings…", null, (sender, args) =>
                BeginInvoke(new Action(() => _openSettings?.Invoke())));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Close lyrics window", null, (sender, args) => Close());
            return menu;
        }

        private void AddToggle(ContextMenuStrip menu, string label, Func<bool> getter,
            Action<bool> setter)
        {
            var item = new ToolStripMenuItem(label);
            menu.Opening += (sender, args) => item.Checked = getter();
            item.Click += (sender, args) =>
            {
                var wasShowingArt = _settings.ShowAlbumArt;
                setter(!getter());
                UpdateFromSettings(_settings);
                if (wasShowingArt != _settings.ShowAlbumArt) RefreshArtwork(true);
                if (_settings.ShowSongQueue) RefreshQueue();
                _settingsChanged?.Invoke(_settings);
                Invalidate();
            };
            menu.Items.Add(item);
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Color.FromArgb(27, 29, 41);
            public override Color MenuBorder => Color.FromArgb(72, 75, 94);
            public override Color MenuItemBorder => Color.FromArgb(92, 96, 119);
            public override Color MenuItemSelected => Color.FromArgb(58, 62, 82);
            public override Color ImageMarginGradientBegin => Color.FromArgb(27, 29, 41);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(27, 29, 41);
            public override Color ImageMarginGradientEnd => Color.FromArgb(27, 29, 41);
            public override Color CheckBackground => Color.FromArgb(70, 79, 116);
            public override Color CheckSelectedBackground => Color.FromArgb(84, 94, 138);
        }

        private void RefreshPlayState()
        {
            try { _playState = _musicBee.Player_GetPlayState(); }
            catch (Exception) { _playState = Plugin.PlayState.Undefined; }
        }

        private void LoadPartyTempo(string trackUrl)
        {
            var saved = _partyTempoStore.Load(trackUrl);
            if (saved != null && (saved.Manual || _partyTagBpm == 0))
            {
                _partyBpm = saved.Bpm;
                _partyOriginMs = saved.OriginMs;
                _partyTempoSource = saved.Manual ? PartyTempoSource.Manual :
                    PartyTempoSource.Saved;
            }
            else
            {
                _partyBpm = _partyTagBpm;
                _partyOriginMs = _partyBpm > 0 ?
                    PartyAnimation.OriginForBeat(0, _partyBpm) : 0;
                _partyTempoSource = _partyBpm > 0 ? PartyTempoSource.Tag :
                    PartyTempoSource.None;
            }
        }

        private void CancelPartyLookup()
        {
            var pending = _partyLookupCancellation;
            _partyLookupCancellation = null;
            pending?.Cancel();
        }

        private bool PartyOnlineEnabled => !_settings.DisableDeezerBpmLookup ||
            !string.IsNullOrWhiteSpace(_partyApiKey);

        private void StartPartyOnlineLookup(bool retry = false)
        {
            if (_animationDisposed || !_settings.PartyMode ||
                !PartyOnlineEnabled || string.IsNullOrWhiteSpace(_artworkTrackUrl)) return;
            var saved = _partyTempoStore.Load(_artworkTrackUrl);
            if (!PartyOnlineLookup.CanLookup(_partyTagBpm, saved)) return;
            if (string.IsNullOrWhiteSpace(_songTitle) ||
                string.IsNullOrWhiteSpace(_songArtist))
            {
                _partyOnlineStatus = PartyOnlineStatus.NoMatch;
                _partyLookupDetail = "The song needs a title and artist tag in MusicBee.";
                Invalidate();
                return;
            }
            if (!retry && _lastOnlineAttemptTrack == _artworkTrackUrl) return;
            CancelPartyLookup();
            var pending = new CancellationTokenSource();
            _partyLookupCancellation = pending;
            _lastOnlineAttemptTrack = _artworkTrackUrl;
            _partyOnlineStatus = PartyOnlineStatus.Searching;
            _partyLookupError = null;
            _partyLookupDetail = null;
            Invalidate();
            LookupPartyTempoAsync(_artworkTrackUrl, _songTitle, _songArtist,
                _songAlbum, _partyApiKey, pending);
        }

        private async void LookupPartyTempoAsync(string trackUrl, string title,
            string artist, string album, string apiKey, CancellationTokenSource pending)
        {
            double bpm = 0;
            string error = null, detail = null, source = null, sourceUrl = null;
            var wasCurrent = false;
            try
            {
                var duration = (_musicBee.NowPlaying_GetDuration?.Invoke() ?? 0) / 1000;
                var result = await PartyOnlineLookup.SearchAsync(title, artist, album,
                    duration, apiKey, !_settings.DisableDeezerBpmLookup, pending.Token);
                bpm = result.Bpm;
                detail = result.Detail;
                source = result.Source;
                sourceUrl = result.SourceUrl;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { error = ex.Message; }
            finally
            {
                wasCurrent = ReferenceEquals(_partyLookupCancellation, pending) &&
                    !pending.IsCancellationRequested;
                if (ReferenceEquals(_partyLookupCancellation, pending))
                    _partyLookupCancellation = null;
                pending.Dispose();
            }
            if (!wasCurrent || _animationDisposed || IsDisposed || !_settings.PartyMode ||
                trackUrl != _artworkTrackUrl || _partyTagBpm > 0 ||
                apiKey != _partyApiKey) return;
            var saved = _partyTempoStore.Load(trackUrl);
            if (!PartyOnlineLookup.CanLookup(_partyTagBpm, saved)) return;
            if (error != null)
            {
                _partyLookupError = error;
                _partyOnlineStatus = PartyOnlineStatus.Error;
            }
            else if (bpm == 0)
            {
                _partyLookupDetail = detail;
                _partyOnlineStatus = PartyOnlineStatus.NoMatch;
            }
            else
            {
                try
                {
                    var origin = PartyAnimation.OriginForBeat(0, bpm);
                    _partyTempoStore.Save(trackUrl, bpm, origin, false, true, source, sourceUrl);
                    _partyBpm = bpm;
                    _partyOriginMs = origin;
                    _partyTempoSource = PartyTempoSource.Online;
                    _partyOnlineStatus = PartyOnlineStatus.None;
                    _partyBeat.Reset();
                }
                catch (Exception ex)
                {
                    _partyLookupError = "Could not save this BPM: " + ex.Message;
                    _partyOnlineStatus = PartyOnlineStatus.Error;
                }
            }
            _lastPartyUpdate = 0;
            UpdatePartyDancers();
            Invalidate();
        }

        private void OpenPartyOnlineSettings()
        {
            using (var dialog = new Form
            {
                Text = "Online Party BPM", ClientSize = new Size(420, 274),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false,
                TopMost = true
            })
            {
                var label = new Label
                {
                    Text = "GetSongBPM API key (stored for your Windows account)",
                    Bounds = new Rectangle(16, 14, 388, 24)
                };
                var input = new TextBox
                {
                    Text = _partyApiKey, UseSystemPasswordChar = true,
                    Bounds = new Rectangle(16, 42, 388, 25)
                };
                var deezer = new CheckBox
                {
                    Text = "Use Deezer too (no key needed)",
                    Checked = !_settings.DisableDeezerBpmLookup,
                    Bounds = new Rectangle(16, 78, 388, 26)
                };
                var explanation = new Label
                {
                    Text = _partyLookupError ?? _partyLookupDetail ??
                        "Searches GetSongBPM first if a key is set, then Deezer. Sends song " +
                        "title and artist only. Existing saved timing is kept; forget it " +
                        "in Adjust Party BPM to request a replacement.",
                    Bounds = new Rectangle(16, 111, 388, 78)
                };
                var credit = new LinkLabel
                {
                    Text = "GetSongBPM — obtain an API key",
                    Bounds = new Rectangle(16, 194, 275, 24)
                };
                credit.LinkClicked += (sender, args) =>
                {
                    try { Process.Start("https://getsongbpm.com/api"); }
                    catch (Exception) { /* The key can still be pasted manually. */ }
                };
                var remove = new Button
                {
                    Text = "Remove key", DialogResult = DialogResult.No,
                    Bounds = new Rectangle(16, 236, 100, 28)
                };
                var cancel = new Button
                {
                    Text = "Cancel", DialogResult = DialogResult.Cancel,
                    Bounds = new Rectangle(238, 236, 76, 28)
                };
                var save = new Button
                {
                    Text = "Save", DialogResult = DialogResult.OK,
                    Bounds = new Rectangle(322, 236, 82, 28)
                };
                dialog.Controls.AddRange(new Control[]
                    { label, input, deezer, explanation, credit, remove, cancel, save });
                dialog.AcceptButton = save;
                dialog.CancelButton = cancel;
                var result = dialog.ShowDialog(this);
                if (result != DialogResult.OK && result != DialogResult.No) return;
                try
                {
                    var key = result == DialogResult.No ? "" : input.Text.Trim();
                    _partyTempoStore.SaveApiKey(key);
                    CancelPartyLookup();
                    _partyApiKey = key;
                    _settings.DisableDeezerBpmLookup = !deezer.Checked;
                    _settingsChanged?.Invoke(_settings);
                    _partyOnlineStatus = PartyOnlineStatus.None;
                    _partyLookupError = null;
                    _partyLookupDetail = null;
                    _lastOnlineAttemptTrack = null;
                    LoadPartyTempo(_artworkTrackUrl);
                    StartPartyOnlineLookup();
                    _lastPartyUpdate = 0;
                    UpdatePartyDancers();
                    Invalidate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save the API key: " + ex.Message,
                        "Online Party BPM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OpenPartyTempoEditor()
        {
            var trackUrl = _artworkTrackUrl;
            if (string.IsNullOrWhiteSpace(trackUrl)) return;
            var saved = _partyTempoStore.Load(trackUrl);
            var currentBpm = _partyBpm > 0 ? _partyBpm :
                _partyBeat.Bpm > 0 ? _partyBeat.Bpm : 120;
            int? tappedBeat = null;
            var tapTempo = new PartyTapTempo();
            using (var dialog = new Form
            {
                Text = "Party BPM for this song", ClientSize = new Size(360, 324),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false,
                TopMost = true
            })
            {
                var title = new Label
                {
                    Text = string.IsNullOrWhiteSpace(_songTitle) ? "Current song" : _songTitle,
                    AutoEllipsis = true, Bounds = new Rectangle(16, 12, 328, 24)
                };
                var bpmLabel = new Label
                {
                    Text = "BPM", Bounds = new Rectangle(16, 48, 64, 22)
                };
                var bpmInput = new NumericUpDown
                {
                    Minimum = 40, Maximum = 240, DecimalPlaces = 2,
                    Increment = 0.1m, Value = (decimal)Math.Round(
                        Math.Max(40, Math.Min(240, currentBpm)), 2),
                    Bounds = new Rectangle(82, 43, 100, 26)
                };
                var syncBeat = new Button
                {
                    Text = "Align to this beat",
                    Bounds = new Rectangle(190, 43, 154, 28)
                };
                var tapButton = new Button
                {
                    Text = "Tap beat", Bounds = new Rectangle(16, 83, 328, 44)
                };
                var half = new Button
                {
                    Text = "½ speed", Bounds = new Rectangle(16, 133, 158, 28)
                };
                var twice = new Button
                {
                    Text = "2× speed", Bounds = new Rectangle(186, 133, 158, 28)
                };
                Action updateSpeedButtons = () =>
                {
                    half.Enabled = bpmInput.Value / 2 >= bpmInput.Minimum;
                    twice.Enabled = bpmInput.Value * 2 <= bpmInput.Maximum;
                };
                half.Click += (sender, args) => bpmInput.Value /= 2;
                twice.Click += (sender, args) => bpmInput.Value *= 2;
                bpmInput.ValueChanged += (sender, args) => updateSpeedButtons();
                updateSpeedButtons();
                var sourceLabel = new LinkLabel
                {
                    Text = saved?.Source == null ? "Timing is stored for this song only." :
                        "Original lookup: " + saved.Source,
                    Bounds = new Rectangle(16, 249, 328, 24)
                };
                if (saved?.SourceUrl == null) sourceLabel.LinkArea = new LinkArea(0, 0);
                sourceLabel.LinkClicked += (sender, args) =>
                {
                    Uri uri;
                    if (Uri.TryCreate(saved?.SourceUrl, UriKind.Absolute, out uri) &&
                        uri.Scheme == "https" && (uri.Host == "www.deezer.com" ||
                        uri.Host == "getsongbpm.com"))
                        try { Process.Start(uri.AbsoluteUri); } catch (Exception) { }
                };
                var tapStatus = new Label
                {
                    Text = "Tap along with the song at least twice.",
                    Bounds = new Rectangle(16, 171, 328, 24)
                };
                var explanation = new Label
                {
                    Text = "Click Align as you hear a beat to preview the raised-arm pose timing. Save keeps it for this song; Cancel discards the preview.",
                    Bounds = new Rectangle(16, 201, 328, 46)
                };
                var forget = new Button
                {
                    Text = "Forget saved BPM", Enabled = saved != null,
                    Bounds = new Rectangle(16, 286, 140, 28)
                };
                var cancel = new Button
                {
                    Text = "Cancel", DialogResult = DialogResult.Cancel,
                    Bounds = new Rectangle(188, 286, 74, 28)
                };
                var save = new Button
                {
                    Text = "Save", DialogResult = DialogResult.OK,
                    Bounds = new Rectangle(270, 286, 74, 28)
                };
                Action previewBeat = () =>
                {
                    if (!tappedBeat.HasValue || _artworkTrackUrl != trackUrl) return;
                    _partyPreviewBpm = (double)bpmInput.Value;
                    _partyPreviewOriginMs = PartyAnimation.OriginForBeat(
                        tappedBeat.Value, _partyPreviewBpm);
                    _partyPreviewTrackUrl = trackUrl;
                    tapStatus.Text = "Previewing alignment — Save to keep";
                    _lastPartyUpdate = 0;
                    UpdatePartyDancers();
                };
                bpmInput.ValueChanged += (sender, args) => previewBeat();
                tapButton.Click += (sender, args) =>
                {
                    // Record the clock before calling MusicBee so the spacing
                    // between clicks is independent of its UI response time.
                    var timestamp = Stopwatch.GetTimestamp();
                    try
                    {
                        if (_musicBee.NowPlaying_GetFileUrl() != trackUrl)
                            throw new InvalidOperationException("The song changed.");
                        var position = ReadPartyPosition(timestamp);
                        double estimatedBpm;
                        if (!tapTempo.Tap(timestamp, Stopwatch.Frequency,
                                out estimatedBpm)) return;
                        tappedBeat = position;
                        if (estimatedBpm > 0)
                        {
                            bpmInput.Value = (decimal)Math.Round(estimatedBpm, 1);
                            tapStatus.Text = tapTempo.TapCount + " taps · " +
                                estimatedBpm.ToString("0.0", CultureInfo.InvariantCulture) +
                                " BPM — Save to keep preview";
                        }
                        else tapStatus.Text = "Previewing first tap. Keep tapping...";
                        var status = tapStatus.Text;
                        previewBeat();
                        tapStatus.Text = status;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, "Could not tap the beat: " + ex.Message,
                            "Party BPM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                syncBeat.Click += (sender, args) =>
                {
                    try
                    {
                        if (_musicBee.NowPlaying_GetFileUrl() != trackUrl)
                            throw new InvalidOperationException("The song changed.");
                        tappedBeat = ReadPartyPosition(Stopwatch.GetTimestamp());
                        previewBeat();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(dialog, "Could not mark the beat: " + ex.Message,
                            "Party BPM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                };
                forget.Click += (sender, args) => { dialog.DialogResult = DialogResult.No; dialog.Close(); };
                dialog.Controls.AddRange(new Control[]
                    { title, bpmLabel, bpmInput, syncBeat, tapButton, half, twice, sourceLabel, tapStatus,
                        explanation, forget, cancel, save });
                dialog.AcceptButton = save;
                dialog.CancelButton = cancel;
                DialogResult result;
                try { result = dialog.ShowDialog(this); }
                finally
                {
                    _partyPreviewTrackUrl = null;
                    _lastPartyUpdate = 0;
                    UpdatePartyDancers();
                }
                if (result != DialogResult.OK && result != DialogResult.No) return;
                try
                {
                    if (_musicBee.NowPlaying_GetFileUrl() != trackUrl)
                    {
                        MessageBox.Show(this, "The song changed. Open Party BPM again for the current song.",
                            "Party BPM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (result == DialogResult.No)
                    {
                        _partyTempoStore.Delete(trackUrl);
                        _partyBeat.Reset();
                        LoadPartyTempo(trackUrl);
                        _lastOnlineAttemptTrack = null;
                        StartPartyOnlineLookup();
                    }
                    else
                    {
                        var bpm = (double)bpmInput.Value;
                        int origin;
                        if (tappedBeat.HasValue)
                            origin = PartyAnimation.OriginForBeat(tappedBeat.Value, bpm);
                        else
                        {
                            var position = ReadPartyPosition(Stopwatch.GetTimestamp());
                            var oldBpm = _partyBpm > 0 ? _partyBpm : _partyBeat.Bpm;
                            var oldOrigin = _partyBpm > 0 ? _partyOriginMs : _partyBeat.OriginMs;
                            origin = PartyAnimation.OriginForPhase(position,
                                oldBpm, oldOrigin, bpm);
                        }
                        _partyTempoStore.Save(trackUrl, bpm, origin, true, false,
                            saved?.Source, saved?.SourceUrl);
                        CancelPartyLookup();
                        _partyBpm = bpm;
                        _partyOriginMs = origin;
                        _partyTempoSource = PartyTempoSource.Manual;
                        _partyBeat.Reset();
                    }
                    _partySpectrumMisses = 0;
                    _lastPartyUpdate = 0;
                    UpdatePartyDancers();
                    Invalidate();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save Party BPM: " + ex.Message,
                        "Party BPM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void UpdatePartyDancers()
        {
            if (!_loaded || _animationDisposed) return;
            if (!_settings.PartyMode)
            {
                DisposePartyDancers();
                _partyClock.Reset();
                _partyLayoutValid = false;
                return;
            }
            if (!Visible || WindowState == FormWindowState.Minimized)
            {
                _leftDancer?.Hide();
                _rightDancer?.Hide();
                return;
            }
            try
            {
                if (_leftDancer == null)
                    _leftDancer = new PartyDancerWindow("MusicBeePlugin.PartyRem.png");
                if (_rightDancer == null)
                    _rightDancer = new PartyDancerWindow("MusicBeePlugin.PartyRam.png");
                var position = ReadPartyPosition(Stopwatch.GetTimestamp());
                var detectedBpm = _partyBpm == 0 ? _partyBeat.Bpm : 0;
                var bpm = _partyBpm > 0 ? _partyBpm : detectedBpm;
                var origin = _partyBpm > 0 ? _partyOriginMs : _partyBeat.OriginMs;
                if (_partyPreviewTrackUrl != null &&
                    _partyPreviewTrackUrl == _artworkTrackUrl)
                {
                    bpm = _partyPreviewBpm;
                    origin = _partyPreviewOriginMs;
                }
                var phasePosition = PartyAnimation.DisplayPhaseAt(position, origin, bpm);
                var frame = PartyAnimation.FrameAt(phasePosition, bpm);
                var impact = PartyAnimation.SideImpactAt(phasePosition, bpm) +
                    PartyAnimation.CentreImpactAt(phasePosition, bpm);
                var sway = PartyAnimation.SwayAt(phasePosition, bpm);
                var anticipation = PartyAnimation.AnticipationAt(phasePosition, bpm);
                RefreshPartyLayout();
                PlacePartyDancer(_leftDancer, _leftPartyBounds, frame, impact,
                    sway, anticipation);
                PlacePartyDancer(_rightDancer, _rightPartyBounds, frame, impact,
                    sway, anticipation);
            }
            catch (Exception ex)
            {
                DisposePartyDancers();
                _settings.PartyMode = false;
                _settingsChanged?.Invoke(_settings);
                Invalidate();
                MessageBox.Show(this, "Could not show the party dancers: " + ex.Message,
                    "Party mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private int ReadPartyPosition(long timestamp)
        {
            var rawPosition = Math.Max(0, _musicBee.Player_GetPosition());
            return _partyClock.PositionAt(rawPosition, timestamp,
                Stopwatch.Frequency, _playState == Plugin.PlayState.Playing);
        }

        private void RefreshPartyLayout()
        {
            if (_partyLayoutValid && _partyLayoutWindow == Bounds &&
                _partyLayoutClientSize == ClientSize &&
                _partyLayoutState == WindowState) return;
            _partyLayoutWindow = Bounds;
            _partyLayoutClientSize = ClientSize;
            _partyLayoutState = WindowState;
            _partyLayoutValid = true;
            if (WindowState == FormWindowState.Maximized)
            {
                var client = RectangleToScreen(ClientRectangle);
                var area = Screen.FromControl(this).WorkingArea;
                var gutter = PartyGutter;
                _leftPartyBounds = PartyLayout.PlaceMaximized(client, area, true, gutter);
                _rightPartyBounds = PartyLayout.PlaceMaximized(client, area, false, gutter);
            }
            else
            {
                var screens = Screen.AllScreens;
                var areas = new Rectangle[screens.Length];
                for (var i = 0; i < screens.Length; i++)
                    areas[i] = screens[i].WorkingArea;
                _leftPartyBounds = PartyLayout.PlaceOutside(Bounds, areas, true);
                _rightPartyBounds = PartyLayout.PlaceOutside(Bounds, areas, false);
            }
        }

        private void PlacePartyDancer(PartyDancerWindow dancer, Rectangle bounds,
            int frame, float impact, float sway, float anticipation)
        {
            if (bounds.IsEmpty)
            {
                dancer.Hide();
                return;
            }
            dancer.Present(bounds, frame, impact, sway, anticipation);
            if (!dancer.Visible) dancer.Show(this);
        }

        private void DisposePartyDancers()
        {
            _leftDancer?.Dispose();
            _rightDancer?.Dispose();
            _leftDancer = _rightDancer = null;
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
            if (IsDisposed || !IsHandleCreated) return;
            string trackUrl;
            try { trackUrl = _musicBee.NowPlaying_GetFileUrl(); }
            catch (Exception) { return; }
            var trackChanged = trackUrl != _artworkTrackUrl;
            if (!force && !trackChanged) return;
            if (_timingEditor != null && !_timingEditor.IsDisposed &&
                trackUrl != _timingEditor.TrackUrl)
                _timingEditor.TrackChanged();
            if (_timingCreator != null && !_timingCreator.IsDisposed &&
                trackUrl != _timingCreator.TrackUrl)
                _timingCreator.TrackChanged();
            if (_lrcPicker != null && !_lrcPicker.IsDisposed &&
                trackUrl != _lrcPicker.TrackUrl)
                _lrcPicker.TrackChanged();
            if (_englishImporter != null && !_englishImporter.IsDisposed &&
                trackUrl != _englishImporter.TrackUrl)
                _englishImporter.TrackChanged();
            _artworkTrackUrl = trackUrl;
            if (trackChanged)
            {
                CancelPartyLookup();
                _lastOnlineAttemptTrack = null;
                _partyOnlineStatus = PartyOnlineStatus.None;
                _partyLookupError = null;
                _partyLookupDetail = null;
                _queueTracks.Clear();
                _lastFutureOffset = 0;
                try
                {
                    _partyTagBpm = PartyAnimation.ReadBpm(
                        _musicBee.NowPlaying_GetFileTag(Plugin.MetaDataType.BeatsPerMin));
                }
                catch (Exception) { _partyTagBpm = 0; }
                _partyBeat.Reset();
                _partyClock.Reset();
                _partyPreviewTrackUrl = null;
                LoadPartyTempo(trackUrl);
                _partySpectrumMisses = 0;
                _lastPartyUpdate = 0;
            }
            if (trackChanged && !string.IsNullOrWhiteSpace(trackUrl))
            {
                _queueNoticeStarted = 0;
                _queueEnded = false;
            }
            var request = Interlocked.Increment(ref _artworkRequestId);
            try
            {
                _songTitle = _musicBee.NowPlaying_GetFileTag(Plugin.MetaDataType.TrackTitle) ?? "";
                _songArtist = _musicBee.NowPlaying_GetFileTag(Plugin.MetaDataType.Artist) ?? "";
                _songAlbum = _musicBee.NowPlaying_GetFileTag(Plugin.MetaDataType.Album) ?? "";
            }
            catch (Exception) { _songTitle = _songArtist = _songAlbum = ""; }
            if (trackChanged) StartPartyOnlineLookup();
            if (_history.Observe(trackUrl, _songTitle, _songArtist) || trackChanged)
            {
                _queueScroll = Math.Max(0, _history.Snapshot().Count - 2);
                RefreshQueue();
            }
            RefreshPlayState();
            if (trackChanged || (!_settings.ShowAlbumArt && !_useArtworkColors))
            {
                var previousArt = _albumArtwork;
                _albumArtwork = null;
                previousArt?.Dispose();
            }
            Invalidate();
            if (!_settings.ShowAlbumArt && !_useArtworkColors)
            {
                SetPalette(ArtworkPalette.Default);
                return;
            }

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
                Bitmap cover;
                if (!ArtworkPalette.TryLoad(imageData, out palette, out cover) &&
                    !ArtworkPalette.TryLoad(artworkUrl, out palette, out cover) &&
                    !ArtworkPalette.TryLoad(artwork, out palette, out cover))
                    palette = ArtworkPalette.Default;
                if (IsDisposed || !IsHandleCreated)
                {
                    cover?.Dispose();
                    return;
                }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed || request != _artworkRequestId)
                        {
                            cover?.Dispose();
                            return;
                        }
                        var previousArt = _albumArtwork;
                        _albumArtwork = cover;
                        previousArt?.Dispose();
                        if (_useArtworkColors) SetPalette(palette);
                        Invalidate();
                    }));
                }
                catch (InvalidOperationException) { cover?.Dispose(); }
            });
        }

        public void RefreshQueue()
        {
            if (IsDisposed || !IsHandleCreated) return;
            _history.RetainPlayingList(UpcomingQueue.ReadPlayingListUrls(_musicBee));
            var requested = Math.Max(12, Math.Min(96, _lastFutureOffset));
            var future = UpcomingQueue.Read(_musicBee, 1, requested);
            _lastFutureOffset = future.Count == 0 ? 0 : future[future.Count - 1].Offset;
            _queueExhausted = future.Count < requested;
            _queueTracks = _history.Timeline(future);
            _queueScroll = Math.Max(0, Math.Min(_queueScroll,
                Math.Max(0, _queueTracks.Count - _queueVisibleCount)));
            _lastQueueCheck = Stopwatch.GetTimestamp();
            Invalidate();
        }

        private void LoadMoreQueue()
        {
            if (_queueExhausted || _lastFutureOffset >= 96) return;
            var first = _lastFutureOffset + 1;
            var count = Math.Min(12, 97 - first);
            if (count <= 0) { _queueExhausted = true; return; }
            var more = UpcomingQueue.Read(_musicBee, first, count);
            foreach (var track in more)
            {
                _lastFutureOffset = track.Offset;
                if (_queueTracks.Exists(existing => existing.Offset > 0 &&
                    existing.Index == track.Index))
                {
                    _queueExhausted = true;
                    break;
                }
                if (_queueTracks.Exists(existing => string.Equals(existing.FileUrl,
                    track.FileUrl, StringComparison.OrdinalIgnoreCase))) continue;
                _queueTracks.Add(track);
            }
            if (more.Count < count) _queueExhausted = true;
        }

        private void ScrollQueue(int lines)
        {
            if (_queueCard.IsEmpty || _queueVisibleCount == 0) return;
            if (lines > 0 && _queueScroll + _queueVisibleCount + lines >= _queueTracks.Count)
                LoadMoreQueue();
            _queueScroll = Math.Max(0, Math.Min(_queueScroll + lines,
                Math.Max(0, _queueTracks.Count - _queueVisibleCount)));
            Invalidate(_queueCard);
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
                _playState = _musicBee.Player_GetPlayState();
                if (_playState == Plugin.PlayState.Playing &&
                    _musicBee.NowPlaying_GetSpectrumData != null)
                    count = _musicBee.NowPlaying_GetSpectrumData(_fft);
            }
            catch (Exception)
            {
                // A missing audio stream should leave the lyrics window usable.
            }

            var validCount = Math.Min(Math.Max(0, count), _fft.Length);
            var upperBin = Math.Min(validCount / 2, 1024);
            if (_settings.PartyMode && !PartyOnlineEnabled &&
                _partyBpm == 0 && _partyBeat.Bpm == 0 &&
                _playState == Plugin.PlayState.Playing)
                _partySpectrumMisses = upperBin > 8 ? 0 :
                    Math.Min(30, _partySpectrumMisses + 1);
            if (_settings.PartyMode && !PartyOnlineEnabled &&
                _partyBpm == 0 && _partyBeat.Bpm == 0 &&
                upperBin > 8 &&
                _playState == Plugin.PlayState.Playing)
            {
                // Use the same mirrored-spectrum handling as the visualizer.
                // Bass transients reveal an approximate pulse even when the
                // user's library has no BPM tags or the bars are hidden.
                double bassEnergy = 0;
                for (var bin = 2; bin < Math.Min(96, upperBin); bin++)
                {
                    var magnitude = Math.Max(
                        Math.Max(SafeMagnitude(_fft[bin]),
                            SafeMagnitude(_fft[validCount - 1 - bin])),
                        Math.Max(SafeMagnitude(_fft[validCount / 2 + bin]),
                            SafeMagnitude(_fft[validCount / 2 - 1 - bin])));
                    bassEnergy += magnitude / Math.Sqrt(bin);
                }
                try
                {
                    _partyBeat.Observe(_musicBee.Player_GetPosition(), bassEnergy);
                    if (_partyBeat.Bpm > 0 && !string.IsNullOrWhiteSpace(_artworkTrackUrl))
                        _partyTempoStore.Save(_artworkTrackUrl, _partyBeat.Bpm,
                            _partyBeat.OriginMs, false);
                }
                catch (Exception)
                {
                    // Playback position or plugin storage can be unavailable.
                    // The current song still uses the tempo if saving fails.
                }
            }
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

        private void DrawBackground(Graphics g, Rectangle bounds)
        {
            if (_settings.TransparentCanvas) return;
            // The gradient and soft radial lights do not change between
            // tracks. Keep a rendered surface for the 120 Hz animation loop.
            if (_movingOrResizing && _backgroundCache != null)
            {
                g.InterpolationMode = InterpolationMode.Low;
                g.DrawImage(_backgroundCache, bounds);
                return;
            }
            if (_paletteStarted != 0)
            {
                DrawBackgroundCore(g, bounds);
                return;
            }
            if (_backgroundCache == null || _backgroundCache.Size != bounds.Size ||
                _cachedBackgroundPalette.Left != _palette.Left ||
                _cachedBackgroundPalette.Right != _palette.Right ||
                _cachedBackgroundPalette.Accent != _palette.Accent ||
                _cachedBackgroundPalette.BarBottom != _palette.BarBottom)
            {
                _backgroundCache?.Dispose();
                _backgroundCache = new Bitmap(bounds.Width, bounds.Height);
                using (var surface = Graphics.FromImage(_backgroundCache))
                {
                    surface.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawBackgroundCore(surface, new Rectangle(Point.Empty, bounds.Size));
                }
                _cachedBackgroundPalette = _palette;
            }
            g.DrawImageUnscaled(_backgroundCache, bounds.Location);
        }

        private void DrawBackgroundCore(Graphics g, Rectangle bounds)
        {
            var accent = _palette.Accent;
            var upper = MixColor(_palette.Left, accent, 0.32f);
            var lower = MixColor(_palette.Right, _palette.BarBottom, 0.17f);
            using (var background = new LinearGradientBrush(bounds,
                       _palette.Left, _palette.Right, LinearGradientMode.Horizontal))
            {
                background.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { _palette.Left, upper, lower, _palette.Right },
                    Positions = new[] { 0f, 0.32f, 0.72f, 1f }
                };
                g.FillRectangle(background, bounds);
            }
            // Broad, low-contrast light from two other cover hues lends depth
            // without putting a pattern directly under the lyric text.
            DrawGlow(g, new RectangleF(-bounds.Width * 0.12f, -bounds.Height * 0.48f,
                bounds.Width * 0.83f, bounds.Height * 1.17f), accent, 34);
            DrawGlow(g, new RectangleF(bounds.Width * 0.52f, bounds.Height * 0.32f,
                bounds.Width * 0.64f, bounds.Height * 0.96f), _palette.BarBottom, 25);
        }

        private static Color MixColor(Color first, Color second, float amount)
        {
            return Color.FromArgb(
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }

        private static void DrawGlow(Graphics g, RectangleF area, Color color, int alpha)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(area);
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Color.FromArgb(alpha, color);
                    glow.SurroundColors = new[] { Color.FromArgb(0, color) };
                    g.FillPath(glow, path);
                }
            }
        }

        private void DrawUpcomingQueue(Graphics g, Rectangle bounds, int sideMargin)
        {
            _queueHits.Clear();
            _queueCard = _queueTab = _queueUpButton = _queueDownButton = Rectangle.Empty;
            _queueVisibleCount = 0;
            if (!_settings.ShowSongQueue) return;
            var narrow = sideMargin < 123 || bounds.Height < 218;
            var compact = narrow || sideMargin < 198 || bounds.Height < 265;
            var top = _settings.ShowSongTitle ?
                (_settings.TransparentCanvas ? (bounds.Height < 260 ? 52 : 65) : 43) : 16;
            var bottom = _settings.ShowTransportControls ?
                (_settings.TransparentCanvas ? (bounds.Height < 260 ? 66 : 78) : 55) : 16;
            Rectangle card;
            if (narrow)
            {
                _queueTab = new Rectangle(bounds.Right - 110, 43, 101, 27);
                using (var tabPath = RoundedRectangle(_queueTab, 8))
                using (var tabShade = new SolidBrush(Color.FromArgb(255, 13, 17, 28)))
                using (var tabFont = new Font("Segoe UI", 8f, FontStyle.Bold))
                using (var tabBrush = new SolidBrush(Color.FromArgb(216, 232, 237, 246)))
                using (var tabFormat = new StringFormat { Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center })
                {
                    g.FillPath(tabShade, tabPath);
                    g.DrawString("QUEUE  " + (_queueExpanded ? "▲" : "▼"),
                        tabFont, tabBrush, _queueTab, tabFormat);
                }
                if (!_queueExpanded) return;
                card = new Rectangle(bounds.Right - Math.Min(214, bounds.Width / 2) - 9,
                    _queueTab.Bottom + 4, Math.Min(214, bounds.Width / 2),
                    Math.Min(148, Math.Max(91, bounds.Height - _queueTab.Bottom - 12)));
            }
            else
            {
                var height = Math.Min(compact ? 215 : 306,
                    Math.Max(92, bounds.Height - top - bottom));
                card = new Rectangle(bounds.Right - sideMargin + (compact ? 7 : 13),
                    Math.Max(top, (bounds.Height - height) / 2),
                    sideMargin - (compact ? 15 : 27), height);
            }
            _queueCard = card;
            var rowHeight = compact ? 43 : 55;
            var rowTop = compact ? 36 : 42;
            var accent = _settings.TransparentCanvas ?
                Color.FromArgb(186, 198, 215) : _palette.Border;
            using (var path = RoundedRectangle(card, 12))
            using (var shade = new SolidBrush(Color.FromArgb(
                       _settings.TransparentCanvas ? 255 : 71, 10, 13, 25)))
            using (var border = new Pen(Color.FromArgb(43, accent)))
            using (var heading = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (var titleFont = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
            using (var artistFont = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point))
            using (var headingBrush = new SolidBrush(Color.FromArgb(171, accent)))
            using (var titleBrush = new SolidBrush(Color.FromArgb(214, 235, 238, 246)))
            using (var artistBrush = new SolidBrush(Color.FromArgb(131, 207, 214, 230)))
            using (var separator = new Pen(Color.FromArgb(32, accent)))
            using (var format = new StringFormat(StringFormatFlags.NoWrap)
                   { Trimming = StringTrimming.EllipsisCharacter })
            {
                var smoothing = g.SmoothingMode;
                if (_settings.TransparentCanvas) g.SmoothingMode = SmoothingMode.None;
                g.FillPath(shade, path);
                if (!_settings.TransparentCanvas) g.DrawPath(border, path);
                g.SmoothingMode = smoothing;
                g.DrawString("QUEUE", heading, headingBrush,
                    new RectangleF(card.Left + 10, card.Top + 10, card.Width - 58, 19), format);

                if (_queueTracks.Count == 0)
                {
                    g.DrawString("No songs up next", artistFont, artistBrush,
                        new RectangleF(card.Left + 10, card.Top + rowTop, card.Width - 20, 30), format);
                    return;
                }

                var visible = Math.Max(1, Math.Min(compact ? 2 : 4,
                    (card.Height - rowTop - 5) / rowHeight));
                _queueVisibleCount = visible;
                _queueScroll = Math.Max(0, Math.Min(_queueScroll,
                    Math.Max(0, _queueTracks.Count - visible)));
                if (_queueTracks.Count > visible || !_queueExhausted)
                {
                    _queueUpButton = new Rectangle(card.Right - 48, card.Top + 11, 17, 19);
                    _queueDownButton = new Rectangle(card.Right - 27, card.Top + 11, 17, 19);
                    using (var arrows = new SolidBrush(Color.FromArgb(150, 214, 220, 235)))
                    {
                        g.DrawString("▲", artistFont, arrows, _queueUpButton, format);
                        g.DrawString("▼", artistFont, arrows, _queueDownButton, format);
                    }
                }
                for (var i = 0; i < visible; i++)
                {
                    var y = card.Top + rowTop + i * rowHeight;
                    var index = _queueScroll + i;
                    if (index >= _queueTracks.Count) break;
                    var track = _queueTracks[index];
                    var hit = new Rectangle(card.Left + 5, y - 2, card.Width - 16,
                        rowHeight - 4);
                    _queueHits.Add(new QueueHit { Area = hit, Track = track });
                    if (track.Offset == 0)
                        using (var playing = new SolidBrush(Color.FromArgb(58, accent)))
                            g.FillRectangle(playing, hit);
                    if (_hoverQueue == track.Offset.ToString())
                        using (var highlight = new SolidBrush(Color.FromArgb(34, accent)))
                            g.FillRectangle(highlight, hit);
                    g.DrawString(track.Offset == 0 ? "▶  " + track.Title : track.Title,
                        titleFont, titleBrush,
                        new RectangleF(card.Left + 10, y, card.Width - 26,
                            compact ? 19 : 25), format);
                    if (!string.IsNullOrWhiteSpace(track.Artist))
                        g.DrawString(track.Artist, artistFont, artistBrush,
                            new RectangleF(card.Left + 10, y + (compact ? 20 : 24),
                                card.Width - 26, 17), format);
                    if (i < visible - 1 && index + 1 < _queueTracks.Count)
                        g.DrawLine(separator, card.Left + 10, y + rowHeight - 3,
                            card.Right - 14, y + rowHeight - 3);
                }
                if (_queueTracks.Count > visible)
                {
                    var rail = new Rectangle(card.Right - 9, card.Top + rowTop + 3, 3,
                        Math.Min(card.Height - rowTop - 12, visible * rowHeight - 7));
                    var thumbHeight = Math.Max(20, rail.Height * visible / _queueTracks.Count);
                    var maxScroll = _queueTracks.Count - visible;
                    var thumbTop = rail.Top + (rail.Height - thumbHeight) *
                        _queueScroll / maxScroll;
                    using (var railBrush = new SolidBrush(Color.FromArgb(32, accent)))
                    using (var thumb = new SolidBrush(Color.FromArgb(139, accent)))
                    {
                        g.FillRectangle(railBrush, rail);
                        g.FillRectangle(thumb, rail.Left, thumbTop, rail.Width, thumbHeight);
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // Clear the entire double buffer before every frame. A sizing
            // operation can expose new pixels beyond the last WM_PAINT region.
            g.Clear(_settings.TransparentCanvas ? ClearKey : BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var client = ClientRectangle;
            var gutter = PartyGutter;
            var bounds = new Rectangle(0, 0, client.Width - gutter * 2, client.Height);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var state = g.Save();
            if (gutter > 0) g.TranslateTransform(gutter, 0);
            try
            {

            DrawBackground(g, bounds);

            if (_settings.ShowVisualizer && !_settings.TransparentCanvas)
                DrawSpectrum(g, bounds);

            var topInset = _settings.ShowSongTitle ?
                (_settings.TransparentCanvas ? (bounds.Height < 260 ? 52f : 65f) : 43f) : 18f;
            var bottomInset = _settings.ShowTransportControls ?
                (_settings.TransparentCanvas ? (bounds.Height < 260 ? 66f : 78f) : 58f) : 15f;
            var region = new RectangleF(0, topInset, bounds.Width,
                Math.Max(24f, bounds.Height - topInset - bottomInset));
            // Give the cover its own vertical space. In a short window it can
            // extend below the lyric row without squeezing down to a thumbnail.
            var artSize = _settings.ShowAlbumArt
                ? Math.Max(0f, Math.Min(260f,
                    Math.Min(bounds.Height - 44f, bounds.Width * 0.16f))) : 0f;
            if (artSize > 0)
                DrawAlbumArt(g, new RectangleF(16,
                    Math.Max(topInset - 4f, (bounds.Height - artSize) / 2f),
                    artSize, artSize));
            var panelLeft = artSize > 0 ? (int)(16 + artSize + 15) : 13;
            // Equal margins keep the lyric centred over the transport controls.
            var panelWidth = Math.Max(40, bounds.Width - panelLeft * 2);
            var content = new RectangleF(panelLeft + 9, region.Top,
                Math.Max(1, panelWidth - 18), region.Height);
            // Keep the next line beneath the central romaji, with English
            // above it. Only a very short lyric area hides the preview.
            var shownNext = content.Height < 130 && !string.IsNullOrWhiteSpace(_line2)
                ? null : _nextLine;
            var previousShownNext = content.Height < 130 &&
                !string.IsNullOrWhiteSpace(_previousLine2) ? null : _previousNextLine;

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
            var scale = (float)Math.Max(0.75, Math.Min(2.6,
                Math.Sqrt((double)content.Width / 690 * (content.Height + 35.0) / 195)));
            var gap = 6f * scale;
            var mainHeight = 58f * scale;
            var subHeight = 38f * scale;
            var hasSideLine = !string.IsNullOrWhiteSpace(_line2) ||
                !string.IsNullOrWhiteSpace(shownNext) ||
                !string.IsNullOrWhiteSpace(_previousLine2) ||
                !string.IsNullOrWhiteSpace(previousShownNext);
            var groupHeight = LyricCardLayout.RequiredHeight(mainHeight, subHeight,
                gap, hasSideLine);
            var availableHeight = Math.Max(12f, content.Height - 18f);
            // A translated or upcoming lyric often needs two lines. The old
            // 38-unit slot could not hold them, forcing a long translation
            // into one tiny row even with ample free space around the card.
            if (hasSideLine)
            {
                var extra = Math.Min(22f * scale,
                    Math.Max(0f, availableHeight - groupHeight) / 2f);
                subHeight += extra;
                groupHeight += 2f * extra;
            }
            mainHeight += Math.Min(18f * scale,
                Math.Max(0f, availableHeight - groupHeight));
            groupHeight = LyricCardLayout.RequiredHeight(mainHeight, subHeight,
                gap, hasSideLine);
            if (groupHeight > availableHeight)
            {
                var fit = availableHeight / groupHeight;
                mainHeight *= fit;
                subHeight *= fit;
                gap *= fit;
            }
            var eased = progress * progress * (3 - 2 * progress);
            var oldOffset = -12f * scale * eased;
            var newOffset = 12f * scale * (1 - eased);
            var promotePreview = progress < 1f &&
                !string.IsNullOrEmpty(previousShownNext) &&
                previousShownNext == _line1 &&
                LyricTextLayout.CanPromotePreview(g, _line1.Trim(),
                    _settings.Font ?? SystemFonts.DefaultFont,
                    (_settings.Font ?? SystemFonts.DefaultFont).SizeInPoints * scale * 0.63f,
                    (_settings.Font ?? SystemFonts.DefaultFont).SizeInPoints * scale,
                    content.Width, subHeight, mainHeight);
            if (_settings.TransparentCanvas)
                DrawTransparentLyricPanel(g, content, panelLeft, panelWidth,
                    mainHeight, subHeight, gap, progress,
                    shownNext, previousShownNext, oldOffset, newOffset);
            else
            {
                if (progress < 1f)
                    DrawLyricPanel(g, content, panelLeft, panelWidth,
                        mainHeight, subHeight, gap, _previousLine1,
                        _previousLine2, previousShownNext,
                        1 - eased, oldOffset);
                DrawLyricPanel(g, content, panelLeft, panelWidth, mainHeight,
                    subHeight, gap, _line1, _line2, shownNext,
                    eased, newOffset);
            }
            if (progress < 1f)
                DrawLyricGroup(g, content, mainHeight, subHeight, gap, scale,
                    _previousLine1, _previousLine2, previousShownNext,
                    1 - eased, oldOffset, true, !promotePreview);
            DrawLyricGroup(g, content, mainHeight, subHeight, gap, scale,
                _line1, _line2, shownNext, eased, newOffset,
                !promotePreview, true);
            if (promotePreview)
            {
                var oldLayout = LyricCardLayout.Create(content, mainHeight, subHeight,
                    gap, !string.IsNullOrWhiteSpace(_previousLine2), true,
                    oldOffset);
                var newLayout = LyricCardLayout.Create(content, mainHeight, subHeight,
                    gap, !string.IsNullOrWhiteSpace(_line2),
                    !string.IsNullOrWhiteSpace(shownNext), newOffset);
                var start = oldLayout.Preview;
                var end = newLayout.Main;
                var traveling = new RectangleF(content.Left,
                    start.Top + (end.Top - start.Top) * eased, content.Width,
                    start.Height + (end.Height - start.Height) * eased);
                var fontSize = (_settings.Font ?? SystemFonts.DefaultFont).SizeInPoints * scale;
                DrawLine(g, _line1, traveling, fontSize * (0.63f + 0.37f * eased),
                    (int)(145 + 110 * eased));
            }
            DrawUpcomingQueue(g, bounds, panelLeft);
            DrawQueueNotice(g, content);
            DrawSongTitle(g, bounds);
            DrawTransport(g, bounds);
            if (_settings.TransparentCanvas || bounds.Width < 700)
                _timingButton = _lrcButton = Rectangle.Empty;
            else
            {
                DrawTimingButton(g, bounds);
                DrawLrcButton(g, bounds);
            }
            DrawBackgroundButton(g, bounds);
            DrawPartyButton(g, bounds);
            DrawMenuButton(g, bounds);
            DrawResizeGrip(g, bounds);
            }
            finally
            {
                g.Restore(state);
                if (gutter > 0) OffsetHitTargets(gutter);
            }
        }

        private int PartyGutter => _settings.PartyMode &&
            WindowState == FormWindowState.Maximized
                ? PartyLayout.MaximizedGutter(ClientSize.Width) : 0;

        private static Rectangle ShiftHit(Rectangle area, int offset)
        {
            return area.IsEmpty ? area : new Rectangle(area.Left + offset,
                area.Top, area.Width, area.Height);
        }

        private void OffsetHitTargets(int gutter)
        {
            _previousButton = ShiftHit(_previousButton, gutter);
            _playButton = ShiftHit(_playButton, gutter);
            _nextButton = ShiftHit(_nextButton, gutter);
            _menuButton = ShiftHit(_menuButton, gutter);
            _timingButton = ShiftHit(_timingButton, gutter);
            _lrcButton = ShiftHit(_lrcButton, gutter);
            _backgroundButton = ShiftHit(_backgroundButton, gutter);
            _partyButton = ShiftHit(_partyButton, gutter);
            _resizeGrip = ShiftHit(_resizeGrip, gutter);
            _queueCard = ShiftHit(_queueCard, gutter);
            _queueTab = ShiftHit(_queueTab, gutter);
            _queueUpButton = ShiftHit(_queueUpButton, gutter);
            _queueDownButton = ShiftHit(_queueDownButton, gutter);
            foreach (var hit in _queueHits)
                hit.Area = ShiftHit(hit.Area, gutter);
        }

        private void DrawTransparentLyricPanel(Graphics g, RectangleF content,
            int left, int width, float mainHeight, float subHeight, float gap,
            float progress, string shownNext,
            string previousShownNext, float oldOffset, float newOffset)
        {
            var hasOld = progress < 1f &&
                (!string.IsNullOrWhiteSpace(_previousLine1) ||
                 !string.IsNullOrWhiteSpace(_previousLine2) ||
                 !string.IsNullOrWhiteSpace(previousShownNext));
            var hasNew = !string.IsNullOrWhiteSpace(_line1) ||
                !string.IsNullOrWhiteSpace(_line2) ||
                !string.IsNullOrWhiteSpace(shownNext);
            if (!hasOld && !hasNew) return;
            var top = float.MaxValue;
            var bottom = float.MinValue;
            if (hasOld)
            {
                var layout = LyricCardLayout.Create(content, mainHeight, subHeight, gap,
                    !string.IsNullOrWhiteSpace(_previousLine2),
                    !string.IsNullOrWhiteSpace(previousShownNext), oldOffset);
                top = Math.Min(top, layout.Bounds.Top);
                bottom = Math.Max(bottom, layout.Bounds.Bottom);
            }
            if (hasNew)
            {
                var layout = LyricCardLayout.Create(content, mainHeight, subHeight, gap,
                    !string.IsNullOrWhiteSpace(_line2),
                    !string.IsNullOrWhiteSpace(shownNext), newOffset);
                top = Math.Min(top, layout.Bounds.Top);
                bottom = Math.Max(bottom, layout.Bounds.Bottom);
            }
            var card = new Rectangle(left, (int)Math.Floor(top - 10), width,
                Math.Max(29, (int)Math.Ceiling(bottom - top + 20)));
            using (var path = RoundedRectangle(card, 14))
            using (var shade = new SolidBrush(Color.FromArgb(255, 13, 17, 28)))
            {
                var smoothing = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.None;
                g.FillPath(shade, path);
                g.SmoothingMode = smoothing;
            }
        }

        private void DrawLyricPanel(Graphics g, RectangleF content, int left, int width,
            float mainHeight, float subHeight, float gap, string line1, string line2,
            string preview, float opacity, float offsetY)
        {
            if (opacity <= 0 || (string.IsNullOrWhiteSpace(line1) &&
                string.IsNullOrWhiteSpace(line2) && string.IsNullOrWhiteSpace(preview))) return;
            var layout = LyricCardLayout.Create(content, mainHeight, subHeight, gap,
                !string.IsNullOrWhiteSpace(line2), !string.IsNullOrWhiteSpace(preview),
                offsetY);
            using (var path = RoundedRectangle(new Rectangle(left,
                       (int)Math.Floor(layout.Bounds.Top - 9), width,
                       Math.Max(29, (int)Math.Ceiling(layout.Bounds.Height + 18))), 14))
            using (var shade = new SolidBrush(Color.FromArgb((int)(128 * opacity), 10, 13, 27)))
            using (var outline = new Pen(Color.FromArgb((int)(56 * opacity), _palette.Border)))
            {
                g.FillPath(shade, path);
                g.DrawPath(outline, path);
            }
        }

        private void DrawAlbumArt(Graphics g, RectangleF area)
        {
            var rect = Rectangle.Round(area);
            using (var path = RoundedRectangle(rect, 10))
            using (var background = new SolidBrush(Color.FromArgb(
                       _settings.TransparentCanvas ? 255 : 115, 9, 12, 22)))
            {
                var smoothing = g.SmoothingMode;
                if (_settings.TransparentCanvas) g.SmoothingMode = SmoothingMode.None;
                g.FillPath(background, path);
                if (_albumArtwork != null)
                {
                    var clip = g.Save();
                    g.SetClip(path);
                    g.DrawImage(_albumArtwork, rect);
                    g.Restore(clip);
                }
                else
                {
                    var middle = new PointF(rect.Left + rect.Width / 2f,
                        rect.Top + rect.Height / 2f);
                    var radius = Math.Min(rect.Width, rect.Height) * 0.27f;
                    var accent = _settings.TransparentCanvas ? Color.White : _palette.Border;
                    using (var line = new Pen(Color.FromArgb(80, accent), 2f))
                    using (var centre = new SolidBrush(Color.FromArgb(90, accent)))
                    {
                        g.DrawEllipse(line, middle.X - radius, middle.Y - radius,
                            radius * 2, radius * 2);
                        g.FillEllipse(centre, middle.X - 4, middle.Y - 4, 8, 8);
                    }
                }
                if (!_settings.TransparentCanvas)
                    using (var border = new Pen(Color.FromArgb(110, _palette.Border)))
                        g.DrawPath(border, path);
                g.SmoothingMode = smoothing;
            }
        }

        private void DrawSongTitle(Graphics g, Rectangle bounds)
        {
            if (!_settings.ShowSongTitle || string.IsNullOrWhiteSpace(_songTitle)) return;
            var text = _songTitle.Trim();
            if (!string.IsNullOrWhiteSpace(_songArtist)) text += "  ·  " + _songArtist.Trim();
            // Centre on the window and its playback controls. Leave room for
            // the regular-mode actions on the right; narrow windows put those
            // actions in the flyout and can give the title more space.
            var titleWidth = Math.Min(460, Math.Max(1, bounds.Width -
                (_settings.TransparentCanvas || bounds.Width < 700 ? 160 : 500)));
            var titleArea = new RectangleF((bounds.Width - titleWidth) / 2f,
                _settings.TransparentCanvas ? (bounds.Height < 260 ? 18 : 26) : 7,
                titleWidth, 29);
            if (_settings.TransparentCanvas)
                using (var path = RoundedRectangle(Rectangle.Round(titleArea), 10))
                using (var shade = new SolidBrush(Color.FromArgb(255, 13, 17, 28)))
                {
                    var smoothing = g.SmoothingMode;
                    g.SmoothingMode = SmoothingMode.None;
                    g.FillPath(shade, path);
                    g.SmoothingMode = smoothing;
                }
            using (var font = new Font("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (var brush = new SolidBrush(Color.FromArgb(185, 234, 235, 242)))
            using (var format = new StringFormat(StringFormatFlags.NoWrap)
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center,
                       Trimming = StringTrimming.EllipsisCharacter
                   })
                g.DrawString(text, font, brush, titleArea, format);
        }

        public void ShowEndOfQueue()
        {
            _queueEnded = true;
            _queueNoticeText = "That's the end of the queue ♪";
            _queueNoticeStarted = Stopwatch.GetTimestamp();
            Invalidate();
        }

        private void ShowQueueFeedback(string message)
        {
            _queueEnded = false;
            _queueNoticeText = message;
            _queueNoticeStarted = Stopwatch.GetTimestamp();
            Invalidate();
        }

        private void DrawQueueNotice(Graphics g, RectangleF content)
        {
            if (!HasQueueNotice) return;
            var elapsed = (float)((Stopwatch.GetTimestamp() - _queueNoticeStarted) *
                1000.0 / Stopwatch.Frequency);
            var fade = Math.Max(0f, Math.Min(1f, (QueueNoticeMs - elapsed) / 450f));
            var width = Math.Min(360f, Math.Max(50f, content.Width - 12f));
            var notice = new Rectangle((int)((ClientSize.Width - width) / 2f),
                (int)(content.Top + (content.Height - 40f) / 2f), (int)width, 40);
            using (var path = RoundedRectangle(notice, 19))
            using (var background = new SolidBrush(Color.FromArgb(
                       _settings.TransparentCanvas ? 255 : (int)(230 * fade), 19, 23, 39)))
            using (var border = new Pen(Color.FromArgb((int)(140 * fade),
                       _settings.TransparentCanvas ? Color.LightGray : _palette.Border)))
            using (var font = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point))
            using (var text = new SolidBrush(Color.FromArgb((int)(240 * fade), 245, 245, 250)))
            using (var format = new StringFormat(StringFormatFlags.NoWrap)
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center,
                       Trimming = StringTrimming.EllipsisCharacter
                   })
            {
                var smoothing = g.SmoothingMode;
                if (_settings.TransparentCanvas) g.SmoothingMode = SmoothingMode.None;
                g.FillPath(background, path);
                if (!_settings.TransparentCanvas) g.DrawPath(border, path);
                g.SmoothingMode = smoothing;
                g.DrawString(_queueNoticeText, font, text, notice, format);
            }
        }

        private void DrawTransport(Graphics g, Rectangle bounds)
        {
            _previousButton = _playButton = _nextButton = Rectangle.Empty;
            if (!_settings.ShowTransportControls) return;
            var y = bounds.Bottom - (_settings.TransparentCanvas ?
                (bounds.Height < 260 ? 53 : 65) : 43);
            var center = bounds.Width / 2;
            _previousButton = new Rectangle(center - 70, y + 2, 34, 34);
            _playButton = new Rectangle(center - 19, y, 38, 38);
            _nextButton = new Rectangle(center + 36, y + 2, 34, 34);
            using (var path = RoundedRectangle(new Rectangle(center - 92, y - 5, 184, 49), 22))
            using (var shade = new SolidBrush(Color.FromArgb(
                       _settings.TransparentCanvas ? 255 : 93, 8, 11, 23)))
            {
                var smoothing = g.SmoothingMode;
                if (_settings.TransparentCanvas) g.SmoothingMode = SmoothingMode.None;
                g.FillPath(shade, path);
                g.SmoothingMode = smoothing;
            }

            DrawControlButton(g, _previousButton, "previous");
            DrawControlButton(g, _playButton, "play");
            DrawControlButton(g, _nextButton, "next");
        }

        private void DrawControlButton(Graphics g, Rectangle area, string action)
        {
            var hovered = _hoverButton == action;
            using (var background = new SolidBrush(Color.FromArgb(hovered ? 118 : 65,
                       _settings.TransparentCanvas ? Color.FromArgb(167, 185, 206) :
                       _palette.Border)))
            using (var symbol = new SolidBrush(Color.FromArgb(hovered ? 255 : 215,
                       Color.White)))
            {
                g.FillEllipse(background, area);
                var middle = area.Top + area.Height / 2f;
                var centerX = area.Left + area.Width / 2f;
                if (action == "play")
                {
                    if (_playState == Plugin.PlayState.Playing)
                    {
                        g.FillRectangle(symbol, area.Left + 12, middle - 8, 5, 16);
                        g.FillRectangle(symbol, area.Left + 21, middle - 8, 5, 16);
                    }
                    else
                        g.FillPolygon(symbol, new[]
                        {
                            new PointF(area.Left + 14, middle - 9),
                            new PointF(area.Left + 14, middle + 9),
                            new PointF(area.Left + 26, middle)
                        });
                }
                else if (action == "previous")
                {
                    g.FillRectangle(symbol, centerX - 10, middle - 7, 2, 14);
                    g.FillPolygon(symbol, new[]
                    {
                        new PointF(centerX - 7, middle),
                        new PointF(centerX + 2, middle - 8),
                        new PointF(centerX + 2, middle + 8)
                    });
                    g.FillPolygon(symbol, new[]
                    {
                        new PointF(centerX + 1, middle),
                        new PointF(centerX + 10, middle - 8),
                        new PointF(centerX + 10, middle + 8)
                    });
                }
                else
                {
                    g.FillPolygon(symbol, new[]
                    {
                        new PointF(centerX - 10, middle - 8),
                        new PointF(centerX - 10, middle + 8),
                        new PointF(centerX - 1, middle)
                    });
                    g.FillPolygon(symbol, new[]
                    {
                        new PointF(centerX - 2, middle - 8),
                        new PointF(centerX - 2, middle + 8),
                        new PointF(centerX + 7, middle)
                    });
                    g.FillRectangle(symbol, centerX + 8, middle - 7, 2, 14);
                }
            }
        }

        private void DrawMenuButton(Graphics g, Rectangle bounds)
        {
            _menuButton = new Rectangle(bounds.Right - 38, 7, 29, 29);
            using (var path = RoundedRectangle(_menuButton, 8))
            using (var shade = new SolidBrush(Color.FromArgb(
                       _settings.TransparentCanvas ? 255 :
                       _hoverButton == "menu" ? 99 : 54, 14, 18, 32)))
            using (var pen = new Pen(Color.FromArgb(175, 226, 228, 237), 1.6f))
            {
                var smoothing = g.SmoothingMode;
                if (_settings.TransparentCanvas) g.SmoothingMode = SmoothingMode.None;
                g.FillPath(shade, path);
                g.SmoothingMode = smoothing;
                var left = _menuButton.Left + 7;
                for (var i = 0; i < 3; i++)
                {
                    var y = _menuButton.Top + 9 + i * 5;
                    g.DrawLine(pen, left, y, left + 15, y);
                }
            }
        }

        private void DrawTimingButton(Graphics g, Rectangle bounds)
        {
            _timingButton = new Rectangle(bounds.Right - 116, 7, 70, 29);
            var active = _timingEditor != null && !_timingEditor.IsDisposed;
            using (var path = RoundedRectangle(_timingButton, 8))
            using (var shade = new SolidBrush(Color.FromArgb(_settings.TransparentCanvas ? 235 : active ? 150 :
                       _hoverButton == "timing" ? 100 : 54, 20, 38, 58)))
            using (var pen = new Pen(Color.FromArgb(active ? 220 : 135, _palette.Border)))
            using (var font = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (var textBrush = new SolidBrush(Color.FromArgb(230, 238, 242, 249)))
            using (var format = new StringFormat
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center
                   })
            {
                g.FillPath(shade, path);
                g.DrawPath(pen, path);
                g.DrawString("TIMING", font, textBrush, _timingButton, format);
            }
        }

        private void DrawLrcButton(Graphics g, Rectangle bounds)
        {
            _lrcButton = new Rectangle(bounds.Right - 198, 7, 74, 29);
            var active = _lrcPicker != null && !_lrcPicker.IsDisposed;
            using (var path = RoundedRectangle(_lrcButton, 8))
            using (var shade = new SolidBrush(Color.FromArgb(_settings.TransparentCanvas ? 235 : active ? 150 :
                       _hoverButton == "lrclib" ? 100 : 54, 20, 38, 58)))
            using (var pen = new Pen(Color.FromArgb(active ? 220 : 135, _palette.Border)))
            using (var font = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.FromArgb(230, 238, 242, 249)))
            using (var format = new StringFormat
                   { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.FillPath(shade, path);
                g.DrawPath(pen, path);
                g.DrawString("LRCLIB", font, brush, _lrcButton, format);
            }
        }

        private void DrawBackgroundButton(Graphics g, Rectangle bounds)
        {
            _backgroundButton = new Rectangle(bounds.Right -
                (_settings.TransparentCanvas || bounds.Width < 700 ? 78 : 238), 7, 32, 29);
            using (var path = RoundedRectangle(_backgroundButton, 8))
            using (var shade = new SolidBrush(Color.FromArgb(
                       _settings.TransparentCanvas ? 255 : 72, 20, 38, 58)))
            using (var border = new Pen(Color.FromArgb(150,
                       _settings.TransparentCanvas ? Color.LightGray : _palette.Border)))
            using (var font = new Font("Segoe UI", 8f, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            using (var format = new StringFormat
                   { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                var smoothing = g.SmoothingMode;
                if (_settings.TransparentCanvas) g.SmoothingMode = SmoothingMode.None;
                g.FillPath(shade, path);
                if (!_settings.TransparentCanvas) g.DrawPath(border, path);
                g.SmoothingMode = smoothing;
                g.DrawString("BG", font, brush, _backgroundButton, format);
            }
        }

        private void DrawPartyButton(Graphics g, Rectangle bounds)
        {
            // Leave a gap before the centred transport controls at the
            // smallest supported window width.
            var partyWidth = Math.Min(105, Math.Max(65, bounds.Width / 2 - 116));
            _partyButton = new Rectangle(16, bounds.Bottom -
                (_settings.TransparentCanvas ? (bounds.Height < 260 ? 50 : 62) : 42),
                partyWidth, 38);
            using (var path = RoundedRectangle(_partyButton, 12))
            using (var shade = new SolidBrush(Color.FromArgb(
                       _settings.TransparentCanvas ? 255 : _settings.PartyMode ? 175 :
                       _hoverButton == "party" ? 112 : 84, 18, 27, 46)))
            using (var border = new Pen(Color.FromArgb(
                       _settings.PartyMode ? 220 : 115,
                       _settings.TransparentCanvas ? Color.White : _palette.Border)))
            using (var font = new Font("Segoe UI", 8.3f, FontStyle.Bold, GraphicsUnit.Point))
            using (var smallFont = new Font("Segoe UI", 7f, FontStyle.Regular, GraphicsUnit.Point))
            using (var brush = new SolidBrush(Color.FromArgb(242, Color.White)))
            using (var format = new StringFormat
                   { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                var smoothing = g.SmoothingMode;
                if (_settings.TransparentCanvas) g.SmoothingMode = SmoothingMode.None;
                g.FillPath(shade, path);
                if (!_settings.TransparentCanvas) g.DrawPath(border, path);
                g.SmoothingMode = smoothing;
                if (_settings.PartyMode)
                {
                    g.DrawString("PARTY", font, brush, new Rectangle(
                        _partyButton.Left, _partyButton.Top + 2, _partyButton.Width, 17), format);
                    var source = _partyTempoSource == PartyTempoSource.Manual ? "SET " :
                        _partyTempoSource == PartyTempoSource.Saved ? "SAVED " :
                        _partyTempoSource == PartyTempoSource.Online ? "WEB " : "TAG ";
                    var tempo = _partyBpm > 0 ? source +
                        _partyBpm.ToString("0.##", CultureInfo.InvariantCulture) :
                        PartyOnlineEnabled ?
                            _partyOnlineStatus == PartyOnlineStatus.Searching ? "SEARCHING..." :
                            _partyOnlineStatus == PartyOnlineStatus.Error ? "API ERROR" :
                            "NO MATCH" :
                        _partyBeat.Bpm > 0 ? "AUTO " +
                            _partyBeat.Bpm.ToString("0.#", CultureInfo.InvariantCulture) :
                        _partySpectrumMisses >= 30 ? "NO SIGNAL" : "LISTENING...";
                    g.DrawString(tempo, smallFont, brush, new Rectangle(
                        _partyButton.Left, _partyButton.Top + 18, _partyButton.Width, 16), format);
                }
                else g.DrawString("PARTY", font, brush, _partyButton, format);
            }
        }

        private void DrawResizeGrip(Graphics g, Rectangle bounds)
        {
            _resizeGrip = Rectangle.Empty;
            if (!_settings.TransparentCanvas || WindowState == FormWindowState.Maximized) return;
            _resizeGrip = new Rectangle(bounds.Right - 27, bounds.Bottom - 27, 20, 20);
            using (var background = new SolidBrush(Color.FromArgb(255, 13, 17, 28)))
            using (var pen = new Pen(Color.FromArgb(142, 170, 182, 199), 1.2f))
            {
                g.FillRectangle(background, _resizeGrip);
                for (var i = 0; i < 3; i++)
                    g.DrawLine(pen, _resizeGrip.Right - 5 - i * 5,
                        _resizeGrip.Bottom - 4,
                        _resizeGrip.Right - 4, _resizeGrip.Bottom - 5 - i * 5);
            }
        }

        private void OpenLrcLibPicker()
        {
            if (_lrcPicker != null && !_lrcPicker.IsDisposed)
            {
                _lrcPicker.BringToFront();
                return;
            }
            FrmLrcLibPicker picker = null;
            try
            {
                var trackUrl = _musicBee.NowPlaying_GetFileUrl();
                if (string.IsNullOrWhiteSpace(trackUrl))
                {
                    MessageBox.Show(this, "Play a song first.", "Find timed lyrics",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var title = _musicBee.NowPlaying_GetFileTag(Plugin.MetaDataType.TrackTitle) ?? "";
                var artist = _musicBee.NowPlaying_GetFileTag(Plugin.MetaDataType.Artist) ?? "";
                if (string.IsNullOrWhiteSpace(title))
                {
                    MessageBox.Show(this, "MusicBee needs a song title to search LRCLIB.",
                        "Find timed lyrics", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var duration = _musicBee.NowPlaying_GetDuration?.Invoke() ?? 0;
                var tag = _musicBee.Library_GetFileTag?.Invoke(trackUrl,
                    Plugin.MetaDataType.Lyrics);
                picker = new FrmLrcLibPicker(_musicBee, trackUrl, title, artist,
                    duration, tag, _savedTiming);
                _lrcPicker = picker;
                picker.FormClosed += (sender, args) =>
                {
                    if (ReferenceEquals(_lrcPicker, picker)) _lrcPicker = null;
                    if (!IsDisposed) Invalidate();
                };
                picker.Show(this);
                Invalidate();
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_lrcPicker, picker)) _lrcPicker = null;
                picker?.Dispose();
                MessageBox.Show(this, "Could not open LRCLIB search: " + ex.Message,
                    "Find timed lyrics", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenEnglishImporter()
        {
            if (_englishImporter != null && !_englishImporter.IsDisposed)
            {
                _englishImporter.BringToFront();
                return;
            }
            FrmEnglishImport importer = null;
            try
            {
                var trackUrl = _musicBee.NowPlaying_GetFileUrl();
                if (string.IsNullOrWhiteSpace(trackUrl))
                {
                    MessageBox.Show(this, "Play a song with timed romaji lyrics first.",
                        "Add English meaning", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
                var tag = _musicBee.Library_GetFileTag?.Invoke(trackUrl,
                    Plugin.MetaDataType.Lyrics);
                var source = string.IsNullOrWhiteSpace(tag) ?
                    _musicBee.NowPlaying_GetLyrics() : tag;
                var lyrics = LyricParser.ParseLyric(source);
                if (lyrics == null || lyrics.Entries.Count == 0)
                {
                    MessageBox.Show(this,
                        "This song needs timed lyrics in MusicBee before English can follow them.",
                        "Add English meaning", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
                importer = new FrmEnglishImport(_musicBee, _englishStore,
                    trackUrl, _songTitle, _songArtist, lyrics.Entries, _englishSaved);
                _englishImporter = importer;
                importer.FormClosed += (sender, args) =>
                {
                    if (ReferenceEquals(_englishImporter, importer))
                        _englishImporter = null;
                };
                importer.Show(this);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_englishImporter, importer)) _englishImporter = null;
                importer?.Dispose();
                MessageBox.Show(this, "Could not open English import: " + ex.Message,
                    "Add English meaning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenTimingEditor()
        {
            if (_timingCreator != null && !_timingCreator.IsDisposed)
            {
                _timingCreator.BringToFront();
                return;
            }
            if (_timingEditor != null && !_timingEditor.IsDisposed)
            {
                _timingEditor.BringToFront();
                return;
            }
            FrmTimingEditor editor = null;
            try
            {
                var trackUrl = _musicBee.NowPlaying_GetFileUrl();
                if (string.IsNullOrWhiteSpace(trackUrl))
                {
                    MessageBox.Show(this, "Play a song with timed lyrics first.",
                        "Edit lyric timing", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var tag = _musicBee.Library_GetFileTag?.Invoke(trackUrl,
                    Plugin.MetaDataType.Lyrics);
                var source = string.IsNullOrWhiteSpace(tag) ?
                    _musicBee.NowPlaying_GetLyrics() : tag;
                LrcTimingDocument document;
                if (!LrcTimingDocument.TryCreate(source, out document))
                {
                    UntimedTimingDocument plain;
                    if (!UntimedTimingDocument.TryCreate(source, out plain))
                    {
                        MessageBox.Show(this, "Add plain lyrics to this song's MusicBee Lyrics field (or import a plain LRCLIB result) first.",
                            "Create lyric timing", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    var creator = new FrmTimingCreator(_musicBee, trackUrl, _songTitle,
                        tag, plain, _previewTiming, _cancelTiming, _savedTiming);
                    _timingCreator = creator;
                    creator.FormClosed += (sender, args) =>
                    {
                        if (ReferenceEquals(_timingCreator, creator)) _timingCreator = null;
                        if (!IsDisposed) Invalidate();
                    };
                    creator.Show(this);
                    Invalidate();
                    return;
                }
                editor = new FrmTimingEditor(_musicBee, trackUrl, _songTitle,
                    tag, document, _previewTiming, _cancelTiming, _savedTiming);
                _timingEditor = editor;
                editor.FormClosed += (sender, args) =>
                {
                    if (ReferenceEquals(_timingEditor, editor)) _timingEditor = null;
                    if (!IsDisposed) Invalidate();
                };
                editor.Show(this);
                Invalidate();
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_timingEditor, editor)) _timingEditor = null;
                editor?.Dispose();
                MessageBox.Show(this, "Could not open the timing editor: " + ex.Message,
                    "Edit lyric timing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_settings.TransparentCanvas && _resizeGrip.Contains(e.Location))
            {
                Cursor = Cursors.SizeNWSE;
                return;
            }
            string queueHit = null;
            foreach (var queueRow in _queueHits)
                if (queueRow.Area.Contains(e.Location))
                {
                    queueHit = queueRow.Track.Offset.ToString();
                    break;
                }
            var hit = _menuButton.Contains(e.Location) ? "menu" :
                _backgroundButton.Contains(e.Location) ? "background" :
                _partyButton.Contains(e.Location) ? "party" :
                _queueTab.Contains(e.Location) ? "queue-tab" :
                _timingButton.Contains(e.Location) ? "timing" :
                _lrcButton.Contains(e.Location) ? "lrclib" :
                _previousButton.Contains(e.Location) ? "previous" :
                _playButton.Contains(e.Location) ? "play" :
                _nextButton.Contains(e.Location) ? "next" :
                !_queueUpButton.IsEmpty && _queueUpButton.Contains(e.Location) ? "queue-up" :
                !_queueDownButton.IsEmpty && _queueDownButton.Contains(e.Location) ? "queue-down" :
                queueHit == null ? null : "queue";
            if (hit == _hoverButton && queueHit == _hoverQueue) return;
            _hoverButton = hit;
            _hoverQueue = queueHit;
            Cursor = hit == null ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x84 && _settings != null &&
                _settings.TransparentCanvas &&
                _resizeGrip.Contains(PointToClient(System.Windows.Forms.Cursor.Position)))
                message.Result = new IntPtr(17); // HTBOTTOMRIGHT
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_settings.TransparentCanvas || e.Button != MouseButtons.Left ||
                _resizeGrip.Contains(e.Location) || _queueCard.Contains(e.Location) ||
                _queueTab.Contains(e.Location) ||
                _menuButton.Contains(e.Location) ||
                _backgroundButton.Contains(e.Location) ||
                _partyButton.Contains(e.Location) ||
                _previousButton.Contains(e.Location) ||
                _playButton.Contains(e.Location) ||
                _nextButton.Contains(e.Location)) return;
            // The title bar is hidden, so the title, artwork and lyric card
            // act as drag surfaces in the borderless layout.
            ReleaseCapture();
            SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); // HTCAPTION
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverButton = null;
            _hoverQueue = null;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_queueTab.Contains(e.Location) && !_queueExpanded)
            {
                _queueExpanded = true;
                Invalidate();
            }
            else if (_queueCard.Contains(e.Location) || _queueTab.Contains(e.Location))
                ScrollQueue(-Math.Sign(e.Delta) * Math.Max(1, Math.Abs(e.Delta) / 120));
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (_queueTab.Contains(e.Location))
            {
                _queueExpanded = !_queueExpanded;
                Invalidate();
                return;
            }
            if (_queueUpButton.Contains(e.Location)) { ScrollQueue(-1); return; }
            if (_queueDownButton.Contains(e.Location)) { ScrollQueue(1); return; }
            foreach (var hit in _queueHits)
            {
                if (!hit.Area.Contains(e.Location)) continue;
                string error;
                if (QueueNavigation.TryPlayQueuedTrack(_musicBee, hit.Track, out error))
                {
                    _queueEnded = false;
                    _queueNoticeStarted = 0;
                    RefreshQueue();
                }
                else ShowQueueFeedback(error);
                return;
            }
            if (_menuButton.Contains(e.Location))
            {
                _flyoutMenu.Show(this, new Point(_menuButton.Right, _menuButton.Bottom),
                    ToolStripDropDownDirection.BelowLeft);
                return;
            }
            if (_backgroundButton.Contains(e.Location))
            {
                _settings.TransparentCanvas = !_settings.TransparentCanvas;
                ApplyTransparency();
                _settingsChanged?.Invoke(_settings);
                return;
            }
            if (_partyButton.Contains(e.Location))
            {
                _settings.PartyMode = !_settings.PartyMode;
                if (_settings.PartyMode) StartPartyOnlineLookup(true);
                else CancelPartyLookup();
                UpdatePartyDancers();
                _settingsChanged?.Invoke(_settings);
                Invalidate();
                return;
            }
            if (_timingButton.Contains(e.Location))
            {
                OpenTimingEditor();
                return;
            }
            if (_lrcButton.Contains(e.Location))
            {
                OpenLrcLibPicker();
                return;
            }
            try
            {
                if (_previousButton.Contains(e.Location)) _musicBee.Player_PlayPreviousTrack();
                else if (_playButton.Contains(e.Location)) _musicBee.Player_PlayPause();
                else if (_nextButton.Contains(e.Location))
                {
                    if (QueueNavigation.TryPlayNext(_musicBee))
                        _queueNoticeStarted = 0;
                    else
                        ShowEndOfQueue();
                }
            }
            catch (Exception)
            {
                // MusicBee may be closing or changing tracks between the queue
                // check and the action; leave the window usable either way.
                if (_nextButton.Contains(e.Location)) ShowEndOfQueue();
            }
            RefreshPlayState();
            Invalidate();
        }

        private void DrawLyricGroup(Graphics g, RectangleF content, float mainHeight,
            float subHeight, float gap, float scale, string line1, string line2,
            string nextLine, float opacity, float offsetY, bool drawMain, bool drawPreview)
        {
            if (opacity <= 0) return;
            var layout = LyricCardLayout.Create(content, mainHeight, subHeight, gap,
                !string.IsNullOrWhiteSpace(line2),
                !string.IsNullOrWhiteSpace(nextLine), offsetY);
            var fontSize = (_settings.Font ?? SystemFonts.DefaultFont).SizeInPoints * scale;

            if (!string.IsNullOrWhiteSpace(line2))
                DrawLine(g, line2, layout.English, fontSize * 0.68f,
                    (int)(225 * opacity));
            if (drawMain)
                DrawLine(g, line1, layout.Main, fontSize, (int)(255 * opacity));
            if (drawPreview && !string.IsNullOrWhiteSpace(nextLine))
                DrawLine(g, nextLine, layout.Preview, fontSize * 0.63f,
                    (int)(145 * opacity));
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
            lyric = lyric?.Trim();
            if (string.IsNullOrEmpty(lyric) || alpha <= 0) return;
            var geometry = GetTextGeometry(g, lyric, area, desiredPoints);
            if (geometry == null) return;
            using (var shadow = new SolidBrush(Color.FromArgb(alpha * 2 / 3, 0, 0, 0)))
            using (var outline = new Pen(Color.FromArgb(alpha, _settings.BorderColor),
                       Math.Max(1.5f, Math.Min(3f, geometry.FittedPoints / 20f))))
            using (var foreground = CreateTextBrush(geometry.Bounds, alpha))
            {
                var state = g.Save();
                try
                {
                    // The glyph path is stored at the origin. Movement during
                    // a lyric transition needs only a graphics translation.
                    // Centre the actual glyph bounds. The parser can retain
                    // spaces after LRC timestamps, and font side bearings
                    // otherwise shift some lines within the same card.
                    var centeredX = (area.Width - geometry.Bounds.Width) / 2f -
                        geometry.Bounds.Left;
                    g.TranslateTransform(area.Left + centeredX, area.Top);
                    g.TranslateTransform(1f, 2f);
                    g.FillPath(shadow, geometry.Path);
                    g.TranslateTransform(-1f, -2f);
                    outline.LineJoin = LineJoin.Round;
                    g.DrawPath(outline, geometry.Path);
                    g.FillPath(foreground, geometry.Path);
                }
                finally { g.Restore(state); }
            }
        }

        private TextGeometry GetTextGeometry(Graphics g, string lyric,
            RectangleF area, float desiredPoints)
        {
            var selected = _settings.Font ?? SystemFonts.DefaultFont;
            var family = selected.FontFamily.Name;
            // Text shaping is the expensive part of dragging the sizing edge.
            // Reuse a path for nearby sizes while the native resize loop runs.
            var width = _movingOrResizing ? (float)Math.Max(8,
                Math.Round(area.Width / 8f) * 8) : area.Width;
            var height = _movingOrResizing ? (float)Math.Max(4,
                Math.Round(area.Height / 4f) * 4) : area.Height;
            var points = _movingOrResizing ?
                (float)(Math.Round(desiredPoints * 2) / 2) : desiredPoints;
            foreach (var cached in _textGeometries)
                if (cached.Text == lyric && cached.FontFamily == family &&
                    cached.FontStyle == selected.Style &&
                    cached.DesiredPoints == points &&
                    cached.Width == width && cached.Height == height &&
                    cached.DpiY == g.DpiY)
                    return cached;

            var availableWidth = Math.Max(8f, width - 8f);
            var fit = LyricTextLayout.Fit(g, lyric, selected, points, width, height);
            var size = fit.Points;
            using (var format = new StringFormat(StringFormatFlags.NoWrap)
                   {
                       Alignment = StringAlignment.Center,
                       LineAlignment = StringAlignment.Center,
                       Trimming = fit.Lines == 1 ?
                           StringTrimming.EllipsisCharacter : StringTrimming.None
                   })
            {
                using (var fitted = new Font(selected.FontFamily, size,
                           selected.Style, GraphicsUnit.Point))
                {
                    var path = new GraphicsPath();
                    path.AddString(fit.Text, fitted.FontFamily, (int)fitted.Style,
                        fitted.SizeInPoints * g.DpiY / 72f,
                        new RectangleF(0, 0, width, height), format);
                    if (path.PointCount == 0)
                    {
                        path.Dispose();
                        return null;
                    }
                    var glyphBounds = path.GetBounds();
                    // MeasureString includes different font padding from the
                    // actual outline. Check the outline too before caching it.
                    if ((glyphBounds.Width > availableWidth ||
                         glyphBounds.Height > height - 2f) && size > 10f)
                    {
                        size = Math.Max(10f, size * Math.Min(
                            availableWidth / Math.Max(1f, glyphBounds.Width),
                            Math.Max(1f, height - 2f) / Math.Max(1f, glyphBounds.Height)));
                        path.Reset();
                        using (var narrower = new Font(selected.FontFamily, size,
                                   selected.Style, GraphicsUnit.Point))
                            path.AddString(fit.Text, narrower.FontFamily, (int)narrower.Style,
                                narrower.SizeInPoints * g.DpiY / 72f,
                                new RectangleF(0, 0, width, height), format);
                        glyphBounds = path.GetBounds();
                    }
                    var geometry = new TextGeometry
                    {
                        Text = lyric, FontFamily = family, FontStyle = selected.Style,
                        DesiredPoints = points, Width = width, Height = height,
                        DpiY = g.DpiY, FittedPoints = size, Path = path,
                        Bounds = glyphBounds
                    };
                    _textGeometries.Add(geometry);
                    if (_textGeometries.Count > 20)
                    {
                        _textGeometries[0].Dispose();
                        _textGeometries.RemoveAt(0);
                    }
                    return geometry;
                }
            }
        }

        private void ClearTextGeometries()
        {
            foreach (var geometry in _textGeometries) geometry.Dispose();
            _textGeometries.Clear();
        }

        private Brush CreateTextBrush(RectangleF area, int alpha)
        {
            var first = Color.FromArgb(alpha, _settings.Color1);
            var second = Color.FromArgb(alpha, _settings.Color2);
            if (_settings.GradientType == (int)GradientType.NoGradient || area.Height < 2)
                return new SolidBrush(first);
            var gradient = new LinearGradientBrush(area, first, second,
                LinearGradientMode.Vertical);
            if (_settings.GradientType == (int)GradientType.TripleColor)
                gradient.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { first, second, first },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
            return gradient;
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
            if (disposing)
            {
                _animationDisposed = true;
                CancelPartyLookup();
                Interlocked.Increment(ref _artworkRequestId);
                _animationTimer?.Dispose();
                DisposePartyDancers();
                _albumArtwork?.Dispose();
                _backgroundCache?.Dispose();
                ClearTextGeometries();
                if (_timingEditor != null && !_timingEditor.IsDisposed)
                    _timingEditor.ForceClose();
                if (_timingCreator != null && !_timingCreator.IsDisposed)
                    _timingCreator.ForceClose();
                if (_lrcPicker != null && !_lrcPicker.IsDisposed)
                    _lrcPicker.Close();
                _flyoutMenu?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
