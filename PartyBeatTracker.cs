using System;

namespace MusicBeePlugin
{
    // Estimates the beat from changes in MusicBee's low-frequency spectrum.
    // This is intentionally transient: it never modifies the track's tags.
    internal sealed class PartyBeatTracker
    {
        private readonly int[] _intervals = new int[7];
        private int _intervalCount, _intervalNext;
        private int _lastPosition = -1, _lastOnset = -1;
        private double _baseline, _previousEnergy, _periodMs, _originMs;

        internal double Bpm => _periodMs > 0 ? 60000d / _periodMs : 0;
        internal int OriginMs => (int)Math.Round(_originMs);

        internal void Reset()
        {
            _intervalCount = _intervalNext = 0;
            _lastPosition = _lastOnset = -1;
            _baseline = _previousEnergy = _periodMs = _originMs = 0;
        }

        internal void Observe(int positionMs, double energy)
        {
            if (positionMs < 0 || double.IsNaN(energy) ||
                double.IsInfinity(energy)) return;
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
                    _periodMs = 0;
                    _originMs = positionMs;
                }
                else if (interval >= 250)
                {
                    _intervals[_intervalNext] = interval;
                    _intervalNext = (_intervalNext + 1) % _intervals.Length;
                    _intervalCount = Math.Min(_intervalCount + 1, _intervals.Length);
                    EstimatePeriod();
                }
            }
            else _originMs = positionMs;
            _lastOnset = positionMs;

            if (_periodMs <= 0) return;
            var closestBeat = Math.Round((positionMs - _originMs) / _periodMs);
            var error = positionMs - (_originMs + closestBeat * _periodMs);
            if (Math.Abs(error) < Math.Min(110, _periodMs * 0.24))
                _originMs += error * 0.25;
        }

        private void EstimatePeriod()
        {
            if (_intervalCount < 3) return;
            var sorted = new int[_intervalCount];
            Array.Copy(_intervals, sorted, _intervalCount);
            Array.Sort(sorted);
            var median = sorted[sorted.Length / 2];
            double total = 0;
            var matches = 0;
            foreach (var interval in sorted)
            {
                if (Math.Abs(interval - median) > median * 0.18) continue;
                total += interval;
                matches++;
            }
            if (matches < 3) return;
            var candidate = total / matches;
            if (_periodMs == 0) _periodMs = candidate;
            else if (candidate > _periodMs * 0.75 && candidate < _periodMs * 1.33)
                _periodMs += (candidate - _periodMs) * 0.18;
            else if (matches >= 5) _periodMs = candidate;
        }
    }
}
