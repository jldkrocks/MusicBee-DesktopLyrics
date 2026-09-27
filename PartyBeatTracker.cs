using System;

namespace MusicBeePlugin
{
    // Estimates one tempo from MusicBee's low-frequency spectrum, then locks it
    // for the rest of the track. It never modifies the track's tags.
    internal sealed class PartyBeatTracker
    {
        private readonly int[] _intervals = new int[8];
        private int _intervalCount, _intervalNext;
        private int _lastPosition = -1, _lastOnset = -1;
        private double _baseline, _previousEnergy;

        internal double Bpm { get; private set; }
        internal int OriginMs { get; private set; }

        internal void Reset()
        {
            _intervalCount = _intervalNext = 0;
            _lastPosition = _lastOnset = -1;
            _baseline = _previousEnergy = Bpm = 0;
            OriginMs = 0;
        }

        internal void Observe(int positionMs, double energy)
        {
            if (positionMs < 0 || double.IsNaN(energy) ||
                double.IsInfinity(energy)) return;
            if (Bpm > 0) return; // Seeking cannot change a locked song speed.
            energy = Math.Max(0, energy);
            if (_lastPosition >= 0 &&
                (positionMs < _lastPosition - 100 || positionMs > _lastPosition + 1500))
                Reset(); // A seek must not reuse the old beat grid.
            if (positionMs <= _lastPosition) return;
            if (_lastPosition < 0)
            {
                _baseline = _previousEnergy = energy;
                _lastPosition = positionMs;
                return;
            }

            var onset = energy > 1e-9 && energy > _baseline * 1.5 &&
                energy > _previousEnergy * 1.18 + 1e-10 &&
                (_lastOnset < 0 || positionMs - _lastOnset >= 235);
            _baseline += (energy - _baseline) * (energy > _baseline ? 0.045 : 0.035);
            _previousEnergy = energy;
            _lastPosition = positionMs;
            if (!onset) return;

            if (_lastOnset >= 0)
            {
                var interval = positionMs - _lastOnset;
                if (interval > 1100)
                {
                    _intervalCount = _intervalNext = 0;
                }
                else if (interval >= 250)
                {
                    _intervals[_intervalNext] = interval;
                    _intervalNext = (_intervalNext + 1) % _intervals.Length;
                    _intervalCount = Math.Min(_intervalCount + 1, _intervals.Length);
                    TryLockTempo(positionMs);
                }
            }
            _lastOnset = positionMs;
        }

        private void TryLockTempo(int positionMs)
        {
            if (_intervalCount < 6) return;
            var sorted = new int[_intervalCount];
            Array.Copy(_intervals, sorted, _intervalCount);
            Array.Sort(sorted);
            var median = sorted[sorted.Length / 2];
            double total = 0;
            var matches = 0;
            foreach (var interval in sorted)
            {
                if (Math.Abs(interval - median) > median * 0.12) continue;
                total += interval;
                matches++;
            }
            if (matches < 5) return;
            Bpm = 60000d * matches / total;

            // The current onset is the beat reference. It selects the raised
            // side pose now and the opposite side on the next beat.
            OriginMs = PartyAnimation.OriginForBeat(positionMs, Bpm);
        }
    }
}
