using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin
{
    // Public catalog metadata only. No audio, account credentials or file paths
    // are sent to Deezer. Never accept a search snippet as a tempo measurement.
    internal static class DeezerBpmClient
    {
        private static readonly SemaphoreSlim Requests = new SemaphoreSlim(1, 1);
        private static DateTime _nextRequestUtc;

        internal static async Task<GetSongBpmClient.LookupResult> SearchAsync(
            string title, string artist, string album, int durationSeconds,
            CancellationToken cancellationToken)
        {
            await Requests.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var query = GetSongBpmClient.CatalogQuery(title, artist);
                var json = await RequestAsync("search?q=" + Uri.EscapeDataString(query) +
                    "&limit=25", cancellationToken).ConfigureAwait(false);
                var candidates = Candidates(json, title, artist, album, durationSeconds);
                if (candidates.Count == 0)
                    return Miss("Deezer: no matching title, artist and recording length.");
                if (candidates.Count > 5)
                    return Miss("Deezer: too many matching recordings; album did not resolve them.");
                var matches = new List<JToken>();
                foreach (var candidate in candidates)
                {
                    var id = (long)candidate["id"];
                    var detail = JObject.Parse(await RequestAsync("track/" +
                        id.ToString(CultureInfo.InvariantCulture), cancellationToken)
                        .ConfigureAwait(false));
                    CheckError(detail);
                    if ((long?)detail["id"] == id) matches.Add(detail);
                }
                return MatchDetails(matches, title, artist, album, durationSeconds);
            }
            finally { Requests.Release(); }
        }

        internal static List<JToken> Candidates(string json, string title, string artist,
            string album, int durationSeconds)
        {
            var root = JObject.Parse(json);
            CheckError(root);
            var data = root["data"] as JArray ?? new JArray();
            var matches = data.Where(t => (long?)t["id"] > 0 &&
                Matches(t, title, artist, durationSeconds)).GroupBy(t => (long)t["id"])
                .Select(g => g.First()).ToList();
            var sameAlbum = matches.Where(t => GetSongBpmClient.CatalogAlbumMatches(
                (string)t["album"]?["title"], album)).ToList();
            return sameAlbum.Count > 0 ? sameAlbum : matches;
        }

        internal static GetSongBpmClient.LookupResult MatchDetails(IEnumerable<JToken> tracks,
            string title, string artist, string album, int durationSeconds)
        {
            var matches = Candidates(new JObject { ["data"] = new JArray(tracks) }.ToString(),
                title, artist, album, durationSeconds);
            double bpm = 0;
            long id = 0;
            foreach (var track in matches)
            {
                double candidate;
                if (!double.TryParse((string)track["bpm"], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out candidate) || double.IsNaN(candidate) ||
                    double.IsInfinity(candidate) || candidate < 40 || candidate > 240) continue;
                if (bpm > 0 && Math.Abs(bpm - candidate) > 0.1)
                    return Miss("Deezer: matching recordings have conflicting BPMs.");
                if (bpm == 0) { bpm = candidate; id = (long)track["id"]; }
            }
            return bpm == 0 ? Miss("Deezer: matching recording has no usable BPM.") :
                new GetSongBpmClient.LookupResult { Bpm = bpm, Source = "Deezer",
                    SourceUrl = "https://www.deezer.com/track/" + id,
                    Detail = "Deezer catalog BPM: " + bpm.ToString("0.##", CultureInfo.InvariantCulture) };
        }

        private static bool Matches(JToken track, string title, string artist, int duration)
        {
            var candidateDuration = (int?)track["duration"] ?? 0;
            return GetSongBpmClient.CatalogTrackMatches((string)track["title"],
                (string)track["artist"]?["name"], title, artist) &&
                (duration <= 0 || (candidateDuration > 0 &&
                    Math.Abs(candidateDuration - duration) <= Math.Max(5, duration * 0.01)));
        }

        private static GetSongBpmClient.LookupResult Miss(string detail)
        {
            return new GetSongBpmClient.LookupResult { Detail = detail };
        }

        private static void CheckError(JObject root)
        {
            if (root["error"] != null)
                throw new InvalidOperationException("Deezer could not complete the metadata lookup.");
        }

        private static async Task<string> RequestAsync(string path, CancellationToken token)
        {
            var delay = _nextRequestUtc - DateTime.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            var request = (HttpWebRequest)WebRequest.Create("https://api.deezer.com/" + path);
            request.UserAgent = "DesktopLyrics/1.15.27 (https://github.com/jldkrocks/MusicBee-DesktopLyrics)";
            request.Accept = "application/json";
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = request.ReadWriteTimeout = 12000;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                using (timeout.Token.Register(() => request.Abort()))
                try
                {
                    using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        var text = new System.Text.StringBuilder();
                        var buffer = new char[4096];
                        int read;
                        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                        {
                            timeout.Token.ThrowIfCancellationRequested();
                            if (text.Length + read > 256000)
                                throw new InvalidOperationException("Deezer returned too much metadata.");
                            text.Append(buffer, 0, read);
                        }
                        return text.ToString();
                    }
                }
                catch (WebException ex)
                {
                    token.ThrowIfCancellationRequested();
                    using (ex.Response)
                        throw new InvalidOperationException(timeout.IsCancellationRequested ?
                            "Deezer metadata lookup timed out." : "Could not reach Deezer metadata.", ex);
                }
                finally { _nextRequestUtc = DateTime.UtcNow.AddMilliseconds(600); }
            }
        }
    }
}
