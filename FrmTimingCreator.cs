using System;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    // Only opened for plain lyrics. Stamp lines as the song plays, then use the
    // same guarded MusicBee tag writer and backup as the existing timing editor.
    internal sealed class FrmTimingCreator : Form
    {
        private readonly Plugin.MusicBeeApiInterface _musicBee;
        private readonly string _trackUrl, _expectedTag;
        private readonly UntimedTimingDocument _document;
        private readonly Action<string, string> _preview, _saved;
        private readonly Action<string> _cancel;
        private readonly DataGridView _rows;
        private readonly Label _status;
        private readonly Button _save, _stamp;
        private readonly FlowLayoutPanel _offset;
        private bool _trackChanged, _savedSuccessfully, _forceClose;

        public string TrackUrl => _trackUrl;

        public FrmTimingCreator(Plugin.MusicBeeApiInterface musicBee, string trackUrl,
            string title, string expectedTag, UntimedTimingDocument document,
            Action<string, string> preview, Action<string> cancel,
            Action<string, string> saved)
        {
            _musicBee = musicBee;
            _trackUrl = trackUrl;
            _expectedTag = expectedTag;
            _document = document;
            _preview = preview;
            _cancel = cancel;
            _saved = saved;
            Text = "Create lyric timing";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            BackColor = Color.FromArgb(20, 24, 37);
            ForeColor = Color.FromArgb(236, 238, 247);
            Font = new Font("Segoe UI", 10f);
            MinimumSize = new Size(650, 370);
            Size = new Size(900, 620);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(17, 13, 17, 13),
                ColumnCount = 1, RowCount = 5
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            Controls.Add(root);
            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "Create timing  ·  " + (string.IsNullOrWhiteSpace(title) ? trackUrl : title),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold), ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "Play the song and press Space (or Stamp next) at each line. Click a stamped lyric to hear it; adjust with the arrows. Save when all lines are stamped.",
                ForeColor = Color.FromArgb(178, 187, 206),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);

            _offset = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            _stamp = MakeButton("Stamp next  (Space)", 165);
            _stamp.Click += (sender, args) => StampNext();
            _offset.Controls.Add(_stamp);
            _offset.Controls.Add(new Label
            {
                Text = "Whole song", Width = 99, Height = 35,
                TextAlign = ContentAlignment.MiddleRight
            });
            AddOffset("−1s", -1000);
            AddOffset("← 0.1s", -100);
            AddOffset("+0.1s →", 100);
            AddOffset("+1s", 1000);
            root.Controls.Add(_offset, 0, 2);

            _rows = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = Color.FromArgb(23, 27, 42),
                BorderStyle = BorderStyle.None, GridColor = Color.FromArgb(52, 58, 78),
                RowHeadersVisible = false, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                ReadOnly = true, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EnableHeadersVisualStyles = false,
                ScrollBars = ScrollBars.Vertical,
                ColumnHeadersHeight = 34,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            _rows.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 40, 58);
            _rows.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            _rows.DefaultCellStyle.BackColor = Color.FromArgb(26, 30, 46);
            _rows.DefaultCellStyle.ForeColor = Color.FromArgb(238, 239, 248);
            _rows.DefaultCellStyle.SelectionBackColor = Color.FromArgb(58, 67, 91);
            _rows.DefaultCellStyle.SelectionForeColor = Color.White;
            _rows.RowTemplate.Height = 37;
            _rows.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Time", Width = 105,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _rows.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Lyric", MinimumWidth = 180,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            AddRowButton("Stamp", 65);
            AddRowButton("−0.1", 62);
            AddRowButton("+0.1", 62);
            foreach (var line in document.Entries)
            {
                var row = _rows.Rows.Add("—", line.Text);
                _rows.Rows[row].Tag = line;
                _rows.Rows[row].Cells[1].ToolTipText = line.Text;
            }
            _rows.CellContentClick += RowClicked;
            _rows.CellClick += LyricClicked;
            root.Controls.Add(_rows, 0, 3);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0, 8, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 274));
            _status = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(174, 204, 230), AutoEllipsis = true
            };
            footer.Controls.Add(_status, 0, 0);
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            _save = MakeButton("Save timing", 104);
            _save.Click += SaveClicked;
            var cancel = MakeButton("Cancel", 72);
            cancel.Click += (sender, args) => Close();
            var reset = MakeButton("Reset", 72);
            reset.Click += (sender, args) =>
            {
                _document.Reset();
                _cancel?.Invoke(_trackUrl);
                RefreshRows();
            };
            actions.Controls.Add(_save);
            actions.Controls.Add(cancel);
            actions.Controls.Add(reset);
            footer.Controls.Add(actions, 1, 0);
            root.Controls.Add(footer, 0, 4);
            RefreshRows();
            FormClosing += (sender, args) =>
            {
                if (_savedSuccessfully || _forceClose || !_document.IsDirty) return;
                if (MessageBox.Show(this, "Discard the new lyric timings?",
                    "Create lyric timing", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes) args.Cancel = true;
            };
            FormClosed += (sender, args) =>
            {
                if (!_savedSuccessfully) _cancel?.Invoke(_trackUrl);
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space && !_trackChanged && !_savedSuccessfully)
            {
                StampNext();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static Button MakeButton(string label, int width)
        {
            return new Button
            {
                Text = label, Width = width, Height = 35,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(49, 57, 79),
                ForeColor = Color.White, Margin = new Padding(3, 0, 3, 0)
            };
        }

        private void AddOffset(string label, int delta)
        {
            var button = MakeButton(label, label.Length > 4 ? 85 : 61);
            button.Click += (sender, args) =>
            {
                if (_document.ShiftAll(delta) == 0)
                    _status.Text = "The stamped lines cannot be shifted further.";
                else { RefreshRows(); Preview(); }
            };
            _offset.Controls.Add(button);
        }

        private void AddRowButton(string label, int width)
        {
            _rows.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = label, Text = label, UseColumnTextForButtonValue = true,
                Width = width, FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private bool IsCurrentTrack()
        {
            try
            {
                if (!_trackChanged && _musicBee.NowPlaying_GetFileUrl() == _trackUrl)
                    return true;
            }
            catch (Exception) { }
            TrackChanged();
            return false;
        }

        private void StampNext()
        {
            if (!IsCurrentTrack()) return;
            for (var i = 0; i < _document.Entries.Count; i++)
                if (!_document.Entries[i].TimeMs.HasValue)
                {
                    Stamp(i);
                    return;
                }
            _status.Text = "Every line has a timestamp. Fine tune, then save.";
        }

        private void Stamp(int index)
        {
            if (!IsCurrentTrack()) return;
            try
            {
                var position = _musicBee.Player_GetPosition();
                if (!_document.Stamp(index, position))
                {
                    _status.Text = "Play past the preceding line before stamping this one.";
                    return;
                }
                RefreshRows();
                Preview();
                if (index + 1 < _rows.Rows.Count)
                {
                    _rows.CurrentCell = _rows.Rows[index + 1].Cells[0];
                    if (_rows.IsHandleCreated && index + 1 >= _rows.FirstDisplayedScrollingRowIndex +
                        _rows.DisplayedRowCount(false))
                        _rows.FirstDisplayedScrollingRowIndex = Math.Max(0, index - 2);
                }
            }
            catch (Exception) { _status.Text = "MusicBee could not read playback position."; }
        }

        private void RowClicked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 2 || !IsCurrentTrack()) return;
            if (e.ColumnIndex == 2) { Stamp(e.RowIndex); return; }
            if (!_document.Adjust(e.RowIndex, e.ColumnIndex == 3 ? -100 : 100))
            {
                _status.Text = "Stamp this line first, or keep it between its neighbours.";
                return;
            }
            RefreshRows();
            Preview();
            Seek(e.RowIndex);
        }

        private void LyricClicked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex < 2 && IsCurrentTrack()) Seek(e.RowIndex);
        }

        private void Seek(int index)
        {
            var time = _document.Entries[index].TimeMs;
            if (!time.HasValue) { _status.Text = "Stamp this lyric before previewing it."; return; }
            try
            {
                if (_musicBee.Player_SetPosition(Math.Max(0, time.Value - 800)) &&
                    _musicBee.Player_GetPlayState() != Plugin.PlayState.Playing)
                    _musicBee.Player_PlayPause?.Invoke();
            }
            catch (Exception) { _status.Text = "MusicBee could not seek to that lyric."; }
        }

        private void Preview()
        {
            try { _preview?.Invoke(_trackUrl, _document.BuildLyrics()); }
            catch (Exception) { _status.Text = "Timing updated, but the preview is unavailable."; }
        }

        private void RefreshRows()
        {
            var count = 0;
            for (var i = 0; i < _rows.Rows.Count; i++)
            {
                var time = _document.Entries[i].TimeMs;
                _rows.Rows[i].Cells[0].Value = time.HasValue ?
                    LrcTimingDocument.FormatTime(time.Value) : "—";
                if (time.HasValue) count++;
            }
            _save.Enabled = _document.IsComplete;
            _stamp.Enabled = !_document.IsComplete && !_trackChanged;
            _status.Text = count + " of " + _rows.Rows.Count + " lines stamped" +
                (_document.IsComplete ? " — ready to save." : ".");
        }

        private void SaveClicked(object sender, EventArgs args)
        {
            if (!_document.IsComplete) return;
            var lyrics = _document.BuildLyrics();
            string error;
            if (!TimingTagStore.Save(_musicBee, _trackUrl, _document.OriginalLyrics,
                _expectedTag, lyrics, out error))
            {
                _status.Text = error;
                MessageBox.Show(this, error, "Could not save lyric timing",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _savedSuccessfully = true;
            try { _saved?.Invoke(_trackUrl, lyrics); }
            catch (Exception) { }
            Close();
        }

        public void TrackChanged()
        {
            if (_trackChanged) return;
            _trackChanged = true;
            _cancel?.Invoke(_trackUrl);
            if (!_document.IsDirty) { _forceClose = true; Close(); return; }
            _rows.Enabled = _offset.Enabled = _stamp.Enabled = false;
            _status.Text = "Song changed. Save this song's completed timings or Cancel.";
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }
    }
}
