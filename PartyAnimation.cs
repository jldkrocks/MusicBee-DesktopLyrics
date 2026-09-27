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
                // A fixed four-beat phrase makes the actual song tempo change
                // the dancer's speed. Rounding the beat count to preserve the
                // native speed made very different BPMs look almost identical.
                return 4 * 60000d / bpm;
            }
            return nativeLoopMs;
        }
    }
}
