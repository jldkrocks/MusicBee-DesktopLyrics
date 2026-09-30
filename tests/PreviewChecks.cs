using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MusicBeePlugin;
internal static class PreviewChecks
{
    static object Field(object x, string n) => x.GetType().GetField(n, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(x);
    static object Call(object x, string n, params object[] args) => x.GetType().GetMethod(n, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(x, args);
    static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    internal static void Run()
    {
        var commands = new List<bool>(); var callbacks = new Queue<Action<bool>>(); var seeks = new List<double>(); bool available = true;
        var preview = new PartyPreviewSession((p,c) => {commands.Add(p); callbacks.Enqueue(c);}, t => seeks.Add(t), () => available, s => {});
        preview.Start(10, 30); Check(preview.Active && !commands[0] && seeks.Count == 0, "Preview must await pause acknowledgement.");
        callbacks.Dequeue()(true); Check(seeks[0] == 9.5 && commands[1], "Preview starts before selected anchor, after pause.");
        callbacks.Dequeue()(true); preview.Tick(11.49); Check(commands.Count == 2, "Do not stop before preview endpoint.");
        preview.Tick(11.5); Check(commands.Count == 3 && !commands[2], "Preview must pause on endpoint.");
        callbacks.Dequeue()(true); Check(!preview.Active && seeks[1] == 10, "Preview returns paused to anchor.");
        preview.Start(20, 30); preview.Stop(); callbacks.Dequeue()(true);
        Check(!commands[commands.Count-1], "Stop during command must never start preview playback.");
        callbacks.Dequeue()(true); Check(!preview.Active && seeks[seeks.Count-1] == 20, "Early stop returns to anchor.");
        preview.Start(20, 30); var old = callbacks.Dequeue(); preview.Cancel(); preview.Start(12, 30); old(true);
        preview.Stop(); Check(callbacks.Count == 1, "Stale callback must not clear new session's pending command.");
        callbacks.Dequeue()(true); callbacks.Dequeue()(true); Check(!preview.Active, "Stop after old completion must finish.");
        int before = seeks.Count; preview.Start(10, 30); available = false; callbacks.Dequeue()(true);
        Check(!preview.Active && seeks.Count == before, "Track change must not seek another song.");
        available = true; preview.Start(10, 30); callbacks.Dequeue()(false);
        Check(!preview.Active && seeks.Count == before, "Rejected pause must not seek.");
        var broken = new PartyPreviewSession((p,c) => c(true), t => {throw new Exception("seek rejected");}, () => true, s=>{});
        broken.Start(2, 10); Check(!broken.Active, "Seek failure must stop preview.");
        var map = new PartyTempoMap { TrackUrl = "test" }; map.Sections.Add(new PartyTempoSection {Bpm=100});
        map.Sections.Add(new PartyTempoSection {StartSeconds=10, Bpm=90});
        double duration = 0; double? position = 0; bool playing = false; bool busy = false; bool displayPlaying = false; int lastSeek = -1;
        using (var editor = new FrmPartyTempoMap(map, "Preview", () => position, t=>lastSeek=t, m=>{}, 0, ()=>{}, ()=>playing, ()=>duration, null, ()=>busy, ()=>displayPlaying))
        {
            duration = 30; Call(editor,"PollPlayback");
            Check(((PartyTimeline)Field(editor,"_timeline")).Duration == 30, "Unknown duration must recover.");
            var grid = (DataGridView)Field(editor,"_grid");
            Call(editor,"SeekDoubleClickedRow",grid,new DataGridViewCellEventArgs(0,1)); Check(lastSeek == 10000, "Double-click numeric row seeks.");
            Call(editor,"SeekDoubleClickedRow",grid,new DataGridViewCellEventArgs(3,0)); Check(lastSeek == 10000, "Combo double-click must not seek.");
            playing = displayPlaying = true; Thread.Sleep(110); var shown = (double?)Call(editor,"EditingPosition");
            Check(shown > 10.07 && shown < 10.9, "Playing seek cursor must advance instead of freezing for a second.");
            position = shown; Call(editor,"EditingPosition"); Check(Field(editor,"_pendingSeek") == null, "Acknowledged seek must release optimistic cursor.");
            playing = displayPlaying = false; Call(editor,"SeekTo",12.345d); Call(editor,"SeekRelative",.01d); Call(editor,"SeekRelative",.01d);
            Check(lastSeek == 12365, "Paused precision steps must accumulate despite stale snapshot.");
            Call(editor,"PollPlayback"); var play=(Button)Field(editor,"_play"); var originalWidth=play.Width;
            busy=true; displayPlaying=true; Call(editor,"PollPlayback");
            Check(play.Text=="Pause" && play.Width==originalWidth && play.Enabled && ((Button)Field(editor,"_back")).Enabled,"Pending commands must not flicker/resize/grey out controls.");
            Call(editor,"SeekTo",20d); Check(lastSeek==12365,"Stable enabled controls must still reject competing seeks.");
            busy=false; displayPlaying=false; Call(editor,"PollPlayback");
            Check(play.Text=="Play" && play.Width==originalWidth,"Play label must keep stable layout.");
        }
        Console.WriteLine("Preview command ordering, early stop, track changes, failures, precise seeking and duration recovery passed.");
    }
}
