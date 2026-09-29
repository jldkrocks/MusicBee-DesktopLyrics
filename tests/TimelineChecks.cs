using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MusicBeePlugin;

internal static class TimelineChecks
{
    private static object Field(object target, string name)
    { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
    private static void Call(object target, string name, params object[] args)
    { target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args); }
    internal static void Run()
    {
        if (PartyTimeline.SecondsAt(-100, 236, 100) != 0 || PartyTimeline.SecondsAt(400, 236, 100) != 100 ||
            PartyTimeline.SecondsAt(118, 236, 100) != 50 || PartyTimeline.SecondsAt(118, 10, 100) != 0 ||
            PartyTimeline.SecondsAt(118, 236, 0) != 0) throw new Exception("Timeline mapping must clamp and handle unknown duration.");
        using (var timeline = new PartyTimeline { Width = 236, Duration = 100 })
        {
            int selected = -1, seeks = 0; double seek = -1;
            timeline.Markers.Add(new PartyTimeline.Marker { Row = 7, Seconds = 50 });
            timeline.MarkerSelected += row => selected = row;
            timeline.SeekRequested += time => { seeks++; seek = time; };
            Call(timeline, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 118, 39, 0));
            if (selected != 7 || seek != 50 || timeline.Scrubbing) throw new Exception("Marker clicks must select and seek, not drag a section.");
            Call(timeline, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 28, 60, 0));
            Call(timeline, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 218, 60, 0));
            if (seeks != 1) throw new Exception("Scrubbing must not flood the player with seek calls.");
            Call(timeline, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 218, 60, 0));
            if (seek != 100 || seeks != 2 || timeline.Markers[0].Seconds != 50) throw new Exception("Release seeks without moving markers.");
        }
        var map = new PartyTempoMap { TrackUrl = "original" };
        map.Sections.Add(new PartyTempoSection { Bpm = 120, Style = PartyDanceStyle.HalfSpeed });
        map.Sections.Add(new PartyTempoSection { StartSeconds = 20, Bpm = 120, CountIn = true, Rhythm = PartyRhythm.Waltz });
        double? position = 10; int saved = 0; PartyTempoMap last = null; int requestedSeek = -1;
        using (var editor = new FrmPartyTempoMap(map, "Test song", () => position,
            p => requestedSeek = p, m => { saved++; last = m; }, 100, () => {}, () => true))
        {
            Call(editor, "SaveMap");
            if (saved != 1 || editor.IsDisposed || editor.DialogResult != DialogResult.None)
                throw new Exception("Save must apply without closing the editor.");
            var grid = (DataGridView)Field(editor, "_grid");
            if (!Convert.ToBoolean(grid.Rows[1].Cells[5].Value) || !last.Sections[1].CountIn)
                throw new Exception("Editor must load and save the count-in checkbox.");
            if (Convert.ToString(grid.Rows[1].Cells[6].Value) != "Waltz (3/4)" || last.Sections[1].Rhythm != PartyRhythm.Waltz)
                throw new Exception("Editor must load and save the selected rhythm.");
            grid.Rows[1].Cells[6].Value = "Swing (2:1)";
            grid.Rows[1].Cells[5].Value = false;
            grid.Rows[0].Cells[1].Value = "90";
            Call(editor, "SaveMap");
            if (saved != 2 || last.Sections[0].Bpm != 90 || map.Sections[0].Bpm != 120 || last.Sections[1].CountIn || !map.Sections[1].CountIn ||
                last.Sections[1].Rhythm != PartyRhythm.Swing || map.Sections[1].Rhythm != PartyRhythm.Waltz)
                throw new Exception("Repeated saves must apply new values without mutating the original map object.");
            Call(editor, "SeekTo", 105d);
            if (requestedSeek != 100000) throw new Exception("Editor seeks must clamp to song length.");
            position = null; Call(editor, "PollPlayback");
            if (((PartyTimeline)Field(editor, "_timeline")).Enabled) throw new Exception("Another song must disable navigation.");
            Call(editor, "SeekTo", 20d);
            if (requestedSeek != 100000) throw new Exception("Navigation must not seek the new song.");
            Call(editor, "SaveMap");
            if (last.TrackUrl != "original") throw new Exception("Save must stay attached to the original song.");
        }
        Console.WriteLine("Timeline seek, section selection, repeat-save and track-change checks passed.");
    }
}
