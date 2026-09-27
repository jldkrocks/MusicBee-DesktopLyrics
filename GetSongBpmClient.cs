using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin
{
    internal static class GetSongBpmClient
    {
        private const string Endpoint = "https://api.getsong.co/search/?type=both&limit=20&lookup=";
        private const string UserAgent =
            "DesktopLyrics/1.15.14 (https://github.com/jldkrocks/MusicBee-DesktopLyrics)";
        private static readonly SemaphoreSlim Requests = new SemaphoreSlim(1, 1);
        private static DateTime _nextRequestUtc = DateTime.MinValue;

        internal static async Task<double> SearchAsync(string title, string artist,
            string album, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist) ||
                string.IsNullOrWhiteSpace(apiKey)) return 0;
            await Requests.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var delay = _nextRequestUtc - DateTime.UtcNow;
                if (delay > TimeSpan.FromMinutes(1))
                    throw new InvalidOperationException("GetSongBPM is rate limiting searches. Try later.");
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                var query = "song:" + title.Trim().Substring(0, Math.Min(150, title.Trim().Length)) +
                    " artist:" + artist.Trim().Substring(0, Math.Min(150, artist.Trim().Length));
                var request = (HttpWebRequest)WebRequest.Create(
                    Endpoint + Uri.EscapeDataString(query));
                request.Method = "GET";
                request.UserAgent = UserAgent;
                request.Accept = "application/json";
                request.Headers["X-API-KEY"] = apiKey.Trim();
                request.Timeout = 12000;
                request.ReadWriteTimeout = 12000;
                request.AutomaticDecompression = DecompressionMethods.GZip |
                    DecompressionMethods.Deflate;
                request.AllowAutoRedirect = false;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    using (timeout.Token.Register(() => request.Abort()))
                    try
                    {
                        using (var response = (HttpWebResponse)await request.GetResponseAsync()
                                   .ConfigureAwait(false))
                        using (var stream = response.GetResponseStream())
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            var json = await ReadLimitedAsync(reader, cancellationToken)
                                .ConfigureAwait(false);
                            return MatchTempo(json, title, artist, album);
                        }
                    }
                    catch (WebException ex)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException(cancellationToken);
                        if (timeout.IsCancellationRequested)
                            throw new InvalidOperationException("GetSongBPM did not respond in time.", ex);
                        using (var response = ex.Response as HttpWebResponse)
                        {
                            if (response != null && response.StatusCode == (HttpStatusCode)429)
                            {
                                _nextRequestUtc = DateTime.UtcNow.AddHours(1);
                                throw new InvalidOperationException("GetSongBPM is rate limiting searches.", ex);
                            }
                            if (response != null && (response.StatusCode == HttpStatusCode.Unauthorized ||
                                response.StatusCode == HttpStatusCode.Forbidden))
                                throw new InvalidOperationException(
                                    "GetSongBPM rejected the API key. Check Online Party BPM settings.", ex);
                            if (response != null)
                                throw new InvalidOperationException("GetSongBPM returned HTTP " +
                                    (int)response.StatusCode + ".", ex);
                        }
                        throw new InvalidOperationException("Could not reach GetSongBPM.", ex);
                    }
                }
            }
            finally
            {
                var courtesy = DateTime.UtcNow.AddMilliseconds(1200);
                if (_nextRequestUtc < courtesy) _nextRequestUtc = courtesy;
                Requests.Release();
            }
        }

        internal static double MatchTempo(string json, string title, string artist, string album)
        {
            var root = JObject.Parse(json);
            if (root["error"] != null)
                throw new InvalidOperationException("GetSongBPM could not complete the search.");
            var results = root["search"] as JArray;
            if (results == null) return 0;
            var wantedTitle = Normalize(title);
            var wantedArtist = Normalize(artist);
            var wantedAlbum = Normalize(album);
            var matches = new List<Tuple<double, bool>>();
            foreach (var item in results)
            {
                if (Normalize((string)item["title"]) != wantedTitle ||
                    !ArtistMatches(item["artist"], wantedArtist)) continue;
                double bpm;
                if (!double.TryParse((string)item["tempo"], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out bpm) || bpm < 40 || bpm > 240 ||
                    double.IsNaN(bpm) || double.IsInfinity(bpm)) continue;
                var itemAlbum = item["album"];
                var albumName = itemAlbum is JArray ? (string)itemAlbum.First?["title"] :
                    (string)itemAlbum?["title"];
                matches.Add(Tuple.Create(bpm, wantedAlbum.Length > 0 &&
                    Normalize(albumName) == wantedAlbum));
            }
            if (matches.Count == 0) return 0;
            // Prefer this album if it resolves conflicting versions. Otherwise
            // conflicting tempos for the same title and artist need manual review.
            var albumMatches = matches.FindAll(match => match.Item2);
            if (albumMatches.Count > 0) matches = albumMatches;
            var first = matches[0].Item1;
            foreach (var match in matches)
                if (Math.Abs(match.Item1 - first) > 0.5) return 0;
            return first;
        }

        private static bool ArtistMatches(JToken artists, string wanted)
        {
            if (wanted.Length == 0 || artists == null) return false;
            if (artists is JArray)
            {
                foreach (var artist in (JArray)artists)
                    if (Normalize((string)artist["name"]) == wanted) return true;
                return false;
            }
            return Normalize((string)artists["name"]) == wanted;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var result = new StringBuilder(normalized.Length);
            foreach (var character in normalized)
                if (char.IsLetterOrDigit(character)) result.Append(character);
            return result.ToString();
        }

        private static async Task<string> ReadLimitedAsync(StreamReader reader,
            CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)
                       .ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (text.Length + read > 256000)
                    throw new InvalidOperationException("GetSongBPM returned too many results.");
                text.Append(buffer, 0, read);
            }
            return text.ToString();
        }
    }
}
