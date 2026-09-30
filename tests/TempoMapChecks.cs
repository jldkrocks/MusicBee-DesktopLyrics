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
        // At the Normal boundary the phase is 10.37, so the next pose beat is
        // at 10.315. The count-in must share THAT clock, not the marker at 10.
        var nextPoseBeat = map.At(10.315);
        Near(nextPoseBeat.Beat, 11, "Incoming pose beat with saved nonzero alignment");
        Near(map.At(8.20).CountInAccent, 0, "No cue before the first beat preparation");
        Near(map.At(8.215).CountInAccent, 0, "Count-in starts smoothly");
        for (int i = 0; i < 4; i++)
        {
            var hit = 8.315 + i * 0.5;
            var pose = map.At(hit);
            Near(pose.CountInAccent, 1, "All four dips land on the incoming pose clock");
            Near(pose.Impact, 1.7, "Every count has a strong downward accent");
            Near(pose.Anticipation, 0, "Half-speed lift must not oppose the downbeat");
            if (map.At(hit - 0.02).CountInAccent >= pose.CountInAccent ||
                map.At(hit + 0.02).CountInAccent >= pose.CountInAccent)
                throw new Exception("The deepest dip must occur on the beat, not halfway between beats.");
        }
        Near(map.At(9.999999).Impact, map.At(10).Impact, "Cue must continue smoothly through the section boundary");
        Near(map.At(10.315).CountInAccent, 1, "One final bop lands on the first Normal beat");
        Near(map.At(10.315).Impact, 1.7, "Final bop is as strong as the lead-in");
        Near(map.At(10.59).CountInAccent, 0, "Final bop recovers completely");
        Near(map.At(10.815).CountInAccent, 0, "No second extra bop");
        Near(map.At(12).CountInAccent, 0, "No cue after transition");
        for (double t = 0; t < 12; t += 0.013)
        {
            returning.CountIn = true; var withCue = map.At(t);
            returning.CountIn = false; var withoutCue = map.At(t);
            if (withCue.Frame != withoutCue.Frame || withCue.Beat != withoutCue.Beat ||
                withCue.Bpm != withoutCue.Bpm || withCue.Sway != withoutCue.Sway || withoutCue.CountInAccent != 0)
                throw new Exception("A count-in must not change the pose sequence or saved timing.");
            if ((t < 8.215 || t >= 10.59) &&
                (withCue.Impact != withoutCue.Impact || withCue.Anticipation != withoutCue.Anticipation))
                throw new Exception("Motion outside the optional cue must be unchanged.");
        }
        returning.CountIn = true;
        returning.AlignBeat = true;
        Near(map.At(10).Beat, 10, "Explicit Align restarts the incoming side pose");
        Near(map.At(10).CountInAccent, 1, "Aligned return gets its final bop exactly at the marker");
        Near(map.At(9.999999).Impact, map.At(10).Impact, "Aligned boundary must not interrupt the landing");
        for (int i = 0; i < 4; i++) Near(map.At(8 + i * 0.5).CountInAccent, 1, "Count-in must anticipate explicit alignment too");
        returning.AlignBeat = false;
        // Moving a non-aligned section by a little must retain its fractional
        // incoming phase instead of making the marker itself an artificial beat.
        returning.StartSeconds = 10.16;
        Near(map.At(8.395).CountInAccent, 1, "Off-beat marker still uses the incoming pose clock");
        Near(map.At(10.395).Beat, 11, "Cue and later normal pose remain one beat grid");
        Near(map.At(10.395).CountInAccent, 1, "Off-beat marker waits for the actual return beat");
        returning.StartSeconds = 10;
        foreach (var origin in new[] { 0.01, 0.05, 0.15, 0.8, -0.2 })
        {
            map.InitialBeat = origin;
            var lastHit = 10 - (origin - Math.Floor(origin)) / 2;
            Near(map.At(lastHit).CountInAccent, 1, "A nearby marker cannot weaken or shift the final downbeat");
            if (map.At(lastHit - 0.001).CountInAccent >= 1 || map.At(lastHit + 0.001).CountInAccent >= 1)
                throw new Exception("Final bob must peak on the beat even near an off-beat boundary.");
        }
        map.InitialBeat = 0.37;
        var beforeSeek = map.At(9.17); map.At(25); map.At(0);
        Near(map.At(9.17).CountInAccent, beforeSeek.CountInAccent, "Seeking must reproduce the count-in");
        returning.RampSeconds = 3;
        Near(map.At(11).Bpm, 120, "Equal BPM values have no ramp effect");
        returning.Bpm = 180;
        Near(map.At(8.315).CountInAccent, 1, "Incoming ramp must count at its initial tempo");
        var rampLanding = (-2 + Math.Sqrt(4 + 4 * 0.63 / 6)) * 3;
        Near(map.At(10 + rampLanding).Beat, 11, "Incoming ramp's integrated first beat");
        Near(map.At(10 + rampLanding).CountInAccent, 1, "Final bop follows the actual ramp, not a constant-BPM estimate");
        returning.RampSeconds = 0; returning.Bpm = 120;
        // A ramp inside the half-speed section must contribute its integrated
        // phase to the incoming clock, not an estimate using the final BPM.
        var rampMap = new PartyTempoMap { TrackUrl = "ramp", InitialBeat = 0.37 };
        rampMap.Sections.Add(new PartyTempoSection { Bpm = 120 });
        rampMap.Sections.Add(new PartyTempoSection { StartSeconds = 4, Bpm = 180, RampSeconds = 2, Style = PartyDanceStyle.HalfSpeed });
        rampMap.Sections.Add(new PartyTempoSection { StartSeconds = 10, Bpm = 90, CountIn = true });
        rampMap.Validate();
        Near(rampMap.At(10).Beat, 16.87, "Half-speed ramp endpoint phase");
        Near(rampMap.At(10 - 0.87 / 1.5).CountInAccent, 1, "Ramped section count-in uses the true endpoint phase");
        // A short half-speed section gets complete bobs only; never a mid-bob pop.
        map.Sections.Insert(0, new PartyTempoSection { Bpm = 120 });
        map.Sections[1].StartSeconds = 9.3; map.Validate();
        Near(map.At(9.3).CountInAccent, 0, "Short section starts without a jump");
        Near(map.At(9.49).CountInAccent, 0, "Short section waits for beat preparation");
        Near(map.At(9.665).CountInAccent, 1, "Short section should fit one strong bob");
        map.Sections[1].StartSeconds = 9.8;
        Near(map.At(9.9).CountInAccent, 0, "Less than one incoming beat cannot fit a bob");
        map.Sections.RemoveAt(0); map.Sections[0].StartSeconds = 0;
        map.Sections[0].Style = PartyDanceStyle.Hold;
        map.Validate();
        foreach(var style in new[]{PartyDanceStyle.Hold,PartyDanceStyle.Rest}) {
            map.Sections[0].Style=style; map.Validate();
            returning.AlignBeat=true;
            var before=map.At(9.5);
            Near(before.CountInAccent,1,"Hold/rest lead-in lands before aligned return");
            Near(map.At(10).CountInAccent,1,"Hold/rest return gets final bop");
            returning.CountIn=false;var plain=map.At(9.5);returning.CountIn=true;
            Near(before.Beat,plain.Beat,"Count-in must not change hold/rest beat integration");
            if(before.Frame!=plain.Frame || !before.Held)throw new Exception("Count-in must retain held pose.");
            returning.AlignBeat=false;
        }
        map.Sections[0].Style = PartyDanceStyle.HalfSpeed; map.Validate();
        map.Sections.Add(new PartyTempoSection { StartSeconds = 10.1, Bpm = 120, Style = PartyDanceStyle.Hold });
        map.Validate();
        if (!map.At(10.315).Held || map.At(10.315).CountInAccent != 0 || map.At(10.315).Impact != 0)
            throw new Exception("A later Hold must stop the landing cue too.");
        map.Sections.RemoveAt(2);
        // Old v1 maps have no CountIn member and remain valid with the cue off.
        var oldJson = JsonConvert.SerializeObject(map).Replace(",\"CountIn\":true", "").Replace(",\"CountIn\":false", "");
        var legacy = JsonConvert.DeserializeObject<PartyTempoMap>(oldJson); legacy.Validate();
        if (legacy.Sections[1].CountIn || legacy.At(9.25).CountInAccent != 0) throw new Exception("Existing maps must not opt in automatically.");
        return map;
    }

    internal static void Run()
    {
        var countInMap = CheckCountIn();
        var accentMap=new PartyTempoMap {TrackUrl="recovery",Sections={new PartyTempoSection {Bpm=120,Style=PartyDanceStyle.Rest}},Accents={new PartyAccentCue {TimeSeconds=1,RecoverySeconds=.1}}};
        accentMap.Validate();
        Near(accentMap.At(1.1).Impact,0,"Custom recovery ends at selected duration");
        if(accentMap.At(1.05).Impact<=0)throw new Exception("Custom recovery must retain its dip before ending.");
        var roundTrip=JsonConvert.DeserializeObject<PartyTempoMap>(JsonConvert.SerializeObject(accentMap));
        Near(roundTrip.Accents[0].EffectiveRecovery,.1,"Recovery persists");
        accentMap.Accents[0].RecoverySeconds=null;Near(accentMap.Accents[0].EffectiveRecovery,.22,"Legacy bop recovery");
        accentMap.Accents[0].Motion=PartyAccentMotion.Rebound;Near(accentMap.Accents[0].EffectiveRecovery,.42,"Legacy rebound recovery");

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
            Near(countInLoaded.At(8.25).CountInAccent, countInMap.At(8.25).CountInAccent, "Saved count-in");
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
        Console.WriteLine("Tempo-map integration, count-in beat alignment/strength/boundaries, holds, styles, seeks and persistence checks passed.");
    }
}
