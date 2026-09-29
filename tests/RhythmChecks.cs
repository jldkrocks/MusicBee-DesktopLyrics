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
        var frames = new[] { 6, 3, 3, 0, 9, 9 };
        for (int beat = 0; beat < 18; beat++)
        {
            var pose = map.At(beat * 0.5);
            if (pose.Frame != frames[beat % 6]) throw new Exception("Waltz needs a side lead and two middle beats, alternating lead sides each bar.");
            Near(pose.Impact, beat % 3 == 0 ? 1 : 0.55, "Waltz accents repeat every three beats");
            Near(pose.Beat, beat, "Waltz must not change BPM or the underlying phase");
        }
        if (map.At(0.5).Frame != map.At(1).Frame || map.At(0.75).Impact >= map.At(1).Impact)
            throw new Exception("Waltz must visibly hit the SAME centre pose twice.");
        if (map.At(1.49).Anticipation <= map.At(0.99).Anticipation)
            throw new Exception("Third-beat rise must prepare the next side landing.");
        section.Style = PartyDanceStyle.SideToSide;
        if (map.At(0).Frame != 6 || map.At(0.5).Frame != 0 || map.At(1.5).Impact != 1)
            throw new Exception("Side-only waltz must retain three-beat accents.");
        section.Style = PartyDanceStyle.HalfSpeed;
        if (map.At(1).Frame != 3 || map.At(3).Frame != 0) throw new Exception("Half speed must slow the selected waltz pattern.");
        section.Style = PartyDanceStyle.Normal;
        map.Sections.Add(new PartyTempoSection { StartSeconds = 1.2, Style = PartyDanceStyle.Hold });
        if (map.At(2).Frame != 3 || !map.At(2).Held || map.At(2).Impact != 0 || map.At(2).Anticipation != 0)
            throw new Exception("Hold must preserve the waltz pose and stop its movement.");
        map.Sections.Add(new PartyTempoSection { StartSeconds = 3.2, Bpm = 120, Rhythm = PartyRhythm.Waltz, AlignBeat = true });
        map.Validate();
        if (map.At(3.2).Frame != 6 || map.At(3.2).Impact != 1 || map.At(3.7).Frame != 3)
            throw new Exception("Waltz Align must start beat one of the bar.");
        var replay = map.At(5.1); map.At(100); map.At(0);
        Near(map.At(5.1).Beat, replay.Beat, "Waltz seeks repeat the same bar position");
        map.Sections.RemoveRange(1, 2);
        section.Rhythm = PartyRhythm.Swing;
        var splitTime = section.SwingPercent / 200;
        if (map.At(0).Frame != 6 || map.At(splitTime - 0.0001).Frame != 6 || map.At(splitTime).Frame != 3 ||
            map.At(0.5).Frame != 0 || map.At(0.5 + splitTime).Frame != 9 || map.At(1).Frame != 6)
            throw new Exception("Swing must hold a side, briefly hit the centre, then land on the opposite side.");
        Near(map.At(splitTime).Impact, 0.4, "Swing middle hit lands on its late subdivision");
        Near(map.At(0.25).Impact, 0, "Default swing must not put its middle hit on the even midpoint");
        Near(map.At(0).Impact, 1, "Swing main side beat remains strong");
        Near(map.At(0.5).Impact, 1, "Both swing side landings must be equally strong");
        for (double t = 0; t < 20; t += 0.031)
        {
            section.Rhythm = PartyRhythm.Straight; var straight = map.At(t);
            section.Rhythm = PartyRhythm.Swing; var swing = map.At(t);
            if (straight.Beat != swing.Beat || straight.Bpm != swing.Bpm)
                throw new Exception("Swing pose subdivisions must not change saved phase or BPM.");
        }
        foreach (var amount in new[] { 50d, 60d, 66.67, 75d })
        {
            section.SwingPercent = amount;
            var time = amount / 200;
            if (map.At(time - 0.0001).Frame != 6 || map.At(time).Frame != 3 || map.At(0.5).Frame != 0)
                throw new Exception("Swing amount must move only the middle subdivision.");
            var same = map.At(time + 0.01); map.At(100); map.At(0);
            if (map.At(time + 0.01).Frame != same.Frame) throw new Exception("Swing seeking must repeat the same pose.");
        }
        section.SwingPercent = 70;
        section.Style = PartyDanceStyle.HalfSpeed;
        if (map.At(0.6999).Frame != 6 || map.At(0.7).Frame != 3 || map.At(1).Frame != 0)
            throw new Exception("Half speed must preserve swing proportions at the slower rate.");
        section.Style = PartyDanceStyle.SideToSide;
        if (map.At(0.35).Frame != 6 || map.At(0.5).Frame != 0)
            throw new Exception("Explicit side-only mode must not introduce centre drawings.");
        section.Style = PartyDanceStyle.Normal;
        section.AlignBeat = true;
        if (map.At(0).Frame != 6) throw new Exception("Swing Align must start a side pose.");
        section.AlignBeat = false;
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
        if (loaded.Sections[0].Rhythm != PartyRhythm.Swing || loaded.Sections[1].Rhythm != PartyRhythm.Waltz || loaded.Sections[0].SwingPercent != 70)
            throw new Exception("Rhythm choices must survive save/load.");
        var legacy = JsonConvert.DeserializeObject<PartyTempoMap>("{\"TrackUrl\":\"old\",\"Sections\":[{\"StartSeconds\":0,\"Bpm\":120}]}");
        legacy.Validate();
        if (legacy.Sections[0].Rhythm != PartyRhythm.Straight || legacy.At(0).Frame != 0)
            throw new Exception("Existing maps must keep their original straight pattern.");
        Near(legacy.Sections[0].SwingPercent, 66.67, "Older maps default to approximately 2:1 swing");
        foreach (var invalid in new[] { 49d, 76d, double.NaN, double.PositiveInfinity })
        {
            section.SwingPercent = invalid;
            bool badAmount = false; try { map.Validate(); } catch (ArgumentException) { badAmount = true; }
            if (!badAmount) throw new Exception("Invalid swing amounts must be rejected before saving.");
        }
        section.SwingPercent = 70;
        section.Rhythm = (PartyRhythm)99;
        bool rejected = false; try { map.Validate(); } catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Unknown rhythm values must be rejected.");
        Console.WriteLine("Waltz grouping, swing subdivision, holds, alignment, count-in and compatibility checks passed.");
    }
}
