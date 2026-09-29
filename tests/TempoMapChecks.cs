using System;
using System.IO;
using MusicBeePlugin;
using Newtonsoft.Json;

internal static class TempoMapChecks
{
    private static void Near(double value, double expected, string message)
    { if (Math.Abs(value - expected) > 0.00001) throw new Exception(message + ": " + value); }
    private static PartyTempoMap CheckCountIn()
    {
        var map = new PartyTempoMap { TrackUrl = "count-in", InitialBeat = 0.37 };
        map.Sections.Add(new PartyTempoSection { Bpm = 120, Style = PartyDanceStyle.HalfSpeed });
        var returning = new PartyTempoSection { StartSeconds = 10, Bpm = 120, CountIn = true };
        map.Sections.Add(returning); map.Validate();
        Near(map.At(7.99).CountInLift, 0, "No cue before the last four incoming beats");
        Near(map.At(8).CountInLift, 0, "Count-in starts at rest");
        for (int i = 0; i < 4; i++)
        {
            Near(map.At(8 + i * 0.5).CountInLift, 0, "Each count lands on the incoming beat grid");
            if (map.At(8.25 + i * 0.5).CountInLift < 0.6) throw new Exception("All four bobs must be visible.");
        }
        Near(map.At(9.99999).CountInLift, 0, "Cue lands smoothly before transition");
        Near(map.At(10).CountInLift, 0, "No residual cue at transition");
        Near(map.At(12).CountInLift, 0, "No cue after transition");
        for (double t = 0; t < 12; t += 0.013)
        {
            returning.CountIn = true; var withCue = map.At(t);
            returning.CountIn = false; var withoutCue = map.At(t);
            if (withCue.Frame != withoutCue.Frame || withCue.Beat != withoutCue.Beat ||
                withCue.Bpm != withoutCue.Bpm || withCue.Impact != withoutCue.Impact ||
                withCue.Sway != withoutCue.Sway || withCue.Anticipation != withoutCue.Anticipation || withoutCue.CountInLift != 0)
                throw new Exception("A count-in must not change existing timing or motion.");
        }
        returning.CountIn = true;
        var beforeSeek = map.At(9.17); map.At(25); map.At(0);
        Near(map.At(9.17).CountInLift, beforeSeek.CountInLift, "Seeking must reproduce the count-in");
        returning.RampSeconds = 3;
        Near(map.At(11).Bpm, 120, "Equal BPM values have no ramp effect");
        returning.RampSeconds = 0;
        // A short half-speed section gets complete bobs only; never a mid-bob pop.
        map.Sections.Insert(0, new PartyTempoSection { Bpm = 120 });
        map.Sections[1].StartSeconds = 9.3; map.Validate();
        Near(map.At(9.3).CountInLift, 0, "Short section starts without a jump");
        Near(map.At(9.49).CountInLift, 0, "Short section waits for a whole bob");
        if (map.At(9.75).CountInLift < 0.6) throw new Exception("Short section should fit one whole bob.");
        map.Sections[1].StartSeconds = 9.8;
        Near(map.At(9.9).CountInLift, 0, "Less than one incoming beat cannot fit a bob");
        map.Sections.RemoveAt(0); map.Sections[0].StartSeconds = 0;
        map.Sections[0].Style = PartyDanceStyle.Hold;
        bool rejected = false;
        try { map.Validate(); } catch (ArgumentException) { rejected = true; }
        if (!rejected || map.At(9.25).CountInLift != 0) throw new Exception("Count-in must not animate a hold.");
        map.Sections[0].Style = PartyDanceStyle.HalfSpeed; map.Validate();
        // Old v1 maps have no CountIn member and remain valid with the cue off.
        var oldJson = JsonConvert.SerializeObject(map).Replace(",\"CountIn\":true", "").Replace(",\"CountIn\":false", "");
        var legacy = JsonConvert.DeserializeObject<PartyTempoMap>(oldJson); legacy.Validate();
        if (legacy.Sections[1].CountIn || legacy.At(9.25).CountInLift != 0) throw new Exception("Existing maps must not opt in automatically.");
        return map;
    }

