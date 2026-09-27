using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace MusicBeePlugin
{
    // English text belongs to the plugin, not the MusicBee Lyrics tag. The
    // signature ignores timestamps so timing edits keep their translation.
    internal sealed class EnglishTranslationStore
    {
        private readonly string _folder;

        private sealed class SavedEnglish
        {
            public string TrackUrl;
            public string LyricSignature;
            public string SourceUrl;
            public List<string> Lines;
        }

        public EnglishTranslationStore(string persistentStoragePath)
        {
            if (string.IsNullOrWhiteSpace(persistentStoragePath))
                throw new ArgumentException("MusicBee did not provide plugin storage.");
            _folder = Path.Combine(persistentStoragePath, "DesktopLyrics-English");
        }

        public string[] Load(string trackUrl, IList<LyricParser.LyricEntry> lyrics)
        {
            if (string.IsNullOrWhiteSpace(trackUrl) || lyrics == null) return null;
            try
            {
                var filename = FileName(trackUrl);
                if (!File.Exists(filename)) return null;
                var saved = JsonConvert.DeserializeObject<SavedEnglish>(File.ReadAllText(filename));
                if (saved == null || saved.TrackUrl != trackUrl ||
                    saved.LyricSignature != Signature(lyrics) ||
                    saved.Lines == null || saved.Lines.Count != lyrics.Count)
                    return null;
                return saved.Lines.ToArray();
            }
            catch (Exception) { return null; } // Corrupt or inaccessible cache.
        }

        public void Save(string trackUrl, IList<LyricParser.LyricEntry> lyrics,
            IList<string> english, string sourceUrl)
        {
            if (string.IsNullOrWhiteSpace(trackUrl) || lyrics == null ||
                english == null || lyrics.Count != english.Count)
                throw new ArgumentException("The English lines no longer match this song.");
            Directory.CreateDirectory(_folder);
            var filename = FileName(trackUrl);
            var temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var saved = new SavedEnglish
            {
                TrackUrl = trackUrl, LyricSignature = Signature(lyrics),
                SourceUrl = sourceUrl, Lines = new List<string>(english)
            };
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(saved),
                    new UTF8Encoding(false));
                if (File.Exists(filename)) File.Replace(temporary, filename, null);
                else File.Move(temporary, filename);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public void Delete(string trackUrl)
        {
            if (string.IsNullOrWhiteSpace(trackUrl)) return;
            var filename = FileName(trackUrl);
            if (File.Exists(filename)) File.Delete(filename);
        }

        public static string Signature(IList<LyricParser.LyricEntry> lyrics)
        {
            var text = new StringBuilder();
            foreach (var line in lyrics)
                text.Append((line.LyricLine1 ?? "").Trim()).Append('\n');
            return Hash(text.ToString());
        }

        private string FileName(string trackUrl)
        {
            return Path.Combine(_folder, Hash(trackUrl) + ".json");
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)))
                    .Replace("-", "");
        }
    }
}
