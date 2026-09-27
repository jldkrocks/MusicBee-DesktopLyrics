using System;

namespace MusicBeePlugin
{
    internal static class ImportedLyricsTagStore
    {
        public static bool Save(Plugin.MusicBeeApiInterface musicBee, string trackUrl,
            string expectedTag, string syncedLyrics, out string error)
        {
            error = null;
            LrcTimingDocument document;
            if (!LrcTimingDocument.TryCreate(syncedLyrics, out document))
            {
                error = "That LRCLIB result has no usable timestamps.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(trackUrl) ||
                musicBee.NowPlaying_GetFileUrl == null ||
                musicBee.Library_GetFileTag == null ||
                musicBee.Library_SetFileTag == null ||
                musicBee.Library_CommitTagsToFile == null)
            {
                error = "MusicBee cannot save lyrics for this track.";
                return false;
            }
            var changed = false;
            try
            {
                if (musicBee.NowPlaying_GetFileUrl() != trackUrl)
                {
                    error = "The song changed. Reopen LRCLIB for the current song.";
                    return false;
                }
                var currentTag = musicBee.Library_GetFileTag(trackUrl,
                    Plugin.MetaDataType.Lyrics);
                if ((currentTag ?? "") != (expectedTag ?? ""))
                {
                    error = "MusicBee's Lyrics field changed. Reopen LRCLIB before replacing it.";
                    return false;
                }
                if (currentTag == syncedLyrics) return true;
                if (!musicBee.Library_SetFileTag(trackUrl, Plugin.MetaDataType.Lyrics,
                    syncedLyrics))
                {
                    error = "MusicBee rejected the Lyrics field for this song.";
                    return false;
                }
                changed = true;
                if (!musicBee.Library_CommitTagsToFile(trackUrl))
                {
                    Restore(musicBee, trackUrl, currentTag);
                    error = "MusicBee could not commit the Lyrics field. The original tag was restored where possible.";
                    return false;
                }
                try { musicBee.MB_RefreshPanels?.Invoke(); }
                catch (Exception) { /* The Lyrics field is already saved. */ }
                return true;
            }
            catch (Exception ex)
            {
                if (changed) Restore(musicBee, trackUrl, expectedTag);
                error = "MusicBee could not save the lyrics: " + ex.Message;
                return false;
            }
        }

        private static void Restore(Plugin.MusicBeeApiInterface musicBee,
            string trackUrl, string previous)
        {
            try
            {
                if (musicBee.Library_SetFileTag(trackUrl, Plugin.MetaDataType.Lyrics,
                    previous ?? ""))
                    musicBee.Library_CommitTagsToFile(trackUrl);
            }
            catch (Exception) { /* The failed commit may indicate a locked file. */ }
        }
    }
}
