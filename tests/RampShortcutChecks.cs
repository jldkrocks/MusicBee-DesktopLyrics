using System;
using System.Reflection;
using System.Windows.Forms;
using MusicBeePlugin;

internal static class RampShortcutChecks
{
    static object Field(object x, string name) => x.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(x);
    static void Call(object x, string name) => x.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(x, null);
    static void Near(double actual, double expected) { if (Math.Abs(actual - expected) > 1e-7) throw new Exception($"Ramp: expected {expected}, got {actual}"); }
    internal static void Run()
    {
        var map = new PartyTempoMap { TrackUrl = "ramp" };
        map.Sections.Add(new PartyTempoSection { Bpm = 120 });
        map.Sections.Add(new PartyTempoSection { StartSeconds = 10, Bpm = 60 });
        map.Sections.Add(new PartyTempoSection { StartSeconds = 20, Bpm = 90 });
        PartyTempoMap saved = null;
        using (var editor = new FrmPartyTempoMap(map, "Ramp", () => 0, p => {}, m => saved = m, 30, () => {}, () => false))
        {
            var grid = (DataGridView)Field(editor, "_grid");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            Call(editor, "RampToRow"); Call(editor, "SaveMap");
            if (saved == null || saved.Version != 6 || !saved.Sections[0].RampToNext) throw new Exception("Linked ramp must save.");
            Near(saved.At(0).Bpm, 120); Near(saved.At(5).Bpm, 90); Near(saved.At(10).Bpm, 60);
            Near(saved.At(10).Beat, 15); Near(saved.At(20).Bpm, 90);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(saved);
            var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<PartyTempoMap>(json); loaded.Validate(); Near(loaded.At(5).Bpm, 90);
            Call(editor, "RampToRow"); Call(editor, "SaveMap"); Near(saved.At(0).Bpm, 120);
            grid.Rows[1].Cells[0].Value = "12"; Call(editor, "SaveMap"); Near(saved.At(6).Bpm, 90);
            grid.CurrentCell = grid.Rows[1].Cells[0]; Call(editor, "DeleteSelectedRow"); Call(editor, "SaveMap");
            Near(saved.At(10).Bpm, 105); Near(saved.At(20).Beat, 35);
            grid.CurrentCell = grid.Rows[1].Cells[0]; Call(editor, "DeleteSelectedRow"); Call(editor, "SaveMap");
            if (saved.Sections[0].RampToNext) throw new Exception("Last point must not ramp to nowhere.");
            Near(map.Sections[1].StartSeconds, 10);
        }
        // Exact legacy endpoint chains migrate, while short ramps and explicit jumps retain their curves.
        foreach (bool custom in new[] { false, true })
        foreach (double speed in new[] { 1d, .5d })
        {
            var old = new PartyTempoMap { Version = 4, TrackUrl = "legacy", InitialBeat = -.23 };
            old.Sections.Add(new PartyTempoSection { Bpm = 100, Rhythm = PartyRhythm.Swing, Speed = speed });
            old.Sections.Add(new PartyTempoSection { StartSeconds = 10, Bpm = 93, RampStartBpm = 100, RampSeconds = custom ? 2 : 4, Rhythm = PartyRhythm.Swing, CountIn = speed == .5 });
            old.Sections.Add(new PartyTempoSection { StartSeconds = 14, Bpm = 85, RampStartBpm = 93, RampSeconds = 5.5, Rhythm = PartyRhythm.Swing });
            old.Sections.Add(new PartyTempoSection { StartSeconds = 19.5, Bpm = 68, RampStartBpm = 85, RampSeconds = 3, Rhythm = PartyRhythm.Swing });
            old.Sections.Add(new PartyTempoSection { StartSeconds = 22.5, Bpm = 68, Style = PartyDanceStyle.Hold });
            old.Accents.Add(new PartyAccentCue { TimeSeconds = 14 }); old.Validate();
            using (var editor = new FrmPartyTempoMap(old, "Legacy", () => 0, p => {}, m => saved = m, 30, () => {}, () => false))
            {
                Call(editor, "SaveMap");
                for (double t = 0; t < 25; t += .017)
                {
                    var a = old.At(t); var b = saved.At(t);
                    Near(a.Bpm, b.Bpm); Near(a.Beat, b.Beat); Near(a.Impact, b.Impact);
                    if (a.Frame != b.Frame || a.Held != b.Held) throw new Exception("Migration changed choreography.");
                }
                if (saved.Sections[1].RampToNext == custom || !saved.Sections[2].RampToNext || !saved.Sections[3].RampToNext)
                    throw new Exception("Only complete endpoint ramps should migrate to links.");
                if (old.Sections[2].RampToNext) throw new Exception("Loading must not modify saved maps.");
            }
        }
        Console.WriteLine("BPM-point migration, curve preservation, moving/deleting endpoints and persistence checks passed.");
    }
}
