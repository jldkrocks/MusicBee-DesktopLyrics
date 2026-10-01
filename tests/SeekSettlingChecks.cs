using System;
using System.Globalization;
using System.IO;
using MusicBeePlugin;

internal static class SeekSettlingChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    internal static void Run()
    {
        // Actual coarse readings captured around five MusicBee main-bar seeks.
        // The fixture includes only timing and playback state, never song data.
        foreach (bool explicitSeek in new[] { false, true })
        {
            var clock = new PartyPlaybackClock(true);
            int lastRaw = 0, previous = 0, releases = 0, seeks = 0;
            double started = -1, lastWall = 0;
            bool initialized = false, wasHeld = false;
            foreach (var line in File.ReadAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Fixtures", "seek-buffering.csv")))
            {
                if (line.StartsWith("wall")) continue;
                var fields = line.Split(',');
                double wall = double.Parse(fields[0], CultureInfo.InvariantCulture);
                double age = double.Parse(fields[1], CultureInfo.InvariantCulture);
                int raw = int.Parse(fields[2]), state = int.Parse(fields[3]);
                long time = (long)Math.Round(wall * 1000), sampledAt = time - (long)Math.Round(age * 1000);
                bool seek = initialized && Math.Abs(raw - lastRaw) > 1000;
                if (seek)
                {
                    Check(!wasHeld, "Previous captured seek did not settle before the next one.");
                    started = wall; seeks++;
                    if (explicitSeek) clock.Seek(raw, time, 1000000, true);
                }
                int position = clock.PositionAt(raw, time, 1000000, state == 3, false, sampledAt);
                if (seek) Check(clock.IsSettling, "Both seek paths must hold during buffered readings.");
                if (clock.IsSettling)
                    Check(started >= 0, "Ordinary captured playback must not enter a spurious seek hold.");
                else if (wasHeld)
                {
                    double delay = wall - started;
                    Check(delay >= 400 && delay <= 1000, "Captured seek must settle after buffered readings, within one second.");
                    Check(Math.Abs(position - raw - age) <= 65, "Release must match the established player phase.");
                    Console.WriteLine("{0} seek settled in {1:0} ms", explicitSeek ? "Explicit" : "External", delay);
                    started = -1; releases++;
                }
                else if (releases > 0 && initialized)
                    Check(Math.Abs(position - previous - (wall - lastWall)) <= 1.01,
                        "Released playback must stay at 1x without further pose corrections.");
                wasHeld = clock.IsSettling;
                previous = position; lastRaw = raw; lastWall = wall; initialized = true;
            }
            Check(seeks == 5 && releases == 5 && clock.PhaseCorrections == 5,
                "Each captured seek must release once, without later jitter corrections.");
        }

        var paused = new PartyPlaybackClock(true);
        paused.PositionAt(5000, 0, 1000, true);
        paused.Seek(10000, 20, 1000, true);
        paused.PreparePause();
        Check(paused.PositionAt(10200, 40, 1000, false) == 10200 && !paused.IsSettling,
            "An explicit pause must immediately cancel the hold and show the paused position.");
        paused.Seek(12345, 60, 1000, false);
        Check(paused.PositionAt(12345, 90, 1000, false) == 12345 && !paused.IsSettling,
            "A paused precision seek must be exact immediately.");
        paused.PrepareResume(100);
        paused.PositionAt(12345, 100, 1000, true);
        Check(paused.IsSettling, "Play after a paused seek must verify the new phase.");
        paused.Reset();
        Check(!paused.IsSettling && paused.PositionAt(1000, 120, 1000, true) == 1000,
            "Changing tracks must clear any seek hold.");

        var ordinary = new PartyPlaybackClock(true);
        ordinary.PositionAt(10000, 0, 1000, false);
        ordinary.PrepareResume(100);
        ordinary.PositionAt(10000, 100, 1000, true);
        Check(!ordinary.IsSettling && ordinary.PositionAt(10500, 116, 1000, true) < 10100,
            "Ordinary requested Play must retain resume smoothing.");

        var repeat = new PartyPlaybackClock(true);
        repeat.Seek(10000, 0, 1000, true);
        for (int time = 20; time <= 600; time += 20)
            repeat.PositionAt(10100, time, 1000, true, false, 20);
        Check(repeat.IsSettling, "Repeated copies of a source reading cannot prove stable playback.");
        int revision = repeat.SeekRevision;
        repeat.PositionAt(50000, 620, 1000, true);
        Check(repeat.IsSettling && repeat.SeekRevision == revision + 1,
            "A new seek during settling must replace the target and restart diagnostics.");
        repeat.PositionAt(50000, 2120, 1000, true);
        Check(!repeat.IsSettling, "Missing position advances must not leave the pose held forever.");

        var externalPause = new PartyPlaybackClock(true);
        externalPause.PositionAt(5000, 0, 1000, true);
        externalPause.PositionAt(10000, 20, 1000, false);
        Check(externalPause.IsSettling, "A transient paused seek must enter the hold.");
        Check(externalPause.PositionAt(10000, 300, 1000, false) == 10000 && !externalPause.IsSettling,
            "A genuine external paused seek must resolve to its exact position.");
        Console.WriteLine("Captured seek replay, hold lifecycle and pause/resume checks passed.");
    }
}
