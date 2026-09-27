using System;
using System.Globalization;

namespace MusicBeePlugin
{
    internal static class PartyAnimation
    {
        internal const int FrameCount = 12;
        internal const int FrameDurationMs = 140;

        internal static double ReadBpm(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return 0;
            var value = tag.Trim();
            var end = 0;
            while (end < value.Length &&
                   (char.IsDigit(value[end]) || value[end] == '.' || value[end] == ','))
                end++;
            if (end == 0) return 0;
            double bpm;
            if (!double.TryParse(value.Substring(0, end).Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out bpm)) return 0;
            return bpm >= 40 && bpm <= 240 ? bpm : 0;
        }

        internal static int FrameAt(int positionMs, double bpm)
        {
            var loopMs = LoopDurationMs(bpm);
            var phase = positionMs % loopMs;
            if (phase < 0) phase += loopMs;
            return Math.Min(FrameCount - 1, (int)(phase * FrameCount / loopMs));
        }

        internal static double LoopDurationMs(double bpm)
        {
            var nativeLoopMs = FrameCount * FrameDurationMs;
            if (bpm >= 40 && bpm <= 240)
            {
                // Frame 6 is the raised-arm side pose; frame 0 is the opposite
                // side. A two-beat loop puts those poses one beat apart.
                return 2 * 60000d / bpm;
            }
            return nativeLoopMs;
        }

        internal static int OriginForBeat(int beatPositionMs, double bpm)
        {
            var loop = LoopDurationMs(bpm);
            // At the beat the animation is halfway through its loop (frame 6).
            // The next beat returns to frame 0. Floor keeps a fractional loop
            // from putting the sampled beat just before frame 6.
            var origin = (beatPositionMs - loop / 2) % loop;
            if (origin < 0) origin += loop;
            return (int)Math.Floor(origin);
        }

        internal static int OriginForPhase(int positionMs, double oldBpm,
            int oldOriginMs, double newBpm)
        {
            var oldLoop = LoopDurationMs(oldBpm);
            var phase = (positionMs - (double)oldOriginMs) % oldLoop;
            if (phase < 0) phase += oldLoop;
            var newLoop = LoopDurationMs(newBpm);
            var origin = (positionMs - phase / oldLoop * newLoop) % newLoop;
            if (origin < 0) origin += newLoop;
            return (int)Math.Round(origin);
        }
    }
}
