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
        public bool Online;
        public string Source, SourceUrl;
        public bool TwoBeatPhase;
        public int BeatPatternVersion;
    }

    // One small file per song avoids rewriting the main plugin settings while
    // the player learns tempos. Nothing is written to the music files or tags.
    internal sealed class PartyTempoStore
    {
        private readonly string _folder;
        internal string PlaybackTracePath => Path.Combine(_folder, "last-playback-seek.log");
        private const string KeyFileName = "getsongbpm-key.bin";
        private static readonly byte[] KeyEntropy =
            Encoding.UTF8.GetBytes("MusicBee-DesktopLyrics:GetSongBPM:v1");

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
                // Older saved values used an arbitrary phase. Keep the BPM,
                // but start at the assumed beat at 0.
                if (!entry.TwoBeatPhase)
                    entry.OriginMs = PartyAnimation.OriginForBeat(0, entry.Bpm);
                else if (entry.BeatPatternVersion < 3)
                {
                    // Earlier builds used two, four or eight beats per loop.
                    // Recover the beat carrying frame 6 before moving every
                    // song to four beats per loop. Preserve manual tap phase.
                    var oldBeats = entry.BeatPatternVersion < 2 ? 2 :
                        entry.Bpm >= 220 ? 8 : entry.Bpm >= 110 ? 4 : 2;
                    if (oldBeats != 4 || entry.BeatPatternVersion < 2)
                    {
                        var oldLoop = oldBeats * 60000d / entry.Bpm;
                        var beat = (entry.OriginMs + oldLoop / 2) % oldLoop;
                        entry.OriginMs = PartyAnimation.OriginForBeat(
                            (int)Math.Round(beat), entry.Bpm);
                    }
                }
                return entry;
            }
            catch (Exception) { return null; } // Inaccessible or invalid cache.
        }

        internal void Save(string trackUrl, double bpm, int originMs, bool manual,
            bool online = false, string source = null, string sourceUrl = null)
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
                    Manual = manual, Online = online, Source = source, SourceUrl = sourceUrl,
                    TwoBeatPhase = true,
                    BeatPatternVersion = 3
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

        internal PartyTempoMap LoadMap(string trackUrl)
        {
            if (string.IsNullOrWhiteSpace(trackUrl)) return null;
            try
            {
                var path = FileName(trackUrl) + ".map";
                if (!File.Exists(path)) return null;
                var map = JsonConvert.DeserializeObject<PartyTempoMap>(File.ReadAllText(path));
                if (map == null || map.TrackUrl != trackUrl) return null;
                map.Validate();
                return map;
            }
            catch (Exception) { return null; }
        }

        internal void SaveMap(PartyTempoMap map)
        {
            map.Validate();
            Directory.CreateDirectory(_folder);
            var path = FileName(map.TrackUrl) + ".map";
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonConvert.SerializeObject(map), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal void DeleteMap(string trackUrl)
        {
            if (string.IsNullOrWhiteSpace(trackUrl)) return;
            var path = FileName(trackUrl) + ".map";
            if (File.Exists(path)) File.Delete(path);
        }

        internal string LoadApiKey()
        {
            try
            {
                var filename = Path.Combine(_folder, KeyFileName);
                return File.Exists(filename) ? Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(File.ReadAllBytes(filename), KeyEntropy,
                        DataProtectionScope.CurrentUser)) : "";
            }
            catch (Exception) { return ""; }
        }

        internal void SaveApiKey(string apiKey)
        {
            var filename = Path.Combine(_folder, KeyFileName);
            apiKey = apiKey?.Trim() ?? "";
            if (apiKey.Length == 0)
            {
                if (File.Exists(filename)) File.Delete(filename);
                return;
            }
            Directory.CreateDirectory(_folder);
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey),
                KeyEntropy, DataProtectionScope.CurrentUser);
            var temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, encrypted);
                if (File.Exists(filename)) File.Replace(temporary, filename, null);
                else File.Move(temporary, filename);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
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
