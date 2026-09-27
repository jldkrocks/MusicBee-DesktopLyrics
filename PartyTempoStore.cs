using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace MusicBeePlugin
{
    internal sealed class PartyTempoEntry
    {
        public string TrackUrl;
        public double Bpm;
        public int OriginMs;
        public bool Manual;
        public bool TwoBeatPhase;
    }

    // One small file per song avoids rewriting the main plugin settings while
    // the player learns tempos. Nothing is written to the music files or tags.
    internal sealed class PartyTempoStore
    {
        private readonly string _folder;

        internal PartyTempoStore(string persistentStoragePath)
        {
            if (string.IsNullOrWhiteSpace(persistentStoragePath))
                throw new ArgumentException("MusicBee did not provide plugin storage.");
            _folder = Path.Combine(persistentStoragePath, "DesktopLyrics-PartyTempo");
        }

        internal PartyTempoEntry Load(string trackUrl)
        {
            if (string.IsNullOrWhiteSpace(trackUrl)) return null;
            try
            {
                var filename = FileName(trackUrl);
                if (!File.Exists(filename)) return null;
                var entry = JsonConvert.DeserializeObject<PartyTempoEntry>(
                    File.ReadAllText(filename));
                if (entry == null || entry.TrackUrl != trackUrl ||
                    entry.Bpm < 40 || entry.Bpm > 240 ||
                    double.IsNaN(entry.Bpm) || double.IsInfinity(entry.Bpm) ||
                    entry.OriginMs < 0 || entry.OriginMs > 6000) return null;
                // Older saved values used a four-beat loop and an arbitrary
                // phase. Keep the BPM, but start at the assumed beat at 0.
                if (!entry.TwoBeatPhase)
                    entry.OriginMs = PartyAnimation.OriginForBeat(0, entry.Bpm);
                return entry;
            }
            catch (Exception) { return null; } // Inaccessible or invalid cache.
        }

        internal void Save(string trackUrl, double bpm, int originMs, bool manual)
        {
            if (string.IsNullOrWhiteSpace(trackUrl) || bpm < 40 || bpm > 240 ||
                double.IsNaN(bpm) || double.IsInfinity(bpm) ||
                originMs < 0 || originMs > 6000)
                throw new ArgumentException("A current song and a BPM from 40 to 240 are required.");
            Directory.CreateDirectory(_folder);
            var filename = FileName(trackUrl);
            var temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(new PartyTempoEntry
                {
                    TrackUrl = trackUrl, Bpm = bpm, OriginMs = originMs,
                    Manual = manual, TwoBeatPhase = true
                }), new UTF8Encoding(false));
                if (File.Exists(filename)) File.Replace(temporary, filename, null);
                else File.Move(temporary, filename);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal void Delete(string trackUrl)
        {
            if (string.IsNullOrWhiteSpace(trackUrl)) return;
            var filename = FileName(trackUrl);
            if (File.Exists(filename)) File.Delete(filename);
        }

        private string FileName(string trackUrl)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(trackUrl));
                return Path.Combine(_folder, BitConverter.ToString(hash).Replace("-", "") + ".json");
            }
        }
    }
}
