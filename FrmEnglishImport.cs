using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    // The user chooses and copies a Genius translation. No page scraping or
    // Genius API key is needed; the resulting text stays in plugin storage.
    internal sealed class FrmEnglishImport : Form
    {
        private readonly Plugin.MusicBeeApiInterface _musicBee;
        private readonly EnglishTranslationStore _store;
        private readonly string _trackUrl, _searchUrl, _signature;
        private readonly IList<LyricParser.LyricEntry> _lyrics;
        private readonly Action<string> _saved;
        private readonly TextBox _english, _romaji, _sourceUrl;
        private readonly DataGridView _pairs;
        private readonly Label _status;
        private readonly Button _save, _remove;
        private EnglishImportDocument _document;
        private bool _trackChanged;

        public string TrackUrl => _trackUrl;

        public FrmEnglishImport(Plugin.MusicBeeApiInterface musicBee,
            EnglishTranslationStore store, string trackUrl, string title, string artist,
            IList<LyricParser.LyricEntry> lyrics, Action<string> saved)
        {
            _musicBee = musicBee;
            _store = store;
            _trackUrl = trackUrl;
            _lyrics = lyrics;
            _saved = saved;
            _signature = EnglishTranslationStore.Signature(lyrics);
            _searchUrl = "https://genius.com/search?q=" +
                Uri.EscapeDataString((artist + " " + title + " English translation").Trim());

            Text = "Add English meaning · " + title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(20, 24, 37);
            ForeColor = Color.FromArgb(236, 238, 247);
            Font = new Font("Segoe UI", 9.5f);
            MinimumSize = new Size(770, 570);
            Size = new Size(1060, 790);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12),
                ColumnCount = 1, RowCount = 8
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Controls.Add(root);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "English meaning  ·  " + title + " — " + artist,
                Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
                ForeColor = Color.White, TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            var first = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var open = MakeButton("1  Open Genius search", 195);
            open.Click += (sender, args) => OpenGenius();
            first.Controls.Add(open);
            first.Controls.Add(new Label
            {
                AutoSize = true, Margin = new Padding(12, 8, 0, 0),
                Text = "Choose the English translation page and copy its lyric text."
            });
            root.Controls.Add(first, 0, 1);

            root.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "2  Paste English lyrics here (section headings like [Verse 1] are fine):",
                ForeColor = Color.FromArgb(174, 204, 230)
            }, 0, 2);
            _english = InputBox();
            root.Controls.Add(_english, 0, 3);

            var optional = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3,
                ColumnCount = 1, Padding = new Padding(0, 5, 0, 0) };
            optional.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            optional.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            optional.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            optional.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            optional.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, Text = "Optional: paste Genius's romaji too if its line breaks differ from MusicBee's.",
                ForeColor = Color.FromArgb(174, 204, 230)
            }, 0, 0);
            _romaji = InputBox();
            optional.Controls.Add(_romaji, 0, 1);
            var source = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            source.Controls.Add(new Label { Text = "Source URL:", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _sourceUrl = new TextBox { Dock = DockStyle.Fill, Text = "",
                BackColor = Color.FromArgb(31, 36, 53), ForeColor = Color.White };
            source.Controls.Add(_sourceUrl, 1, 0);
            optional.Controls.Add(source, 0, 2);
            root.Controls.Add(optional, 0, 4);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var align = MakeButton("3  Align and review", 170);
            align.Click += (sender, args) => Align();
            actions.Controls.Add(align);
            var repeat = MakeButton("Repeat previous English here", 225);
            repeat.Click += (sender, args) => ChangeSelected(true);
            actions.Controls.Add(repeat);
            var join = MakeButton("Join next English here", 194);
            join.Click += (sender, args) => ChangeSelected(false);
            actions.Controls.Add(join);
            root.Controls.Add(actions, 0, 5);

            _pairs = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = Color.FromArgb(23, 27, 42),
                BorderStyle = BorderStyle.None, GridColor = Color.FromArgb(52, 58, 78),
                RowHeadersVisible = false, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                EnableHeadersVisualStyles = false, ColumnHeadersHeight = 30,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            _pairs.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(35, 40, 58);
            _pairs.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(216, 224, 240);
            _pairs.DefaultCellStyle.BackColor = Color.FromArgb(26, 30, 46);
            _pairs.DefaultCellStyle.ForeColor = Color.FromArgb(238, 239, 248);
            _pairs.DefaultCellStyle.SelectionBackColor = Color.FromArgb(58, 67, 91);
            _pairs.DefaultCellStyle.SelectionForeColor = Color.White;
            _pairs.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Time", Width = 83, ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _pairs.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "MusicBee lyric", Width = 380, ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True },
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _pairs.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "English (editable)", Width = 435,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                DefaultCellStyle = { WrapMode = DataGridViewTriState.True },
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _pairs.CellEndEdit += (sender, args) =>
            {
                if (_document == null || args.ColumnIndex != 2) return;
                _document.SetEnglish(args.RowIndex,
                    Convert.ToString(_pairs.Rows[args.RowIndex].Cells[2].Value));
                UpdateStatus();
            };
            root.Controls.Add(_pairs, 0, 6);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3,
                Padding = new Padding(0, 7, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true,
                Text = "Paste English, then align and review before saving.",
                ForeColor = Color.FromArgb(174, 204, 230),
                TextAlign = ContentAlignment.MiddleLeft };
            footer.Controls.Add(_status, 0, 0);
            _remove = MakeButton("Remove saved English", 160);
            _remove.Click += (sender, args) => Remove();
            footer.Controls.Add(_remove, 1, 0);
            _save = MakeButton("4  Save English", 150);
            _save.Enabled = false;
            _save.Click += (sender, args) => Save();
            footer.Controls.Add(_save, 2, 0);
            root.Controls.Add(footer, 0, 7);

            var existing = _store.Load(_trackUrl, _lyrics);
            _remove.Enabled = existing != null;
            if (existing != null)
            {
                _status.Text = "Saved English is active. Paste again to replace it, or remove it.";
            }
        }

        public void TrackChanged()
        {
            _trackChanged = true;
            _save.Enabled = _remove.Enabled = false;
            _status.Text = "The song changed. Reopen this window for the current song.";
        }

        private static TextBox InputBox()
        {
            return new TextBox { Dock = DockStyle.Fill, Multiline = true,
                ScrollBars = ScrollBars.Vertical, WordWrap = false,
                BackColor = Color.FromArgb(30, 36, 54), ForeColor = Color.White,
                Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle };
        }

        private static Button MakeButton(string label, int width)
        {
            return new Button { Text = label, Width = width, Height = 30,
                Margin = new Padding(0, 0, 9, 0), FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(43, 52, 76), ForeColor = Color.White };
        }

        private void OpenGenius()
        {
            try { Process.Start(_searchUrl); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open Genius: " + ex.Message,
                    "Find English", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Align()
        {
            if (_trackChanged) return;
            if (string.IsNullOrWhiteSpace(_english.Text))
            {
                _status.Text = "Copy the English lyric text from Genius and paste it above first.";
                return;
            }
            _document = new EnglishImportDocument(_lyrics, _english.Text, _romaji.Text);
            if (_document.Rows.All(row => string.IsNullOrWhiteSpace(row.English)))
            {
                _status.Text = "No English lyric lines were found in the pasted text.";
                _save.Enabled = false;
                return;
            }
            FillPairs(0);
            _save.Enabled = true;
        }

        private void FillPairs(int selected)
        {
            _pairs.Rows.Clear();
            foreach (var row in _document.Rows)
            {
                var index = _pairs.Rows.Add(FormatTime(row.TimeMs), row.Romaji, row.English);
                if (row.Check)
                    _pairs.Rows[index].DefaultCellStyle.BackColor = Color.FromArgb(82, 63, 43);
            }
            if (_pairs.Rows.Count > 0)
            {
                selected = Math.Max(0, Math.Min(selected, _pairs.Rows.Count - 1));
                _pairs.ClearSelection();
                _pairs.Rows[selected].Selected = true;
                _pairs.FirstDisplayedScrollingRowIndex = selected;
            }
            UpdateStatus();
        }

        private void ChangeSelected(bool repeat)
        {
            if (_trackChanged || _document == null || _pairs.CurrentRow == null) return;
            var selected = _pairs.CurrentRow.Index;
            if (_pairs.IsCurrentCellInEditMode) _pairs.EndEdit();
            if (repeat) _document.RepeatPrevious(selected);
            else _document.JoinNext(selected);
            FillPairs(selected);
        }

        private void UpdateStatus()
        {
            if (_document == null || _trackChanged) return;
            _status.Text = _document.Note + "  Empty: " + _document.EmptyCount +
                "  Extra English: " + _document.ExtraCount;
        }

        private void Save()
        {
            if (_trackChanged || _document == null) return;
            if (_pairs.IsCurrentCellInEditMode) _pairs.EndEdit();
            try
            {
                if (_musicBee.NowPlaying_GetFileUrl() != _trackUrl)
                {
                    TrackChanged();
                    return;
                }
                var tag = _musicBee.Library_GetFileTag?.Invoke(_trackUrl,
                    Plugin.MetaDataType.Lyrics);
                var current = LyricParser.ParseLyric(string.IsNullOrWhiteSpace(tag) ?
                    _musicBee.NowPlaying_GetLyrics() : tag);
                if (current == null ||
                    EnglishTranslationStore.Signature(current.Entries) != _signature)
                {
                    _status.Text = "MusicBee's lyrics changed. Reopen to review the new lines.";
                    _save.Enabled = false;
                    return;
                }
                var warnings = new List<string>();
                if (_document.ExtraCount > 0)
                    warnings.Add(_document.ExtraCount + " extra English lines will be left out");
                if (_document.EmptyCount > 0)
                    warnings.Add(_document.EmptyCount + " timed lines have no English");
                if (_document.Rows.Any(row => row.Check))
                    warnings.Add("highlighted pairs need a check");
                if (warnings.Count > 0 && MessageBox.Show(this,
                    "Please review: " + string.Join("; ", warnings) +
                    ".\n\nSave this alignment anyway?", "Review English alignment",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                var source = _sourceUrl.Text.Trim();
                if (source.Length > 0 && (!Uri.TryCreate(source, UriKind.Absolute, out var url) ||
                    (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)))
                {
                    _status.Text = "Source URL must be a complete http or https link, or blank.";
                    return;
                }
                _store.Save(_trackUrl, _lyrics, _document.TranslationLines(), source);
                _saved?.Invoke(_trackUrl);
                _remove.Enabled = true;
                _status.Text = "Saved separately from MusicBee's Lyrics field. English is now visible.";
            }
            catch (Exception ex)
            {
                _status.Text = "Could not save English: " + ex.Message;
            }
        }

        private void Remove()
        {
            if (_trackChanged || MessageBox.Show(this,
                "Remove the saved English for this song? MusicBee's lyrics stay as they are.",
                "Remove English", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                _store.Delete(_trackUrl);
                _saved?.Invoke(_trackUrl);
                _remove.Enabled = false;
                _status.Text = "Saved English removed. MusicBee's lyrics were not changed.";
            }
            catch (Exception ex) { _status.Text = "Could not remove English: " + ex.Message; }
        }

        private static string FormatTime(double milliseconds)
        {
            var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            return string.Format("{0:00}:{1:00}.{2:00}", (int)time.TotalMinutes,
                time.Seconds, time.Milliseconds / 10);
        }
    }
}
