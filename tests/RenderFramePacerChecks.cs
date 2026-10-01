using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using MusicBeePlugin;

internal static class RenderFramePacerChecks
{
    private sealed class BusyWindow : System.Windows.Forms.Form
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
        private RenderFramePacer _pacer;
        private readonly System.Windows.Forms.Timer _uiTimer=new System.Windows.Forms.Timer {Interval=25};
        internal int Ticks;
        internal bool Supported;
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);var window=Handle;string failure;
            _pacer=RenderFramePacer.TryCreate(token=>PostMessage(window,0x8055,new IntPtr(token),IntPtr.Zero),out failure);
            Supported=_pacer!=null;
            if(!Supported){Close();return;}
            _uiTimer.Tick+=(s,a)=>Ticks++;_uiTimer.Start();_pacer.Start(120);
        }
        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if(m.Msg==0x8055){
                var p=_pacer;if(p==null)return;double late;long skipped;
                try { if(p.BeginFrame(m.WParam.ToInt32(),out late,out skipped))Thread.Sleep(12); }
                finally { p.EndFrame(m.WParam.ToInt32()); }
                return;
            }
            base.WndProc(ref m);
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing){_pacer?.Dispose();_uiTimer.Dispose();}
            base.Dispose(disposing);
        }
    }
    internal static void Run()
    {
        if (RenderFramePacer.NextDeadline(100, 100, 10) != 110 ||
            RenderFramePacer.NextDeadline(100, 157, 10) != 160)
            throw new Exception("Render deadlines must skip stale frames without catch-up bursts.");
        string failure;
        var posted = new ConcurrentQueue<int>();
        using (var signal = new AutoResetEvent(false))
        using (var pacer = RenderFramePacer.TryCreate(token => { posted.Enqueue(token); signal.Set(); return true; }, out failure))
        {
            if (pacer == null) { Console.WriteLine("High-resolution timer unavailable; compatibility path required: " + failure); return; }
            pacer.Start(120);
            if (!signal.WaitOne(2000)) throw new Exception("Frame worker did not wake.");
            Thread.Sleep(80); // Deliberately blocked UI: no queued catch-up frames.
            if (posted.Count != 1) throw new Exception("Only one frame may be outstanding.");
            int first; posted.TryDequeue(out first);
            double late; long skipped;
            if (!pacer.BeginFrame(first, out late, out skipped) || late < 40 ||
                pacer.BeginFrame(first, out late, out skipped))
                throw new Exception("Delayed-frame reporting or reentrancy guard failed.");
            Thread.Sleep(40);
            if (posted.Count != 0) throw new Exception("Drawing must stay part of the outstanding frame.");
            pacer.EndFrame(first);
            if (!signal.WaitOne(2000) || !posted.TryDequeue(out first)) throw new Exception("Acknowledged frame must allow the next frame.");
            pacer.Stop();
            if (pacer.BeginFrame(first, out late, out skipped)) throw new Exception("Stopped wakeup must be ignored.");
            pacer.Start(60);
            Thread.Sleep(50);
            if (posted.Count != 0) throw new Exception("Restart must not bypass an outstanding stale message.");
            pacer.EndFrame(first);
            if (!signal.WaitOne(2000) || !posted.TryDequeue(out first) || !pacer.BeginFrame(first, out late, out skipped))
                throw new Exception("Restart must render with a new generation.");
            pacer.Dispose();
            pacer.EndFrame(first);
            var until = Stopwatch.StartNew();
            while (pacer.IsAlive && until.ElapsedMilliseconds < 2000) Thread.Sleep(5);
            if (pacer.IsAlive || pacer.BeginFrame(first, out late, out skipped))
                throw new Exception("Disposal must stop the worker and reject queued messages.");
        }
        using (var failed = RenderFramePacer.TryCreate(token => false, out failure))
        {
            if (failed != null)
            {
                failed.Start(120);
                var until = Stopwatch.StartNew();
                while (failed.IsAlive && until.ElapsedMilliseconds < 2000) Thread.Sleep(5);
                if (failed.IsAlive || failed.Failure == null) throw new Exception("Failed message delivery must stop safely.");
            }
        }
        using(var window=new BusyWindow {ShowInTaskbar=false,StartPosition=System.Windows.Forms.FormStartPosition.Manual,Location=new System.Drawing.Point(-20000,-20000)}) {
            var handle=window.Handle;
            using(var stop=new System.Threading.Timer(_=>window.BeginInvoke(new Action(()=>window.Close())),null,1500,Timeout.Infinite))
                System.Windows.Forms.Application.Run(window);
            if(window.Supported && window.Ticks<10)throw new Exception("Over-budget rendering must yield to ordinary UI timers: "+window.Ticks);
        }
        Console.WriteLine("Render pacing: bounded queue, skipped deadlines, no reentrancy, stop/restart, disposal, failure and busy-UI fairness checks passed.");
    }
}
