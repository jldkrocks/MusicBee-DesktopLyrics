using System;
using System.Globalization;

namespace MusicBeePlugin
{
    internal static class PartyAnimation
    {
        internal const int FrameCount = 12;
        internal const int FrameDurationMs = 140;
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

            // The last drawing before a side hit is brief, so the side pose
            // arrives with a visible snap at the next segment boundary.
            var travel = (progress - hold) / (1 - hold);
            // Briefly show the final anticipation drawing before the next
            // side pose; the side pose itself arrives exactly on the beat.
            return poseFrame + (travel < (segment % 2 == 0 ? 0.5 : 0.65) ? 1 : 2);
        }

        internal static double LoopDurationMs(double bpm)
        {
            var nativeLoopMs = FrameCount * FrameDurationMs;
            if (bpm >= 40 && bpm <= 240)
            {
                // Each side pose lasts two beats at every supported tempo.
                return 4 * 60000d / bpm;
            }
            return nativeLoopMs;
        }

        internal static int OriginForBeat(int beatPositionMs, double bpm)
        {
            var loop = LoopDurationMs(bpm);
            // At the beat the animation is halfway through its loop (frame 6).
            // The opposite side pose arrives two beats later at any BPM.
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

        internal static float SideImpactAt(int positionMs, double bpm)
        {
            if (bpm < 40 || bpm > 240) return 0;
            var halfLoop = LoopDurationMs(bpm) / 2;
            var sinceSideBeat = positionMs % halfLoop;
            if (sinceSideBeat < 0) sinceSideBeat += halfLoop;
            var duration = Math.Min(150d, 60000d / bpm * 0.35);
            if (sinceSideBeat >= duration) return 0;
            var remaining = 1 - sinceSideBeat / duration;
            return (float)(remaining * remaining);
        }
    }
}
