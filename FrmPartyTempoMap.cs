using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class FrmPartyTempoMap : Form
    {
        private readonly DataGridView _grid = new DataGridView();
        private readonly DataGridView _accentGrid = new DataGridView();
        private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly ToolTip _tips = new ToolTip { InitialDelay = 350, AutoPopDelay = 20000, ShowAlways = true };
        private DataGridView ActiveGrid => _tabs.SelectedIndex == 1 ? _accentGrid : _grid;
        private readonly CheckBox _enabled = new CheckBox();
        private readonly PartyTempoMap _source;
        private readonly Func<double?> _position;
        private readonly Action<int> _seek;
        private readonly Action<PartyTempoMap> _save;
        private readonly Action _togglePlayback;
        private readonly Func<bool> _playing;
        private readonly Func<double> _duration;
        private readonly Func<bool> _playbackBusy;
        private readonly PartyPreviewSession _preview;
        private Button _previewButton;
        private bool _closeAfterPreview;
        private readonly PartyTimeline _timeline = new PartyTimeline();
        private readonly Timer _timer = new Timer { Interval = 120 };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Font _editorFont = new Font("Segoe UI", 9f);
        private Button _play, _back, _forward, _add, _seekRow, _seekExact;
        private readonly NumericUpDown _seekStep = new NumericUpDown();
        private readonly NumericUpDown _seekTime = new NumericUpDown();
        private readonly System.Diagnostics.Stopwatch _seekAge = new System.Diagnostics.Stopwatch();
        private double? _pendingSeek;
        private bool _dirty, _trackWasAvailable = true;
        private static readonly string[] Styles = { "Normal", "Side to side", "Hold pose", "Rest (keep counting)" };

        private static readonly string[] Speeds = { "Half (0.5x)", "Normal (1x)", "Double (2x)" };

        private static readonly string[] Rhythms = { "Straight", "Waltz (3/4)", "Swing", "4/4 - accent on 4" };

        internal FrmPartyTempoMap(PartyTempoMap map, string title, Func<double?> position,
            Action<int> seek, Action<PartyTempoMap> save, double duration,
            Action togglePlayback, Func<bool> playing, Func<double> durationProvider = null,
            Action<bool, Action<bool>> setPlaying = null, Func<bool> playbackBusy = null)
        {
            _source = map; _position = position; _seek = seek; _save = save;
            _togglePlayback = togglePlayback; _playing = playing;
            _duration = durationProvider ?? (() => duration); _playbackBusy = playbackBusy ?? (() => false);
            _preview = new PartyPreviewSession(setPlaying ?? ((wanted, complete) => { if (_playing() != wanted) _togglePlayback(); complete(true); }),
                seconds => SeekCore(seconds, true), () => !IsDisposed && _position().HasValue,
                message => { if (!IsDisposed) _status.Text = message; });
            Font = _editorFont; BackColor = Color.FromArgb(23, 27, 38); ForeColor = Color.FromArgb(232, 236, 245);
            Text = "Tempo map — " + title;
            Size = new Size(1200, 690); MinimumSize = new Size(1080, 650);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            MinimizeBox = false; TopMost = true;
            var help = new Label { Dock = DockStyle.Fill };
            _enabled.Text = "Use this map for this song"; _enabled.Checked = map.Enabled;
            _enabled.Dock = DockStyle.Top; _enabled.Height = 28; _enabled.Padding = new Padding(10, 0, 0, 0);
            _grid.Dock = DockStyle.Fill; _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add("start", "Start (s)"); _grid.Columns.Add("bpm", "BPM");
            _grid.Columns.Add("ramp", "BPM ramp (s)");
            _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "style", HeaderText = "Dance", DataSource = Styles });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "align", HeaderText = "Align" });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "countIn", HeaderText = "Bob count-in" });
            _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "rhythm", HeaderText = "Rhythm", DataSource = Rhythms });
            _grid.Columns.Add("swing", "Swing %");
            _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "speed", HeaderText = "Speed", DataSource = Speeds });
            _grid.Columns.Add("fromBpm", "From BPM");
            _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "toNext", HeaderText = "To next point", DataSource = new[] { "Keep BPM", "Ramp to next", "Custom (saved)" } });
            _grid.Columns[2].Visible = _grid.Columns[9].Visible = false;
            _grid.Columns[10].DisplayIndex = 2;
            _grid.Columns[10].ToolTipText = "Keep BPM: stay at this BPM until the next point. Ramp to next: gradually reach the next point's BPM at its time. Moving or deleting points recalculates the ramp. Custom (saved) preserves an imported ramp that ends between points; choose Ramp to next to replace it.";
            _grid.Columns[1].HeaderText = "BPM at point";
            _grid.Columns[9].ToolTipText = "Optional starting BPM for this row's forward ramp. Blank uses the preceding tempo. Ramp to row fills this on the preceding row.";
            _grid.Columns[8].FillWeight = 120;
            _grid.Columns[8].ToolTipText = "Half, normal or double dance speed, independent of Dance and Rhythm. Does not change the song BPM. Hold stops all motion.";
            _grid.Columns[6].DisplayIndex = 4;
            _grid.Columns[7].DisplayIndex = 5;
            _grid.Columns[8].DisplayIndex = 4;
            _grid.Columns[7].FillWeight = 80;
            _grid.Columns[7].ToolTipText = "Swing only: percentage of each beat spent in the side pose. 50 = even, 60 = light swing, 66.67 = about 2:1, 75 = strong swing. BPM does not change.";
            _grid.Columns[6].FillWeight = 185;
            _grid.Columns[6].ToolTipText = "4/4 accent on 4: three small centre bops, then a strong side landing on FOUR; opposite side next bar. BPM counts all four beats. Straight: existing motion. Waltz: side, centre bop, second centre bop, then the opposite side. Swing: longer side pose, short middle pose, opposite side. Swing % controls the long-short split. Half speed slows the chosen pattern; Hold stops it.";
            _grid.Columns[2].ToolTipText = "Seconds after this row starts to reach its BPM; starts from From BPM if set, otherwise the preceding tempo. Example: 120 to 150 over 4 seconds. Equal BPM values do not ramp; dance styles switch at the start.";
            _grid.Columns[4].ToolTipText = "Restart on a side pose at this row's start (beat 1 for Waltz; strong FOUR for 4/4 accent on 4). Uses the row Start time, NOT when you click Align or Save. Save applies the setting. Leave off to preserve the ongoing beat phase.";
            _grid.Columns[5].ToolTipText = "Check on a Normal-speed row after Half speed: up to four lead-in bobs, then one final bop on the first beat at or after the return. Uses saved alignment, or this row's start when Align is checked.";
            _grid.Columns[0].ToolTipText = "Double-click a row to seek. Press F2 or type to edit. Song position in seconds. A section lasts until the next start; the first starts at 0. Save applies edits without closing.";
            _grid.Columns[1].ToolTipText = "Musical BPM at this time, 40-240. Ramp to next connects this BPM to the next point automatically. Speed changes the dance rate separately.";
            _grid.Columns[3].ToolTipText = "Normal or Side to side chooses the dance. Hold pose freezes movement AND the beat counter. Rest freezes movement but keeps counting at this row's BPM and Speed. End either with a new dancing row; leave Align off after Rest to keep phase.";
            _grid.Columns[4].ToolTipText += " For a smooth slowdown leave Align off. Use an accent cue for emphasis without restarting the side poses.";
            _grid.Columns[3].FillWeight = 205;
            _grid.Columns[4].FillWeight = 65;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            var weights = new float[] { 85, 85, 95, 185, 50, 95, 170, 65, 110, 80, 150 };
            for (int c = 0; c < weights.Length; c++) _grid.Columns[c].FillWeight = weights[c];
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns[3].MinimumWidth = 180;
            _grid.Columns[6].MinimumWidth = 155;
            _grid.RowTemplate.Height = 29;
            foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            for (int i = 0; i < map.Sections.Count; i++)
            {
                var section = map.Sections[i];
                var custom = section.RampSeconds > 0;
                var startBpm = custom ? map.At(section.StartSeconds).Bpm : section.Bpm;
                var link = section.RampToNext || (custom && i + 1 < map.Sections.Count &&
                    Math.Abs(section.StartSeconds + section.RampSeconds - map.Sections[i + 1].StartSeconds) < 1e-7 &&
                    Math.Abs(section.Bpm - (map.Sections[i + 1].RampSeconds > 0 ? map.At(map.Sections[i + 1].StartSeconds).Bpm : map.Sections[i + 1].Bpm)) < 1e-7);
                AddRow(section.StartSeconds, startBpm, custom ? section.RampSeconds : 0, section.Style, section.AlignBeat, section.CountIn, section.Rhythm, section.SwingPercent, section.EffectiveSpeed, custom ? (double?)section.Bpm : null);
                _grid.Rows[_grid.Rows.Count - 1].Cells[10].Value = link ? "Ramp to next" : custom ? "Custom (saved)" : "Keep BPM";
            }
            _grid.BackgroundColor = Color.FromArgb(30, 35, 48);
            _grid.BorderStyle = BorderStyle.None; _grid.GridColor = Color.FromArgb(54, 62, 79);
            _grid.EnableHeadersVisualStyles = false; _grid.ColumnHeadersHeight = 32; _grid.RowTemplate.Height = 29;
            _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(43, 50, 67),
                ForeColor = ForeColor, SelectionBackColor = Color.FromArgb(43, 50, 67) };
            _grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(30, 35, 48),
                ForeColor = ForeColor, SelectionBackColor = Color.FromArgb(60, 87, 118), SelectionForeColor = Color.White,
                Padding = new Padding(4, 2, 4, 2) };
            _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(34, 40, 54);
            var transport = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            transport.Controls.Add(new Label { Text = "Step (s)", AutoSize = true, Margin = new Padding(3, 9, 3, 0) });
            ConfigureSeekNumber(_seekStep, 0.01m, 5, 0.01m, 0.1m, 2);
            transport.Controls.Add(_seekStep);
            _back = AddButton(transport, "- step", () => SeekRelative(-(double)_seekStep.Value));
            _play = AddButton(transport, "Play / pause", () => { try { _togglePlayback(); PollPlayback(); } catch (Exception ex) { _status.Text = ex.Message; } });
            _forward = AddButton(transport, "+ step", () => SeekRelative((double)_seekStep.Value));
            transport.Controls.Add(new Label { Text = "Seek to (s)", AutoSize = true, Margin = new Padding(15, 9, 3, 0) });
            ConfigureSeekNumber(_seekTime, 0, (decimal)Math.Max(0, duration), 0.001m,
                (decimal)Math.Max(0, Math.Min(duration, _position() ?? 0)), 3);
            _seekTime.Width = 105;
            transport.Controls.Add(_seekTime);
            _seekExact = AddButton(transport, "Seek", () => SeekTo((double)_seekTime.Value));
            _previewButton = AddButton(transport, "Preview 2 s", () => _preview.Start((double)_seekTime.Value, _timeline.Duration));
            _tips.SetToolTip(_previewButton, "Hear 0.5 s before and 1.5 s after the Seek to time. Playback then pauses and returns to that exact time. Click again to stop early. Preview uses saved changes; Save first to audition edits.");
            _seekTime.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Enter) return;
                SeekTo((double)_seekTime.Value); args.SuppressKeyPress = true;
            };
            _timeline.Duration = Math.Max(0, duration); _timeline.Dock = DockStyle.Fill;
            _timeline.SeekRequested += SeekTo;
            _timeline.MarkerSelected += row => { _tabs.SelectedIndex = 0; if (row >= 0 && row < _grid.Rows.Count) _grid.CurrentCell = _grid.Rows[row].Cells[0]; };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _add = AddButton(actions, "Add at playhead", () =>
            {
                var now = EditingPosition();
                if (!now.HasValue) { _status.Text = "Play the original song to capture its position."; return; }
                if (_tabs.SelectedIndex == 1)
                {
                    _accentGrid.Rows.Add(Math.Round(now.Value, 3).ToString(CultureInfo.CurrentCulture), 1.7.ToString(CultureInfo.CurrentCulture), 0.1.ToString(CultureInfo.CurrentCulture), "0");
                    _accentGrid.CurrentCell = _accentGrid.Rows[_accentGrid.Rows.Count - 1].Cells[0];
                    MarkDirty(); return;
                }
                var selected = _grid.CurrentRow;
                double bpm = 120;
                if (selected != null) double.TryParse(Convert.ToString(selected.Cells[1].Value), out bpm);
                var rhythm = selected == null ? PartyRhythm.Straight :
                    (PartyRhythm)Math.Max(0, Array.IndexOf(Rhythms, Convert.ToString(selected.Cells[6].Value)));
                double swingPercent;
                if (selected == null || !double.TryParse(Convert.ToString(selected.Cells[7].Value), out swingPercent)) swingPercent = 66.67;
                AddRow(Math.Round(now.Value, 3), bpm >= 40 && bpm <= 240 ? bpm : 120, 0, PartyDanceStyle.Normal, false, false, rhythm, swingPercent, selected == null ? 1 : SpeedAt(selected));
                _grid.CurrentCell = _grid.Rows[_grid.Rows.Count - 1].Cells[0];
                MarkDirty();
            });
            AddButton(actions, "Delete row", DeleteSelectedRow);
            _seekRow = AddButton(actions, "Seek to row", () =>
            {
                if (ActiveGrid.CurrentRow == null) return;
                try { SeekTo(Number(ActiveGrid.CurrentRow, 0)); }
                catch (Exception ex) { _status.Text = ex.Message; }
            });
            var rampButton = AddButton(actions, "Ramp from previous", RampToRow);
            _tips.SetToolTip(rampButton, "Shortcut: set the previous point to Ramp to next. Both BPM values stay at their own points. Moving or deleting either point reconnects the curve. Save applies.");
            AddButton(actions, "Save", SaveMap);
            AddButton(actions, "Close", () => Close());
            _status.Text = "Diamonds select sections; gold circles select accents. Save applies both tabs and keeps this window open.";
            _status.ForeColor = Color.FromArgb(178, 192, 212);
            help.Text = "BPM belongs to each point. Ramp to next connects points automatically. Hover over controls for help.";
            _tips.SetToolTip(_seekStep, "Seconds moved by - step and + step. Pause for precise placement; 0.01 s is the smallest step.");
            _tips.SetToolTip(_seekTime, "Exact song position in seconds. Enter or Seek moves playback without changing your rows.");
            _tips.SetToolTip(_enabled, "Apply this song's saved sections and accent cues. Uncheck to use its ordinary BPM settings.");
            _tips.SetToolTip(_timeline, "Diamonds select sections; gold circles select accent cues. Both seek to their saved time. Drag the playhead to seek. Blue = Normal, purple = Side to side, grey = Hold, teal = Rest.");
            _tips.SetToolTip(_add, "Add a section or accent at the playhead, depending on the selected tab. Pause and fine-seek first for exact placement. Save applies the new row.");
            SetupAccentGrid(map);
            var sectionsTab = new TabPage("Sections") { BackColor = BackColor, Padding = new Padding(3) };
            var accentsTab = new TabPage("Accent cues") { BackColor = BackColor, Padding = new Padding(3) };
            sectionsTab.Controls.Add(_grid); accentsTab.Controls.Add(_accentGrid);
            _tabs.TabPages.Add(sectionsTab); _tabs.TabPages.Add(accentsTab);
            _tabs.SelectedIndexChanged += (sender, args) => { rampButton.Enabled = _tabs.SelectedIndex == 0; RefreshMarkers(); };
            _timeline.AccentSelected += row => { _tabs.SelectedIndex = 1; if (row >= 0 && row < _accentGrid.Rows.Count) _accentGrid.CurrentCell = _accentGrid.Rows[row].Cells[0]; };
            _enabled.Dock = DockStyle.Fill; _enabled.Padding = Padding.Empty;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.Controls.Add(help, 0, 0); layout.Controls.Add(_timeline, 0, 1);
            layout.Controls.Add(transport, 0, 2); layout.Controls.Add(_tabs, 0, 3);
            layout.Controls.Add(_enabled, 0, 4); layout.Controls.Add(_status, 0, 5); layout.Controls.Add(actions, 0, 6);
            Controls.Add(layout);
            _grid.CellValueChanged += (sender, args) => { RefreshSwingCells(); MarkDirty(); };
            _grid.CellToolTipTextNeeded += (sender, args) =>
            {
                if (args.ColumnIndex >= 0) args.ToolTipText = _grid.Columns[args.ColumnIndex].ToolTipText;
                if (args.RowIndex >= 0 && args.ColumnIndex == 10 && Convert.ToString(_grid.Rows[args.RowIndex].Cells[10].Value) == "Custom (saved)")
                    args.ToolTipText = "Preserved saved ramp: reaches " + _grid.Rows[args.RowIndex].Cells[9].Value +
                        " BPM over " + _grid.Rows[args.RowIndex].Cells[2].Value + " seconds from this point. Choose Ramp to next to link it to the next point instead.";
            };
            _grid.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (_grid.IsCurrentCellDirty && (_grid.CurrentCell is DataGridViewCheckBoxCell ||
                    _grid.CurrentCell is DataGridViewComboBoxCell)) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            // Open combo cells after the grid has finished its selection/click handling.
            // Opening during MouseDown can let the same click close them again.
            _grid.CellClick += OpenComboOnClick;
            _grid.CellDoubleClick += SeekDoubleClickedRow;
            _accentGrid.CellDoubleClick += SeekDoubleClickedRow;
            _grid.SelectionChanged += (sender, args) => RefreshMarkers();
            _enabled.CheckedChanged += (sender, args) => MarkDirty();
            _timer.Tick += (sender, args) => PollPlayback();
            Shown += (sender, args) => { RefreshMarkers(); PollPlayback(); _timer.Start(); };
            FormClosing += (sender, args) =>
            {
                if (_preview.Active) { args.Cancel = true; _closeAfterPreview = true; _preview.Stop(); return; }
                if (_dirty && MessageBox.Show(this, "Close without saving your latest edits? Earlier saves will stay applied.",
                    "Unsaved tempo-map edits", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    args.Cancel = true;
            };
            _grid.DataError += (sender, args) => { args.ThrowException = false; };
        }

        private void SetupAccentGrid(PartyTempoMap map)
        {
            _accentGrid.Dock = DockStyle.Fill; _accentGrid.AllowUserToAddRows = false;
            _accentGrid.AllowUserToDeleteRows = false; _accentGrid.RowHeadersVisible = false;
            _accentGrid.MultiSelect = false; _accentGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _accentGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _accentGrid.BackgroundColor = _grid.BackgroundColor; _accentGrid.GridColor = _grid.GridColor;
            _accentGrid.BorderStyle = BorderStyle.None; _accentGrid.EnableHeadersVisualStyles = false;
            _accentGrid.ColumnHeadersHeight = 32; _accentGrid.RowTemplate.Height = 29;
            _accentGrid.DefaultCellStyle = _grid.DefaultCellStyle.Clone();
            _accentGrid.AlternatingRowsDefaultCellStyle = _grid.AlternatingRowsDefaultCellStyle.Clone();
            _accentGrid.ColumnHeadersDefaultCellStyle = _grid.ColumnHeadersDefaultCellStyle.Clone();
            _accentGrid.Columns.Add("time", "Hit time (s)");
            _accentGrid.Columns.Add("strength", "Strength");
            _accentGrid.Columns.Add("prepare", "Lead-in (s)");
            _accentGrid.Columns.Add("hold", "Hold after hit (s)");
            _accentGrid.Columns[0].ToolTipText = "Double-click a row to seek. Exact song time of the deepest downward bop. Not when you click Save. Cues do not change BPM, rhythm or beat alignment. They can also add a hit during Rest or Hold.";
            _accentGrid.Columns[1].ToolTipText = "Bop strength: 0.5 = light, 1 = regular, 1.7 = strong (default), 2.5 = maximum. An accent emphasizes the current pose without forcing another side landing.";
            _accentGrid.Columns[2].ToolTipText = "Seconds to crouch into the hit, 0-1. Default 0.1. Zero gives an immediate hit. The deepest dip occurs at Hit time, followed by a 0.22 s recovery.";
            _accentGrid.Columns[3].ToolTipText = "Optional 0-5 seconds to keep the landing pose and dip after the hit. Zero recovers immediately. The beat keeps counting, then the normal pose sequence resumes. For a longer silent passage use Rest in Sections.";
            foreach (DataGridViewColumn c in _accentGrid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var cue in map.Accents)
                _accentGrid.Rows.Add(cue.TimeSeconds.ToString("0.#########", CultureInfo.CurrentCulture), cue.Strength.ToString(CultureInfo.CurrentCulture),
                    cue.PrepareSeconds.ToString(CultureInfo.CurrentCulture), cue.HoldSeconds.ToString(CultureInfo.CurrentCulture));
            _accentGrid.CellToolTipTextNeeded += (sender, args) => { if (args.ColumnIndex >= 0) args.ToolTipText = _accentGrid.Columns[args.ColumnIndex].ToolTipText; };
            _accentGrid.CellValueChanged += (sender, args) => MarkDirty();
            _accentGrid.SelectionChanged += (sender, args) => RefreshMarkers();
            _accentGrid.DataError += (sender, args) => { args.ThrowException = false; };
        }

        private static void ConfigureSeekNumber(NumericUpDown input, decimal minimum, decimal maximum,
            decimal increment, decimal value, int places)
        {
            input.Minimum = minimum; input.Maximum = maximum; input.Increment = increment;
            input.DecimalPlaces = places; input.Value = value; input.Width = 75;
            input.Margin = new Padding(3, 7, 3, 3);
            input.BackColor = Color.FromArgb(30, 35, 48); input.ForeColor = Color.FromArgb(232, 236, 245);
        }

        private void OpenComboOnClick(object sender, DataGridViewCellEventArgs args)
        {
            if (args.RowIndex < 0 || args.ColumnIndex < 0 ||
                !(_grid[args.ColumnIndex, args.RowIndex] is DataGridViewComboBoxCell)) return;
            var cell = _grid[args.ColumnIndex, args.RowIndex];
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || _grid.IsDisposed || _grid.CurrentCell != cell || cell.ReadOnly) return;
                if (_grid.BeginEdit(true) && _grid.EditingControl is ComboBox combo && !combo.DroppedDown)
                    combo.DroppedDown = true;
            }));
        }

        private void SeekDoubleClickedRow(object sender, DataGridViewCellEventArgs args)
        {
            if (args.RowIndex < 0) return;
            var grid = (DataGridView)sender;
            if (args.ColumnIndex >= 0 && (grid[args.ColumnIndex, args.RowIndex] is DataGridViewComboBoxCell ||
                grid[args.ColumnIndex, args.RowIndex] is DataGridViewCheckBoxCell)) return;
            try { grid.EndEdit(); SeekTo(Number(grid.Rows[args.RowIndex], 0)); }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private double? EditingPosition()
        {
            var reported = _position();
            if (!reported.HasValue) { _pendingSeek = null; return null; }
            if (_pendingSeek.HasValue && _seekAge.ElapsedMilliseconds < 1000)
            {
                var expected = _pendingSeek.Value + (_playing() ? _seekAge.Elapsed.TotalSeconds : 0);
                // Release the temporary cursor as soon as the player acknowledges
                // it. While playing it must advance, not freeze for a full second.
                if (Math.Abs(reported.Value - expected) <= (_playing() ? .075 : .001)) _pendingSeek = null;
                else return Math.Min(_timeline.Duration, expected);
            }
            _pendingSeek = null;
            return reported;
        }

        private static PartyDanceStyle StyleAt(DataGridViewRow row)
        {
            var value = Convert.ToString(row.Cells[3].Value);
            if (value == "Normal") return PartyDanceStyle.Normal;
            if (value == "Side to side") return PartyDanceStyle.SideToSide;
            if (value == "Hold pose") return PartyDanceStyle.Hold;
            if (value == "Rest (keep counting)") return PartyDanceStyle.Rest;
            throw new ArgumentException("Choose a listed dance.");
        }

        private static double SpeedAt(DataGridViewRow row)
        {
            var index = Array.IndexOf(Speeds, Convert.ToString(row.Cells[8].Value));
            if (index < 0) throw new ArgumentException("Choose a listed speed.");
            return index == 0 ? 0.5 : index == 2 ? 2 : 1;
        }

        private void AddRow(double start, double bpm, double ramp, PartyDanceStyle style, bool align, bool countIn = false, PartyRhythm rhythm = PartyRhythm.Straight, double swingPercent = 66.67, double speed = 1, double? fromBpm = null)
        {
            _grid.Rows.Add(start.ToString("0.#########", CultureInfo.CurrentCulture), bpm.ToString("0.#########", CultureInfo.CurrentCulture),
                ramp.ToString("0.#########", CultureInfo.CurrentCulture), style == PartyDanceStyle.Rest ? "Rest (keep counting)" : style == PartyDanceStyle.Hold ? "Hold pose" : style == PartyDanceStyle.SideToSide ? "Side to side" : "Normal", align, countIn, Rhythms[(int)rhythm], swingPercent.ToString("0.#########", CultureInfo.CurrentCulture), Speeds[(style == PartyDanceStyle.HalfSpeed || speed == 0.5) ? 0 : speed == 2 ? 2 : 1], fromBpm?.ToString("0.#########", CultureInfo.CurrentCulture), "Keep BPM");
            if (ramp <= 0)
                ((DataGridViewComboBoxCell)_grid.Rows[_grid.Rows.Count - 1].Cells[10]).DataSource = new[] { "Keep BPM", "Ramp to next" };
            RefreshSwingCells();
        }

        private void RefreshSwingCells()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var active = Convert.ToString(row.Cells[6].Value) == "Swing";
                row.Cells[7].ReadOnly = !active;
                row.Cells[7].Style.ForeColor = active ? ForeColor : Color.FromArgb(135, 148, 168);
            }
        }

        private Button AddButton(FlowLayoutPanel panel, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(43, 53, 73), ForeColor = Color.FromArgb(238, 241, 248), Padding = new Padding(6, 2, 6, 2) };
            button.FlatAppearance.BorderColor = Color.FromArgb(83, 99, 124);
            _tips.SetToolTip(button, text == "Save" ? "Apply both tabs to this song and keep the editor open. Saving does not capture the current beat." :
                text == "Delete row" ? "Delete the selected row from the current tab. Save applies the deletion." :
                text == "Seek to row" ? "Seek to the selected section or accent without changing it." :
                text == "Close" ? "Close the editor; unsaved edits require confirmation." :
                text == "Play / pause" ? "Toggle playback to preview your saved changes." : "Move playback by the Step (s) value without changing row times.");
            button.Click += (sender, args) => action(); panel.Controls.Add(button); return button;
        }

        private void MarkDirty()
        {
            _dirty = true; _status.Text = "Unsaved edits — Save applies them while keeping this window open.";
            RefreshMarkers();
        }

        private void RefreshMarkers()
        {
            _timeline.Markers.Clear();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                double seconds;
                if (double.TryParse(Convert.ToString(row.Cells[0].Value), out seconds) &&
                    !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds >= 0 && seconds <= _timeline.Duration)
                    _timeline.Markers.Add(new PartyTimeline.Marker { Row = row.Index, Seconds = seconds,
                        Style = Convert.ToString(row.Cells[3].Value) == "Rest (keep counting)" ? PartyDanceStyle.Rest :
                            Convert.ToString(row.Cells[3].Value) == "Hold pose" ? PartyDanceStyle.Hold :
                            Convert.ToString(row.Cells[3].Value) == "Side to side" ? PartyDanceStyle.SideToSide : PartyDanceStyle.Normal });
            }
            _timeline.Accents.Clear();
            foreach (DataGridViewRow row in _accentGrid.Rows)
            {
                double time;
                if (double.TryParse(Convert.ToString(row.Cells[0].Value), out time) && !double.IsNaN(time) && !double.IsInfinity(time) && time >= 0 && time <= _timeline.Duration)
                    _timeline.Accents.Add(new PartyTimeline.Marker { Row = row.Index, Seconds = time });
            }
            _timeline.SelectedRow = _tabs.SelectedIndex == 0 ? _grid.CurrentRow?.Index ?? -1 : -1;
            _timeline.SelectedAccent = _tabs.SelectedIndex == 1 ? _accentGrid.CurrentRow?.Index ?? -1 : -1;
            _timeline.Invalidate();
        }

        private void PollPlayback()
        {
            try
            {
                var position = EditingPosition(); var available = position.HasValue;
                if (available)
                {
                    var durationNow = _duration();
                    if (durationNow > 0 && Math.Abs(durationNow - _timeline.Duration) > .001)
                    { _timeline.Duration = durationNow; _seekTime.Maximum = (decimal)durationNow; RefreshMarkers(); }
                }
                _preview.Tick(position);
                if (_closeAfterPreview && !_preview.Active) { _closeAfterPreview = false; Close(); return; }
                var transportReady = available && !_playbackBusy() && !_preview.Active;
                _timeline.Enabled = _back.Enabled = _forward.Enabled = _seekRow.Enabled = _seekExact.Enabled = _seekTime.Enabled = _seekStep.Enabled = transportReady && _timeline.Duration > 0;
                _play.Enabled = transportReady; _add.Enabled = available && !_preview.Active;
                _previewButton.Enabled = available && (_preview.Active || (!_playbackBusy() && _timeline.Duration > 0));
                _previewButton.Text = _preview.Active ? "Stop preview" : "Preview 2 s";
                _play.Text = _playbackBusy() ? "Working..." : available && _playing() ? "Pause" : "Play";
                if (available && !_timeline.Scrubbing) _timeline.Position = position.Value;
                if (available && !_trackWasAvailable) _status.Text = _dirty ? "Original song ready — unsaved edits." : "Original song ready.";
                _trackWasAvailable = available;
                if (!available) _status.Text = "Another song is playing. Edits and Save still belong to the original song.";
                _timeline.Invalidate();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private void SeekRelative(double seconds)
        {
            var position = EditingPosition();
            if (position.HasValue) SeekTo(position.Value + seconds);
        }

        private void SeekTo(double seconds) { SeekCore(seconds, false); }

        private void SeekCore(double seconds, bool fromPreview)
        {
            try
            {
                if (!fromPreview && (_preview.Active || _playbackBusy())) return;
                if (!_position().HasValue || _timeline.Duration <= 0)
                { if (fromPreview) throw new InvalidOperationException("Original song is not available for preview."); return; }
                var clamped = Math.Max(0, Math.Min(_timeline.Duration, seconds));
                var milliseconds = checked((int)Math.Round(clamped * 1000));
                _seek(milliseconds);
                clamped = milliseconds / 1000d;
                _pendingSeek = clamped; _seekAge.Restart();
                _seekTime.Value = Math.Max(_seekTime.Minimum, Math.Min(_seekTime.Maximum, (decimal)clamped));
                _timeline.Position = clamped; _timeline.Invalidate();
            }
            catch (Exception ex) { _status.Text = ex.Message; if (fromPreview) throw; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _preview.Cancel(); _timer.Dispose(); _tips.Dispose(); _editorFont.Dispose(); }
            base.Dispose(disposing);
        }

        private static double Number(DataGridViewRow row, int column)
        {
            double value;
            if (!double.TryParse(Convert.ToString(row.Cells[column].Value), NumberStyles.Float,
                CultureInfo.CurrentCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Enter a valid number in " + row.DataGridView.Columns[column].HeaderText + " on row " + (row.Index + 1) + ".");
            return value;
        }

        private void DeleteSelectedRow()
        {
            if (ActiveGrid.CurrentRow == null) return;
            ActiveGrid.Rows.Remove(ActiveGrid.CurrentRow);
            NormalizeLastPoint(); MarkDirty();
        }

        private void NormalizeLastPoint()
        {
            if (_grid.Rows.Count == 0) return;
            try
            {
                var last = _grid.Rows.Cast<DataGridViewRow>().OrderBy(r => Number(r, 0)).Last();
                if (Convert.ToString(last.Cells[10].Value) == "Ramp to next") last.Cells[10].Value = "Keep BPM";
            }
            catch (ArgumentException) { /* Save reports unfinished numeric edits. */ }
        }

        private void RampToRow()
        {
            try
            {
                _grid.EndEdit();
                var rows = _grid.Rows.Cast<DataGridViewRow>().OrderBy(r => Number(r, 0)).ToList();
                var index = rows.IndexOf(_grid.CurrentRow);
                if (index <= 0) throw new ArgumentException("Select a destination after the first point.");
                if (StyleAt(rows[index - 1]) == PartyDanceStyle.Hold)
                    throw new ArgumentException("Hold freezes tempo. Change the previous point to a dance or Rest first.");
                rows[index - 1].Cells[10].Value = "Ramp to next";
                MarkDirty();
                _status.Text = "Previous point now ramps to this point. Times and BPM stay on their own rows. Save to apply.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        private void SaveMap()
        {
            try
            {
                _grid.EndEdit(); _accentGrid.EndEdit();
                NormalizeLastPoint();
                var map = new PartyTempoMap { Version = 5, TrackUrl = _source.TrackUrl, InitialBeat = _source.InitialBeat, Enabled = _enabled.Checked };
                foreach (DataGridViewRow row in _grid.Rows)
                    map.Sections.Add(new PartyTempoSection { StartSeconds = Number(row, 0),
                        Bpm = Convert.ToString(row.Cells[10].Value) == "Custom (saved)" ? Number(row, 9) : Number(row, 1),
                        RampToNext = Convert.ToString(row.Cells[10].Value) == "Ramp to next",
                        RampSeconds = Convert.ToString(row.Cells[10].Value) == "Custom (saved)" ? Number(row, 2) : 0,
                        RampStartBpm = Convert.ToString(row.Cells[10].Value) == "Custom (saved)" ? (double?)Number(row, 1) : null, Style = StyleAt(row),
                        AlignBeat = Convert.ToBoolean(row.Cells[4].Value ?? false),
                        CountIn = Convert.ToBoolean(row.Cells[5].Value ?? false),
                        Rhythm = (PartyRhythm)Array.IndexOf(Rhythms, Convert.ToString(row.Cells[6].Value)),
                        SwingPercent = Number(row, 7), Speed = SpeedAt(row) });
                foreach (DataGridViewRow row in _accentGrid.Rows)
                    map.Accents.Add(new PartyAccentCue { TimeSeconds = Number(row, 0), Strength = Number(row, 1),
                        PrepareSeconds = Number(row, 2), HoldSeconds = Number(row, 3) });
                map.Accents = map.Accents.OrderBy(c => c.TimeSeconds).ToList();
                map.Sections = map.Sections.OrderBy(s => s.StartSeconds).ToList();
                map.Validate();
                _save(map); _dirty = false;
                _status.Text = map.Enabled ? "Saved — map applied. Keep listening and editing; this window stays open." : "Saved — map disabled; original BPM timing restored.";
                RefreshMarkers();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Tempo map", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
