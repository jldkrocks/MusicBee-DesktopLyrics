using System;

namespace MusicBeePlugin
{
    // MusicBee can return the same millisecond position for several UI ticks,
    // then advance it in a larger step. Once a fresh step has anchored the
    // clock, use monotonic elapsed time between samples for steady pose hits.
    internal sealed class PartyPlaybackClock
    {
        private bool _initialized, _playing, _anchored;
        private long _lastTimestamp;
        private int _lastRawPosition;
        private double _positionMs;

        internal void Reset()
        {
            _initialized = _playing = _anchored = false;
            _lastTimestamp = 0;
            _lastRawPosition = 0;
            _positionMs = 0;
        }

        internal int PositionAt(int rawPositionMs, long timestamp,
            long frequency, bool playing)
        {
            if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
            rawPositionMs = Math.Max(0, rawPositionMs);
            if (!_initialized || timestamp < _lastTimestamp)
            {
                _initialized = true;
                _playing = playing;
                _anchored = false;
                _positionMs = rawPositionMs;
            }
            else if (!playing || !_playing)
            {
                // Pausing, resuming, or seeking while paused starts from the
                // player's actual position instead of an old elapsed clock.
                _positionMs = rawPositionMs;
                _playing = playing;
                _anchored = false;
            }
            else
            {
                var elapsedMs = (timestamp - _lastTimestamp) * 1000d / frequency;
                var predicted = _positionMs + elapsedMs;
                if (rawPositionMs < _lastRawPosition - 100 ||
                    rawPositionMs > predicted + 250 ||
                    predicted - rawPositionMs > 750)
                {
                    // A real seek or stalled player must replace the estimate.
                    _positionMs = rawPositionMs;
                    _anchored = false;
                }
                else if (!_anchored && rawPositionMs > _lastRawPosition)
                {
                    // The first fresh position step removes the arbitrary
                    // age of the sample taken when the window appeared.
                    _positionMs = rawPositionMs;
                    _anchored = true;
                }
                else _positionMs = predicted;
            }

            _lastTimestamp = timestamp;
            _lastRawPosition = rawPositionMs;
            return (int)Math.Round(_positionMs);
        }
    }
}
