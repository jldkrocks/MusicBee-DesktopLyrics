using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MusicBeePlugin
{
    internal sealed class LrcLibRecord
    {
        public long Id;
        public string TrackName;
        public string ArtistName;
        public string AlbumName;
        public double Duration;
        public bool Instrumental;
        public string PlainLyrics;
        public string SyncedLyrics;
        public bool HasTimedLyrics => !Instrumental && !string.IsNullOrWhiteSpace(SyncedLyrics);
    }

    internal static class LrcLibClient
    {
        private const string Endpoint = "https://lrclib.net/api/search?q=";
        private const string ClientName =
            "DesktopLyrics/1.14.0 (https://github.com/jldkrocks/MusicBee-DesktopLyrics)";
        private static readonly SemaphoreSlim Requests = new SemaphoreSlim(1, 1);
        private static DateTime _nextRequestUtc = DateTime.MinValue;

        public static async Task<List<LrcLibRecord>> SearchAsync(string query,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<LrcLibRecord>();
            await Requests.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var delay = _nextRequestUtc - DateTime.UtcNow;
                if (delay > TimeSpan.FromSeconds(1))
                    throw new InvalidOperationException("LRCLIB asked us to wait. Try again in " +
                        Math.Ceiling(delay.TotalSeconds).ToString(CultureInfo.InvariantCulture) +
                        " seconds.");
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                var request = (HttpWebRequest)WebRequest.Create(
                    Endpoint + Uri.EscapeDataString(query.Trim()));
                request.Method = "GET";
                request.UserAgent = ClientName;
                request.Accept = "application/json";
                request.Timeout = 15000;
                request.ReadWriteTimeout = 15000;
                request.AutomaticDecompression = DecompressionMethods.GZip |
                    DecompressionMethods.Deflate;
                request.MaximumAutomaticRedirections = 2;
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
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
                            return ParseResults(json);
                        }
                    }
                    catch (WebException ex)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException(cancellationToken);
                        if (timeout.IsCancellationRequested)
                            throw new InvalidOperationException(
                                "LRCLIB did not respond in time. Try again later.", ex);
                        using (var response = ex.Response as HttpWebResponse)
                        {
                            if (response != null &&
                                response.StatusCode == (HttpStatusCode)429)
                            {
                                var retry = RetryDelay(response.Headers["Retry-After"]);
                                _nextRequestUtc = DateTime.UtcNow + retry;
                                throw new InvalidOperationException(
                                    "LRCLIB is rate limiting requests. Try again in " +
                                    Math.Ceiling(retry.TotalSeconds).ToString(CultureInfo.InvariantCulture) +
                                    " seconds.", ex);
                            }
                            if (response != null)
                                throw new InvalidOperationException("LRCLIB returned HTTP " +
                                    (int)response.StatusCode + ". Try again later.", ex);
                        }
                        throw new InvalidOperationException(
                            "Could not reach LRCLIB. Check the connection and try again.", ex);
                    }
                }
            }
            finally
            {
                var courtesyDelay = DateTime.UtcNow.AddMilliseconds(350);
                if (_nextRequestUtc < courtesyDelay) _nextRequestUtc = courtesyDelay;
                Requests.Release();
            }
        }

        internal static List<LrcLibRecord> ParseResults(string json)
        {
            var results = JsonConvert.DeserializeObject<List<LrcLibRecord>>(json) ??
                new List<LrcLibRecord>();
            return results.Take(20).ToList();
        }

        internal static List<LrcLibRecord> SortByDuration(IEnumerable<LrcLibRecord> records,
            int durationMs)
        {
            var seconds = durationMs > 0 ? durationMs / 1000.0 : 0;
            return records.OrderBy(record => seconds <= 0 || record.Duration <= 0 ?
                    double.MaxValue : Math.Abs(record.Duration - seconds))
                .ThenBy(record => record.HasTimedLyrics ? 0 : 1).ToList();
        }

        internal static TimeSpan RetryDelay(string header)
        {
            int seconds;
            if (int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out seconds)) return TimeSpan.FromSeconds(Math.Max(1, Math.Min(86400, seconds)));
            DateTimeOffset date;
            if (DateTimeOffset.TryParse(header, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out date))
            {
                var wait = date - DateTimeOffset.UtcNow;
                return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) :
                    wait > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : wait;
            }
            return TimeSpan.FromSeconds(30);
        }

        private static async Task<string> ReadLimitedAsync(StreamReader reader,
            CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            var buffer = new char[8192];
            int read;
            while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)
                       .ConfigureAwait(false)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (text.Length + read > 4_000_000)
                    throw new InvalidOperationException("LRCLIB returned too much data for one search.");
                text.Append(buffer, 0, read);
            }
            return text.ToString();
        }
    }
}
