using System;
using System.Globalization;

namespace MusicBeePlugin
{
    internal static class PartyAnimation
    {
        internal const int FrameCount = 12;
        internal const int FrameDurationMs = 140;
        internal const int HalfTimeFromBpm = 110;
        internal const int QuarterTimeFromBpm = 220;
        private const double SidePoseHold = 0.42;
        private const double CentrePoseHold = 0.26;

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
            // Four clear accents per loop: side, centre, opposite side,
            // centre. At half-time tempos the centre pose arrives ON the
            // intervening beat instead of appearing before it.
            var poseSegmentMs = loopMs / 4;
            var segment = (int)(phase / poseSegmentMs);
            var progress = (phase - segment * poseSegmentMs) / poseSegmentMs;
            var poseFrame = segment * 3;
            var hold = segment % 2 == 0 ? SidePoseHold : CentrePoseHold;
            if (progress < hold) return poseFrame;

            // Give the two travel drawings equal time within each segment.
            // The next accented pose arrives at the next segment boundary.
            var travel = (progress - hold) / (1 - hold);
            return poseFrame + (travel < 0.5 ? 1 : 2);
        }

        internal static double LoopDurationMs(double bpm)
        {
            var nativeLoopMs = FrameCount * FrameDurationMs;
            if (bpm >= 40 && bpm <= 240)
            {
                // Fold fast tempos into a comfortable dance pace. Each side
                // spans one, two, or four beats respectively.
                var beatsPerLoop = bpm >= QuarterTimeFromBpm ? 8 :
                    bpm >= HalfTimeFromBpm ? 4 : 2;
                return beatsPerLoop * 60000d / bpm;
            }
            return nativeLoopMs;
        }

        internal static int OriginForBeat(int beatPositionMs, double bpm)
        {
            var loop = LoopDurationMs(bpm);
            // At the beat the animation is halfway through its loop (frame 6).
            // At higher tempos frame 0 arrives two or four beats later.
            // Floor keeps a fractional loop from putting the sampled beat
            // just before frame 6.
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
