using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace MusicBeePlugin
{
    internal static class Program
    {
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
            Console.WriteLine("LRC parser checks passed.");

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
            var playback = new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileUrl = () => currentTrack,
                NowPlaying_GetFileTag = field => "Y",
                NowPlaying_GetLyrics = () => "[00:52.27] Original\n[00:54.00] Later",
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
            controller.KeepSavedLyrics(currentTrack,
                "[00:53.27] Adjusted\n[00:54.00] Later");
            controller.CancelPreview(currentTrack);
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Adjusted")
                throw new Exception("Saved timings must remain visible if MusicBee caches old lyrics.");
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
    }
}
