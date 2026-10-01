using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MusicBeePlugin
{
    internal static class PartyOnlineLookup
    {
        // Even old automatic results may have been confirmed by the listener.
        internal static bool CanLookup(double tagBpm, PartyTempoEntry saved)
        {
            return tagBpm <= 0 && saved == null;
        }

        internal static Task<GetSongBpmClient.LookupResult> SearchAsync(string title,
            string artist, string album, int durationSeconds, string apiKey, bool deezer,
            CancellationToken token)
        {
            var providers = new List<Func<Task<GetSongBpmClient.LookupResult>>>();
            if (!string.IsNullOrWhiteSpace(apiKey)) providers.Add(() =>
                GetSongBpmClient.SearchAsync(title, artist, album, apiKey, token));
            if (deezer) providers.Add(() =>
                DeezerBpmClient.SearchAsync(title, artist, album, durationSeconds, token));
            return FirstMatchAsync(providers, token);
        }

        internal static async Task<GetSongBpmClient.LookupResult> FirstMatchAsync(
            IEnumerable<Func<Task<GetSongBpmClient.LookupResult>>> providers, CancellationToken token)
        {
            var details = new List<string>();
            foreach (var provider in providers)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var result = await provider().ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (result.Bpm > 0) return result;
                    if (!string.IsNullOrEmpty(result.Detail)) details.Add(result.Detail);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { details.Add(ex.Message); }
            }
            return new GetSongBpmClient.LookupResult { Detail = string.Join(" ", details) };
        }
    }
}
