using System;
using System.IO;
using System.Threading;
using MusicBeePlugin;

internal static class SeekClockChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    internal static void Run()
    {
        // A seek in MusicBee can expose a temporary non-playing snapshot,
        // then a buffered step. It must not become a 120% resume catch-up.
        foreach (int target in new[] { 5000, 100000 })
        {
            var clock = new PartyPlaybackClock();
            clock.PositionAt(50000, 0, 1000, true);
            clock.PositionAt(target, 16, 1000, false);
            clock.PositionAt(target, 32, 1000, true);
            Check(clock.PositionAt(target + 500, 48, 1000, true) == target + 500,
                "External seek must adopt the fresh player anchor instead of accelerating toward it.");
            for (int t = 64; t <= 4048; t += 16)
            {
                int position = target + 500 + t - 48;
                Check(clock.PositionAt(position, t, 1000, true) == position,
                    "Post-seek clock must run at playback speed immediately.");
            }
        }
        // Keep the existing ordinary plugin Play smoothing, but never apply it
        // to a paused seek followed by Play/Preview.
        var resume = new PartyPlaybackClock();
        resume.PositionAt(10000, 0, 1000, false);
        resume.PrepareResume(100);
        resume.PositionAt(10000, 100, 1000, true);
        Check(resume.PositionAt(10500, 116, 1000, true) < 10100,
            "Known ordinary Play must retain buffered-resume smoothing.");
        foreach (bool explicitSeek in new[] { false, true })
        {
            var clock = new PartyPlaybackClock();
            clock.PositionAt(10000, 0, 1000, false);
            if (explicitSeek) clock.Seek(20000, 50, 1000, false);
            else clock.PositionAt(20000, 50, 1000, false);
            clock.PrepareResume(100);
            clock.PositionAt(20000, 100, 1000, true);
            Check(clock.PositionAt(20500, 116, 1000, true) == 20500,
                "Play after a paused seek must not enter resume catch-up.");
        }
        var expired = new PartyPlaybackClock();
        expired.PositionAt(10000, 0, 1000, false);
        expired.PrepareResume(0);
        expired.PositionAt(10000, 1000, 1000, true);
        Check(expired.PositionAt(10500, 1016, 1000, true) == 10500,
            "An old Play request must not affect a later external seek.");
        // Background spectrum work and drawing can deliver a position much
        // later than it was read. That delay must not become a tempo correction.
        foreach (bool explicitSeek in new[] { false, true })
        {
            var clock = new PartyPlaybackClock();
            if (explicitSeek) clock.Seek(5000, 0, 1000, true);
            else { clock.PositionAt(10000, -20, 1000, true); clock.PositionAt(5000, 0, 1000, true); }
            clock.PositionAt(5100, 180, 1000, true, false, 100);
            Check(clock.PositionAt(5200, 280, 1000, true, false, 200) == 5280,
                "Seek phase must use position acquisition time, not delayed delivery time.");
            int previous = 5280;
            for (int wall = 290; wall <= 6000; wall += 10)
            {
                int delay = wall < 1500 ? 80 : 10;
                int sampledAt = (wall - delay) / 100 * 100;
                int position = clock.PositionAt(5000 + sampledAt, wall, 1000, true, false, sampledAt);
                Check(position - previous == 10 && position == 5000 + wall,
                    "Post-seek beats must stay at 1x even when delivery latency improves.");
                previous = position;
            }
            // A new backward or forward seek must still replace the phase,
            // not remain attached to the previous 1x run.
            Check(clock.PositionAt(2000, 6020, 1000, true, false, 6000) == 2020,
                "Later backward seek must remain authoritative.");
            Check(clock.PositionAt(20000, 6040, 1000, true, false, 6020) == 20020,
                "Later forward seek must remain authoritative.");
        }
        var delayedPause = new PartyPlaybackClock();
        Check(delayedPause.PositionAt(12345, 100, 1000, false, false, 0) == 12345,
            "Paused samples must not be projected forward.");
        // Early readings can advance normally before their buffered offset
        // settles. Two advancing samples are not proof of a final phase.
        foreach (bool explicitSeek in new[] { false, true })
        foreach (int direction in new[] { -1, 1 })
        {
            var clock = new PartyPlaybackClock();
            if (explicitSeek) clock.Seek(10000, 0, 1000, true);
            else { clock.PositionAt(50000, -20, 1000, true); clock.PositionAt(10000, 0, 1000, true); }
            int previous = 0;
            for (int wall = 20; wall <= 4000; wall += 20)
            {
                int step = wall / 100 * 100;
                int earlyOffset = direction * (step < 400 ? 200 : Math.Max(0, 200 - (step - 300) * 2 / 5));
                int position = clock.PositionAt(10000 + step + earlyOffset, wall, 1000, true, false, wall);
                if (wall >= 1500)
                    Check(Math.Abs(position - (10000 + wall)) <= 20,
                        "A provisional post-seek phase must recover when later reads establish a different phase.");
                if (wall > 1500)
                    Check(position - previous == 20, "Verified phase must retain normal playback speed.");
                previous = position;
            }
        }
        var quantized = new PartyPlaybackClock();
        quantized.Seek(10000, 0, 1000, true);
        int tick = 0, iteration = 0;
        var gaps = new[] { 13, 27, 43, 7, 81, 16, 24 };
        while (tick < 30000)
        {
            tick += gaps[iteration++ % gaps.Length];
            Check(quantized.PositionAt(10000 + tick / 100 * 100, tick, 1000, true, false, tick) == 10000 + tick,
                "Irregular polling of a coarse position must not produce false phase corrections.");
        }
        Check(quantized.PhaseCorrections == 0, "Sampling uncertainty must be respected.");
        var tracePath = Path.Combine(Path.GetTempPath(), "DesktopLyrics-trace-" + Guid.NewGuid(), "seek.log");
        var traceClock = new PartyPlaybackClock();
        var trace = new PlaybackTimingTrace(tracePath);
        for (int i = 0; i < 1000; i++)
        {
            if (i == 0 || i == 700) traceClock.Seek(i, i * 10, 1000, true);
            trace.Record(i * 10, 1000, new PlaybackSnapshotReader.Snapshot {
                Position = i, PositionTimestamp = i * 10, TrackUrl = "private-song-path",
                State = Plugin.PlayState.Playing }, i, true, traceClock);
        }
        // A fast test may still have the first async write in progress.
        string text = null;
        Check(SpinWait.SpinUntil(() => {
            trace.Flush();
            try {
                using (var stream = new FileStream(tracePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
                return text.Contains(",999,999,");
            }
            catch (IOException) { return false; }
        }, 3000), "Latest seek diagnostics were not written.");
        Check(text.Split('\n').Length <= 603 && text.Length < 100000 && !text.Contains("private-song-path"),
            "Trace must be bounded, replace earlier data and exclude song paths.");
        Check(SpinWait.SpinUntil(() => {
            try { File.Delete(tracePath); Directory.Delete(Path.GetDirectoryName(tracePath)); return true; }
            catch (IOException) { return false; }
        }, 3000), "Trace cleanup did not complete.");
        Console.WriteLine("External seek speed, paused-seek resume and explicit Play intent checks passed.");
    }
}
