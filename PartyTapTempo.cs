using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    // Measure manual taps with a monotonic clock. A short rolling median
    // steadies normal click timing without letting one missed tap double the
    // beat interval and halve the displayed BPM.
    internal sealed class PartyTapTempo
    {
        private const int MaxIntervals = 7;
        private readonly List<double> _intervals = new List<double>();
        private long _lastTimestamp;
        private bool _hasLastTap;

        internal int TapCount { get; private set; }

        internal bool Tap(long timestamp, long frequency, out double bpm)
        {
            bpm = 0;
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            if (_hasLastTap)
            {
                var interval = (timestamp - _lastTimestamp) * 1000d / frequency;
                if (interval < 0 || interval > 1500)
                {
                    _intervals.Clear();
                    TapCount = 0;
                }
                else if (interval < 250)
                    return false; // Ignore an accidental double click.
                else
                {
                    _intervals.Add(interval);
                    if (_intervals.Count > MaxIntervals) _intervals.RemoveAt(0);
                }
            }

            _lastTimestamp = timestamp;
            _hasLastTap = true;
            TapCount++;
            if (_intervals.Count == 0) return true;
            var sorted = _intervals.ToArray();
            Array.Sort(sorted);
            var middle = sorted.Length / 2;
            var median = sorted.Length % 2 == 0 ?
                (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
            bpm = 60000d / median;
            return true;
        }
    }
}
