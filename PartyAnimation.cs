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
            var nativeLoopMs = FrameCount * FrameDurationMs;
            var loopMs = (double)nativeLoopMs;
            if (bpm >= 40 && bpm <= 240)
            {
                // Preserve the dance's rough speed while each complete loop
                // starts on a beat. Playback position also handles seeking.
                var beats = Math.Max(1, (int)Math.Round(nativeLoopMs * bpm / 60000d));
                loopMs = beats * 60000d / bpm;
            }
            var phase = Math.Max(0, positionMs) % loopMs;
            return Math.Min(FrameCount - 1, (int)(phase * FrameCount / loopMs));
        }
    }
}
