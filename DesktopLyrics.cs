using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Timers;
using System.Windows.Forms;
using Newtonsoft.Json;
using Exception = System.Exception;
using Timer = System.Timers.Timer;

namespace MusicBeePlugin
{
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    // ReSharper disable once ClassNeverInstantiated.Global
    public partial class Plugin
    {
        private const long UpdateIntervalMs = 100L;
        private const string SettingsFileName = "desktopLyrics.json";
        private const string SettingsFileName2 = "desktopLyrics_Window.set";

        // MB related
        private MusicBeeApiInterface _mbApiInterface;
        private readonly PluginInfo _about = new PluginInfo();
        private string SettingsPath => Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), SettingsFileName);
        private string SettingsPath2 => Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(), SettingsFileName2);
        // Customed
        private volatile SettingsObj _settings;
        private volatile IDesktopLyricsView _frmLyrics;
        private LyricsWindowThread _windowThread;
        private Control _musicBeeControl;
        private volatile bool _closing;
        private ToolStripMenuItem _visibilityMenuItem;
        private readonly PlaybackHistory _history = new PlaybackHistory();
        private Timer _timer;
        private LyricsController _lyricsCtrl;
        private EnglishTranslationStore _englishStore;
        private PartyTempoStore _partyTempoStore;
        private readonly object _lock = new object();
        private readonly object _settingsSaveLock = new object();
        private DateTime _missingLyricsSinceUtc = DateTime.MinValue;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            _closing = false;
            var versions = Assembly.GetExecutingAssembly().GetName().Version.ToString().Split('.');

            _mbApiInterface = new MusicBeeApiInterface();
            _mbApiInterface.Initialise(apiInterfacePtr);
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "KoreKara";
            _about.Description = "Display lyrics on your desktop!";
            _about.Author = "Charlie Jiang";
            _about.TargetApplication = "";   // current only applies to artwork, lyrics or instant messenger name that appears in the provider drop down selector or target Instant Messenger
            _about.Type = PluginType.General;
            _about.VersionMajor = short.Parse(versions[0]);  // your plugin version
            _about.VersionMinor = short.Parse(versions[1]);
            _about.Revision = short.Parse(versions[2]);
            _about.MinInterfaceVersion = MinInterfaceVersion;
            _about.MinApiRevision = MinApiRevision;
            _about.ReceiveNotifications = ReceiveNotificationFlags.PlayerEvents;
            _about.ConfigurationPanelHeight = 0;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function

            _englishStore = new EnglishTranslationStore(
                _mbApiInterface.Setting_GetPersistentStoragePath());
            _partyTempoStore = new PartyTempoStore(
                _mbApiInterface.Setting_GetPersistentStoragePath());
            _lyricsCtrl = new LyricsController(_mbApiInterface, _englishStore);
            return _about;
        }

        // ReSharper disable once UnusedParameter.Global
        private FrmSettings _settingsForm;
        public bool Configure(IntPtr panelHandle)
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Activate();
                return true;
            }
            var settingsForm = new FrmSettings(_settings);
            _settingsForm = settingsForm;
            settingsForm.TopMost = _frmLyrics is FrmLyricsWindow;
            settingsForm.ShowWindowRequested += (sender, args) => ShowLyricsWindow();
            settingsForm.SettingsChanged += (sender, settings) =>
            {
                var view = _frmLyrics;
                if (view == null) return;
                if (!view.Form.IsDisposed) view.Form.BeginInvoke(new Action(() => { if (!view.Form.IsDisposed) view.UpdateFromSettings(settings); }));
            };
            try { settingsForm.ShowDialog(); }
            finally { _settingsForm = null; settingsForm.Dispose(); }
            SaveSettings(_settings);
            LyricParser.PreserveSlash = _settings.PreserveSlash;
            _lyricsCtrl.NextLineWhenNoTranslation = _settings.NextLineWhenNoTranslation;
            _lyricsCtrl.ShowTranslation = _settings.ShowTranslation;
            var activeView = _frmLyrics;
            activeView?.Form.BeginInvoke(new Action(() =>
            {
                if (!activeView.Form.IsDisposed) activeView.UpdateFromSettings(_settings);
            }));
            return true;
        }

        private void SaveSettings(SettingsObj settings)
        {
            lock (_settingsSaveLock)
                File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(settings));
        }

        public void SaveSettings()
        {
            // Still, we've saved all settings already.
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        // ReSharper disable once UnusedParameter.Global
        public void Close(PluginCloseReason reason)
        {
            _closing = true;
            _timer?.Stop();
            _frmLyrics = null;
            _windowThread?.Dispose();
            _windowThread = null;
            SaveSettings(_settings);
        }
        
        public void Uninstall()
        {
            if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            if (File.Exists(SettingsPath2)) File.Delete(SettingsPath2); 
        }

        // ReSharper disable once UnusedParameter.Global
        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            // perform some action depending on the notification type
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (type)
            {
                case NotificationType.PluginStartup:
                    // while (!Debugger.IsAttached) System.Threading.Thread.Sleep(1);
                    try
                    {
                        try
                        {
                            _settings = JsonConvert.DeserializeObject<SettingsObj>(File.ReadAllText(SettingsPath));
                        }
                        catch (Exception)
                        {
                            _settings = SettingsObj.GenerateDefault();
                        }

                        // Existing installations did not have a display mode preference.
                        // Switch them to the newly requested window once; subsequent
                        // choices in Settings or View are respected.
                        if (!_settings.CompactWindowPreferenceSet)
                        {
                            _settings.CompactWindow = true;
                            _settings.CompactWindowPreferenceSet = true;
                            SaveSettings(_settings);
                        }

                        // Earlier builds left the artwork feature unchecked. Enable
                        // it once on upgrade; the checkbox retains later choices.
                        if (!_settings.ArtworkColorsPreferenceSet)
                        {
                            _settings.UseArtworkColors = true;
                            _settings.ArtworkColorsPreferenceSet = true;
                            SaveSettings(_settings);
                        }

                        // Older versions saved a click on the window's X as a
                        // permanent hide. Restore those windows once on upgrade.
                        if (!_settings.WindowCloseRecoveryApplied)
                        {
                            _settings.HideOnStartup = false;
                            _settings.WindowCloseRecoveryApplied = true;
                            SaveSettings(_settings);
                        }

                        if (!_settings.WindowFeaturesPreferenceSet)
                        {
                            _settings.ShowTransportControls = true;
                            _settings.ShowSongTitle = true;
                            _settings.ShowAlbumArt = true;
                            _settings.ShowVisualizer = true;
                            _settings.WindowFeaturesPreferenceSet = true;
                            SaveSettings(_settings);
                        }

                        if (!_settings.SongQueuePreferenceSet)
                        {
                            _settings.ShowSongQueue = true;
                            _settings.SongQueuePreferenceSet = true;
                            SaveSettings(_settings);
                        }

                        if (!_settings.TranslationPreferenceSet)
                        {
                            _settings.ShowTranslation = true;
                            _settings.TranslationPreferenceSet = true;
                            SaveSettings(_settings);
                        }

                        LyricParser.PreserveSlash = _settings.PreserveSlash;
                        _lyricsCtrl.NextLineWhenNoTranslation = _settings.NextLineWhenNoTranslation;
                        _lyricsCtrl.ShowTranslation = _settings.ShowTranslation;

                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);

                        StartupMenuItem();
                        if (!_settings.HideOnStartup)
                            StartupForm();

                        if (_timer != null && _timer.Enabled) _timer.Enabled = false;
                        _timer = new Timer(UpdateIntervalMs) {AutoReset = false};
                        _timer.Elapsed += TimerTick;
                        _timer.Start();
                    }
                    catch (Exception e)
                    {
                        _mbApiInterface.MB_Trace(e.ToString());
                    }
                    break;
                case NotificationType.PlayStateChanged:
                    try { UpdatePlayState(_mbApiInterface.Player_GetPlayState()); }
                    catch (Exception e) { _mbApiInterface.MB_Trace(e.ToString()); }
                    break;
                case NotificationType.TrackChanged:
                    try
                    {
                        _history.Observe(_mbApiInterface.NowPlaying_GetFileUrl(),
                            _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.TrackTitle),
                            _mbApiInterface.NowPlaying_GetFileTag(MetaDataType.Artist));
                    }
                    catch (Exception) { /* MusicBee can change tracks mid-query. */ }
                    goto case NotificationType.NowPlayingArtworkReady;
                case NotificationType.NowPlayingArtworkReady:
                    var artworkView = _frmLyrics as FrmLyricsWindow;
                    if (artworkView != null && !artworkView.IsDisposed && artworkView.IsHandleCreated)
                        try { artworkView.BeginInvoke(new Action(() =>
                        {
                            if (!artworkView.IsDisposed) artworkView.RefreshArtwork(true);
                        })); }
                        catch (InvalidOperationException) { /* Window closed during a track change. */ }
                    break;
                case NotificationType.PlayingTracksChanged:
                case NotificationType.PlayingTracksQueueChanged:
