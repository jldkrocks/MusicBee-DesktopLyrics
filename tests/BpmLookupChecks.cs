using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin
{
    internal static class BpmLookupChecks
    {
        internal static void Run()
        {
            Func<long, string, string, int, double, string, JObject> track = (id, title, artist, seconds, bpm, album) =>
                new JObject { ["id"] = id, ["title"] = title, ["artist"] = new JObject { ["name"] = artist },
                    ["duration"] = seconds, ["bpm"] = bpm, ["album"] = new JObject { ["title"] = album } };
            var studio = track(1, "Bitter Sweet Symphony (Remastered 2016)", "The Verve", 357, 170.84, "Urban Hymns");
            var live = track(2, "Bitter Sweet Symphony (Live)", "The Verve", 358, 180, "Live");
            var cover = track(3, "Bitter Sweet Symphony", "A Cover Band", 357, 170, "Urban Hymns");
            var edit = track(4, "Bitter Sweet Symphony", "The Verve", 275, 171, "Urban Hymns");
            var list = new JArray(studio, live, cover, edit);
            var candidates = DeezerBpmClient.Candidates(new JObject { ["data"] = list }.ToString(),
                "Bitter Sweet Symphony", "The Verve", "Urban Hymns", 358);
            if (candidates.Count != 1 || (long)candidates[0]["id"] != 1)
                throw new Exception("Deezer must reject live versions, covers and wrong recording lengths.");
            var result = DeezerBpmClient.MatchDetails(candidates, "Bitter Sweet Symphony", "The Verve", "Urban Hymns", 358);
            if (result.Bpm != 170.84 || result.Source != "Deezer" || result.SourceUrl != "https://www.deezer.com/track/1")
                throw new Exception("Catalog decimals and provenance must survive lookup.");
            var different = track(5, "Bitter Sweet Symphony", "The Verve", 357, 171, "Other album");
            if (DeezerBpmClient.MatchDetails(new[] { studio, different }, "Bitter Sweet Symphony", "The Verve", "", 357).Bpm != 0 ||
                DeezerBpmClient.MatchDetails(new[] { studio, different }, "Bitter Sweet Symphony", "The Verve", "Urban Hymns", 357).Bpm != 170.84)
                throw new Exception("Album matching must resolve conflicts; unresolved BPMs must be rejected.");
            foreach (var badBpm in new[] { 0d, -1d, 241d, double.NaN, double.PositiveInfinity })
            {
                var bad = (JObject)studio.DeepClone(); bad["bpm"] = badBpm;
                if (DeezerBpmClient.MatchDetails(new[] { bad }, "Bitter Sweet Symphony", "The Verve", "", 357).Bpm != 0)
                    throw new Exception("Missing or invalid catalog BPM must not become a saved tempo.");
            }
            if (DeezerBpmClient.Candidates("{\"data\":[]}", "Missing", "Artist", "", 0).Count != 0)
                throw new Exception("Empty catalog results must be handled.");
            if (!PartyOnlineLookup.CanLookup(0, null) || PartyOnlineLookup.CanLookup(85.4, null))
                throw new Exception("BPM tags must prevent automatic replacement.");
            foreach (var entry in new[] { new PartyTempoEntry(), new PartyTempoEntry { Manual = true }, new PartyTempoEntry { Online = true } })
                if (PartyOnlineLookup.CanLookup(0, entry))
                    throw new Exception("All existing saved timings must be protected, including automatic ones.");
            var calls = 0;
            Func<Task<GetSongBpmClient.LookupResult>> found = () =>
            { calls++; return Task.FromResult(new GetSongBpmClient.LookupResult { Bpm = 85.42, Source = "Deezer" }); };
            result = PartyOnlineLookup.FirstMatchAsync(new Func<Task<GetSongBpmClient.LookupResult>>[] {
                () => Task.FromResult(new GetSongBpmClient.LookupResult { Detail = "No match" }), found
            }, CancellationToken.None).GetAwaiter().GetResult();
            if (calls != 1 || result.Bpm != 85.42) throw new Exception("No match must try the next provider.");
            calls = 0;
            result = PartyOnlineLookup.FirstMatchAsync(new Func<Task<GetSongBpmClient.LookupResult>>[] {
                () => { throw new InvalidOperationException("Unavailable"); }, found
            }, CancellationToken.None).GetAwaiter().GetResult();
            if (calls != 1 || result.Bpm != 85.42) throw new Exception("A failed provider must allow fallback.");
            calls = 0;
            PartyOnlineLookup.FirstMatchAsync(new[] { found, found }, CancellationToken.None).GetAwaiter().GetResult();
            if (calls != 1) throw new Exception("A successful lookup must not be replaced by a later provider.");
            calls = 0;
            try
            {
                PartyOnlineLookup.FirstMatchAsync(new Func<Task<GetSongBpmClient.LookupResult>>[] {
                    () => { throw new OperationCanceledException(); }, found
                }, CancellationToken.None).GetAwaiter().GetResult();
                throw new Exception("Cancellation was swallowed.");
            }
            catch (OperationCanceledException) { }
            if (calls != 0) throw new Exception("Canceled lookups must not start another request.");
            Console.WriteLine("Online BPM matching, fallback and saved-timing protection checks passed.");
        }
    }
}
