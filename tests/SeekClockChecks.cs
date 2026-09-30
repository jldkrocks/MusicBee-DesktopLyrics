using System;
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
        Console.WriteLine("External seek speed, paused-seek resume and explicit Play intent checks passed.");
    }
}
