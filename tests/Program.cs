using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            const string lrc = "[00:09.68] First line\n" +
                               "[00:17.30] \n" +
                               "[00:17.30] Second line\n" +
                               "[00:24.68] \n" +
                               "[00:31.38] Third line\n" +
                               "[00:40.00] Original\n" +
                               "[00:40.00] Translation";

            var lyrics = LyricParser.ParseLyric(lrc);
            if (lyrics == null || lyrics.Entries.Count != 4)
                throw new Exception("Empty timestamps must not become timed lyrics.");
            if (lyrics.Entries[1].LyricLine1 != " Second line" || lyrics.Entries[1].LyricLine2 != null)
                throw new Exception("An empty duplicate timestamp must not become a translation.");
            if (lyrics.Entries[2].LyricLine1 != " Third line")
                throw new Exception("An empty pause must not replace the active lyric.");
            if (lyrics.Entries[3].LyricLine2 != " Translation")
                throw new Exception("A real translation must remain on the second line.");
            var bilingual = new LyricsController(new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileUrl = () => "bilingual.mp3",
                NowPlaying_GetFileTag = field => "Y",
                Library_GetFileTag = (url, field) => lrc,
                Player_GetPosition = () => 41000
            });
            if (bilingual.UpdateLyrics(false)?.LyricLine2 != " Translation")
                throw new Exception("The translation must share its romaji timestamp.");
            bilingual.ShowTranslation = false;
            if (bilingual.UpdateLyrics(false)?.LyricLine2 != null)
                throw new Exception("The flyout toggle must hide only the translation.");
            Console.WriteLine("LRC parser checks passed.");

            const string romajiLrc = "[00:00.00] ...\n" +
                "[00:09.00] The moon above\n" +
                "[00:12.00] An alley flickers\n" +
                "[00:16.00] The city sleeps";
            var romaji = LyricParser.ParseLyric(romajiLrc).Entries;
            var sequential = new EnglishImportDocument(romaji,
                "[Verse 1]\nMoonlight\nThe lights\nThe city is quiet", null);
            if (sequential.Rows[0].English != "" ||
                sequential.Rows[1].English != "Moonlight" ||
                sequential.Rows[3].English != "The city is quiet" ||
                sequential.EmptyCount != 0 || sequential.ExtraCount != 0)
                throw new Exception("English lines must skip instrumental placeholders.");
            var bridge = new EnglishImportDocument(romaji,
                "[Verse 1]\nMoonlight\nThe lights shine\nThe city is quiet",
                "[Verse 1]\nThe moon above\nAn alley\nflickers\nThe city sleeps");
            if (bridge.Rows[1].English != "Moonlight" ||
                bridge.Rows[2].English != "The lights shine" ||
                bridge.Rows[3].English != "The city is quiet" ||
                !bridge.Rows[2].Check)
                throw new Exception("Optional romaji must bridge different line breaks and flag uncertain pairs.");
            var oneMeaning = new EnglishImportDocument(romaji,
                "Moonlight\nThe city is quiet", null);
            oneMeaning.RepeatPrevious(2);
            if (oneMeaning.Rows[0].English != "" ||
                oneMeaning.Rows[1].English != "Moonlight" ||
                oneMeaning.Rows[2].English != "Moonlight" ||
                oneMeaning.Rows[3].English != "The city is quiet" ||
                oneMeaning.EmptyCount != 0)
                throw new Exception("Repeating a meaning must shift only active lyric rows.");
            var splitMeaning = new EnglishImportDocument(romaji,
                "Moonlight\nThrough the alley\nThe lights shine\nThe city is quiet", null);
            splitMeaning.JoinNext(1);
            if (splitMeaning.Rows[0].English != "" ||
                splitMeaning.Rows[1].English != "Moonlight / Through the alley" ||
                splitMeaning.Rows[2].English != "The lights shine" ||
                splitMeaning.Rows[3].English != "The city is quiet" ||
                splitMeaning.ExtraCount != 0)
                throw new Exception("Joining two meanings must use an extra line and skip placeholders.");

            var englishRoot = Path.Combine(Path.GetTempPath(), "LyricsEnglish-" + Guid.NewGuid());
            try
            {
                var store = new EnglishTranslationStore(englishRoot);
                store.Save("romaji.mp3", romaji, sequential.TranslationLines(),
                    "https://genius.com/example");
                var saved = store.Load("romaji.mp3", romaji);
                if (saved == null || saved[1] != "Moonlight" ||
                    store.Load("another.mp3", romaji) != null)
                    throw new Exception("Saved English must belong only to its song.");
                var shifted = LyricParser.ParseLyric(romajiLrc.Replace("[00:09.00]",
                    "[00:10.00]")).Entries;
                if (store.Load("romaji.mp3", shifted)?[1] != "Moonlight")
                    throw new Exception("Changing only LRC timing must preserve saved English.");
                var revised = LyricParser.ParseLyric(romajiLrc.Replace("moon", "sun")).Entries;
                if (store.Load("romaji.mp3", revised) != null)
                    throw new Exception("Changed romaji must require a new English review.");

                var englishApi = new Plugin.MusicBeeApiInterface
                {
                    NowPlaying_GetFileUrl = () => "romaji.mp3",
                    NowPlaying_GetFileTag = field => "Y",
                    Library_GetFileTag = (url, field) => romajiLrc,
                    Player_GetPosition = () => 10000
                };
                var englishController = new LyricsController(englishApi, store);
                if (englishController.UpdateLyrics(false)?.LyricLine2 != "Moonlight")
                    throw new Exception("The saved English must follow the current timed lyric.");
                var replacement = new[] { "", "New meaning", "The lights", "The city" };
                store.Save("romaji.mp3", romaji, replacement, "");
                englishController.InvalidateImportedEnglish();
                if (englishController.UpdateLyrics(false)?.LyricLine2 != "New meaning")
                    throw new Exception("Saved English must refresh without switching songs.");
                englishController.ShowTranslation = false;
                if (englishController.UpdateLyrics(false)?.LyricLine2 != null)
                    throw new Exception("The flyout switch must hide imported English.");
                store.Delete("romaji.mp3");
                englishController.InvalidateImportedEnglish();
                englishController.ShowTranslation = true;
                if (englishController.UpdateLyrics(false)?.LyricLine2 != null)
                    throw new Exception("Removing saved English must clear the visible meaning.");
            }
            finally
            {
                if (Directory.Exists(englishRoot)) Directory.Delete(englishRoot, true);
            }
            Console.WriteLine("English alignment and storage checks passed.");

            const string editable = "[ti:Test]\r\n[offset:+150]\r\n" +
                "[00:52.27] Main\r\n[00:52.27] Translation\r\n" +
                "[00:52.27] \r\n[00:54.00][01:10.5] Repeated\r\n";
            LrcTimingDocument timing;
            if (!LrcTimingDocument.TryCreate(editable, out timing) ||
                timing.Entries.Count != 3 || timing.OffsetMs != 150 ||
                timing.BuildLyrics() != editable)
                throw new Exception("The timing editor must read the original LRC unchanged.");
            if (!timing.ShiftEntry(timing.Entries[0], 1000) ||
                timing.ShiftEntry(timing.Entries[0], 1000))
                throw new Exception("A fine-tuned line must not overtake the next line.");
            var changed = timing.BuildLyrics();
            if (!changed.Contains("[00:53.27] Main\r\n[00:53.27] Translation\r\n" +
                "[00:53.27] \r\n") || !changed.Contains("[ti:Test]\r\n[offset:+150]"))
                throw new Exception("Paired lyrics, gap markers and metadata must survive line edits.");
            if (timing.ShiftAll(100) != 100 ||
                !timing.BuildLyrics().Contains("[00:54.10][01:10.60] Repeated"))
                throw new Exception("Whole-song offsets must change every timestamp.");
            timing.Reset();
            if (timing.IsDirty || timing.BuildLyrics() != editable)
                throw new Exception("Cancel/reset must restore the original LRC exactly.");
            Console.WriteLine("Timing edit checks passed.");

            const string plainSource = "[ar:Someone]\r\nFirst line\r\n\r\nSecond line";
            UntimedTimingDocument plain;
            if (!UntimedTimingDocument.TryCreate(plainSource, out plain) ||
                plain.Entries.Count != 2 || plain.IsComplete ||
                UntimedTimingDocument.TryCreate(editable, out plain))
                throw new Exception("Creation mode must only accept untimed lyric lines.");
            UntimedTimingDocument.TryCreate(plainSource, out plain);
            if (!plain.Stamp(0, 1000) || plain.Stamp(1, 900) ||
                !plain.Stamp(1, 2200) || !plain.Adjust(1, -100) ||
                !plain.IsComplete || plain.ShiftAll(100) != 100 ||
                plain.BuildLyrics() != "[ar:Someone]\r\n[00:01.10] First line\r\n\r\n[00:02.20] Second line")
                throw new Exception("New timings must be ordered and preserve plain lyrics and spacing.");
            if (LyricParser.ParseLyric(plain.BuildLyrics())?.Entries.Count != 2)
                throw new Exception("The completed document must be valid timed lyrics.");
            plain.Reset();
            if (plain.IsDirty || plain.IsComplete || plain.BuildLyrics() != plainSource)
                throw new Exception("Reset must leave the original plain lyrics intact.");
            Console.WriteLine("Untimed lyric creation checks passed.");

            var backupRoot = Path.Combine(Path.GetTempPath(), "LyricsTiming-" + Guid.NewGuid());
            try
            {
                var storedLyrics = editable;
                var panelRefreshes = 0;
                var api = new Plugin.MusicBeeApiInterface
                {
                    Setting_GetPersistentStoragePath = () => backupRoot,
                    Library_GetFileTag = (url, field) => storedLyrics,
                    Library_SetFileTag = (url, field, value) =>
                    {
                        if (url != "track.mp3" || field != Plugin.MetaDataType.Lyrics)
                            throw new Exception("Timing edits must target this song's Lyrics field.");
                        storedLyrics = value;
                        return true;
                    },
                    Library_CommitTagsToFile = url => true,
                    MB_RefreshPanels = () => panelRefreshes++
                };
                string saveError;
                var edited = editable.Replace("[00:52.27]", "[00:53.27]");
                if (!TimingTagStore.Save(api, "track.mp3", editable, editable,
                    edited, out saveError) || storedLyrics != edited || panelRefreshes != 1)
                    throw new Exception("Saving must update MusicBee's Lyrics field and its panels: " + saveError);
                var backups = Directory.GetFiles(Path.Combine(backupRoot,
                    "DesktopLyrics-TimingBackups"), "*.lrc");
                if (backups.Length != 1 || File.ReadAllText(backups[0]) != editable)
                    throw new Exception("Saving must retain an exact backup of the old lyrics.");
                if (TimingTagStore.Save(api, "track.mp3", editable, editable,
                    "unexpected", out saveError) || storedLyrics != edited)
                    throw new Exception("A stale editor must not overwrite updated lyrics.");
                api.Library_CommitTagsToFile = url => false;
                if (TimingTagStore.Save(api, "track.mp3", edited, edited,
                    "another change", out saveError) || storedLyrics != edited)
                    throw new Exception("Failed file writes must restore the old in-memory lyrics.");

                var seekPosition = -1;
                var resumeCalls = 0;
                var previewCalls = 0;
                api.NowPlaying_GetFileUrl = () => "track.mp3";
                api.Player_GetPlayState = () => Plugin.PlayState.Paused;
                api.Player_SetPosition = position => { seekPosition = position; return true; };
                api.Player_PlayPause = () => { resumeCalls++; return true; };
                using (var editor = new FrmTimingEditor(api, "track.mp3", "Test song",
                    editable, timing, (url, text) => previewCalls++, url => { },
                    (url, text) => { }))
                {
                    var grid = FindGrid(editor.Controls);
                    if (grid == null || grid.Rows.Count != timing.Entries.Count)
                        throw new Exception("The timing mode must display every timestamped row.");
                    var click = typeof(FrmTimingEditor).GetMethod("LyricClicked",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    click.Invoke(editor, new object[] { grid,
                        new DataGridViewCellEventArgs(1, 0) });
                    if (seekPosition != 51320 || resumeCalls != 1 ||
                        previewCalls != 0 || timing.IsDirty)
                        throw new Exception("Clicking a lyric must seek with a short lead-in without editing it.");
                }
            }
            finally
            {
                if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
            }
            Console.WriteLine("Lyrics tag save checks passed.");

            var noTrack = new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileTag = field => null,
                NowPlaying_GetLyrics = () => null
            };
            if (new LyricsController(noTrack).UpdateLyrics(false) != null)
                throw new Exception("No current track must not produce lyrics.");

            var currentTrack = "track.mp3";
            string currentLyricsTag = null;
            var playback = new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileUrl = () => currentTrack,
                NowPlaying_GetFileTag = field => "Y",
                NowPlaying_GetLyrics = () => "[00:52.27] Original\n[00:54.00] Later",
                Library_GetFileTag = (url, field) => url == "track.mp3" ?
                    currentLyricsTag : null,
                Player_GetPosition = () => 53500
            };
            var controller = new LyricsController(playback);
            controller.PreviewLyrics(currentTrack,
                "[00:53.27] Adjusted\n[00:54.00] Later");
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Adjusted")
                throw new Exception("Unsaved timing changes must preview in the lyric window.");
            controller.CancelPreview(currentTrack);
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Original")
                throw new Exception("Cancel must restore MusicBee's original timings.");
            currentLyricsTag = "[00:52.27] Original\n[00:54.00] Later";
            controller.KeepSavedLyrics(currentTrack,
                "[00:53.27] Adjusted\n[00:54.00] Later");
            controller.CancelPreview(currentTrack);
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Adjusted")
                throw new Exception("Saved timings must remain visible if MusicBee caches old lyrics.");
            currentLyricsTag = "[00:53.27] Adjusted\n[00:54.00] Later";
            controller.InvalidateTag();
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Adjusted")
                throw new Exception("The newly saved tag must confirm the live lyrics.");
            currentLyricsTag = "[00:53.27] New tag\n[00:54.00] Later";
            controller.InvalidateTag();
            if (controller.UpdateLyrics(false)?.LyricLine1 != " New tag")
                throw new Exception("A newly saved MusicBee Lyrics tag must appear without switching songs.");
            currentTrack = "different.mp3";
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Original")
                throw new Exception("Saved lyric preview must not leak to a different song.");
            Console.WriteLine("Live timing preview checks passed.");

            var nextCalls = 0;
            var atEnd = new Plugin.MusicBeeApiInterface
            {
                Player_GetButtonEnabled = button => true,
                NowPlayingList_IsAnyFollowingTracks = () => false,
                Player_GetRepeat = () => Plugin.RepeatMode.None,
                Player_GetAutoDjEnabled = () => false,
                Player_PlayNextTrack = () => { nextCalls++; return true; }
            };
            if (QueueNavigation.TryPlayNext(atEnd) || QueueNavigation.TryPlayNext(atEnd) ||
                nextCalls != 0)
                throw new Exception("Repeated Next at the end must not call MusicBee.");
            atEnd.NowPlayingList_IsAnyFollowingTracks = () => true;
            if (!QueueNavigation.TryPlayNext(atEnd) || nextCalls != 1)
                throw new Exception("Next must still advance when tracks remain.");
            atEnd.Player_GetButtonEnabled = button => false;
            if (QueueNavigation.TryPlayNext(atEnd) || nextCalls != 1)
                throw new Exception("A disabled Next button must not advance.");
            Console.WriteLine("End-of-queue checks passed.");

            var upcoming = new Plugin.MusicBeeApiInterface
            {
                NowPlayingList_GetCurrentIndex = () => 5,
                NowPlayingList_GetNextIndex = offset =>
                    offset == 1 ? 10 : offset == 2 ? 7 : -1,
                NowPlayingList_GetFileTag = (index, field) =>
                    field == Plugin.MetaDataType.Artist ? "Artist " + index :
                    index == 10 ? "Queued first" : null,
                NowPlayingList_GetListFileUrl = index => index == 10 ?
                    @"C:\Music\Queued first.mp3" : @"C:\Music\Other title.mp3"
            };
            var queue = UpcomingQueue.Read(upcoming);
            if (queue.Count != 2 || queue[0].Title != "Queued first" ||
                queue[1].Title != "Other title" || queue[1].Artist != "Artist 7")
                throw new Exception("The queue must use playback order and fall back to a file name.");
            upcoming.NowPlayingList_GetNextIndex = offset => offset == 1 ? 5 : 7;
            if (UpcomingQueue.Read(upcoming).Count != 0)
                throw new Exception("The current track must not appear in the upcoming queue.");
            upcoming.NowPlayingList_GetNextIndex = offset =>
                offset == 1 ? 10 : offset == 2 ? 7 : -1;
            var page = UpcomingQueue.Read(upcoming, 2, 1);
            if (page.Count != 1 || page[0].Index != 7 || page[0].Offset != 2)
                throw new Exception("Scrolling must fetch the correct queue position.");
            var played = "";
            upcoming.NowPlaying_GetFileUrl = () => @"C:\Music\Current.mp3";
            upcoming.NowPlayingList_PlayNow = url => { played = url; return true; };
            string queueError;
            if (!QueueNavigation.TryPlayQueuedTrack(upcoming, page[0], out queueError) ||
                played != page[0].FileUrl)
                throw new Exception("Clicking a queue song must play that file: " + queueError);
            upcoming.NowPlayingList_GetNextIndex = offset =>
                offset == 1 ? 10 : offset == 2 ? 9 : -1;
            if (QueueNavigation.TryPlayQueuedTrack(upcoming, page[0], out queueError))
                throw new Exception("A stale queue row must not play a different song.");
            upcoming.NowPlayingList_GetNextIndex = offset =>
                offset == 1 ? 10 : offset == 2 ? 7 : -1;
            upcoming.NowPlayingList_GetListFileUrl = index => @"C:\Music\Repeat.mp3";
            var repeated = UpcomingQueue.Read(upcoming, 2, 1)[0];
            if (QueueNavigation.TryPlayQueuedTrack(upcoming, repeated, out queueError) ||
                !queueError.Contains("duplicate"))
                throw new Exception("MusicBee cannot address the second copy of a queued file.");
            Console.WriteLine("Upcoming queue checks passed.");

            var history = new PlaybackHistory();
            history.Observe("first.mp3", "First", "Artist");
            history.Observe("second.mp3", "Second", "Artist");
            history.Observe("third.mp3", "Third", "Artist");
            var previous = history.Snapshot();
            if (previous.Count != 3 || previous[0].Offset != -2 ||
                previous[1].Offset != -1 || previous[2].Offset != 0 ||
                history.Observe("third.mp3", "Third", "Artist"))
                throw new Exception("The queue must retain play history around the current track.");
            upcoming.NowPlaying_GetFileUrl = () => "third.mp3";
            if (!QueueNavigation.TryPlayQueuedTrack(upcoming, previous[0], out queueError) ||
                played != "first.mp3" ||
                QueueNavigation.TryPlayQueuedTrack(upcoming, previous[2], out queueError))
                throw new Exception("A played track must be clickable while current is not replayed.");
            Console.WriteLine("Queue history checks passed.");

            history.Observe("first.mp3", "First", "Artist");
            var timeline = history.Timeline(new[]
            {
                new UpcomingQueue.Track { FileUrl = "second.mp3", Offset = 1 },
                new UpcomingQueue.Track { FileUrl = "second.mp3", Offset = 2 },
                new UpcomingQueue.Track { FileUrl = "fourth.mp3", Offset = 3 }
            });
            if (timeline.Count != 4 || timeline[0].FileUrl != "third.mp3" ||
                timeline[1].FileUrl != "first.mp3" ||
                timeline[2].FileUrl != "second.mp3" || timeline[2].Offset != 1 ||
                timeline[3].FileUrl != "fourth.mp3")
                throw new Exception("Clicking around must not duplicate played or upcoming songs.");

            var playingUrls = new[] { "first.mp3", "fourth.mp3" };
            upcoming.NowPlayingList_QueryFilesEx = (string query, out string[] files) =>
            {
                files = playingUrls;
                return true;
            };
            history.RetainPlayingList(UpcomingQueue.ReadPlayingListUrls(upcoming));
            var revised = history.Timeline(new[]
            {
                new UpcomingQueue.Track { FileUrl = "fourth.mp3", Offset = 1 }
            });
            if (revised.Count != 2 || revised[0].FileUrl != "first.mp3" ||
                revised[1].FileUrl != "fourth.mp3")
                throw new Exception("Removed MusicBee tracks must leave the plugin history.");
            upcoming.NowPlayingList_QueryFilesEx = (string query, out string[] files) =>
            {
                files = null;
                return false;
            };
            history.RetainPlayingList(UpcomingQueue.ReadPlayingListUrls(upcoming));
            if (history.Snapshot().Count != 1)
                throw new Exception("A failed MusicBee query must not discard history.");
            playingUrls = new string[0];
            upcoming.NowPlayingList_QueryFilesEx = (string query, out string[] files) =>
            {
                files = playingUrls;
                return true;
            };
            history.RetainPlayingList(UpcomingQueue.ReadPlayingListUrls(upcoming));
            if (history.Snapshot().Count != 0)
                throw new Exception("Clearing the Playing Tracks list must clear old history and current track.");

            var searchJson = "[{\"id\":1,\"trackName\":\"Anytime Anywhere\",\"artistName\":\"milet\",\"duration\":50," +
                "\"syncedLyrics\":\"[00:01.00] Short\"},{\"id\":2,\"trackName\":\"Anytime Anywhere\"," +
                "\"artistName\":\"milet\",\"duration\":230,\"syncedLyrics\":\"[00:01.00] Correct\"}," +
                "{\"id\":3,\"duration\":225,\"plainLyrics\":\"No timings\"}]";
            var records = LrcLibClient.SortByDuration(LrcLibClient.ParseResults(searchJson), 230000);
            if (records.Count != 3 || records[0].Id != 2 || !records[0].HasTimedLyrics ||
                records[1].HasTimedLyrics || LrcLibClient.RetryDelay("12").TotalSeconds != 12)
                throw new Exception("LRCLIB results must show the matching timed version first and honor Retry-After.");
            var importedTag = "[00:01.00] Previous";
            var importApi = new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileUrl = () => "track.mp3",
                Library_GetFileTag = (url, field) => importedTag,
                Library_SetFileTag = (url, field, text) =>
                {
                    if (field != Plugin.MetaDataType.Lyrics || url != "track.mp3")
                        throw new Exception("Import must only change this song's Lyrics field.");
                    importedTag = text;
                    return true;
                },
                Library_CommitTagsToFile = url => true,
                MB_RefreshPanels = () => { }
            };
            string importError;
            if (!ImportedLyricsTagStore.Save(importApi, "track.mp3", importedTag,
                    records[0].SyncedLyrics, out importError) ||
                importedTag != records[0].SyncedLyrics)
                throw new Exception("Timed lyrics must save in MusicBee's Lyrics field: " + importError);
            if (ImportedLyricsTagStore.Save(importApi, "track.mp3", "stale",
                    "[00:02.00] Wrong", out importError) ||
                importedTag != records[0].SyncedLyrics)
                throw new Exception("Import must not overwrite a tag changed since search opened.");
            if (!ImportedLyricsTagStore.Save(importApi, "track.mp3", importedTag,
                    records[1].PlainLyrics, out importError) ||
                importedTag != records[1].PlainLyrics)
                throw new Exception("Plain LRCLIB lyrics must be saved for later timing: " + importError);
            importApi.Library_CommitTagsToFile = url => false;
            if (ImportedLyricsTagStore.Save(importApi, "track.mp3", importedTag,
                    "[00:02.00] Failed", out importError) ||
                importedTag != records[1].PlainLyrics)
                throw new Exception("A failed MusicBee commit must restore the old Lyrics field.");
            using (var picker = new FrmLrcLibPicker(importApi, "track.mp3",
                "Anytime Anywhere", "milet", 230000, importedTag, (url, text) => { }))
            {
                if (picker.TrackUrl != "track.mp3")
                    throw new Exception("The LRCLIB picker must remain tied to the selected song.");
            }
            Console.WriteLine("LRCLIB import checks passed.");

            var imagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                using (var image = new Bitmap(48, 48))
                {
                    using (var graphics = Graphics.FromImage(image))
                    {
                        graphics.Clear(Color.FromArgb(210, 45, 55));
                        graphics.FillRectangle(Brushes.RoyalBlue, 36, 0, 12, 48);
                    }
                    image.Save(imagePath, ImageFormat.Png);
                }
                ArtworkPalette palette;
                if (!ArtworkPalette.TryLoad(imagePath, out palette))
                    throw new Exception("Album artwork should produce a palette.");
                if (palette.Left.R <= palette.Left.B || palette.Right.B <= palette.Right.R)
                    throw new Exception("The palette should reflect both artwork colours.");
                var imageBytes = File.ReadAllBytes(imagePath);
                ArtworkPalette embeddedPalette;
                Bitmap cover;
                if (!ArtworkPalette.TryLoad(imageBytes, out embeddedPalette, out cover))
                    throw new Exception("Embedded artwork should produce a palette and cover.");
                using (cover)
                {
                    if (embeddedPalette.BarTop.ToArgb() != palette.BarTop.ToArgb() ||
                        cover.Width != 256 || cover.Height != 256 ||
                        cover.GetPixel(16, 128).R <= cover.GetPixel(16, 128).B ||
                        cover.GetPixel(240, 128).B <= cover.GetPixel(240, 128).R)
                        throw new Exception("The displayed cover should match the artwork colours.");
                }
                ArtworkPalette urlPalette;
                if (!ArtworkPalette.TryLoad(new Uri(imagePath).AbsoluteUri, out urlPalette) ||
                    urlPalette.BarBottom.ToArgb() != palette.BarBottom.ToArgb())
                    throw new Exception("File URLs should resolve to the artwork.");
                ArtworkPalette encodedPalette;
                if (!ArtworkPalette.TryLoad(Convert.ToBase64String(imageBytes), out encodedPalette) ||
                    encodedPalette.Left.ToArgb() != palette.Left.ToArgb())
                    throw new Exception("Encoded artwork should produce the same palette.");

                File.Delete(imagePath);
                using (var colourful = new Bitmap(48, 48))
                {
                    using (var graphics = Graphics.FromImage(colourful))
                    {
                        graphics.Clear(Color.FromArgb(210, 45, 55));
                        graphics.FillRectangle(Brushes.RoyalBlue, 26, 0, 14, 48);
                        graphics.FillRectangle(Brushes.LimeGreen, 40, 0, 8, 48);
                    }
                    colourful.Save(imagePath, ImageFormat.Png);
                }
                ArtworkPalette colourfulPalette;
                if (!ArtworkPalette.TryLoad(imagePath, out colourfulPalette) ||
                    colourfulPalette.Accent.G <= colourfulPalette.Accent.R ||
                    colourfulPalette.Accent.G <= colourfulPalette.Accent.B)
                    throw new Exception("A third artwork hue should colour the background lights.");

                File.Delete(imagePath);
                using (var neutralImage = new Bitmap(48, 48))
                {
                    using (var graphics = Graphics.FromImage(neutralImage))
                        graphics.Clear(Color.White);
                    neutralImage.Save(imagePath, ImageFormat.Png);
                }
                ArtworkPalette neutralPalette;
                if (!ArtworkPalette.TryLoad(imagePath, out neutralPalette) ||
                    neutralPalette.BarTop.R != neutralPalette.BarTop.G)
                    throw new Exception("Monochrome artwork should produce a neutral palette.");
            }
            finally
            {
                File.Delete(imagePath);
            }
            Console.WriteLine("Artwork palette checks passed.");
        }

        private static DataGridView FindGrid(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                var grid = control as DataGridView;
                if (grid != null) return grid;
                grid = FindGrid(control.Controls);
                if (grid != null) return grid;
            }
            return null;
        }
    }
}
