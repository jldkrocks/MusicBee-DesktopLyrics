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
        private readonly bool _holdSeeks;
        private long _settlingStarted;
        internal bool IsSettling { get; private set; }
        internal PartyPlaybackClock(bool holdSeeks = false) { _holdSeeks = holdSeeks; }
        private bool _initialized, _playing, _anchored;
        private long _lastTimestamp;
        private int _lastRawPosition;
        private double _positionMs;
        private bool _resumeBlend, _resumeSawAdvance;
        private bool _seekBeforeResume;
        private long _resumeRequestedAt = long.MinValue;
        private bool _seekClock;
        internal int SeekRevision { get; private set; }
        internal int PhaseCorrections { get; private set; }
        private long _lastSampleTimestamp = long.MinValue;
        private readonly Queue<PhaseRange> _phaseRanges = new Queue<PhaseRange>();
        private struct PhaseRange
        {
            internal double Lower, Upper;
        }
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
            _lastSampleTimestamp = long.MinValue;
            _phaseRanges.Clear();
            IsSettling = false;
        }

        // Only a known Play command may ease a resume. MusicBee's own seek
        // can briefly report a non-playing state, which is not a normal resume.
        internal void PrepareResume(long timestamp) { _resumeRequestedAt = timestamp; }

        internal void PreparePause()
        {
            _seekBeforeResume |= IsSettling;
            IsSettling = false;
            _playing = false;
            _resumeRequestedAt = long.MinValue;
        }

        internal void Seek(int positionMs, long timestamp, long frequency, bool playing)
        {
            Reset();
            PositionAt(positionMs, timestamp, frequency, playing);
            _seekBeforeResume = !playing;
            StartSeekClock();
            if (_holdSeeks && playing) BeginSettling(timestamp);
        }

        private void BeginSettling(long timestamp)
        {
            IsSettling = true;
            _settlingStarted = timestamp;
            _resumeBlend = false;
            _samples.Clear();
            _phaseRanges.Clear();
        }

        private void SettleAt(int rawPosition, double reported, long timestamp, long sampledAt,
            long frequency, bool playing)
        {
            _positionMs = reported;
            var age = (timestamp - _settlingStarted) * 1000d / frequency;
            if (!playing)
            {
                _samples.Clear();
                // A real paused seek must not leave the drawing held indefinitely.
                if (age >= 250) { IsSettling = false; _seekBeforeResume = true; }
            }
            else
            {
                if (rawPosition > _lastRawPosition && sampledAt > _lastSampleTimestamp)
                {
                    Observe(rawPosition, sampledAt, frequency);
                    while (_samples.Count > 4) _samples.Dequeue();
                    if (_samples.Count == 4 &&
                        (sampledAt - _samples.Peek().Timestamp) * 1000d / frequency >= 180)
                    {
                        double low = double.PositiveInfinity, high = double.NegativeInfinity;
                        foreach (var sample in _samples)
                        { low = Math.Min(low, sample.Offset); high = Math.Max(high, sample.Offset); }
                        // The captured API step is about 60 ms. During seek
                        // buffering the phase moves by hundreds of milliseconds.
                        if (high - low <= 60 && (timestamp - sampledAt) * 1000d / frequency <= 200)
                        {
                            _positionMs = timestamp * 1000d / frequency + high;
                            IsSettling = false;
                            ++PhaseCorrections;
                        }
                    }
                }
                // A missing or abnormal player stream must not freeze the UI.
                if (age >= 1500) IsSettling = false;
            }
            if (!IsSettling) { _samples.Clear(); _phaseRanges.Clear(); }
            _playing = playing;
        }

        private void StartSeekClock()
        {
            _seekClock = true;
            ++SeekRevision;
            _anchored = true;
            _samples.Clear();
            _phaseRanges.Clear();
        }

        private void VerifySeekPhase(int position, long sampledAt, long timestamp, long frequency)
        {
            if (_lastSampleTimestamp == long.MinValue || sampledAt <= _lastSampleTimestamp) return;
            // A coarse position advanced between the last read and this one.
            // Its phase lies within that interval, not at an exact UI tick.
            _phaseRanges.Enqueue(new PhaseRange {
                Lower = position - sampledAt * 1000d / frequency,
                Upper = position - _lastSampleTimestamp * 1000d / frequency });
            while (_phaseRanges.Count > 5) _phaseRanges.Dequeue();
            while (_phaseRanges.Count >= 3)
            {
                double lower = double.NegativeInfinity, upper = double.PositiveInfinity;
                double lowest = double.PositiveInfinity;
                foreach (var range in _phaseRanges)
                { lower = Math.Max(lower, range.Lower); upper = Math.Min(upper, range.Upper); lowest = Math.Min(lowest, range.Lower); }
                if (lower > upper)
                {
                    // Transient buffered readings disagree. Wait for a newer
                    // consistent group instead of averaging incompatible phases.
                    _phaseRanges.Dequeue();
                    continue;
                }
                var phase = _positionMs - timestamp * 1000d / frequency;
                var allowance = Math.Max(40, Math.Min(100, lower - lowest + 20));
                if (phase < lower - allowance || phase > upper + allowance)
                {
                    // Re-anchor once when several real readings disprove the
                    // estimate. No ongoing acceleration or slow-down is needed.
                    _positionMs = timestamp * 1000d / frequency + (lower + upper) / 2;
                    ++PhaseCorrections;
                    _phaseRanges.Clear();
                }
                break;
            }
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
            var predictedNow = _positionMs + (timestamp - _lastTimestamp) * 1000d / frequency;
            var discontinuity = rawPositionMs < _lastRawPosition - 100 ||
                reported > predictedNow + 250 || predictedNow - reported > 750;
            var wasSettling = IsSettling;
            if (_holdSeeks && _initialized && !IsSettling && (playing || _playing) &&
                !_resumeBlend && !(requestedResume && !_seekBeforeResume) && discontinuity)
            {
                StartSeekClock(); BeginSettling(timestamp);
            }
            if (IsSettling)
            {
                // Another seek during the hold supersedes the previous target.
                // Ordinary buffered steps (120/240 ms in the trace) do not.
                if (wasSettling && (rawPositionMs < _lastRawPosition - 100 ||
                    rawPositionMs - _lastRawPosition > Math.Max(1000, (timestamp - _lastTimestamp) * 1000d / frequency + 500)))
                { StartSeekClock(); BeginSettling(timestamp); }
                SettleAt(rawPositionMs, reported, timestamp, sampledAt, frequency, playing);
            }
            else if (!_initialized || timestamp < _lastTimestamp)
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
                if (_holdSeeks && playing && !_playing && _seekBeforeResume)
                    BeginSettling(timestamp);
                if (_resumeBlend) _seekClock = false;
                if (playing) _seekBeforeResume = false;
                _resumeSawAdvance = rawPositionMs > _lastRawPosition;
                _positionMs = _resumeBlend ? _positionMs : reported;
                _playing = playing;
                _anchored = false;
                _samples.Clear();
                _phaseRanges.Clear();
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
                    // Keep checking phase after a seek; a couple of advancing
                    // readings can still be part of the player's seek buffering.
                    if (rawPositionMs > _lastRawPosition)
                        VerifySeekPhase(rawPositionMs, sampledAt, timestamp, frequency);
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
            _lastSampleTimestamp = sampledAt;
            return (int)Math.Round(_positionMs);
        }
    }
}