    internal static void Run()
    {
        var countInMap = CheckCountIn();
        var map = new PartyTempoMap { TrackUrl = "test-track", InitialBeat = -0.17 };
        map.Sections.Add(new PartyTempoSection { Bpm = 85.4 }); map.Validate();
        for (int t = 0; t < 300000; t += 37)
        {
            var expected = PartyAnimation.FrameAt(t, 85.4);
            map.InitialBeat = 0;
            if (map.At(t / 1000d).Frame != expected) throw new Exception("Constant map must preserve the original pose sequence.");
        }
        map.Sections.Add(new PartyTempoSection { StartSeconds = 10, Bpm = 180, RampSeconds = 2 });
        map.Sections[0].Bpm = 120;
        map.Sections.Add(new PartyTempoSection { StartSeconds = 12, Style = PartyDanceStyle.Hold });
        map.Sections.Add(new PartyTempoSection { StartSeconds = 15, Bpm = 180 }); map.Validate();
        Near(map.At(11).Beat, 22.25, "Linear ramp must integrate BPM");
        Near(map.At(12).Beat, 25, "Ramp endpoint");
        Near(map.At(14).Beat, 25, "Hold freezes timeline phase");
        Near(map.At(16).Beat, 28, "Resume preserves held phase");
        var held = map.At(13);
        if (!held.Held || held.Impact != 0 || held.Sway != 0 || held.Anticipation != 0)
            throw new Exception("A hold must stop all movement.");
        var replay = map.At(11.31);
        map.At(100); map.At(0);
        Near(map.At(11.31).Beat, replay.Beat, "Seeks cannot change phase");
        Near(map.At(10 - 0.000001).Beat, map.At(10).Beat, "Continuous boundary");
        map.Sections[3].Style = PartyDanceStyle.HalfSpeed;
        Near(map.At(16).Beat, 26.5, "Half speed affects the dance only");
        map.Sections[3].Style = PartyDanceStyle.SideToSide;
        map.Sections[3].AlignBeat = true;
        if (map.At(15).Frame != 6 || map.At(15.334).Frame != 0)
            throw new Exception("Side-only alignment alternates the two side poses.");
        var folder = Path.Combine(Path.GetTempPath(), "DesktopLyrics-map-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PartyTempoStore(folder);
            store.SaveMap(countInMap);
            var countInLoaded = store.LoadMap(countInMap.TrackUrl);
            if (!countInLoaded.Sections[1].CountIn) throw new Exception("Count-in checkbox must persist.");
            Near(countInLoaded.At(8.25).CountInLift, countInMap.At(8.25).CountInLift, "Saved count-in");
            store.Save(map.TrackUrl, 85.4, 180, true);
            store.SaveMap(map);
            var loaded = store.LoadMap(map.TrackUrl);
            if (loaded == null || loaded.Sections.Count != 4) throw new Exception("Map round trip");
            Near(loaded.At(16).Beat, map.At(16).Beat, "Round trip phase");
            map.Enabled = false; store.SaveMap(map);
            if (store.LoadMap(map.TrackUrl).Enabled || store.Load(map.TrackUrl).Bpm != 85.4)
                throw new Exception("Disabling maps must preserve the underlying timing.");
            map.Sections[1].StartSeconds = 0;
            bool rejected = false;
            try { store.SaveMap(map); } catch (ArgumentException) { rejected = true; }
            if (!rejected || store.LoadMap(map.TrackUrl).Sections[1].StartSeconds != 10)
                throw new Exception("Invalid edits must not replace saved maps.");
            store.DeleteMap(map.TrackUrl);
            if (store.LoadMap(map.TrackUrl) != null || store.Load(map.TrackUrl) == null)
                throw new Exception("Removing a map must preserve saved BPM.");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        Console.WriteLine("Tempo-map integration, count-in boundaries, holds, styles, seeks and persistence checks passed.");
    }
}
