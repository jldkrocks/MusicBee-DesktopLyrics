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
        private ToolStripMenuItem _visibilityMenuItem;
        private ToolStripMenuItem _compactMenuItem;
        private Timer _timer;
        private System.Windows.Forms.Timer _stopHideTimer;
        private LyricsController _lyricsCtrl;
        private readonly object _lock = new object();
        private DateTime _missingLyricsSinceUtc = DateTime.MinValue;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            var versions = Assembly.GetExecutingAssembly().GetName().Version.ToString().Split('.');

            _mbApiInterface = new MusicBeeApiInterface();
            _mbApiInterface.Initialise(apiInterfacePtr);
            _about.PluginInfoVersion = PluginInfoVersion;
            _about.Name = "Desktop Lyrics";
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

            _lyricsCtrl = new LyricsController(_mbApiInterface);
            return _about;
        }

        // ReSharper disable once UnusedParameter.Global
        public bool Configure(IntPtr panelHandle)
        {
            var settingsForm = new FrmSettings(_settings);
            settingsForm.SettingsChanged += (sender, settings) =>
            {
                if (_compactMenuItem != null) _compactMenuItem.Checked = settings.CompactWindow;
                var view = _frmLyrics;
                if (view == null) return;
                if ((view is FrmLyricsWindow) != settings.CompactWindow)
                    StartupForm();
                else
                    view.UpdateFromSettings(settings);
            };
            settingsForm.ShowDialog();
            SaveSettings(_settings);
            LyricParser.PreserveSlash = _settings.PreserveSlash;
            _lyricsCtrl.NextLineWhenNoTranslation = _settings.NextLineWhenNoTranslation;
            var activeView = _frmLyrics;
            activeView?.Form.Invoke(new Action(() =>
            {
                activeView.UpdateFromSettings(_settings);
            }));
            return true;
        }

        private void SaveSettings(SettingsObj settings)
        {
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
            _timer?.Stop();
            var view = _frmLyrics;
            view?.Form.Invoke(new Action(() =>
            {
                _stopHideTimer?.Dispose();
                _stopHideTimer = null;
                view.Form.Dispose();
                _frmLyrics = null;
            }));
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

                        LyricParser.PreserveSlash = _settings.PreserveSlash;
                        _lyricsCtrl.NextLineWhenNoTranslation = _settings.NextLineWhenNoTranslation;

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
                    UpdatePlayState(_mbApiInterface.Player_GetPlayState());
                    break;
                case NotificationType.TrackChanged:
                case NotificationType.NowPlayingArtworkReady:
                    var artworkView = _frmLyrics as FrmLyricsWindow;
                    if (artworkView != null && !artworkView.IsDisposed && artworkView.IsHandleCreated)
                        artworkView.BeginInvoke(new Action(() =>
                        {
                            if (!artworkView.IsDisposed) artworkView.RefreshArtwork(true);
                        }));
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
                if (state == PlayState.Stopped)
                {
                    if (_stopHideTimer == null)
                    {
                        _stopHideTimer = new System.Windows.Forms.Timer { Interval = 1200 };
                        _stopHideTimer.Tick += (sender, args) =>
                        {
                            _stopHideTimer.Stop();
                            var active = _frmLyrics;
                            if (_settings.AutoHide && active != null &&
                                _mbApiInterface.Player_GetPlayState() == PlayState.Stopped)
                                active.Form.Hide();
                        };
                    }
                    _stopHideTimer.Stop();
                    _stopHideTimer.Start();
                }
                else if (state == PlayState.Playing)
                {
                    _stopHideTimer?.Stop();
                    view.Form.Show();
                }
            }));
        }

        private void StartupMenuItem()
        {
            var menuItem = (ToolStripMenuItem) _mbApiInterface.MB_AddMenuItem(
                "mnuView/Desktop Lyrics", "Toggle Desktop Lyrics visibility.",
                ToggleLyrics);
            _visibilityMenuItem = menuItem;
            menuItem.Checked = !_settings.HideOnStartup;

            _compactMenuItem = (ToolStripMenuItem)_mbApiInterface.MB_AddMenuItem(
                "mnuView/Desktop Lyrics Visualizer Window",
                "Switch between the compact visualizer window and the desktop overlay.",
                ToggleCompactWindow);
            if (_compactMenuItem != null) _compactMenuItem.Checked = _settings.CompactWindow;

            void ToggleLyrics(object sender, EventArgs args)
            {
                var menuItem2 = _visibilityMenuItem;
                if (menuItem2 == null) return;

                menuItem2.Checked = !menuItem2.Checked;
                if (!menuItem2.Checked)
                {
                    _frmLyrics?.Form.Dispose();
                    _frmLyrics = null;
                }
                else
                    StartupForm();
                _settings.HideOnStartup = !menuItem2.Checked;
                SaveSettings(_settings);
            }

            void ToggleCompactWindow(object sender, EventArgs args)
            {
                _settings.CompactWindow = !_settings.CompactWindow;
                _settings.CompactWindowPreferenceSet = true;
                if (_compactMenuItem != null) _compactMenuItem.Checked = _settings.CompactWindow;
                if (_frmLyrics != null) StartupForm();
                SaveSettings(_settings);
            }
        }

        private void StartupForm()
        {
            var f = (Form)Control.FromHandle(_mbApiInterface.MB_GetWindowHandle());
            f.Invoke(new Action(() =>
            {
                _frmLyrics?.Form.Dispose();
                var view = _settings.CompactWindow
                    ? (IDesktopLyricsView)new FrmLyricsWindow(_settings, _mbApiInterface)
                    : new FrmLyrics(_settings);
                _frmLyrics = view;
                view.Form.FormClosed += (sender, args) =>
                {
                    if (args.CloseReason != CloseReason.UserClosing || !ReferenceEquals(_frmLyrics, view)) return;
                    _frmLyrics = null;
                    if (_visibilityMenuItem != null) _visibilityMenuItem.Checked = false;
                    _settings.HideOnStartup = true;
                    SaveSettings(_settings);
                };
                view.Form.Show();
                try
                {
                    UpdateLyrics(force: true);
                }
                catch (Exception e)
                {
                    _mbApiInterface.MB_Trace(e.ToString());
                }
            }));
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
            ((Timer) sender).Start();
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
