using System;
using MusicBeePlugin;
using Newtonsoft.Json;

internal static class RhythmChecks
{
    private static void Near(double value, double expected, string message)
    { if (Math.Abs(value - expected) > 0.00001) throw new Exception(message + ": " + value); }

    internal static void Run()
    {
        var map = new PartyTempoMap { TrackUrl = "rhythm-test" };
        var section = new PartyTempoSection { Bpm = 120, Rhythm = PartyRhythm.Waltz };
        map.Sections.Add(section); map.Validate();
        var frames = new[] { 6, 3, 9, 0, 9, 3 };
        for (int beat = 0; beat < 18; beat++)
        {
            var pose = map.At(beat * 0.5);
            if (pose.Frame != frames[beat % 6]) throw new Exception("Waltz needs a side lead and two middle beats, alternating lead sides each bar.");
            Near(pose.Impact, beat % 3 == 0 ? 1 : 0.42, "Waltz accents repeat every three beats");
            Near(pose.Beat, beat, "Waltz must not change BPM or the underlying phase");
        }
        section.Style = PartyDanceStyle.SideToSide;
        if (map.At(0).Frame != 6 || map.At(0.5).Frame != 0 || map.At(1.5).Impact != 1)
            throw new Exception("Side-only waltz must retain three-beat accents.");
        section.Style = PartyDanceStyle.HalfSpeed;
        if (map.At(1).Frame != 3 || map.At(3).Frame != 0) throw new Exception("Half speed must slow the selected waltz pattern.");
        section.Style = PartyDanceStyle.Normal;
        map.Sections.Add(new PartyTempoSection { StartSeconds = 1.2, Style = PartyDanceStyle.Hold });
        if (map.At(2).Frame != 9 || !map.At(2).Held || map.At(2).Impact != 0 || map.At(2).Anticipation != 0)
            throw new Exception("Hold must preserve the waltz pose and stop its movement.");
        map.Sections.Add(new PartyTempoSection { StartSeconds = 3.2, Bpm = 120, Rhythm = PartyRhythm.Waltz, AlignBeat = true });
        map.Validate();
        if (map.At(3.2).Frame != 6 || map.At(3.2).Impact != 1 || map.At(3.7).Frame != 3)
            throw new Exception("Waltz Align must start beat one of the bar.");
        var replay = map.At(5.1); map.At(100); map.At(0);
        Near(map.At(5.1).Beat, replay.Beat, "Waltz seeks repeat the same bar position");
        map.Sections.RemoveRange(1, 2);
        section.Rhythm = PartyRhythm.Swing;
        Near(map.At(1d / 3).Impact, 0.4, "Swing offbeat lands two-thirds through the beat");
        Near(map.At(0.25).Impact, 0, "Swing must not put its offbeat on the straight midpoint");
        Near(map.At(0).Impact, 1, "Swing main side beat remains strong");
        Near(map.At(0.5).Impact, 0.65, "Swing main centre beat remains stronger than its offbeat");
        if (map.At(1d / 3 - 0.01).Impact >= 0.4 || map.At(1d / 3 + 0.01).Impact >= 0.4)
            throw new Exception("Swing offbeat must peak at the late subdivision.");
        for (double t = 0; t < 20; t += 0.031)
        {
            section.Rhythm = PartyRhythm.Straight; var straight = map.At(t);
            section.Rhythm = PartyRhythm.Swing; var swing = map.At(t);
            if (straight.Frame != swing.Frame || straight.Beat != swing.Beat || straight.Bpm != swing.Bpm)
                throw new Exception("Swing must preserve the main pose clock and BPM.");
        }
        map.Sections.Add(new PartyTempoSection { StartSeconds = 4, Style = PartyDanceStyle.Hold });
        if (map.At(4 + 1d / 3).Impact != 0) throw new Exception("Hold must stop swing offbeats.");
        map.Sections.RemoveAt(1);
        section.Style = PartyDanceStyle.HalfSpeed;
        map.Sections.Add(new PartyTempoSection { StartSeconds = 10, Bpm = 120, Rhythm = PartyRhythm.Waltz, AlignBeat = true, CountIn = true });
        map.Validate();
        if (map.At(10).Frame != 6) throw new Exception("Waltz count-in lands on its first side pose.");
        Near(map.At(10).CountInAccent, 1, "Final count-in bop respects waltz alignment");
        var json = JsonConvert.SerializeObject(map);
        var loaded = JsonConvert.DeserializeObject<PartyTempoMap>(json); loaded.Validate();
        if (loaded.Sections[0].Rhythm != PartyRhythm.Swing || loaded.Sections[1].Rhythm != PartyRhythm.Waltz)
            throw new Exception("Rhythm choices must survive save/load.");
        var legacy = JsonConvert.DeserializeObject<PartyTempoMap>("{\"TrackUrl\":\"old\",\"Sections\":[{\"StartSeconds\":0,\"Bpm\":120}]}");
        legacy.Validate();
        if (legacy.Sections[0].Rhythm != PartyRhythm.Straight || legacy.At(0).Frame != 0)
            throw new Exception("Existing maps must keep their original straight pattern.");
        section.Rhythm = (PartyRhythm)99;
        bool rejected = false; try { map.Validate(); } catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Unknown rhythm values must be rejected.");
        Console.WriteLine("Waltz grouping, swing subdivision, holds, alignment, count-in and compatibility checks passed.");
    }
}
