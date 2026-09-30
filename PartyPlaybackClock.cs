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
        private bool _seekBeforeResume;
        private long _resumeRequestedAt = long.MinValue;
        private bool _seekClock;
        private int _seekSamples;
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
            _seekBeforeResume = false;
            _resumeRequestedAt = long.MinValue;
            _seekClock = false;
            _seekSamples = 0;
        }

        // Only a known Play command may ease a resume. MusicBee's own seek
        // can briefly report a non-playing state, which is not a normal resume.
        internal void PrepareResume(long timestamp) { _resumeRequestedAt = timestamp; }

        internal void Seek(int positionMs, long timestamp, long frequency, bool playing)
        {
            Reset();
            PositionAt(positionMs, timestamp, frequency, playing);
            _seekBeforeResume = !playing;
            StartSeekClock();
        }

        private void StartSeekClock()
        {
            _seekClock = true;
            _seekSamples = 0;
            _anchored = true;
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
            long frequency, bool playing, bool smoothResume = false, long? positionTimestamp = null)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            var requestedResume = _resumeRequestedAt != long.MinValue &&
                timestamp >= _resumeRequestedAt &&
                (timestamp - _resumeRequestedAt) * 1000d / frequency <= 750;
            _resumeRequestedAt = long.MinValue;
            rawPositionMs = Math.Max(0, rawPositionMs);
            // Position was read before spectrum/metadata work and before painting.
            // Project from its acquisition time, not the time the UI consumes it.
            var sampledAt = Math.Min(timestamp, positionTimestamp ?? timestamp);
            var reported = rawPositionMs + (playing ? Math.Min(250, (timestamp - sampledAt) * 1000d / frequency) : 0);
            if (!_initialized || timestamp < _lastTimestamp)
            {
                _initialized = true;
                _playing = playing;
                _anchored = false;
                _positionMs = reported;
                _samples.Clear();
            }
            else if (!playing || !_playing)
            {
                // Paused seeks use the actual position. Optional resume easing
                // starts from the settled paused display, before a buffered step.
                if (Math.Abs(rawPositionMs - _lastRawPosition) > 100)
                { _seekBeforeResume = true; StartSeekClock(); }
                _resumeBlend = (smoothResume || requestedResume) && playing && !_playing && !_seekBeforeResume;
                if (_resumeBlend) _seekClock = false;
                if (playing) _seekBeforeResume = false;
                _resumeSawAdvance = rawPositionMs > _lastRawPosition;
                _positionMs = _resumeBlend ? _positionMs : reported;
                _playing = playing;
                _anchored = false;
                _samples.Clear();
            }
            else
            {
                var elapsedMs = (timestamp - _lastTimestamp) * 1000d / frequency;
                var predicted = _positionMs + elapsedMs;
                if (_resumeBlend && rawPositionMs >= _lastRawPosition - 100 && Math.Abs(reported - predicted) < 1000)
                {
                    // A resume often publishes a buffered position in one large step.
                    // Ease that display-only correction; never seek or change saved phase.
                    if (rawPositionMs > _lastRawPosition) _resumeSawAdvance = true;
                    var difference = reported - predicted;
                    _positionMs = predicted + Math.Max(-elapsedMs * .2, Math.Min(elapsedMs * .2, difference));
                    if (_resumeSawAdvance && Math.Abs(difference) < 25) { _resumeBlend = false; _anchored = false; }
                }
                else if (rawPositionMs < _lastRawPosition - 100 ||
                    reported > predicted + 250 ||
                    predicted - reported > 750)
                {
                    // A real seek or stalled player must replace the estimate.
                    _resumeBlend = false;
                    _positionMs = reported;
                    StartSeekClock();
                }
                else if (_seekClock)
                {
                    _positionMs = predicted;
                    // Reacquire phase once from two fresh position steps. Then
                    // run at 1x, without stretching beats to repay sample delay.
                    // A later real discontinuity still replaces the anchor above.
                    if (_seekSamples < 2 && rawPositionMs > _lastRawPosition)
                    {
                        Observe(rawPositionMs, sampledAt, frequency);
                        if (++_seekSamples == 2)
                        {
                            var best = double.NegativeInfinity;
                            foreach (var sample in _samples) best = Math.Max(best, sample.Offset);
                            _positionMs = timestamp * 1000d / frequency + best;
                            _samples.Clear();
                        }
                    }
                }
                else if (!_anchored && rawPositionMs > _lastRawPosition)
                {
                    // The first fresh position step removes the arbitrary
                    // age of the sample taken when the window appeared.
                    _positionMs = reported;
                    _anchored = true;
                    Observe(rawPositionMs, sampledAt, frequency);
                }
                else
                {
                    _positionMs = predicted;
                    if (_anchored && rawPositionMs > _lastRawPosition)
                        Observe(rawPositionMs, sampledAt, frequency);
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
