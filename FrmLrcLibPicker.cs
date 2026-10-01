using System;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal sealed class FrmLrcLibPicker : Form
    {
        private readonly Plugin.MusicBeeApiInterface _musicBee;
        private readonly string _trackUrl, _expectedTag;
        private readonly int _durationMs;
        private readonly Action<string, string> _lyricsSaved;
        private readonly TextBox _query, _preview;
        private readonly DataGridView _results;
        private readonly Label _status;
        private readonly Button _search, _import;
        private CancellationTokenSource _searchToken;
        private bool _trackChanged;

        public string TrackUrl => _trackUrl;

        public FrmLrcLibPicker(Plugin.MusicBeeApiInterface musicBee, string trackUrl,
            string title, string artist, int durationMs, string expectedTag,
            Action<string, string> lyricsSaved)
        {
            _musicBee = musicBee;
            _trackUrl = trackUrl;
            _expectedTag = expectedTag;
            _durationMs = durationMs;
            _lyricsSaved = lyricsSaved;

            Text = "Find lyrics on LRCLIB";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(20, 24, 37);
            ForeColor = Color.FromArgb(236, 238, 247);
            Font = new Font("Segoe UI", 10f);
            MinimumSize = new Size(710, 455);
            Size = new Size(960, 670);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(17, 12, 17, 13),
                ColumnCount = 1, RowCount = 6
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 185));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Controls.Add(root);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "Find lyrics  ·  " + title + " — " + artist +
                    (_durationMs > 0 ? "  (" + FormatDuration(_durationMs / 1000.0) + ")" : ""),
                Font = new Font("Segoe UI", 13f, FontStyle.Bold),
                ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var searchBar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Padding = new Padding(0, 3, 0, 5)
            };
            searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 113));
            _query = new TextBox
            {
                Dock = DockStyle.Fill, Text = (title + " " + artist).Trim(),
                BackColor = Color.FromArgb(30, 36, 54), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            _query.KeyDown += (sender, args) =>
            {
                if (args.KeyCode != Keys.Enter) return;
                args.SuppressKeyPress = true;
                SearchClicked(sender, EventArgs.Empty);
            };
            searchBar.Controls.Add(_query, 0, 0);
            _search = MakeButton("Search", 103);
            _search.Click += SearchClicked;
            searchBar.Controls.Add(_search, 1, 0);
            root.Controls.Add(searchBar, 0, 1);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "Select a result by duration. Plain lyrics can be saved and timed later with the TIMING editor.",
                ForeColor = Color.FromArgb(178, 187, 206),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 2);

            _results = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = Color.FromArgb(23, 27, 42),
                BorderStyle = BorderStyle.None, GridColor = Color.FromArgb(52, 58, 78),
                RowHeadersVisible = false, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, ReadOnly = true, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                EnableHeadersVisualStyles = false, ColumnHeadersHeight = 32,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ScrollBars = ScrollBars.Vertical
            };
            _results.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 40, 58);
            _results.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(216, 224, 240);
            _results.DefaultCellStyle.BackColor = Color.FromArgb(26, 30, 46);
            _results.DefaultCellStyle.ForeColor = Color.FromArgb(238, 239, 248);
            _results.DefaultCellStyle.SelectionBackColor = Color.FromArgb(58, 67, 91);
            _results.DefaultCellStyle.SelectionForeColor = Color.White;
            _results.RowTemplate.Height = 33;
            AddTextColumn("Length", 73);
            AddTextColumn("Difference", 85);
            _results.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Song", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 125, SortMode = DataGridViewColumnSortMode.NotSortable
            });
            AddTextColumn("Artist", 155);
            AddTextColumn("Album", 150);
            AddTextColumn("Timing", 70);
            _results.SelectionChanged += (sender, args) => ShowSelection();
            root.Controls.Add(_results, 0, 3);

            var previewPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                Padding = new Padding(0, 8, 0, 0)
            };
            previewPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            previewPanel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, Text = "LYRIC PREVIEW",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(155, 183, 218)
            }, 0, 0);
            _preview = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Vertical, WordWrap = false,
                BackColor = Color.FromArgb(24, 29, 45),
                ForeColor = Color.FromArgb(216, 224, 239),
                Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle
            };
            previewPanel.Controls.Add(_preview, 0, 1);
            root.Controls.Add(previewPanel, 0, 4);

            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                Padding = new Padding(0, 9, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            _status = new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true,
                ForeColor = Color.FromArgb(174, 204, 230),
                Text = "Searching LRCLIB…", TextAlign = ContentAlignment.MiddleLeft
            };
            footer.Controls.Add(_status, 0, 0);
            _import = MakeButton("Save to MusicBee", 160);
            _import.Enabled = false;
            _import.Click += ImportClicked;
            footer.Controls.Add(_import, 1, 0);
            var close = MakeButton("Close", 75);
            close.Click += (sender, args) => Close();
            footer.Controls.Add(close, 2, 0);
            root.Controls.Add(footer, 0, 5);

            Shown += SearchClicked;
            FormClosed += (sender, args) => _searchToken?.Cancel();
        }

        private static Button MakeButton(string text, int width)
        {
            return new Button
            {
                Text = text, Width = width, Height = 34,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(49, 57, 79), ForeColor = Color.White,
                Margin = new Padding(3, 0, 3, 0)
            };
        }

        private void AddTextColumn(string label, int width)
        {
            _results.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = label, Width = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private async void SearchClicked(object sender, EventArgs args)
        {
            if (_trackChanged || !_search.Enabled) return;
            var query = _query.Text.Trim();
            if (query.Length == 0)
            {
                _status.Text = "Enter a song title or artist to search.";
                return;
            }
            _search.Enabled = false;
            _import.Enabled = false;
            _results.Rows.Clear();
            _preview.Clear();
            _status.Text = "Searching LRCLIB…";
            var token = new CancellationTokenSource();
            _searchToken = token;
            try
            {
                var records = await LrcLibClient.SearchAsync(query, token.Token);
                if (IsDisposed || _trackChanged) return;
                records = LrcLibClient.SortByDuration(records, _durationMs);
                foreach (var record in records)
                {
                    var length = record.DurationSeconds;
                    var difference = _durationMs <= 0 || length <= 0 ? "—" :
                        Math.Abs(length - _durationMs / 1000.0) < 0.5 ? "Exact" :
                        (length > _durationMs / 1000.0 ? "+" : "−") +
                        FormatDuration(Math.Abs(length - _durationMs / 1000.0));
                    var index = _results.Rows.Add(length <= 0 ? "—" :
                        FormatDuration(length), difference,
                        record.TrackName, record.ArtistName, record.AlbumName,
                        record.HasTimedLyrics ? "Timed" : "Plain");
                    _results.Rows[index].Tag = record;
                    if (!record.HasTimedLyrics)
                        _results.Rows[index].DefaultCellStyle.ForeColor =
                            Color.FromArgb(141, 150, 169);
                }
                _status.Text = records.Count == 0 ? "No results. Try a shorter title or a different spelling." :
                    records.Count == 20 ? "Showing LRCLIB's first 20 results. Refine the search to see others." :
                    records.Count + " results. Check the duration and preview before saving.";
                if (_results.Rows.Count > 0)
                {
                    _results.CurrentCell = _results.Rows[0].Cells[0];
                    ShowSelection();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!IsDisposed) _status.Text = ex.Message;
            }
            finally
            {
                if (!IsDisposed && !_trackChanged) _search.Enabled = true;
                if (ReferenceEquals(_searchToken, token)) _searchToken = null;
                token.Dispose();
            }
        }

        private void ShowSelection()
        {
            var record = _results.CurrentRow?.Tag as LrcLibRecord;
            _preview.Text = record == null ? "" : record.HasTimedLyrics ?
                record.SyncedLyrics : record.Instrumental ?
                "This result is marked instrumental." :
                "Plain lyrics — use TIMING after importing to add timestamps.\r\n\r\n" +
                (record.PlainLyrics ?? "");
            _import.Enabled = !_trackChanged && record != null &&
                (record.HasTimedLyrics || !record.Instrumental &&
                 !string.IsNullOrWhiteSpace(record.PlainLyrics));
            _import.Text = record != null && !record.HasTimedLyrics ?
                "Save plain lyrics" : "Save to MusicBee";
        }

        private void ImportClicked(object sender, EventArgs args)
        {
            var record = _results.CurrentRow?.Tag as LrcLibRecord;
            if (record == null || _trackChanged ||
                !record.HasTimedLyrics && (record.Instrumental ||
                string.IsNullOrWhiteSpace(record.PlainLyrics))) return;
            try
            {
                if (_musicBee.NowPlaying_GetFileUrl() != _trackUrl)
                {
                    TrackChanged();
                    return;
                }
                var length = record.DurationSeconds;
                var difference = _durationMs > 0 && length > 0 ?
                    Math.Abs(length - _durationMs / 1000.0) : 0;
                var unknownLength = _durationMs > 0 && length <= 0;
                if (!string.IsNullOrWhiteSpace(_expectedTag) || difference > 5 ||
                    unknownLength)
                {
                    var message = "Save the selected " + (record.HasTimedLyrics ?
                        "timed" : "plain") + " lyrics to this song's MusicBee Lyrics field?";
                    if (!string.IsNullOrWhiteSpace(_expectedTag))
                        message += "\n\nThis replaces the lyrics already in that field.";
                    if (difference > 5)
                        message += "\n\nThe LRCLIB result differs from this song by " +
                            FormatDuration(difference) + ". Check that it is the right version.";
                    if (unknownLength)
                        message += "\n\nThis LRCLIB result has no duration. Check the title and preview carefully.";
                    if (MessageBox.Show(this, message, "Save LRCLIB lyrics",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;
                }
                _import.Enabled = false;
                string error;
                var lyrics = record.HasTimedLyrics ? record.SyncedLyrics : record.PlainLyrics;
                if (!ImportedLyricsTagStore.Save(_musicBee, _trackUrl, _expectedTag,
                    lyrics, out error))
                {
                    _status.Text = error;
                    _import.Enabled = true;
                    return;
                }
                try { _lyricsSaved?.Invoke(_trackUrl, lyrics); }
                catch (Exception) { /* MusicBee already saved the tag. */ }
                Close();
            }
            catch (Exception ex)
            {
                _status.Text = "Could not import the lyrics: " + ex.Message;
                _import.Enabled = true;
            }
        }

        public void TrackChanged()
        {
            if (_trackChanged) return;
            _trackChanged = true;
            _searchToken?.Cancel();
            _search.Enabled = _import.Enabled = _query.Enabled = false;
            _status.Text = "Song changed. Reopen LRCLIB for the current song.";
        }

        private static string FormatDuration(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                return "—";
            var span = TimeSpan.FromSeconds(Math.Round(seconds));
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) :
                span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }
}