#pragma warning disable 618
                case NotificationType.NowPlayingListChanged:
#pragma warning restore 618
                    var queueView = _frmLyrics as FrmLyricsWindow;
                    if (queueView != null && !queueView.IsDisposed && queueView.IsHandleCreated)
                        try { queueView.BeginInvoke(new Action(() =>
                        {
                            if (!queueView.IsDisposed) queueView.RefreshQueue();
                        })); }
                        catch (InvalidOperationException) { /* Window closed while queue changed. */ }
                    break;
                case NotificationType.NowPlayingListEnded:
                    var endedView = _frmLyrics as FrmLyricsWindow;
                    if (endedView != null && !endedView.IsDisposed && endedView.IsHandleCreated)
                        try { endedView.BeginInvoke(new Action(() =>
                        {
                            if (!endedView.IsDisposed) endedView.ShowEndOfQueue();
                        })); }
                        catch (InvalidOperationException) { /* Window closed as the queue ended. */ }
                    break;
                case NotificationType.NowPlayingLyricsReady:
                    try
                    {
                        UpdateLyrics(force: true);
                    }
                    catch (Exception e)
                    {
                        _mbApiInterface.MB_Trace(e.ToString());
                    }
                    break;
                case NotificationType.TagsChanged:
                    try
                    {
                        lock (_lock) _lyricsCtrl.InvalidateTag();
                        UpdateLyrics(force: true);
                    }
                    catch (Exception e)
                    {
                        _mbApiInterface.MB_Trace(e.ToString());
                    }
                    break;
            }
        }

        // Change form according to play state
        private void UpdatePlayState(PlayState state)
        {
            if (!_settings.AutoHide) return;
            var view = _frmLyrics;
            if (view == null || view.Form.IsDisposed || !view.Form.IsHandleCreated) return;
            view.Form.BeginInvoke(new Action(() =>
            {
                if (!ReferenceEquals(view, _frmLyrics) || view.Form.IsDisposed) return;
                if (view is FrmLyricsWindow window) window.UpdatePlaybackVisibility(state);
            }));
        }

        private void StartupMenuItem()
        {
            var menuItem = (ToolStripMenuItem) _mbApiInterface.MB_AddMenuItem(
                "mnuView/KoreKara", "Toggle KoreKara visibility.",
                ToggleLyrics);
            _visibilityMenuItem = menuItem;
            menuItem.Checked = !_settings.HideOnStartup;

            // Registered commands appear in MusicBee's Hotkeys and toolbar
            // command chooser, including the dialog in the user's screenshot.
            _mbApiInterface.MB_RegisterCommand?.Invoke(
                "View: Toggle KoreKara Window", ToggleLyrics);

            void ToggleLyrics(object sender, EventArgs args)
            {
                var main = Control.FromHandle(_mbApiInterface.MB_GetWindowHandle());
                if (main != null && main.InvokeRequired)
                {
                    main.BeginInvoke(new Action(() => ToggleLyrics(sender, args)));
                    return;
                }
                var view = _frmLyrics;
                if (view == null || view.Form.IsDisposed)
                    ShowLyricsWindow();
                else
                {
                    _frmLyrics = null;
                    _windowThread?.Dispose();
                    _windowThread = null;
                    _visibilityMenuItem.Checked = false;
                    _settings.HideOnStartup = true;
                    SaveSettings(_settings);
                }
            }

        }

        private void ShowLyricsWindow()
        {
            var view = _frmLyrics;
            _settings.HideOnStartup = false;
            if (_visibilityMenuItem != null) _visibilityMenuItem.Checked = true;
            if (view == null || view.Form.IsDisposed)
                StartupForm();
            else
                view.Form.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(_frmLyrics, view) || view.Form.IsDisposed) return;
                    view.Form.Show();
                    view.Form.BringToFront();
                }));
            SaveSettings(_settings);
        }

        private void PostToMusicBee(Action action)
        {
            var main = _musicBeeControl;
            if (_closing || main == null || main.IsDisposed) throw new ObjectDisposedException("MusicBee");
            if (main.InvokeRequired) main.BeginInvoke(action); else action();
        }

        private void StartupForm()
        {
            if (_closing) return;
            _musicBeeControl = Control.FromHandle(_mbApiInterface.MB_GetWindowHandle());
            var host = new LyricsWindowThread();
            _windowThread?.Dispose();
            _windowThread = host;
            _frmLyrics = null;
            host.Start(() => new FrmLyricsWindow(_settings,
                _mbApiInterface, _history, WindowSettingsChanged,
                () => PostToMusicBee(() => Configure(IntPtr.Zero)), PreviewTimingLyrics,
                CancelTimingPreview, TimingLyricsSaved, _englishStore,
                ImportedEnglishSaved, _partyTempoStore, PostToMusicBee), form =>
            {
                if (_closing || !ReferenceEquals(_windowThread, host)) { host.Dispose(); return; }
                var view = (IDesktopLyricsView)form;
                _frmLyrics = view;
                form.FormClosed += (sender, args) =>
                {
                    if (!ReferenceEquals(_frmLyrics, view)) return;
                    _frmLyrics = null;
                    if (!_closing) PostToMusicBee(() =>
                    {
                        if (_frmLyrics == null && _visibilityMenuItem != null) _visibilityMenuItem.Checked = false;
                    });
                };
                try { UpdateLyrics(force: true); }
                catch (Exception ex) { _mbApiInterface.MB_Trace(ex.ToString()); }
            }, ex =>
            {
                if (ReferenceEquals(_windowThread, host)) _frmLyrics = null;
                // Keep one bounded report if the private UI thread fails. Trace
                // output alone is not retained by every MusicBee installation.
                try
                {
                    var report = DateTime.UtcNow.ToString("O") + "\n" + ex;
                    File.WriteAllText(Path.Combine(_mbApiInterface.Setting_GetPersistentStoragePath(),
                        "DesktopLyrics-last-ui-error.log"), report.Substring(0, Math.Min(report.Length, 16384)));
                }
                catch (Exception) { }
                if (!_closing) _mbApiInterface.MB_Trace("KoreKara UI: " + ex);
            });
        }

        private void WindowSettingsChanged(SettingsObj settings)
        {
            _lyricsCtrl.NextLineWhenNoTranslation = settings.NextLineWhenNoTranslation;
            _lyricsCtrl.ShowTranslation = settings.ShowTranslation;
            SaveSettings(settings);
            UpdateLyrics(force: true);
        }

        private void PreviewTimingLyrics(string trackUrl, string lyrics)
        {
            lock (_lock) _lyricsCtrl.PreviewLyrics(trackUrl, lyrics);
            UpdateLyrics(force: true);
        }

        private void CancelTimingPreview(string trackUrl)
        {
            lock (_lock) _lyricsCtrl.CancelPreview(trackUrl);
            UpdateLyrics(force: true);
        }

        private void TimingLyricsSaved(string trackUrl, string lyrics)
        {
            lock (_lock) _lyricsCtrl.KeepSavedLyrics(trackUrl, lyrics);
            UpdateLyrics(force: true);
        }

        private void ImportedEnglishSaved(string trackUrl)
        {
            lock (_lock) _lyricsCtrl.InvalidateImportedEnglish();
            UpdateLyrics(force: true);
        }

        private void TimerTick(object sender, ElapsedEventArgs args)
        {
            Debug.Assert(sender is Timer);
            try
            {
                UpdateLyrics();
            }
            catch (Exception e)
            {
                _mbApiInterface.MB_Trace(e.ToString());
            }
            if (!_closing) ((Timer) sender).Start();
        }

        private string _line1, _line2, _nextLine;
        private void UpdateLyrics(bool force = false)
        {
            lock (_lock)
            {
                var view = _frmLyrics;
                if (view == null || view.Form.IsDisposed || !view.Form.IsHandleCreated) return;
                // The compact window shows only the visualizer when a track has
                // no lyrics. Keep the title/artist fallback for the old overlay.
                var entry = _lyricsCtrl.UpdateLyrics(!(view is FrmLyricsWindow) &&
                    !_settings.HideWhenUnavailable);
                if (entry == null && _line1 != "")
                {
                    if (_missingLyricsSinceUtc == DateTime.MinValue)
                        _missingLyricsSinceUtc = DateTime.UtcNow;
                    if ((DateTime.UtcNow - _missingLyricsSinceUtc).TotalMilliseconds < 650)
                        return;
                    _line1 = "";
                    _line2 = null;
                    _nextLine = null;
                    view.Form.BeginInvoke(new Action(() =>
                    {
                        if (!view.Form.IsDisposed) view.Clear();
                    }));
                    return;
                }

                if (entry == null) return;
                _missingLyricsSinceUtc = DateTime.MinValue;
                if (!force && entry.LyricLine1 == _line1 && entry.LyricLine2 == _line2 && entry.NextLine == _nextLine) return;
                view.Form.BeginInvoke(new Action<string, string, string>((line1, line2, nextLine) =>
                {
                    if (!view.Form.IsDisposed) view.UpdateLyrics(line1, line2, nextLine);
                }), entry.LyricLine1, entry.LyricLine2, entry.NextLine);
                _line1 = entry.LyricLine1;
                _line2 = entry.LyricLine2;
                _nextLine = entry.NextLine;
            }
        }

        public string[] GetProviders() { return null; }
   }
}
