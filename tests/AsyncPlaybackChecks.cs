using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using MusicBeePlugin;

internal static class AsyncPlaybackChecks
{
    internal static void Run()
    {
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        using (var finished = new ManualResetEventSlim())
        {
            int calls = 0; long spectrumStarted = 0;
            var api = new Plugin.MusicBeeApiInterface {
                NowPlaying_GetFileUrl = () => "song",
                Player_GetPlayState = () => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(5000); return Plugin.PlayState.Playing; },
                Player_GetPosition = () => 1234,
                NowPlaying_GetDuration = () => 30000,
                NowPlaying_GetSpectrumData = data => { spectrumStarted = Stopwatch.GetTimestamp(); data[0] = .5f; finished.Set(); return 1; }
            };
            using (var reader = new PlaybackSnapshotReader(api))
            {
                var watch = Stopwatch.StartNew(); reader.Request(true);
                if (!entered.Wait(2000)) throw new Exception("Snapshot worker did not start.");
                for (int i = 0; i < 100; i++) reader.Request(true);
                if (calls != 1 || watch.ElapsedMilliseconds > 1000) throw new Exception("Requests must return while the API is blocked, with one outstanding read.");
                release.Set();
                if (!SpinWait.SpinUntil(() => reader.Latest.Position == 1234, 2000)) throw new Exception("Snapshot not published.");
                if (reader.Latest.Duration != 30000 || reader.Latest.Count != 1 || reader.Latest.Spectrum[0] != .5f) throw new Exception("Incomplete snapshot.");
                if (reader.Latest.PositionTimestamp <= 0 || reader.Latest.PositionTimestamp > spectrumStarted)
                    throw new Exception("Playback position must be timestamped before spectrum work, not at publication.");
            }
            entered.Reset(); release.Reset(); finished.Reset();
            var disposed = new PlaybackSnapshotReader(api); disposed.Request(true);
            if (!entered.Wait(2000)) throw new Exception("Disposal test failed to start.");
            disposed.Dispose(); release.Set();
            if (!finished.Wait(2000)) throw new Exception("Worker failed to finish.");
            Thread.Sleep(30);
            if (disposed.Latest.Position != 0) throw new Exception("Disposed reader published late data.");
        }
        var trackReads = 0;
        var switchApi = new Plugin.MusicBeeApiInterface {
            NowPlaying_GetFileUrl = () => Interlocked.Increment(ref trackReads) == 1 ? "old" : "new",
            Player_GetPlayState = () => Plugin.PlayState.Playing, Player_GetPosition = () => 999
        };
        using (var reader = new PlaybackSnapshotReader(switchApi))
        {
            reader.Request(false);
            if (!SpinWait.SpinUntil(() => trackReads >= 2, 2000)) throw new Exception("Track-switch read did not finish.");
            Thread.Sleep(20);
            if (reader.Latest.Position != 0) throw new Exception("Cross-track snapshot must be discarded.");
        }
        using (var ready = new ManualResetEventSlim())
        using (var closed = new ManualResetEventSlim())
        using (var ticked = new ManualResetEventSlim())
        {
            Exception failure = null; int caller = Thread.CurrentThread.ManagedThreadId;
            using (var host = new LyricsWindowThread())
            {
                host.Start(() => new Form { ShowInTaskbar = false, Opacity = 0 }, form => {
                    if (Thread.CurrentThread.ManagedThreadId == caller || Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                        throw new Exception("Lyrics window must own a separate STA.");
                    form.Disposed += (s, e) => closed.Set();
                    var timer = new System.Windows.Forms.Timer { Interval = 20 };
                    timer.Tick += (s, e) => ticked.Set();
                    form.Disposed += (s, e) => timer.Dispose();
                    timer.Start(); ready.Set();
                }, ex => { failure = ex; ready.Set(); closed.Set(); });
                if (!ready.Wait(2000) || failure != null) throw new Exception("Dedicated window startup failed.", failure);
                // The caller is blocked here; the independent UI must still animate.
                if (!ticked.Wait(2000)) throw new Exception("Dedicated message loop did not advance independently.");
                host.Dispose();
                if (!closed.Wait(2000)) throw new Exception("Dedicated window did not close asynchronously.");
            }
        }
        Console.WriteLine("Asynchronous playback sampling and independent STA lifecycle checks passed.");
    }
}
