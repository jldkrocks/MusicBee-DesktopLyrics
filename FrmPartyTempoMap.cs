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
        private readonly CheckBox _enabled = new CheckBox();
        private readonly PartyTempoMap _source;
        private readonly Func<double?> _position;
        private readonly Action<int> _seek;
        private readonly Action<PartyTempoMap> _save;
        private readonly Action _togglePlayback;
        private readonly Func<bool> _playing;
        private readonly PartyTimeline _timeline = new PartyTimeline();
        private readonly Timer _timer = new Timer { Interval = 120 };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Font _editorFont = new Font("Segoe UI", 9f);
        private Button _play, _back, _forward, _add, _seekRow;
        private bool _dirty, _trackWasAvailable = true;
        private static readonly string[] Styles = { "Normal", "Side to side", "Half speed", "Hold pose" };

        private static readonly string[] Rhythms = { "Straight", "Waltz (3/4)", "Swing", "4/4 - accent on 4" };

        internal FrmPartyTempoMap(PartyTempoMap map, string title, Func<double?> position,
            Action<int> seek, Action<PartyTempoMap> save, double duration,
            Action togglePlayback, Func<bool> playing)
        {
            _source = map; _position = position; _seek = seek; _save = save;
            _togglePlayback = togglePlayback; _playing = playing;
            Font = _editorFont; BackColor = Color.FromArgb(23, 27, 38); ForeColor = Color.FromArgb(232, 236, 245);
            Text = "Tempo map — " + title;
            Size = new Size(1100, 690); MinimumSize = new Size(980, 650);
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
            _grid.Columns[6].DisplayIndex = 4;
            _grid.Columns[7].DisplayIndex = 5;
            _grid.Columns[7].FillWeight = 80;
            _grid.Columns[7].ToolTipText = "Swing only: percentage of each beat spent in the side pose. 50 = even, 60 = light swing, 66.67 = about 2:1, 75 = strong swing. BPM does not change.";
            _grid.Columns[6].FillWeight = 185;
            _grid.Columns[6].ToolTipText = "4/4 accent on 4: three small centre bops, then a strong side landing on FOUR; opposite side next bar. BPM counts all four beats. Straight: existing motion. Waltz: side, centre bop, second centre bop, then the opposite side. Swing: longer side pose, short middle pose, opposite side. Swing % controls the long-short split. Half speed slows the chosen pattern; Hold stops it.";
            _grid.Columns[2].ToolTipText = "Seconds to blend from the previous BPM to this row's BPM. Example: 120 to 150 over 4 seconds. Equal BPM values do not ramp; dance styles switch at the start.";
            _grid.Columns[4].ToolTipText = "Restart on a side pose at this row's start (beat 1 for Waltz; strong FOUR for 4/4 accent on 4). Leave off to preserve the ongoing beat phase.";
            _grid.Columns[5].ToolTipText = "Check on the Normal row after Half speed: up to four lead-in bobs, then one final bop on the first beat at or after the return. Uses saved alignment, or this row's start when Align is checked.";
            _grid.Columns[3].FillWeight = 140;
            _grid.Columns[4].FillWeight = 65;
            _grid.RowTemplate.Height = 29;
            foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var section in map.Sections) AddRow(section.StartSeconds, section.Bpm, section.RampSeconds, section.Style, section.AlignBeat, section.CountIn, section.Rhythm, section.SwingPercent);
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
            _back = AddButton(transport, "−5 seconds", () => SeekRelative(-5));
            _play = AddButton(transport, "Play / pause", () => { try { _togglePlayback(); PollPlayback(); } catch (Exception ex) { _status.Text = ex.Message; } });
            _forward = AddButton(transport, "+5 seconds", () => SeekRelative(5));
            _timeline.Duration = Math.Max(0, duration); _timeline.Dock = DockStyle.Fill;
            _timeline.SeekRequested += SeekTo;
            _timeline.MarkerSelected += row => { if (row >= 0 && row < _grid.Rows.Count) _grid.CurrentCell = _grid.Rows[row].Cells[0]; };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _add = AddButton(actions, "Add at playhead", () =>
            {
                var now = _position();
                if (!now.HasValue) { _status.Text = "Play the original song to capture its position."; return; }
                var selected = _grid.CurrentRow;
                double bpm = 120;
                if (selected != null) double.TryParse(Convert.ToString(selected.Cells[1].Value), out bpm);
                var rhythm = selected == null ? PartyRhythm.Straight :
                    (PartyRhythm)Math.Max(0, Array.IndexOf(Rhythms, Convert.ToString(selected.Cells[6].Value)));
                double swingPercent;
                if (selected == null || !double.TryParse(Convert.ToString(selected.Cells[7].Value), out swingPercent)) swingPercent = 66.67;
                AddRow(Math.Round(now.Value, 3), bpm >= 40 && bpm <= 240 ? bpm : 120, 0, PartyDanceStyle.Normal, false, false, rhythm, swingPercent);
                _grid.CurrentCell = _grid.Rows[_grid.Rows.Count - 1].Cells[0];
                MarkDirty();
            });
            AddButton(actions, "Delete row", () => { if (_grid.CurrentRow != null) { _grid.Rows.Remove(_grid.CurrentRow); MarkDirty(); } });
            _seekRow = AddButton(actions, "Seek to row", () =>
            {
                if (_grid.CurrentRow == null) return;
                try { SeekTo(Number(_grid.CurrentRow, 0)); }
                catch (Exception ex) { _status.Text = ex.Message; }
            });
            AddButton(actions, "Save", SaveMap);
            AddButton(actions, "Close", () => Close());
            _status.Text = "Click a section diamond to select and seek. Drag the playhead to scrub; Save applies edits and keeps this window open.";
            _status.ForeColor = Color.FromArgb(178, 192, 212);
            help.Text = "Sections last until the next start; times are seconds. First row starts at 0. Save applies edits without closing.\r\n" +
                "BPM ramp: 120 to 150 over 4 seconds = gradual tempo change. Equal BPM values do nothing; dance styles change at the start.\r\n" +
                "Bob count-in: tick the Normal row after Half speed for lead-in bobs PLUS a final bop on the return beat.\r\n" +
                "Waltz = side, centre bop, centre bop. Swing = long side, short middle; Swing %: 50 = even, 66.67 = about 2:1, 75 = strong.\r\n" +
                "4/4 accent on 4 = centre, centre, centre, SIDE. Align marks FOUR; BPM counts every beat.\r\n" +
                "Hold pose stops all motion. Align restarts a side pose. Colours: blue = normal, purple = side to side, amber = half speed, grey = hold.";
            _enabled.Dock = DockStyle.Fill; _enabled.Padding = Padding.Empty;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 7 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.Controls.Add(help, 0, 0); layout.Controls.Add(_timeline, 0, 1);
            layout.Controls.Add(transport, 0, 2); layout.Controls.Add(_grid, 0, 3);
            layout.Controls.Add(_enabled, 0, 4); layout.Controls.Add(_status, 0, 5); layout.Controls.Add(actions, 0, 6);
            Controls.Add(layout);
            _grid.CellValueChanged += (sender, args) => { RefreshSwingCells(); MarkDirty(); };
            _grid.CellToolTipTextNeeded += (sender, args) =>
            {
                if (args.ColumnIndex >= 0) args.ToolTipText = _grid.Columns[args.ColumnIndex].ToolTipText;
            };
            _grid.CurrentCellDirtyStateChanged += (sender, args) =>
            {
                if (_grid.IsCurrentCellDirty && (_grid.CurrentCell is DataGridViewCheckBoxCell ||
                    _grid.CurrentCell is DataGridViewComboBoxCell)) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellBeginEdit += (sender, args) => MarkDirty();
            _grid.SelectionChanged += (sender, args) => RefreshMarkers();
            _enabled.CheckedChanged += (sender, args) => MarkDirty();
            _timer.Tick += (sender, args) => PollPlayback();
            Shown += (sender, args) => { RefreshMarkers(); PollPlayback(); _timer.Start(); };
            FormClosing += (sender, args) =>
            {
                if (_dirty && MessageBox.Show(this, "Close without saving your latest edits? Earlier saves will stay applied.",
                    "Unsaved tempo-map edits", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    args.Cancel = true;
            };
            _grid.DataError += (sender, args) => { args.ThrowException = false; };
        }

        private void AddRow(double start, double bpm, double ramp, PartyDanceStyle style, bool align, bool countIn = false, PartyRhythm rhythm = PartyRhythm.Straight, double swingPercent = 66.67)
        {
            _grid.Rows.Add(start.ToString("0.###", CultureInfo.CurrentCulture), bpm.ToString("0.###", CultureInfo.CurrentCulture),
                ramp.ToString("0.###", CultureInfo.CurrentCulture), Styles[(int)style], align, countIn, Rhythms[(int)rhythm], swingPercent.ToString("0.##", CultureInfo.CurrentCulture));
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

        private static Button AddButton(FlowLayoutPanel panel, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(43, 53, 73), ForeColor = Color.FromArgb(238, 241, 248), Padding = new Padding(6, 2, 6, 2) };
            button.FlatAppearance.BorderColor = Color.FromArgb(83, 99, 124);
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
                        Style = (PartyDanceStyle)Math.Max(0, Array.IndexOf(Styles, Convert.ToString(row.Cells[3].Value))) });
            }
            _timeline.SelectedRow = _grid.CurrentRow?.Index ?? -1;
            _timeline.Invalidate();
        }

        private void PollPlayback()
        {
            try
            {
                var position = _position(); var available = position.HasValue;
                _timeline.Enabled = _back.Enabled = _forward.Enabled = _seekRow.Enabled = available && _timeline.Duration > 0;
                _play.Enabled = _add.Enabled = available;
                _play.Text = available && _playing() ? "Pause" : "Play";
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
            var position = _position();
            if (position.HasValue) SeekTo(position.Value + seconds);
        }

        private void SeekTo(double seconds)
        {
            try
            {
                if (!_position().HasValue || _timeline.Duration <= 0) return;
                var clamped = Math.Max(0, Math.Min(_timeline.Duration, seconds));
                _seek(checked((int)Math.Round(clamped * 1000)));
                _timeline.Position = clamped; _timeline.Invalidate();
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _editorFont.Dispose(); }
            base.Dispose(disposing);
        }

        private static double Number(DataGridViewRow row, int column)
        {
            double value;
            if (!double.TryParse(Convert.ToString(row.Cells[column].Value), NumberStyles.Float,
                CultureInfo.CurrentCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Enter valid numbers for start, BPM, ramp and Swing % on every row.");
            return value;
        }

        private void SaveMap()
        {
            try
            {
                _grid.EndEdit();
                var map = new PartyTempoMap { TrackUrl = _source.TrackUrl, InitialBeat = _source.InitialBeat, Enabled = _enabled.Checked };
                foreach (DataGridViewRow row in _grid.Rows)
                    map.Sections.Add(new PartyTempoSection { StartSeconds = Number(row, 0), Bpm = Number(row, 1),
                        RampSeconds = Number(row, 2), Style = (PartyDanceStyle)Array.IndexOf(Styles, Convert.ToString(row.Cells[3].Value)),
                        AlignBeat = Convert.ToBoolean(row.Cells[4].Value ?? false),
                        CountIn = Convert.ToBoolean(row.Cells[5].Value ?? false),
                        Rhythm = (PartyRhythm)Array.IndexOf(Rhythms, Convert.ToString(row.Cells[6].Value)),
                        SwingPercent = Number(row, 7) });
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
