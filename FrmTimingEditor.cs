using System;
using System.Drawing;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    // The visualizer stays open while this dedicated editing mode is active.
    internal sealed class FrmTimingEditor : Form
    {
        private readonly Plugin.MusicBeeApiInterface _musicBee;
        private readonly string _trackUrl, _expectedTag;
        private readonly LrcTimingDocument _document;
        private readonly Action<string, string> _previewLyrics;
        private readonly Action<string> _cancelPreview;
        private readonly Action<string, string> _lyricsSaved;
        private readonly DataGridView _rows;
        private readonly Label _status;
        private readonly Button _save, _reset;
        private readonly FlowLayoutPanel _wholeSong;
        private bool _saved, _trackChanged, _forceClose;

        public string TrackUrl => _trackUrl;

        public FrmTimingEditor(Plugin.MusicBeeApiInterface musicBee, string trackUrl,
            string title, string expectedTag,
            LrcTimingDocument document, Action<string, string> previewLyrics,
            Action<string> cancelPreview, Action<string, string> lyricsSaved)
        {
            _musicBee = musicBee;
            _trackUrl = trackUrl;
            _expectedTag = expectedTag;
            _document = document;
            _previewLyrics = previewLyrics;
            _cancelPreview = cancelPreview;
            _lyricsSaved = lyricsSaved;

            Text = "Edit lyric timing";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(20, 24, 37);
            ForeColor = Color.FromArgb(236, 238, 247);
            Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
            MinimumSize = new Size(660, 370);
            Size = new Size(960, 610);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(17, 13, 17, 13),
                ColumnCount = 1,
                RowCount = 5,
                BackColor = BackColor
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            Controls.Add(root);

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = "Edit timing  ·  " + (string.IsNullOrWhiteSpace(title) ? trackUrl : title),
                Font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft
            };
            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "Click a lyric to play from that line, or use the arrows to adjust its time. Save writes the Lyrics field in MusicBee.",
                ForeColor = Color.FromArgb(178, 187, 206),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            }, 0, 1);

            _wholeSong = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 2)
            };
            _wholeSong.Controls.Add(new Label
            {
                Text = "Whole song",
                AutoSize = false,
                Width = 110,
                Height = 35,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(217, 219, 235)
            });
            AddShiftButton(_wholeSong, "−1s", -1000);
            AddShiftButton(_wholeSong, "← 0.1s", -100);
            AddShiftButton(_wholeSong, "+ 0.1s →", 100);
            AddShiftButton(_wholeSong, "+1s", 1000);
            root.Controls.Add(_wholeSong, 0, 2);

            _rows = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.FromArgb(23, 27, 42),
                BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(52, 58, 78),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 34,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ScrollBars = ScrollBars.Vertical
            };
            _rows.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 40, 58);
            _rows.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(216, 224, 240);
            _rows.DefaultCellStyle.BackColor = Color.FromArgb(26, 30, 46);
            _rows.DefaultCellStyle.ForeColor = Color.FromArgb(238, 239, 248);
            _rows.DefaultCellStyle.SelectionBackColor = Color.FromArgb(58, 67, 91);
            _rows.DefaultCellStyle.SelectionForeColor = Color.White;
            _rows.DefaultCellStyle.Padding = new Padding(5, 8, 5, 8);
            _rows.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _rows.RowTemplate.Height = 38;
            _rows.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Time",
                Width = 100,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _rows.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Lyric",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 200,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            AddRowButton("−1s", 55);
            AddRowButton("← 0.1", 62);
            AddRowButton("0.1 →", 62);
            AddRowButton("+1s", 55);
            foreach (var entry in _document.Entries)
            {
                var index = _rows.Rows.Add(LrcTimingDocument.FormatTime(entry.TimeMs),
                    entry.Text);
                _rows.Rows[index].Tag = entry;
                _rows.Rows[index].Cells[1].ToolTipText = entry.Text;
            }
            _rows.CellContentClick += RowClicked;
            _rows.CellClick += LyricClicked;
            root.Controls.Add(_rows, 0, 3);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 9, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 294));
            _status = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(174, 204, 230),
                Text = "No changes yet",
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            footer.Controls.Add(_status, 0, 0);
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };
            _save = ActionButton("Save edits", 92);
            _save.Enabled = false;
            _save.Click += SaveClicked;
            var cancel = ActionButton("Cancel", 77);
            cancel.Click += (sender, args) => Close();
            _reset = ActionButton("Reset", 77);
            _reset.Enabled = false;
            _reset.Click += (sender, args) => ResetChanges();
            actions.Controls.Add(_save);
            actions.Controls.Add(cancel);
            actions.Controls.Add(_reset);
            footer.Controls.Add(actions, 1, 0);
            root.Controls.Add(footer, 0, 4);

            Shown += (sender, args) => SelectCurrentLine();
            FormClosing += EditorClosing;
            FormClosed += (sender, args) =>
            {
                if (!_saved)
                    try { _cancelPreview?.Invoke(_trackUrl); }
                    catch (Exception) { }
            };
        }

        private static Button ActionButton(string label, int width)
        {
            return new Button
            {
                Text = label,
                Width = width,
                Height = 35,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(49, 57, 79),
                ForeColor = Color.White,
                Margin = new Padding(3, 0, 3, 0)
            };
        }

        private void AddShiftButton(FlowLayoutPanel panel, string label, int delta)
        {
            var button = ActionButton(label, label.Length > 4 ? 93 : 65);
            button.Click += (sender, args) => ShiftAll(delta);
            panel.Controls.Add(button);
        }

        private void AddRowButton(string label, int width)
        {
            _rows.Columns.Add(new DataGridViewButtonColumn
            {
                HeaderText = label,
                Text = label,
                UseColumnTextForButtonValue = true,
                Width = width,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private void RowClicked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 2 || _trackChanged) return;
            var delta = e.ColumnIndex == 2 ? -1000 : e.ColumnIndex == 3 ? -100 :
                e.ColumnIndex == 4 ? 100 : 1000;
            var entry = (LrcTimingDocument.TimingEntry)_rows.Rows[e.RowIndex].Tag;
            if (!_document.ShiftEntry(entry, delta))
            {
                _status.Text = "That line cannot move past the song start or an adjacent line.";
                return;
            }
            RefreshTimes();
            PreviewAndSeek(entry);
        }

        private void LyricClicked(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.ColumnIndex > 1 ||
                _trackChanged) return;
            var entry = (LrcTimingDocument.TimingEntry)_rows.Rows[e.RowIndex].Tag;
            SeekToEntry(entry, false);
        }

        private void ShiftAll(int delta)
        {
            if (_trackChanged) return;
            if (_document.ShiftAll(delta) == 0)
            {
                _status.Text = "The first timestamp cannot go earlier than 00:00.00.";
                return;
            }
            RefreshTimes();
            var selected = _rows.CurrentRow?.Tag as LrcTimingDocument.TimingEntry;
            PreviewAndSeek(selected ?? _document.Entries[0]);
        }

        private void RefreshTimes()
        {
            foreach (DataGridViewRow row in _rows.Rows)
            {
                var entry = (LrcTimingDocument.TimingEntry)row.Tag;
                row.Cells[0].Value = LrcTimingDocument.FormatTime(entry.TimeMs);
                row.Cells[0].Style.ForeColor = entry.TimeMs == entry.OriginalTimeMs ?
                    Color.FromArgb(238, 239, 248) : Color.FromArgb(137, 209, 237);
            }
            _save.Enabled = _document.IsDirty;
            _reset.Enabled = _document.IsDirty && !_trackChanged;
        }

        private void PreviewAndSeek(LrcTimingDocument.TimingEntry entry)
        {
            try
            {
                _previewLyrics?.Invoke(_trackUrl, _document.BuildLyrics());
                _status.Text = "Timing preview updated. Save edits to keep it.";
            }
            catch (Exception)
            {
                _status.Text = "Timing changed, but the visual preview is unavailable.";
            }
            SeekToEntry(entry, true);
        }

        private void SeekToEntry(LrcTimingDocument.TimingEntry entry, bool timingChanged)
        {
            try
            {
                if (_musicBee.NowPlaying_GetFileUrl() != _trackUrl)
                {
                    TrackChanged();
                    return;
                }
                var state = _musicBee.Player_GetPlayState();
                var position = (int)Math.Max(0L, Math.Min(int.MaxValue,
                    (long)entry.TimeMs - _document.OffsetMs - 800));
                if (_musicBee.Player_SetPosition == null ||
                    !_musicBee.Player_SetPosition(position))
                    _status.Text = timingChanged ?
                        "Timing preview updated, but MusicBee could not seek playback." :
                        "MusicBee could not seek to that line.";
                else
                {
                    if (state == Plugin.PlayState.Paused || state == Plugin.PlayState.Stopped)
                        _musicBee.Player_PlayPause?.Invoke();
                    if (!timingChanged)
                        _status.Text = "Playing from " + LrcTimingDocument.FormatTime(entry.TimeMs) + ".";
                }
            }
            catch (Exception)
            {
                _status.Text = timingChanged ?
                    "Timing preview updated, but playback could not be moved." :
                    "Playback could not be moved to that line.";
            }
        }

        private void ResetChanges()
        {
            _document.Reset();
            RefreshTimes();
            try { _cancelPreview?.Invoke(_trackUrl); }
            catch (Exception) { }
            _status.Text = "Original timings restored. Nothing to save.";
        }

        private void SaveClicked(object sender, EventArgs args)
        {
            if (!_document.IsDirty) return;
            string error;
            var updated = _document.BuildLyrics();
            if (!TimingTagStore.Save(_musicBee, _trackUrl, _document.OriginalLyrics,
                _expectedTag, updated, out error))
            {
                _status.Text = error;
                MessageBox.Show(this, error, "Could not save lyric timing",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _saved = true;
            try { _lyricsSaved?.Invoke(_trackUrl, updated); }
            catch (Exception) { /* The lyrics were already saved by MusicBee. */ }
            Close();
        }

        public void TrackChanged()
        {
            if (_trackChanged) return;
            _trackChanged = true;
            try { _cancelPreview?.Invoke(_trackUrl); }
            catch (Exception) { }
            if (!_document.IsDirty)
            {
                _forceClose = true;
                Close();
                return;
            }
            _wholeSong.Enabled = false;
            _rows.Enabled = false;
            _reset.Enabled = false;
            _status.Text = "Song changed. Save these edits to the previous song, or Cancel.";
        }

        public void ForceClose()
        {
            _forceClose = true;
            Close();
        }

        private void SelectCurrentLine()
        {
            if (_rows.Rows.Count == 0) return;
            try
            {
                var position = _musicBee.Player_GetPosition() + _document.OffsetMs;
                var selected = 0;
                for (var i = 0; i < _document.Entries.Count; i++)
                {
                    if (_document.Entries[i].TimeMs > position) break;
                    selected = i;
                }
                _rows.CurrentCell = _rows.Rows[selected].Cells[0];
                _rows.FirstDisplayedScrollingRowIndex = Math.Max(0, selected - 2);
            }
            catch (Exception) { /* The song may have changed while opening. */ }
        }

        private void EditorClosing(object sender, FormClosingEventArgs e)
        {
            if (_saved || _forceClose || !_document.IsDirty) return;
            if (MessageBox.Show(this, "Discard the unsaved timing changes?",
                    "Edit lyric timing", MessageBoxButtons.YesNo, MessageBoxIcon.Question) !=
                DialogResult.Yes)
                e.Cancel = true;
        }
    }
}
