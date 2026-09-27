using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin
{
    internal static class GetSongBpmClient
    {
        private const string Endpoint = "https://api.getsong.co/search/?";
        private const string UserAgent =
            "DesktopLyrics/1.15.16 (https://github.com/jldkrocks/MusicBee-DesktopLyrics)";
        private static readonly SemaphoreSlim Requests = new SemaphoreSlim(1, 1);
        private static DateTime _nextRequestUtc = DateTime.MinValue;
        private static readonly Regex TrailingDetail = new Regex(
            @"(?:\s*[\(\[]\s*(?:(?:feat\.?|ft\.?|featuring)\s+[^\)\]]+|(?:\d{4}\s+)?re-?master(?:ed)?(?:\s+\d{4})?)\s*[\)\]]|\s+[-–—]\s*(?:(?:feat\.?|ft\.?|featuring)\s+.+|(?:\d{4}\s+)?re-?master(?:ed)?(?:\s+\d{4})?)|\s+(?:feat\.?|ft\.?|featuring)\s+.+)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex FeaturedArtist = new Regex(
            @"\s+(?:feat\.?|ft\.?|featuring)\s+.+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex RecordingVersion = new Regex(
            @"\b(?:live|remix|acoustic|instrumental|radio\s+edit|sped\s+up|slowed|demo|karaoke|re-recorded)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OstSuffix = new Regex(
            @"\s*[-–—]\s*OST\s+ver\.?[-.]?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal sealed class LookupResult
        {
            internal double Bpm;
            internal string Detail;
        }

        private sealed class MatchEvaluation
        {
            internal double Bpm;
            internal bool HasResults, HasTitle, HasArtist, HasTempo, Ambiguous;
        }

        internal static async Task<LookupResult> SearchAsync(string title, string artist,
            string album, string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist) ||
                string.IsNullOrWhiteSpace(apiKey)) return new LookupResult
                { Detail = "This song needs a title, artist and API key." };
            await Requests.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var combined = new MatchEvaluation();
                foreach (var query in BuildQueries(title, artist))
                {
                    var json = await RequestAsync(query.Item1, query.Item2, apiKey,
                        cancellationToken).ConfigureAwait(false);
                    var match = Evaluate(json, title, artist, album);
                    if (match.Bpm > 0 && !combined.Ambiguous)
                        return new LookupResult { Bpm = match.Bpm };
                    combined.HasResults |= match.HasResults;
                    combined.HasTitle |= match.HasTitle;
                    combined.HasArtist |= match.HasArtist;
                    combined.HasTempo |= match.HasTempo;
                    combined.Ambiguous |= match.Ambiguous;
                }
                return new LookupResult { Detail = combined.Ambiguous ?
                    "Multiple BPMs match this title and artist; the album did not resolve them." :
                    combined.HasTempo ? "Matching BPMs could not be resolved." :
                    combined.HasArtist ? "Title and artist found, but no usable BPM was listed." :
                    combined.HasTitle ? "Title found, but the listed artist does not match." :
                    combined.HasResults ? "Songs were returned, but none matched this title/version." :
                    "GetSongBPM returned no songs for this title." };
            }
            finally { Requests.Release(); }
        }

        // The combined search is most selective, but the catalog often has a
        // shorter title or only the lead artist. The broader search is still
        // checked against the original song tags before accepting its BPM.
        internal static List<Tuple<string, string>> BuildQueries(string title, string artist)
        {
            var queries = new List<Tuple<string, string>>();
            var cleanTitle = SearchTitle(title);
            var leadArtist = PrimaryArtist(artist);
            queries.Add(Tuple.Create("both", "song:" + Limit(title) +
                " artist:" + Limit(artist)));
            if (cleanTitle != title.Trim() || leadArtist != artist.Trim())
                queries.Add(Tuple.Create("both", "song:" + Limit(cleanTitle) +
                    " artist:" + Limit(leadArtist)));
            queries.Add(Tuple.Create("song", Limit(cleanTitle)));
            return queries;
        }

        private static string Limit(string value)
        {
            value = value.Trim();
            return value.Substring(0, Math.Min(150, value.Length));
        }

        private static async Task<string> RequestAsync(string type, string lookup,
            string apiKey, CancellationToken cancellationToken)
        {
            var delay = _nextRequestUtc - DateTime.UtcNow;
            if (delay > TimeSpan.FromMinutes(1))
                throw new InvalidOperationException("GetSongBPM is rate limiting searches. Try later.");
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            var request = (HttpWebRequest)WebRequest.Create(Endpoint + "type=" + type +
                "&limit=" + (type == "song" ? "50" : "30") + "&lookup=" +
                Uri.EscapeDataString(lookup));
            try
            {
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
                            return await ReadLimitedAsync(reader, cancellationToken)
                                .ConfigureAwait(false);
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
            }
        }

        internal static double MatchTempo(string json, string title, string artist, string album)
        {
            return Evaluate(json, title, artist, album).Bpm;
        }

        private static MatchEvaluation Evaluate(string json, string title,
            string artist, string album)
        {
            var root = JObject.Parse(json);
            if (root["error"] != null)
                throw new InvalidOperationException("GetSongBPM could not complete the search.");
            var evaluation = new MatchEvaluation();
            var results = root["search"] as JArray;
            if (results == null) return evaluation;
            evaluation.HasResults = results.Count > 0;
            var wantedTitle = Normalize(CoreTitle(title));
            var wantedArtist = Normalize(artist);
            var wantedAlbum = Normalize(album);
            var matches = new List<Tuple<double, bool>>();
            foreach (var item in results)
            {
                var candidateTitle = (string)item["title"];
                if (Normalize(CoreTitle(candidateTitle)) != wantedTitle ||
                    // Distinct featured versions must not collapse to one song.
                    (Normalize(title) != Normalize(candidateTitle) &&
                     HasFeature(title) && HasFeature(candidateTitle))) continue;
                evaluation.HasTitle = true;
                if (!ArtistMatches(item["artist"], wantedArtist,
                        Normalize(PrimaryArtist(artist)))) continue;
                evaluation.HasArtist = true;
                double bpm;
                if (!double.TryParse((string)item["tempo"], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out bpm) || bpm < 40 || bpm > 240 ||
                    double.IsNaN(bpm) || double.IsInfinity(bpm)) continue;
                evaluation.HasTempo = true;
                var itemAlbum = item["album"];
                matches.Add(Tuple.Create(bpm, wantedAlbum.Length > 0 &&
                    AlbumMatches(itemAlbum, wantedAlbum)));
            }
            if (matches.Count == 0) return evaluation;
            // Prefer this album if it resolves conflicting versions. Otherwise
            // conflicting tempos for the same title and artist need manual review.
            var albumMatches = matches.FindAll(match => match.Item2);
            if (albumMatches.Count > 0) matches = albumMatches;
            var first = matches[0].Item1;
            foreach (var match in matches)
                if (Math.Abs(match.Item1 - first) > 0.5)
                {
                    evaluation.Ambiguous = true;
                    return evaluation;
                }
            evaluation.Bpm = first;
            return evaluation;
        }

        private static bool AlbumMatches(JToken albums, string wanted)
        {
            if (albums is JArray)
            {
                foreach (var album in (JArray)albums)
                    if (Normalize((string)album["title"]) == wanted) return true;
                return false;
            }
            return Normalize((string)albums?["title"]) == wanted;
        }

        private static bool ArtistMatches(JToken artists, string wanted, string lead)
        {
            if (lead.Length == 0 || artists == null) return false;
            if (artists is JArray)
            {
                foreach (var artist in (JArray)artists)
                    if (ArtistNameMatches((string)artist["name"], wanted, lead)) return true;
                return false;
            }
            return ArtistNameMatches((string)artists["name"], wanted, lead);
        }

        private static bool ArtistNameMatches(string name, string wanted, string lead)
        {
            return Normalize(name) == wanted ||
                Normalize(PrimaryArtist(name)) == lead;
        }

        private static string PrimaryArtist(string artist)
        {
            if (string.IsNullOrWhiteSpace(artist)) return "";
            var primary = artist.Split(new[] { ';' }, 2)[0];
            return FeaturedArtist.Replace(primary, "").Trim();
        }

        private static bool HasFeature(string title)
        {
            return !string.IsNullOrEmpty(title) && Regex.IsMatch(title,
                @"\b(?:feat\.?|ft\.?|featuring)\s+", RegexOptions.IgnoreCase);
        }

        private static string CoreTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "";
            var core = title.Trim();
            string previous;
            do
            {
                previous = core;
                var shortened = TrailingDetail.Replace(core, "").Trim();
                if (VersionTerms(shortened) != VersionTerms(core)) break;
                core = shortened;
            } while (core.Length > 0 && core != previous);
            return core.Length > 0 ? core : title.Trim();
        }

        private static string SearchTitle(string title)
        {
            var query = CoreTitle(title).Trim(' ', '~', '*', '♪', '☆', '★');
            var shorter = OstSuffix.Replace(query, "").Trim();
            return shorter.Length > 0 ? shorter : query;
        }

        private static string VersionTerms(string title)
        {
            var versions = new StringBuilder();
            foreach (Match match in RecordingVersion.Matches(title))
                versions.Append(Normalize(match.Value)).Append(';');
            return versions.ToString();
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var normalized = value.Normalize(NormalizationForm.FormKC)
                .Normalize(NormalizationForm.FormD).ToLowerInvariant();
            var result = new StringBuilder(normalized.Length);
            foreach (var character in normalized)
            {
                if (char.IsLetterOrDigit(character)) result.Append(character);
                else if (CharUnicodeInfo.GetUnicodeCategory(character) ==
                         UnicodeCategory.NonSpacingMark && result.Length > 0 &&
                         result[result.Length - 1] > 127)
                    result.Append(character);
            }
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
