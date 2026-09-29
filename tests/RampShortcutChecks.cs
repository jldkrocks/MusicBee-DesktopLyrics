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
        map.Sections.Add(new PartyTempoSection { StartSeconds = 10.123, Bpm = 60 });
        map.Sections.Add(new PartyTempoSection { StartSeconds = 20, Bpm = 90 });
        PartyTempoMap saved = null;
        using (var editor = new FrmPartyTempoMap(map, "Ramp", () => 0, p => {}, m => saved = m, 30, () => {}, () => false))
        {
            var grid = (DataGridView)Field(editor, "_grid");
            grid.CurrentCell = grid.Rows[1].Cells[0];
            Call(editor, "RampToRow"); Call(editor, "SaveMap");
            if (saved == null || saved.Version != 3) throw new Exception("Ramp shortcut must save.");
            Near(saved.Sections[0].RampSeconds, 10.123); Near(saved.Sections[0].RampStartBpm.Value, 120);
            Near(saved.At(0).Bpm, 120); Near(saved.At(5.0615).Bpm, 90); Near(saved.At(10.123).Bpm, 60);
            Near(saved.At(10.123).Beat, 10.123 * 1.5); Near(saved.At(20).Bpm, 90);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(saved);
            var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<PartyTempoMap>(json); loaded.Validate(); Near(loaded.At(5.0615).Bpm, 90);
            // Repeating the shortcut keeps the original starting tempo, rather than flattening it.
            Call(editor, "RampToRow"); Call(editor, "SaveMap"); Near(saved.At(0).Bpm, 120);
            // Chronological order, not grid order; appended row lies between existing times.
            grid.Rows.Add("5", "100", "0", "Normal", false, false, "Straight", "66.67", "Normal", null);
            grid.CurrentCell = grid.Rows[1].Cells[0]; Call(editor, "RampToRow");
            Near(double.Parse(Convert.ToString(grid.Rows[3].Cells[2].Value)), 5.123);
            // First row and Holds must not mutate data.
            grid.CurrentCell = grid.Rows[0].Cells[0]; var before = grid.Rows[0].Cells[2].Value;
            Call(editor, "RampToRow"); if (!Equals(before, grid.Rows[0].Cells[2].Value)) throw new Exception("First row mutated.");
            grid.Rows[3].Cells[3].Value = "Hold pose"; grid.CurrentCell = grid.Rows[1].Cells[0];
            before = grid.Rows[3].Cells[2].Value; Call(editor, "RampToRow");
            if (!Equals(before, grid.Rows[3].Cells[2].Value)) throw new Exception("Hold ramp mutated.");
        }
        Console.WriteLine("Destination ramp shortcut, first-row integration, persistence and validation checks passed.");
    }
}
