using System;
using System.Reflection;
using System.Windows.Forms;
using MusicBeePlugin;
using Newtonsoft.Json;

internal static class RestAccentChecks
{
    static void Near(double a, double b, string message) { if (Math.Abs(a-b) > 1e-6) throw new Exception(message + ": " + a + " != " + b); }
    static object Field(object x, string name) => x.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(x);
    static void Call(object x, string name) => x.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(x, null);
    internal static void Run()
    {
        foreach (PartyRhythm rhythm in Enum.GetValues(typeof(PartyRhythm)))
        foreach (var speed in new[] { .5, 1, 2 })
        {
            var baseline = new PartyTempoMap { TrackUrl = "rest", InitialBeat = .37 };
            baseline.Sections.Add(new PartyTempoSection { Bpm = 100, Rhythm = rhythm, Speed = speed });
            baseline.Sections.Add(new PartyTempoSection { StartSeconds = 2.123, Bpm = 130, RampSeconds = 1, Rhythm = rhythm, Speed = speed });
            baseline.Sections.Add(new PartyTempoSection { StartSeconds = 3.651, Bpm = 130, Rhythm = rhythm, Speed = speed });
            var resting = JsonConvert.DeserializeObject<PartyTempoMap>(JsonConvert.SerializeObject(baseline));
            resting.Version = 4; resting.Sections[1].Style = PartyDanceStyle.Rest; resting.Validate();
            int frozen = resting.At(2.123).Frame;
            for (double t = 0; t < 7; t += .017)
            {
                var a = baseline.At(t); var b = resting.At(t);
                Near(a.Beat, b.Beat, "Rest must preserve beat integration, including ramps and speed");
                Near(a.Bpm, b.Bpm, "Rest must advance tempo ramps");
                if (t >= 2.123 && t < 3.651)
                {
                    if (b.Frame != frozen || !b.Held || b.Impact != 0 || b.Anticipation != 0 || b.Sway != 0)
                        throw new Exception("Rest must pin the drawing and silence its regular motion.");
                }
                else if (a.Frame != b.Frame || a.Impact != b.Impact || a.Anticipation != b.Anticipation)
                    throw new Exception("Rest must not change motion outside its interval.");
            }
            resting.At(100); resting.At(0); Near(resting.At(4.2).Beat, baseline.At(4.2).Beat, "Seeking preserves Rest phase");
        }
        var map = new PartyTempoMap { Version = 4, TrackUrl = "cues", InitialBeat = .23 };
        map.Sections.Add(new PartyTempoSection { Bpm = 100, Rhythm = PartyRhythm.Swing });
        map.Sections.Add(new PartyTempoSection { StartSeconds = 3, Bpm = 90, RampSeconds = 2 });
        var plain = JsonConvert.DeserializeObject<PartyTempoMap>(JsonConvert.SerializeObject(map));
        var cue = new PartyAccentCue { TimeSeconds = 3.123, Strength = 1.7, PrepareSeconds = .1 };
        map.Accents.Add(cue); map.Validate();
        Near(map.At(cue.TimeSeconds).Impact, 1.7, "Cue peaks exactly at saved time");
        Near(map.At(cue.TimeSeconds).Anticipation, 0, "Lift must not oppose the accent");
        for (double t = 0; t < 7; t += .013)
        {
            var a = map.At(t); var b = plain.At(t);
            Near(a.Beat, b.Beat, "Cue cannot reset phase"); Near(a.Bpm, b.Bpm, "Cue cannot change ramp");
            if (a.Frame != b.Frame) throw new Exception("Default bop must not force a side pose.");
            if ((t < 3.023 || t >= 3.343) && (a.Impact != b.Impact || a.Anticipation != b.Anticipation || a.Sway != b.Sway))
                throw new Exception("Cue changes motion outside its window.");
        }
        cue.PrepareSeconds = 0; cue.HoldSeconds = .4;
        int held = map.At(cue.TimeSeconds).Frame;
        for (double t = cue.TimeSeconds; t < cue.TimeSeconds + .4; t += .01)
        { Near(map.At(t).Impact, 1.7, "Post-hit hold keeps dip"); if (map.At(t).Frame != held) throw new Exception("Post-hit hold changes pose"); }
        Near(map.At(4).Beat, plain.At(4).Beat, "Post-hit hold must keep counting");
        map.Sections[1].Style = PartyDanceStyle.Rest; map.Validate();
        Near(map.At(cue.TimeSeconds).Impact, 1.7, "Explicit cue may accent a rest");
        Near(map.At(4).Impact, 0, "Rest resumes after cue");
        var loaded = JsonConvert.DeserializeObject<PartyTempoMap>(JsonConvert.SerializeObject(map)); loaded.Validate();
        Near(loaded.At(3.2).Impact, map.At(3.2).Impact, "Cue JSON round trip");
        var legacy = JsonConvert.DeserializeObject<PartyTempoMap>("{\"Version\":1,\"TrackUrl\":\"old\",\"Sections\":[{\"Bpm\":100}]} ");
        legacy.Validate(); if (legacy.Accents.Count != 0) throw new Exception("Old maps must default to no cues");
        cue.TimeSeconds = double.NaN;
        bool rejected = false; try { map.Validate(); } catch (ArgumentException) { rejected = true; }
        if (!rejected) throw new Exception("Nonfinite cue accepted");
        cue.TimeSeconds = 3.123;

        PartyTempoMap saved = null;
        using (var editor = new FrmPartyTempoMap(map, "Rest and cues", () => 5.25, _ => {}, m => saved = m, 20, () => {}, () => false))
        {
            var grid = (DataGridView)Field(editor, "_grid"); var cues = (DataGridView)Field(editor, "_accentGrid");
            if (Convert.ToString(grid.Rows[1].Cells[3].Value) != "Rest (keep counting)" || cues.Rows.Count != 1)
                throw new Exception("Editor did not load rest/cues");
            var tabs = (TabControl)Field(editor, "_tabs"); tabs.SelectedIndex = 1;
            editor.Opacity = 0; editor.Show(); Application.DoEvents();
            ((Button)Field(editor, "_add")).PerformClick();
            if (cues.Rows.Count != 2 || grid.Rows.Count != 2) throw new Exception("Add must target the selected tab");
            cues.Rows[1].Cells[4].Value = "Rebound"; cues.Rows[1].Cells[5].Value = "Alternate sides";
            ((CheckBox)Field(editor, "_flowAccents")).Checked = true;
            Call(editor, "SaveMap");
            if (saved == null || saved.Version != 8 || !saved.FlowAccentSequences || saved.Accents.Count != 2 || saved.Accents[1].EffectivePose != PartyAccentPose.Alternate || editor.IsDisposed) throw new Exception("Save must preserve both tabs and keep editor open");
            Near(saved.Accents[1].TimeSeconds, 5.25, "Capture cue at playhead");
            var timeline = (PartyTimeline)Field(editor, "_timeline");
            if (timeline.Accents.Count != 2 || timeline.Markers[1].Style != PartyDanceStyle.Rest) throw new Exception("Timeline markers missing");
            foreach (DataGridViewColumn col in cues.Columns) if (string.IsNullOrWhiteSpace(col.ToolTipText)) throw new Exception("Cue tooltip missing");
            foreach (DataGridViewColumn col in grid.Columns) if (string.IsNullOrWhiteSpace(col.ToolTipText)) throw new Exception("Section tooltip missing");
            editor.Hide();
        }
        using (var timeline = new PartyTimeline { Width = 236, Duration = 10 })
        {
            int selected = -1; double sought = -1;
            timeline.Accents.Add(new PartyTimeline.Marker { Row = 4, Seconds = 5 });
            timeline.AccentSelected += row => selected = row; timeline.SeekRequested += time => sought = time;
            typeof(PartyTimeline).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(timeline, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 118, 27, 0) });
            if (selected != 4 || sought != 5 || timeline.Scrubbing) throw new Exception("Cue marker must select and seek");
        }
        Console.WriteLine("Rest phase/ramp preservation, accent timing, compatibility, cue editing and tooltips passed.");
    }
}
