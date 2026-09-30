using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    // MusicBee can return the same millisecond position for several UI ticks,
    // then advance it in a larger step. Once a fresh step has anchored the
    // clock, use monotonic elapsed time between samples for steady pose hits.
    // Reconcile against the least-delayed recent fresh samples so a late
    // startup poll does not permanently shift a saved beat alignment.
    internal sealed class PartyPlaybackClock
    {
        private bool _initialized, _playing, _anchored;
        private long _lastTimestamp;
        private int _lastRawPosition;
        private double _positionMs;
        private bool _resumeBlend, _resumeSawAdvance;
        private readonly Queue<Sample> _samples = new Queue<Sample>();
        private struct Sample
        {
            internal long Timestamp;
            internal double Offset;
        }

        internal void Reset()
        {
            _initialized = _playing = _anchored = _resumeBlend = false;
            _lastTimestamp = 0;
            _lastRawPosition = 0;
            _positionMs = 0;
            _samples.Clear();
        }

        private void Observe(int position, long timestamp, long frequency)
        {
            while (_samples.Count > 0 &&
                (timestamp - _samples.Peek().Timestamp) * 1000d / frequency > 3000)
                _samples.Dequeue();
            _samples.Enqueue(new Sample { Timestamp = timestamp,
                Offset = position - timestamp * 1000d / frequency });
        }

        internal int PositionAt(int rawPositionMs, long timestamp,
            long frequency, bool playing, bool smoothResume = false)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            rawPositionMs = Math.Max(0, rawPositionMs);
            if (!_initialized || timestamp < _lastTimestamp)
            {
                _initialized = true;
                _playing = playing;
                _anchored = false;
                _positionMs = rawPositionMs;
                _samples.Clear();
            }
            else if (!playing || !_playing)
            {
                // Paused seeks use the actual position. Optional resume easing
                // starts from the settled paused display, before a buffered step.
                _resumeBlend = smoothResume && playing && !_playing;
                _resumeSawAdvance = rawPositionMs > _lastRawPosition;
                _positionMs = _resumeBlend ? _positionMs : rawPositionMs;
                _playing = playing;
                _anchored = false;
                _samples.Clear();
            }
            else
            {
                var elapsedMs = (timestamp - _lastTimestamp) * 1000d / frequency;
                var predicted = _positionMs + elapsedMs;
                if (_resumeBlend && rawPositionMs >= _lastRawPosition - 100 && Math.Abs(rawPositionMs - predicted) < 1000)
                {
                    // A resume often publishes a buffered position in one large step.
                    // Ease that display-only correction; never seek or change saved phase.
                    if (rawPositionMs > _lastRawPosition) _resumeSawAdvance = true;
                    var difference = rawPositionMs - predicted;
                    _positionMs = predicted + Math.Max(-elapsedMs * .2, Math.Min(elapsedMs * .2, difference));
                    if (_resumeSawAdvance && Math.Abs(difference) < 25) { _resumeBlend = false; _anchored = false; }
                }
                else if (rawPositionMs < _lastRawPosition - 100 ||
                    rawPositionMs > predicted + 250 ||
                    predicted - rawPositionMs > 750)
                {
                    // A real seek or stalled player must replace the estimate.
                    _resumeBlend = false;
                    _positionMs = rawPositionMs;
                    _anchored = false;
                    _samples.Clear();
                }
                else if (!_anchored && rawPositionMs > _lastRawPosition)
                {
                    // The first fresh position step removes the arbitrary
                    // age of the sample taken when the window appeared.
                    _positionMs = rawPositionMs;
                    _anchored = true;
                    Observe(rawPositionMs, timestamp, frequency);
                }
                else
                {
                    _positionMs = predicted;
                    if (_anchored && rawPositionMs > _lastRawPosition)
                        Observe(rawPositionMs, timestamp, frequency);
                    if (_samples.Count > 0)
                    {
                        var best = double.NegativeInfinity;
                        foreach (var sample in _samples) best = Math.Max(best, sample.Offset);
                        var target = timestamp * 1000d / frequency + best;
                        // At most 3% rate correction, never a jump between poses.
                        var limit = Math.Min(5d, elapsedMs * 0.03);
                        _positionMs += Math.Max(-limit, Math.Min(limit, target - predicted));
                    }
                }
            }

            _lastTimestamp = timestamp;
            _lastRawPosition = rawPositionMs;
            return (int)Math.Round(_positionMs);
        }
    }
}
