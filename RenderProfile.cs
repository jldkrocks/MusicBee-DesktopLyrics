using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace MusicBeePlugin
{
    // Opt-in capture only. Never drives animation, playback, or song state.
    internal enum RenderMetric
    {
        FrameInterval, PaintDispatch, Scene, Clear, Background, Spectrum, Artwork,
        Lyrics, Queue, Controls, TickInterval, Tick, Dancers, DancerRaster,
        DancerUpload, MainUiLatency, CpuOneCorePercent, CpuMachinePercent,
        WorkingSetMiB, PrivateMiB, FrameWork, ForegroundRaster, ForegroundUpload, GpuSubmit
    }

    internal sealed class RenderProfile : IDisposable
    {
        private static readonly object FileGate = new object();
        internal static void Save(string path, string json)
        {
            lock (FileGate)
            {
                var reports = new Newtonsoft.Json.Linq.JArray();
                try {
                    if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length < 262144)
                        reports = Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(path));
                } catch (Exception) { }
                while (reports.Count >= 3) reports.RemoveAt(0);
                reports.Add(Newtonsoft.Json.Linq.JObject.Parse(json));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, reports.ToString());
            }
        }
        internal const int SampleLimit = 16384;
        private readonly object _gate = new object();
        private readonly List<double>[] _samples = new List<double>[Enum.GetValues(typeof(RenderMetric)).Length];
        private readonly int[] _dropped = new int[Enum.GetValues(typeof(RenderMetric)).Length];
        private long _frames, _lyricFrames, _englishFrames, _previewFrames, _gpuFrames, _foregroundFrames;
        internal void FrameActivity(bool lyrics, bool english, bool preview, bool gpu, bool foreground)
        {
            if (Stamp == 0) return;
            lock (_gate) {
                if (_finished) return;
                _frames++; if (lyrics) _lyricFrames++; if (english) _englishFrames++;
                if (preview) _previewFrames++; if (gpu) _gpuFrames++; if (foreground) _foregroundFrames++;
            }
        }
        private readonly Dictionary<string, object> _metadata;
        private readonly long _start, _end;
        private readonly Action<Action> _postMain;
        private readonly Action<string> _completed;
        private Timer _timer;
        private volatile bool _finished;
        private long _lastPaint, _lastTick, _cpuAt, _pendingAt;
        private double _lastCpu;
        private int _pending;

        internal RenderProfile(Dictionary<string, object> metadata, Action<Action> postMain,
            Action<string> completed, double seconds = 30, double warmup = 3, bool poll = true)
        {
            _metadata = metadata; _postMain = postMain; _completed = completed;
            _start = Stopwatch.GetTimestamp() + (long)(warmup * Stopwatch.Frequency);
            _end = _start + (long)(seconds * Stopwatch.Frequency);
            if (poll) _timer = new Timer(Poll, null, 500, 500);
        }

        internal long Stamp
        {
            get
            {
                if (_finished) return 0;
                var now = Stopwatch.GetTimestamp();
                return now >= _start && now < _end ? now : 0;
            }
        }

        internal void Add(RenderMetric metric, double value)
        {
            lock (_gate)
            {
                if (_finished) return;
                var index = (int)metric;
                var list = _samples[index] ?? (_samples[index] = new List<double>(512));
                if (list.Count < SampleLimit) list.Add(value); else _dropped[index]++;
            }
        }

        internal void End(RenderMetric metric, long started)
        {
            if (started != 0) Add(metric, (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency);
        }

        internal long BeginPaint()
        {
            var now = Stamp;
            if (now != 0 && _lastPaint != 0) Add(RenderMetric.FrameInterval, (now - _lastPaint) * 1000d / Stopwatch.Frequency);
            if (now != 0) _lastPaint = now;
            return now;
        }

        internal long BeginTick()
        {
            var now = Stamp;
            if (now != 0 && _lastTick != 0) Add(RenderMetric.TickInterval, (now - _lastTick) * 1000d / Stopwatch.Frequency);
            if (now != 0) _lastTick = now;
            return now;
        }

        private int _polling;
        private void Poll(object unused)
        {
            if (Interlocked.Exchange(ref _polling, 1) != 0) return;
            try
            {
                if (_finished) return;
                var now = Stopwatch.GetTimestamp();
                if (now >= _end) { Finish("completed"); return; }
                if (now < _start) return;
                using (var process = Process.GetCurrentProcess())
                {
                var cpu = process.TotalProcessorTime.TotalMilliseconds;
                if (_cpuAt != 0)
                {
                    var percent = (cpu - _lastCpu) * Stopwatch.Frequency / (now - _cpuAt) / 10d;
                    Add(RenderMetric.CpuOneCorePercent, percent);
                    Add(RenderMetric.CpuMachinePercent, percent / Environment.ProcessorCount);
                }
                _lastCpu = cpu; _cpuAt = now;
                Add(RenderMetric.WorkingSetMiB, process.WorkingSet64 / 1048576d);
                Add(RenderMetric.PrivateMiB, process.PrivateMemorySize64 / 1048576d);
                }
                // At most one outstanding UI callback. A slow MusicBee UI
                // cannot cause the profiler to build an ever-growing queue.
                if (_postMain != null && Interlocked.CompareExchange(ref _pending, 1, 0) == 0)
                {
                    _pendingAt = now;
                    _postMain(() => { End(RenderMetric.MainUiLatency, now); Interlocked.Exchange(ref _pending, 0); });
                }
            }
            catch (Exception) { Finish("sampling unavailable"); }
            finally { Interlocked.Exchange(ref _polling, 0); }
        }

        internal static object Summarize(double[] values, int dropped)
        {
            Array.Sort(values);
            var count = values.Length;
            Func<double, double> percentile = p => count == 0 ? 0 : values[Math.Max(0, (int)Math.Ceiling(count * p) - 1)];
            return new {
                count, dropped, average = count == 0 ? 0 : values.Average(),
                p50 = percentile(.50), p95 = percentile(.95), p99 = percentile(.99),
                maximum = count == 0 ? 0 : values[count - 1],
                over_20 = values.Count(v => v > 20), over_33_333 = values.Count(v => v > 1000d / 30),
                over_50 = values.Count(v => v > 50)
            };
        }

        internal string Finish(string reason)
        {
            string json;
            lock (_gate)
            {
                if (_finished) return null;
                _finished = true;
                _timer?.Dispose();
                var metrics = new Dictionary<string, object>();
                for (int i = 0; i < _samples.Length; i++)
                    metrics[((RenderMetric)i).ToString()] = Summarize(_samples[i]?.ToArray() ?? new double[0], _dropped[i]);
                json = JsonConvert.SerializeObject(new {
                    version = typeof(RenderProfile).Assembly.GetName().Version.ToString(),
                    utc = DateTime.UtcNow, reason, metadata = _metadata,
                    activity = new { frames = _frames, lyrics = _lyricFrames, english = _englishFrames,
                        preview = _previewFrames, gpu = _gpuFrames, foreground_redrawn = _foregroundFrames },
                    measured_seconds = Math.Max(0, (Math.Min(_end, Stopwatch.GetTimestamp()) - _start) / (double)Stopwatch.Frequency),
                    pending_main_ui_ms = _pending == 0 ? 0 : (Stopwatch.GetTimestamp() - _pendingAt) * 1000d / Stopwatch.Frequency,
                    notes = "Times are milliseconds except CPU percent and memory MiB. Scene/layers are nested; do not sum all metrics. PaintDispatch includes WinForms buffer copy, not display scan-out. FrameInterval is WM_PAINT cadence, not proven presentation. CPU covers the entire host process. Missing metrics are unavailable, not zero cost.",
                    metrics
                }, Formatting.Indented);
                for (int i = 0; i < _samples.Length; i++) _samples[i] = null;
            }
            try { _completed?.Invoke(json); } catch (Exception) { /* Diagnostics must not break playback. */ }
            return json;
        }

        public void Dispose() { Finish("window closed or capture replaced"); }
    }
}
