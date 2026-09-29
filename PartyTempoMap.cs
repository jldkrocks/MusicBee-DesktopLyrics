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
        public bool CountIn;
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
                throw new ArgumentException("The map needs a song and 1-500 sections.");
            double previous = -1;
            for (int i = 0; i < Sections.Count; i++)
            {
                var s = Sections[i];
                if (s == null || !Finite(s.StartSeconds) || s.StartSeconds < 0 ||
                    s.StartSeconds > 604800 || s.StartSeconds <= previous ||
                    !Finite(s.Bpm) || s.Bpm < 40 || s.Bpm > 240 ||
                    !Finite(s.RampSeconds) || s.RampSeconds < 0 ||
                    !Enum.IsDefined(typeof(PartyDanceStyle), s.Style))
                    throw new ArgumentException("Use increasing start times, BPM 40-240 and nonnegative ramp lengths.");
                if (i == 0 && (s.StartSeconds != 0 || s.RampSeconds != 0))
                    throw new ArgumentException("The first section must start at 0 with no ramp.");
                if (s.Style == PartyDanceStyle.Hold && (s.RampSeconds != 0 || s.AlignBeat))
                    throw new ArgumentException("Hold sections cannot have tempo ramps or beat alignment.");
                if (s.CountIn && (i == 0 || s.Style != PartyDanceStyle.Normal ||
                    Sections[i - 1].Style != PartyDanceStyle.HalfSpeed))
                    throw new ArgumentException("Count-in belongs on a Normal row immediately after Half speed. It adds up to four bobs before that row starts.");
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
                var sectionStartBeat = beat;
                var sectionStartTempo = tempo;
                var end = i + 1 < Sections.Count ? Sections[i + 1].StartSeconds : seconds;
                var elapsed = Math.Max(0, Math.Min(seconds, end) - section.StartSeconds);
                var held = section.Style == PartyDanceStyle.Hold;
                if (!held)
                {
                    style = section.Style;
                    var ramp = section.RampSeconds;
                    var beats = IntegratedBeats(sectionStartTempo, section, elapsed);
                    beat += beats * (style == PartyDanceStyle.HalfSpeed ? 0.5 : 1);
                    tempo = ramp > 0 && elapsed < ramp ?
                        tempo + (section.Bpm - tempo) * elapsed / ramp : section.Bpm;
                }
                if (seconds < end || i == Sections.Count - 1)
                {
                    var pose = MakePose(beat, tempo, style, held);
                    if (!held && style == PartyDanceStyle.HalfSpeed && i + 1 < Sections.Count && Sections[i + 1].CountIn)
                    {
                        var endBeat = sectionStartBeat + IntegratedBeats(sectionStartTempo, section,
                            end - section.StartSeconds) * 0.5;
                        ApplyCountIn(ref pose, section, Sections[i + 1], endBeat, seconds);
                    }
                    return pose;
                }
            }
            return MakePose(beat, tempo, style, false);
        }

        private static double IntegratedBeats(double fromBpm, PartyTempoSection section, double elapsed)
        {
            var rampElapsed = Math.Min(elapsed, section.RampSeconds);
            var beats = section.RampSeconds > 0 ? (fromBpm * rampElapsed +
                (section.Bpm - fromBpm) * rampElapsed * rampElapsed / (2 * section.RampSeconds)) / 60 : 0;
            return beats + Math.Max(0, elapsed - section.RampSeconds) * section.Bpm / 60;
        }

        // Project the incoming pose clock backwards. A section boundary can be
        // between beats, so it must not become a new beat origin unless Align is on.
        // The cue changes vertical motion only, never the saved phase or poses.
        private static void ApplyCountIn(ref PartyMapPose pose, PartyTempoSection previous,
            PartyTempoSection next, double endBeat, double seconds)
        {
            if (next.Style != PartyDanceStyle.Normal) return;
            if (next.AlignBeat) endBeat = Math.Floor(endBeat / 4) * 4 + 2;
            // A BPM ramp begins at the preceding tempo, not at its final target.
            var incomingBpm = next.RampSeconds > 0 ? previous.Bpm : next.Bpm;
            var period = 60 / incomingBpm;
            if (next.StartSeconds - previous.StartSeconds + 1e-9 < period) return;
            var phase = endBeat - (next.StartSeconds - seconds) / period;
            var earliestPhase = endBeat - (next.StartSeconds - previous.StartSeconds) / period;
            var lastBeat = Math.Ceiling(endBeat - 1e-9) - 1;
            var firstBeat = Math.Max(lastBeat - 3, Math.Ceiling(earliestPhase + 0.2 - 1e-9));
            if (firstBeat > lastBeat) return;
            var start = firstBeat - 0.2;
            var end = Math.Min(endBeat, lastBeat + 0.55);
            if (phase <= start || phase >= end) return;
            // If the marker closely follows the last beat, shorten the recovery
            // fade instead of weakening that hit or pulling it ahead of the beat.
            var endFade = Math.Min(0.2, end - lastBeat);
            var mix = SmoothStep((phase - start) / 0.2) * SmoothStep((end - phase) / endFade);
            var nearestBeat = Math.Max(firstBeat, Math.Min(lastBeat, Math.Floor(phase + 0.5)));
            var offset = phase - nearestBeat;
            // Crouch into the beat, reach the deepest dip ON it, then recover.
            // All count-in beats are equally strong; no faint introductory bob.
            var accent = offset < 0 ? SmoothStep(1 + offset / 0.2) : 1 - SmoothStep(offset / 0.4);
            pose.CountInAccent = (float)(accent * mix);
            // Replace the half-speed squash/lift during the cue. Adding a small
            // upward float to those opposing movements made the old bob weak.
            pose.Impact = (float)(pose.Impact * (1 - mix) + 1.7 * pose.CountInAccent);
            pose.Anticipation *= (float)(1 - mix);
        }

        private static double SmoothStep(double value)
        {
            value = Math.Max(0, Math.Min(1, value));
            return value * value * (3 - 2 * value);
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
        internal float Impact, Sway, Anticipation, CountInAccent;
    }
}
