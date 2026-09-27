using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MusicBeePlugin
{
    internal static class TimingTagStore
    {
        public static bool Save(Plugin.MusicBeeApiInterface musicBee, string trackUrl,
            string originalLyrics, string expectedTag, string editedLyrics,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(trackUrl) ||
                musicBee.Library_SetFileTag == null ||
                musicBee.Library_CommitTagsToFile == null)
            {
                error = "MusicBee cannot save lyrics for this track.";
                return false;
            }
            var tagUpdated = false;
            try
            {
                if (expectedTag != null && musicBee.Library_GetFileTag != null &&
                    musicBee.Library_GetFileTag(trackUrl, Plugin.MetaDataType.Lyrics) != expectedTag)
                {
                    error = "The Lyrics field changed since editing began. Reopen the editor to avoid overwriting it.";
                    return false;
                }

                // Keep an exact copy before asking MusicBee to replace the tag.
                var storage = musicBee.Setting_GetPersistentStoragePath?.Invoke();
                if (string.IsNullOrWhiteSpace(storage))
                {
                    error = "MusicBee did not provide a place to back up the original lyrics.";
                    return false;
                }
                var folder = Path.Combine(storage, "DesktopLyrics-TimingBackups");
                Directory.CreateDirectory(folder);
                string key;
                using (var sha = SHA256.Create())
                    key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(trackUrl)),
                        0, 8).Replace("-", "");
                var filename = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff",
                    CultureInfo.InvariantCulture) + "-" + key + ".lrc";
                File.WriteAllText(Path.Combine(folder, filename), originalLyrics,
                    new UTF8Encoding(false));

                tagUpdated = musicBee.Library_SetFileTag(trackUrl,
                    Plugin.MetaDataType.Lyrics, editedLyrics);
                if (!tagUpdated)
                {
                    error = "MusicBee rejected the updated Lyrics field. No lyrics were saved.";
                    return false;
                }
                if (!musicBee.Library_CommitTagsToFile(trackUrl))
                {
                    RestoreOriginalTag(musicBee, trackUrl, expectedTag);
                    error = "MusicBee could not write the updated Lyrics field to the file. The original lyrics were backed up.";
                    return false;
                }
                try { musicBee.MB_RefreshPanels?.Invoke(); }
                catch (Exception) { /* Tag has already been saved. */ }
                return true;
            }
            catch (Exception ex)
            {
                if (tagUpdated) RestoreOriginalTag(musicBee, trackUrl, expectedTag);
                error = "MusicBee could not save the updated lyrics: " + ex.Message;
                return false;
            }
        }

        private static void RestoreOriginalTag(Plugin.MusicBeeApiInterface musicBee,
            string trackUrl, string expectedTag)
        {
            try
            {
                musicBee.Library_SetFileTag(trackUrl, Plugin.MetaDataType.Lyrics,
                    expectedTag ?? "");
            }
            catch (Exception) { /* The original LRC remains in the backup. */ }
        }
    }
}
