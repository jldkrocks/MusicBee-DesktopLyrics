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
            // The four key drawings each land on a beat and stay until the
            // next one. Frames 0 and 6 are the side hits; 3 and 9 are the
            // intervening middle poses. The other sprite drawings are skipped.
            // The tiny tolerance keeps a fractional BPM's exact beat from
            // falling one drawing short due to floating-point rounding.
            return Math.Min(3, (int)Math.Floor(phase / (loopMs / 4) + 1e-9)) * 3;
        }

        internal static float SwayAt(int positionMs, double bpm)
        {
            if (bpm < 40 || bpm >= 120) return 0;
            var beatMs = 60000d / bpm;
            var loopMs = LoopDurationMs(bpm);
            var phase = positionMs % loopMs;
            if (phase < 0) phase += loopMs;
            var beat = Math.Min(3, (int)Math.Floor(phase / beatMs + 1e-9));
            var progress = Math.Max(0, (phase - beat * beatMs) / beatMs);
            // A small arc returns to centre on each beat, so changing poses
            // never jumps sideways. It tapers away on faster songs.
            var amount = Math.Min(1d, (120 - bpm) / 20d);
            return (float)(0.014 * amount * Math.Sin(Math.PI * progress) *
                (beat % 2 == 0 ? 1 : -1));
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
