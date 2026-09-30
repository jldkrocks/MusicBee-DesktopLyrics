using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MusicBeePlugin
{
    // Presentation wakeups only. Never reads or advances playback/animation state.
    // One posted message remains outstanding until drawing finishes. Missed
    // deadlines are discarded, not replayed. All drawing stays on the window STA.
    internal sealed class RenderFramePacer : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period,
            IntPtr completion, IntPtr argument, bool resume);

        private sealed class TimerHandle : WaitHandle
        {
            internal TimerHandle(IntPtr handle) { SafeWaitHandle = new SafeWaitHandle(handle, true); }
        }
        private readonly object _gate = new object();
        private readonly Func<int, bool> _post;
        private readonly TimerHandle _timer;
        private readonly AutoResetEvent _changed = new AutoResetEvent(false);
        private readonly Thread _worker;
        private bool _active, _disposed, _executing;
        private int _generation, _fps, _pending;
        private long _pendingDeadline, _skipped;
        private string _failure;

        internal static RenderFramePacer TryCreate(Func<int, bool> post, out string failure)
        {
            failure = null;
            try
            {
                // Windows 10 1803+. No global timer-resolution change and no
                // spinning. Unsupported systems retain the WinForms timer.
                var handle = CreateWaitableTimerEx(IntPtr.Zero, null, 2, 0x00100002);
                if (handle == IntPtr.Zero) { failure = "Timer error " + Marshal.GetLastWin32Error(); return null; }
                return new RenderFramePacer(post, new TimerHandle(handle));
            }
            catch (Exception ex) { failure = ex.GetType().Name; return null; }
        }

        private RenderFramePacer(Func<int, bool> post, TimerHandle timer)
        {
            _post = post; _timer = timer;
            _worker = new Thread(Run, 256 * 1024) { IsBackground = true, Name = "DesktopLyrics frame wakeups" };
            try { _worker.Start(); }
            catch { _timer.Dispose(); _changed.Dispose(); throw; }
        }

        internal string Failure { get { lock (_gate) return _failure; } }
        internal bool IsAlive => _worker.IsAlive;
        internal void Start(int fps)
        {
            if (fps != 60 && fps != 120) throw new ArgumentOutOfRangeException(nameof(fps));
            lock (_gate)
            {
                if (_disposed || _failure != null || (_active && _fps == fps)) return;
                _fps = fps; _active = true; _generation++; _changed.Set();
            }
        }
        internal void Stop()
        {
            lock (_gate)
            {
                if (_disposed || !_active) return;
                _active = false; _generation++; _changed.Set();
            }
        }
        internal bool BeginFrame(int token, out double latenessMs, out long skipped)
        {
            lock (_gate)
            {
                latenessMs = 0; skipped = 0;
                if (_disposed || !_active || _failure != null || _executing ||
                    token != _generation || token != _pending) return false;
                _executing = true;
                latenessMs = Math.Max(0, (Stopwatch.GetTimestamp() - _pendingDeadline) * 1000d / Stopwatch.Frequency);
                skipped = _skipped; _skipped = 0;
                return true;
            }
        }
        internal void EndFrame(int token)
        {
            lock (_gate)
            {
                if (_pending != token) return;
                _pending = 0; _executing = false;
                if (!_disposed) _changed.Set();
            }
        }
        internal static long NextDeadline(long deadline, long now, long period)
        {
            return deadline + (Math.Max(0, now - deadline) / period + 1) * period;
        }
        private void Run()
        {
            var waits = new WaitHandle[] { _changed, _timer };
            int generation = -1;
            long deadline = 0;
            bool drawingOverran = false;
            try
            {
                while (true)
                {
                    int current, fps; bool active;
                    lock (_gate)
                    {
                        if (_disposed) return;
                        current = _generation; fps = _fps; active = _active;
                    }
                    if (!active) { _changed.WaitOne(); continue; }
                    long period = Stopwatch.Frequency / fps;
                    if (generation != current) { generation = current; deadline = Stopwatch.GetTimestamp() + period; drawingOverran = false; }
                    long due = -Math.Max(1, (long)Math.Ceiling((deadline - Stopwatch.GetTimestamp()) * 10000000d / Stopwatch.Frequency));
                    if (!SetWaitableTimer(_timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    if (WaitHandle.WaitAny(waits) == 0) continue;
                    long now = Stopwatch.GetTimestamp();
                    long next = NextDeadline(deadline, now, period);
                    bool post = false;
                    lock (_gate)
                    {
                        if (_disposed) return;
                        if (!_active || _generation != generation) continue;
                        if (_pending == 0)
                        {
                            _skipped += Math.Max(0, (next - deadline) / period - 1);
                            _pending = generation; _pendingDeadline = deadline; post = true;
                        }
                    }
                    if (!post)
                    {
                        // A draw a fraction over budget should not force an
                        // entire empty frame slot. Wait for its acknowledgement,
                        // then submit only the latest state. Re-anchor the next
                        // deadline so a long stall cannot cause catch-up bursts.
                        drawingOverran = true; _changed.WaitOne(); continue;
                    }
                    deadline = drawingOverran ? now + period : next;
                    drawingOverran = false;
                    if (post && !_post(generation)) throw new InvalidOperationException("Frame message could not be posted");
                }
            }
            catch (Exception ex)
            {
                bool notify;
                lock (_gate) { notify = !_disposed; _failure = ex.GetType().Name; _active = false; }
                // A valid HWND receives a failure message and selects the old
                // timer. A destroyed HWND is already stopping/discarding us.
                if (notify) { try { _post(0); } catch (Exception) { } }
            }
            finally
            {
                lock (_gate) { _disposed = true; _timer.Dispose(); _changed.Dispose(); }
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true; _active = false; _generation++; _changed.Set();
            }
            // No waiting on the UI thread. The worker owns its handles and
            // closes them after its interruptible wait exits.
        }
    }
}
