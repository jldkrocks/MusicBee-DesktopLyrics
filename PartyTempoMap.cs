using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    internal enum PartyDanceStyle { Normal, SideToSide, HalfSpeed, Hold }

    internal sealed class PartyTempoSection
    {
        public double StartSeconds;
        public double Bpm = 120;
        public double RampSeconds;
        public PartyDanceStyle Style;
        public bool AlignBeat;
    }

    internal sealed class PartyTempoMap
    {
        public int Version = 1;
        public bool Enabled = true;
        public string TrackUrl;
        public double InitialBeat;
        public List<PartyTempoSection> Sections = new List<PartyTempoSection>();

        internal void Validate()
        {
            if (Version != 1 || string.IsNullOrWhiteSpace(TrackUrl) || Sections == null ||
                Sections.Count == 0 || Sections.Count > 500 || !Finite(InitialBeat))
                throw new ArgumentException("The map needs a song and 1Ã¢â‚¬â€œ500 sections.");
            double previous = -1;
            for (int i = 0; i < Sections.Count; i++)
            {
                var s = Sections[i];
                if (s == null || !Finite(s.StartSeconds) || s.StartSeconds < 0 ||
                    s.StartSeconds > 604800 || s.StartSeconds <= previous ||
                    !Finite(s.Bpm) || s.Bpm < 40 || s.Bpm > 240 ||
                    !Finite(s.RampSeconds) || s.RampSeconds < 0 ||
                    !Enum.IsDefined(typeof(PartyDanceStyle), s.Style))
                    throw new ArgumentException("Use increasing start times, BPM 40Ã¢â‚¬â€œ240 and nonnegative ramp lengths.");
                if (i == 0 && (s.StartSeconds != 0 || s.RampSeconds != 0))
                    throw new ArgumentException("The first section must start at 0 with no ramp.");
                if (s.Style == PartyDanceStyle.Hold && (s.RampSeconds != 0 || s.AlignBeat))
                    throw new ArgumentException("Hold sections cannot have tempo ramps or beat alignment.");
                if (i + 1 < Sections.Count && Sections[i + 1] != null &&
                    s.RampSeconds > Sections[i + 1].StartSeconds - s.StartSeconds)
                    throw new ArgumentException("A ramp must finish before the next section.");
                previous = s.StartSeconds;
            }
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        // Integrate from the song timeline, not from UI ticks. Seeking, replay,
        // pause and dropped frames therefore produce the same pose at a time.
        internal PartyMapPose At(double seconds)
        {
            var beat = InitialBeat;
            var tempo = Sections[0].Bpm;
            var style = PartyDanceStyle.Normal;
            seconds = Math.Max(0, seconds);
            for (var i = 0; i < Sections.Count; i++)
            {
                var section = Sections[i];
                if (section.StartSeconds > seconds) break;
                if (section.AlignBeat && section.Style != PartyDanceStyle.Hold)
                    beat = Math.Floor(beat / 4) * 4 + (section.Style == PartyDanceStyle.SideToSide ? 3 : 2);
                var end = i + 1 < Sections.Count ? Sections[i + 1].StartSeconds : seconds;
                var elapsed = Math.Max(0, Math.Min(seconds, end) - section.StartSeconds);
                var held = section.Style == PartyDanceStyle.Hold;
                if (!held)
                {
                    style = section.Style;
                    var ramp = section.RampSeconds;
                    var rampElapsed = Math.Min(elapsed, ramp);
                    var beats = ramp > 0 ?
                        (tempo * rampElapsed + (section.Bpm - tempo) * rampElapsed * rampElapsed / (2 * ramp)) / 60 : 0;
                    beats += Math.Max(0, elapsed - ramp) * section.Bpm / 60;
                    beat += beats * (style == PartyDanceStyle.HalfSpeed ? 0.5 : 1);
                    tempo = ramp > 0 && elapsed < ramp ?
                        tempo + (section.Bpm - tempo) * elapsed / ramp : section.Bpm;
                }
                if (seconds < end || i == Sections.Count - 1)
                    return MakePose(beat, tempo, style, held);
            }
            return MakePose(beat, tempo, style, false);
        }

        private static PartyMapPose MakePose(double beat, double bpm, PartyDanceStyle style, bool held)
        {
            var phase = (beat % 4 + 4) % 4;
            var slot = (int)Math.Floor(phase + 1e-9) % 4;
            var effective = bpm * (style == PartyDanceStyle.HalfSpeed ? 0.5 : 1);
            var fraction = phase - Math.Floor(phase + 1e-9);
            fraction = Math.Max(0, fraction);
            var sideOnly = style == PartyDanceStyle.SideToSide;
            var frame = sideOnly ? slot % 2 * 6 : slot * 3;
            var since = fraction * 60000 / effective;
            var side = sideOnly || slot % 2 == 0;
            var duration = side ? Math.Min(150, 60000 / effective * 0.35) : Math.Min(110, 60000 / effective * 0.28);
            var remaining = Math.Max(0, 1 - since / duration);
            var anticipationDuration = Math.Min(130, 60000 / effective * 0.24);
            var lift = Math.Max(0, 1 - (1 - fraction) * 60000 / effective / anticipationDuration);
            return new PartyMapPose { Beat = beat, Bpm = bpm, Held = held, Frame = frame,
                Impact = held ? 0 : (float)(remaining * remaining * (side ? 1 : 0.28)),
                Anticipation = held ? 0 : (float)(lift * lift * (3 - 2 * lift)),
                Sway = held || effective >= 120 ? 0 : (float)(0.014 * Math.Min(1, (120 - effective) / 20) *
                    Math.Sin(Math.PI * fraction) * (slot % 2 == 0 ? 1 : -1)) };
        }
    }

    internal struct PartyMapPose
    {
        internal double Beat, Bpm;
        internal int Frame;
        internal bool Held;
        internal float Impact, Sway, Anticipation;
    }
}
