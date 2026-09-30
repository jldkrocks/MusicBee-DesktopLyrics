using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MusicBeePlugin
{
    // One bounded local seek trace. No song paths, titles or audio are logged.
    // Recording runs on the lyrics thread; formatting and disk I/O do not.
    internal sealed class PlaybackTimingTrace
    {
        private const int Capacity = 600;
        private static readonly object FileGate = new object();
        private readonly string _path;
        private readonly Queue<Row> _rows = new Queue<Row>(Capacity);
        private int _revision, _writing;
        private long _due;
        private bool _pending;
        private struct Row
        {
            internal long Time, SampleTime, Frequency;
            internal int Raw, Display, State, Seek, Correction;
            internal bool Playing;
        }
        internal PlaybackTimingTrace(string path) { _path = path; }
        internal void Record(long time, long frequency, PlaybackSnapshotReader.Snapshot sample,
            int display, bool playing, PartyPlaybackClock clock)
        {
            if (_rows.Count == Capacity) _rows.Dequeue();
            _rows.Enqueue(new Row { Time = time, SampleTime = sample.PositionTimestamp, Frequency = frequency,
                Raw = sample.Position, Display = display, State = (int)sample.State, Playing = playing,
                Seek = clock.SeekRevision, Correction = clock.PhaseCorrections });
            if (_revision != clock.SeekRevision)
            {
                _revision = clock.SeekRevision;
                _pending = true;
                _due = time + frequency * 4;
            }
            if (_pending && time >= _due) Flush();
        }
        internal void Flush()
        {
            if (!_pending || _rows.Count == 0 || Interlocked.CompareExchange(ref _writing, 1, 0) != 0) return;
            var rows = _rows.ToArray();
            _pending = false;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var text = new StringBuilder("DesktopLyrics " + typeof(PlaybackTimingTrace).Assembly.GetName().Version +
                        "\nwall_ms,sample_age_ms,raw_ms,display_ms,reported_state,display_playing,seek_revision,phase_corrections\n");
                    var origin = rows[0].Time;
                    foreach (var row in rows)
                        text.AppendFormat(CultureInfo.InvariantCulture, "{0:0.000},{1:0.000},{2},{3},{4},{5},{6},{7}\n",
                            (row.Time - origin) * 1000d / row.Frequency,
                            (row.Time - row.SampleTime) * 1000d / row.Frequency,
                            row.Raw, row.Display, row.State, row.Playing ? 1 : 0, row.Seek, row.Correction);
                    lock (FileGate)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_path));
                        File.WriteAllText(_path, text.ToString());
                    }
                }
                catch (Exception) { /* Diagnostics must never interrupt playback. */ }
                finally { Interlocked.Exchange(ref _writing, 0); }
            });
        }
    }
}
