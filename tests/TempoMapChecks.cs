using System;
using System.IO;
using MusicBeePlugin;

internal static class TempoMapChecks
{
    private static void Near(double value, double expected, string message)
    { if (Math.Abs(value - expected) > 0.00001) throw new Exception(message + ": " + value); }
    internal static void Run()
    {
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
        Console.WriteLine("Tempo-map integration, holds, styles, seeks and persistence checks passed.");
    }
}
