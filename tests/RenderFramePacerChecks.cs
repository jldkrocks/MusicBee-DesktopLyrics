using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using MusicBeePlugin;

internal static class RenderFramePacerChecks
{
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
        Console.WriteLine("Render pacing: bounded queue, skipped deadlines, no reentrancy, stop/restart, disposal and failure checks passed.");
    }
}
