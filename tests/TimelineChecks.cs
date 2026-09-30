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
        using(var timeline=new PartyTimeline {Width=236,Duration=100,Position=50,EditAccents=true}) {
            timeline.Zoom(.1);
            if(timeline.ViewLength!=10 || timeline.ViewStart!=45)throw new Exception("Zoom must center around playhead.");
            var cue=new PartyTimeline.Marker {Row=0,Seconds=50};timeline.Accents.Add(cue);
            int moves=0,seeks=0;timeline.SeekRequested+=t=>seeks++;
            timeline.AccentMoved+=(r,t)=>{moves++;cue.Seconds=t;};
            Call(timeline,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,118,27,0));
            Call(timeline,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,138,27,0));
            if(moves!=0 || seeks!=0)throw new Exception("Dragging must not submit changes or seek before release.");
            Call(timeline,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,138,27,0));
            if(moves!=1 || seeks!=0 || cue.Seconds!=51)throw new Exception("Drag must commit in zoom coordinates without seeking.");
            Call(timeline,"OnKeyDown",new KeyEventArgs(Keys.Right));
            Call(timeline,"OnKeyDown",new KeyEventArgs(Keys.Left|Keys.Shift));
            if(Math.Abs(cue.Seconds-51.009)>1e-8)throw new Exception("Accent nudges must support 10 and 1 milliseconds.");
        }
        using(var t=new PartyTimeline {Width=236,Duration=100,ViewStart=20,ViewLength=10,EditAccents=true}) {
            t.ZoomAt(.5,168);
            if(t.ViewLength!=5 || t.ViewStart!=23.75)throw new Exception("Pointer anchor must stay at 75 percent when zooming.");
            t.ViewStart=20;t.ViewLength=10;
            var cue=new PartyTimeline.Marker {Row=0,Seconds=25};t.Accents.Add(cue);
            double moved=-1;t.AccentMoved+=(r,v)=>moved=v;
            Call(t,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,121,27,0));
            Call(t,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,141,27,0));
            if(cue.Seconds!=26)throw new Exception("Drag must preserve grab offset.");
            Call(t,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,122,27,0));
            Call(t,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,122,27,0));
            if(moved!=25.05)throw new Exception("Returning near drag origin must not stick or lose commit.");
        }
        using(var t=new PartyTimeline {Width=236,Duration=100,Overview=true,LoopStart=20,LoopEnd=25}) {
            int seeks=0;t.SeekRequested+=v=>seeks++;
            Call(t,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,63,15,0));
            Call(t,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,83,15,0));
            Call(t,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,83,15,0));
            if(t.LoopStart!=30 || t.LoopEnd!=35 || seeks!=0)throw new Exception("Overview must pan preserving span without seeking.");
            Call(t,"OnMouseWheel",new MouseEventArgs(MouseButtons.None,0,83,15,-120));
            if(t.LoopStart!=31 || t.LoopEnd!=36)throw new Exception("Overview wheel must pan by visible span.");
        }
        using(var t=new PartyTimeline {Width=236,Duration=10,EditAccents=true,SelectedAccent=0}) {
            var cue=new PartyTimeline.Marker {Row=0,Seconds=2,Prepare=.5,Hold=.5,Recovery=.5};t.Accents.Add(cue);
            int changes=0;t.EnvelopeChanged+=(r,p,h,recovery)=>{changes++;if(recovery!=1)throw new Exception("Recovery handle used wrong origin.");};
            Call(t,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,78,138,0));
            Call(t,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,88,138,0));
            if(changes!=0)throw new Exception("Duration drag must commit only on release.");
            Call(t,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,88,138,0));
            if(changes!=1)throw new Exception("Duration drag did not commit.");
            Call(t,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,48,138,0));
            Call(t,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,38,138,0));
            Call(t,"OnKeyDown",new KeyEventArgs(Keys.Escape));
            if(cue.Prepare!=.5 || changes!=1)throw new Exception("Escape must restore duration without committing.");
        }
        var map = new PartyTempoMap { TrackUrl = "original" };
        map.Sections.Add(new PartyTempoSection { Bpm = 120, Style = PartyDanceStyle.HalfSpeed });
        map.Sections.Add(new PartyTempoSection { StartSeconds = 20, Bpm = 120, CountIn = true, Rhythm = PartyRhythm.Waltz });
        double? position = 10; int saved = 0; PartyTempoMap last = null; int requestedSeek = -1;
        using (var editor = new FrmPartyTempoMap(map, "Test song", () => position,
            p => requestedSeek = p, m => { saved++; last = m; }, 100, () => {}, () => false))
        {
            Call(editor, "SaveMap");
            if (saved != 1 || editor.IsDisposed || editor.DialogResult != DialogResult.None)
                throw new Exception("Save must apply without closing the editor.");
            var grid = (DataGridView)Field(editor, "_grid");
            if (Convert.ToString(grid.Rows[0].Cells[3].Value) != "Normal" || Convert.ToString(grid.Rows[0].Cells[8].Value) != "Half (0.5x)" ||
                last.Sections[0].Style != PartyDanceStyle.Normal || last.Sections[0].Speed != 0.5 || last.Version != 7)
                throw new Exception("Editor must preserve legacy Half speed as an independent speed choice.");
            if (!Convert.ToBoolean(grid.Rows[1].Cells[5].Value) || !last.Sections[1].CountIn)
                throw new Exception("Editor must load and save the count-in checkbox.");
            if (Convert.ToString(grid.Rows[1].Cells[6].Value) != "Waltz (3/4)" || last.Sections[1].Rhythm != PartyRhythm.Waltz)
                throw new Exception("Editor must load and save the selected rhythm.");
            if (!grid.Rows[1].Cells[7].ReadOnly) throw new Exception("Swing amount must be disabled for Waltz.");
            grid.Rows[1].Cells[6].Value = "Swing";
            if (grid.Rows[1].Cells[7].ReadOnly) throw new Exception("Selecting Swing must enable its amount.");
            grid.Rows[1].Cells[7].Value = "70";
            grid.Rows[1].Cells[5].Value = false;
            grid.Rows[0].Cells[1].Value = "90";
            Call(editor, "SaveMap");
            if (saved != 2 || last.Sections[0].Bpm != 90 || map.Sections[0].Bpm != 120 || last.Sections[1].CountIn || !map.Sections[1].CountIn ||
                last.Sections[1].SwingPercent != 70 || last.Sections[1].Rhythm != PartyRhythm.Swing || map.Sections[1].Rhythm != PartyRhythm.Waltz)
                throw new Exception("Repeated saves must apply new values without mutating the original map object.");
            grid.Rows[1].Cells[6].Value = "4/4 - accent on 4";
            grid.Rows[1].Cells[4].Value = true;
            Call(editor, "SaveMap");
            if (last.Sections[1].Rhythm != PartyRhythm.AccentFour || !last.Sections[1].AlignBeat || !grid.Rows[1].Cells[7].ReadOnly)
                throw new Exception("Editor must save fourth-beat rhythm/alignment and disable Swing amount.");
            Call(editor, "SeekTo", 12.345d);
            if (requestedSeek != 12345) throw new Exception("Exact seek must preserve milliseconds.");
            Call(editor, "SeekRelative", 0.01d);
            Call(editor, "SeekRelative", 0.01d);
            if (requestedSeek != 12365) throw new Exception("Rapid fine seeks must accumulate despite stale player position.");
            Call(editor, "SeekRelative", -0.01d);
            if (requestedSeek != 12355) throw new Exception("Fine backward seek must retain precision.");
            var seekInput = (NumericUpDown)Field(editor, "_seekTime");
            seekInput.Value = 23.456m; Call(editor, "PollPlayback");
            if (seekInput.Value != 23.456m) throw new Exception("Playback polling must not overwrite an exact time being entered.");
            Call(editor, "SeekTo", -1d);
            if (requestedSeek != 0) throw new Exception("Seek must clamp at zero.");
            Call(editor, "SeekTo", 105d);
            if (requestedSeek != 100000) throw new Exception("Editor seeks must clamp to song length.");
            position = null; Call(editor, "PollPlayback");
            if (((PartyTimeline)Field(editor, "_timeline")).Enabled || seekInput.Enabled || ((Button)Field(editor, "_seekExact")).Enabled) throw new Exception("Another song must disable navigation.");
            Call(editor, "SeekTo", 20d);
            if (requestedSeek != 100000) throw new Exception("Navigation must not seek the new song.");
            Call(editor, "SaveMap");
            if (last.TrackUrl != "original") throw new Exception("Save must stay attached to the original song.");
            editor.Opacity = 0; editor.Show(); Application.DoEvents();
            foreach (var column in new[] { 3, 6, 8, 10 })
            {
                grid.CurrentCell = grid.Rows[0].Cells[0];
                grid.CurrentCell = grid.Rows[1].Cells[column];
                typeof(DataGridView).GetMethod("OnCellClick", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(grid, new object[] { new DataGridViewCellEventArgs(column, 1) });
                Application.DoEvents();
                var combo = grid.EditingControl as ComboBox;
                if (combo == null || !combo.DroppedDown) throw new Exception("One click must open each tempo-map dropdown.");
                combo.DroppedDown = false; grid.EndEdit();
            }
            if ((bool)Field(editor, "_dirty")) throw new Exception("Opening dropdowns without changes must not dirty the map.");
            grid.Rows[1].Cells[3].Value = "Side to side";
            grid.Rows[1].Cells[8].Value = "Double (2x)";
            Call(editor, "SaveMap");
            if (last.Sections[1].Style != PartyDanceStyle.SideToSide || last.Sections[1].Speed != 2)
                throw new Exception("Editor must save side-to-side at double speed independently.");
            position = 10; Call(editor, "PollPlayback");
            var step = (NumericUpDown)Field(editor, "_seekStep");
            if (step.Value != 0.1m || step.Minimum != 0.01m || step.Maximum != 5) throw new Exception("Seek step must offer useful fine and coarse values.");
            step.Value = 0.01m;
            Call(editor, "SeekTo", 10d);
            ((Button)Field(editor, "_forward")).PerformClick();
            if (requestedSeek != 10010) throw new Exception("Forward button must use the selected fine step.");
            ((Button)Field(editor, "_back")).PerformClick();
            if (requestedSeek != 10000) throw new Exception("Backward button must use the selected fine step.");
            seekInput.Value = 12.345m;
            ((Button)Field(editor, "_seekExact")).PerformClick();
            if (requestedSeek != 12345) throw new Exception("Exact seek button must use the entered time.");
            editor.Hide();
        }
        Console.WriteLine("Timeline seek, section selection, repeat-save and track-change checks passed.");
    }
}
