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
        private static readonly string[] Styles = { "Normal", "Side to side", "Half speed", "Hold pose" };

        internal FrmPartyTempoMap(PartyTempoMap map, string title, Func<double?> position,
            Action<int> seek, Action<PartyTempoMap> save)
        {
            _source = map; _position = position; _seek = seek; _save = save;
            Text = "Tempo map — " + title;
            Size = new Size(850, 480); MinimumSize = new Size(740, 410);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            MinimizeBox = false; TopMost = true;
            var help = new Label { Dock = DockStyle.Top, Height = 78, Padding = new Padding(10),
                Text = "Sections last until the next start time. First row starts at 0. Times are seconds (decimals allowed)." +
                    "\r\nRamp smoothly reaches the row's BPM from the previous tempo; 0 changes speed immediately." +
                    "\r\nHold pose freezes until the next row. Align starts a side pose on that row's start; leave it off for continuous phase." };
            _enabled.Text = "Use this map for this song"; _enabled.Checked = map.Enabled;
            _enabled.Dock = DockStyle.Top; _enabled.Height = 28; _enabled.Padding = new Padding(10, 0, 0, 0);
            _grid.Dock = DockStyle.Fill; _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; _grid.MultiSelect = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add("start", "Start (s)"); _grid.Columns.Add("bpm", "BPM");
            _grid.Columns.Add("ramp", "Ramp (s)");
            _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "style", HeaderText = "Dance", DataSource = Styles });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "align", HeaderText = "Align" });
            foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            foreach (var section in map.Sections) AddRow(section.StartSeconds, section.Bpm, section.RampSeconds, section.Style, section.AlignBeat);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 72, Padding = new Padding(8), WrapContents = true };
            AddButton(actions, "Add at current time", () =>
            {
                var now = _position();
                if (!now.HasValue) { MessageBox.Show(this, "Play the song being edited to capture its position."); return; }
                var selected = _grid.CurrentRow;
                double bpm = 120;
                if (selected != null) double.TryParse(Convert.ToString(selected.Cells[1].Value), out bpm);
                AddRow(Math.Round(now.Value, 3), bpm >= 40 && bpm <= 240 ? bpm : 120, 0, PartyDanceStyle.Normal, false);
                _grid.CurrentCell = _grid.Rows[_grid.Rows.Count - 1].Cells[0];
            });
            AddButton(actions, "Delete row", () => { if (_grid.CurrentRow != null) _grid.Rows.Remove(_grid.CurrentRow); });
            AddButton(actions, "Play from row", () =>
            {
                if (_grid.CurrentRow == null) return;
                try { _seek(checked((int)Math.Round(Number(_grid.CurrentRow, 0) * 1000))); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Play from row"); }
            });
            AddButton(actions, "Save", SaveMap);
            AddButton(actions, "Cancel", () => Close());
            Controls.Add(_grid); Controls.Add(_enabled); Controls.Add(help); Controls.Add(actions);
            _grid.DataError += (sender, args) => { args.ThrowException = false; };
        }

        private void AddRow(double start, double bpm, double ramp, PartyDanceStyle style, bool align)
        {
            _grid.Rows.Add(start.ToString("0.###", CultureInfo.CurrentCulture), bpm.ToString("0.###", CultureInfo.CurrentCulture),
                ramp.ToString("0.###", CultureInfo.CurrentCulture), Styles[(int)style], align);
        }

        private static void AddButton(FlowLayoutPanel panel, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 28 };
            button.Click += (sender, args) => action(); panel.Controls.Add(button);
        }

        private static double Number(DataGridViewRow row, int column)
        {
            double value;
            if (!double.TryParse(Convert.ToString(row.Cells[column].Value), NumberStyles.Float,
                CultureInfo.CurrentCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Enter valid numbers for start, BPM and ramp on every row.");
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
                        AlignBeat = Convert.ToBoolean(row.Cells[4].Value ?? false) });
                map.Sections = map.Sections.OrderBy(s => s.StartSeconds).ToList();
                map.Validate();
                _save(map); DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Tempo map", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
