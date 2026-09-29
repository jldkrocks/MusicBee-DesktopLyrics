using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    internal enum PartyDanceStyle { Normal, SideToSide, HalfSpeed, Hold }
    internal enum PartyRhythm { Straight, Waltz, Swing }

    internal sealed class PartyTempoSection
    {
        public double StartSeconds;
        public double Bpm = 120;
        public double RampSeconds;
        public PartyDanceStyle Style;
        public PartyRhythm Rhythm;
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
                    !Enum.IsDefined(typeof(PartyDanceStyle), s.Style) ||
                    !Enum.IsDefined(typeof(PartyRhythm), s.Rhythm))
                    throw new ArgumentException("Use increasing start times, BPM 40-240, nonnegative ramps and a listed dance/rhythm.");
                if (i == 0 && (s.StartSeconds != 0 || s.RampSeconds != 0))
                    throw new ArgumentException("The first section must start at 0 with no ramp.");
                if (s.Style == PartyDanceStyle.Hold && (s.RampSeconds != 0 || s.AlignBeat))
                    throw new ArgumentException("Hold sections cannot have tempo ramps or beat alignment.");
                if (s.CountIn && (i == 0 || s.Style != PartyDanceStyle.Normal ||
                    Sections[i - 1].Style != PartyDanceStyle.HalfSpeed))
                    throw new ArgumentException("Count-in belongs on a Normal row immediately after Half speed. It adds up to four lead-in bobs and a final bop on the return beat.");
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
            var rhythm = PartyRhythm.Straight;
            seconds = Math.Max(0, seconds);
            for (var i = 0; i < Sections.Count; i++)
            {
                var section = Sections[i];
                if (section.StartSeconds > seconds) break;
                if (section.AlignBeat && section.Style != PartyDanceStyle.Hold)
                    beat = AlignedBeat(beat, section);
                var sectionStartBeat = beat;
                var sectionStartTempo = tempo;
                var end = i + 1 < Sections.Count ? Sections[i + 1].StartSeconds : seconds;
                var elapsed = Math.Max(0, Math.Min(seconds, end) - section.StartSeconds);
                var held = section.Style == PartyDanceStyle.Hold;
                if (!held)
                {
                    style = section.Style;
                    rhythm = section.Rhythm;
                    var ramp = section.RampSeconds;
                    var beats = IntegratedBeats(sectionStartTempo, section, elapsed);
                    beat += beats * (style == PartyDanceStyle.HalfSpeed ? 0.5 : 1);
                    tempo = ramp > 0 && elapsed < ramp ?
                        tempo + (section.Bpm - tempo) * elapsed / ramp : section.Bpm;
                }
                if (seconds < end || i == Sections.Count - 1)
                {
                    var pose = MakePose(beat, tempo, style, rhythm, held);
                    if (!held && style == PartyDanceStyle.HalfSpeed && i + 1 < Sections.Count && Sections[i + 1].CountIn)
                    {
                        var endBeat = sectionStartBeat + IntegratedBeats(sectionStartTempo, section,
                            end - section.StartSeconds) * 0.5;
                        var incoming = Sections[i + 1];
                        if (incoming.AlignBeat) endBeat = AlignedBeat(endBeat, incoming);
                        var incomingBpm = incoming.RampSeconds > 0 ? section.Bpm : incoming.Bpm;
                        var cuePhase = endBeat - (end - seconds) * incomingBpm / 60;
                        ApplyCountIn(ref pose, section, incoming, endBeat, cuePhase);
                    }
                    else if (!held && section.CountIn && i > 0 && Sections[i - 1].Style == PartyDanceStyle.HalfSpeed)
                        ApplyCountIn(ref pose, Sections[i - 1], section, sectionStartBeat, beat);
                    return pose;
                }
            }
            return MakePose(beat, tempo, style, rhythm, false);
        }

        private static double AlignedBeat(double beat, PartyTempoSection section)
        {
            // In waltz, Align marks the first (strong) beat of a three-beat bar.
            return section.Rhythm == PartyRhythm.Waltz ? Math.Floor(beat / 6) * 6 :
                Math.Floor(beat / 4) * 4 + (section.Style == PartyDanceStyle.SideToSide ? 3 : 2);
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
            PartyTempoSection next, double endBeat, double phase)
        {
            if (next.Style != PartyDanceStyle.Normal) return;
            // A BPM ramp begins at the preceding tempo, not at its final target.
            var incomingBpm = next.RampSeconds > 0 ? previous.Bpm : next.Bpm;
            var period = 60 / incomingBpm;
            if (next.StartSeconds - previous.StartSeconds + 1e-9 < period) return;
            var earliestPhase = endBeat - (next.StartSeconds - previous.StartSeconds) / period;
            var lastLeadInBeat = Math.Ceiling(endBeat - 1e-9) - 1;
            var firstBeat = Math.Max(lastLeadInBeat - 3, Math.Ceiling(earliestPhase + 0.2 - 1e-9));
            if (firstBeat > lastLeadInBeat) return;
            // Finish with one equally strong landing on the first actual beat
            // at/after the return, then recover into the regular movement.
            var lastBeat = lastLeadInBeat + 1;
            var start = firstBeat - 0.2;
            var end = lastBeat + 0.55;
            if (phase <= start || phase >= end) return;
            var mix = SmoothStep((phase - start) / 0.2) * SmoothStep((end - phase) / 0.2);
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

        private static readonly int[] WaltzFrames = { 6, 3, 9, 0, 9, 3 };

        private static PartyMapPose MakePose(double beat, double bpm, PartyDanceStyle style, PartyRhythm rhythm, bool held)
        {
            var waltz = rhythm == PartyRhythm.Waltz;
            var swing = rhythm == PartyRhythm.Swing;
            var cycle = waltz ? 6 : 4;
            var phase = (beat % cycle + cycle) % cycle;
            var slot = (int)Math.Floor(phase + 1e-9) % cycle;
            var effective = bpm * (style == PartyDanceStyle.HalfSpeed ? 0.5 : 1);
            var fraction = phase - Math.Floor(phase + 1e-9);
            fraction = Math.Max(0, fraction);
            var sideOnly = style == PartyDanceStyle.SideToSide;
            var frame = waltz ? (sideOnly ? (slot % 2 == 0 ? 6 : 0) : WaltzFrames[slot]) :
                sideOnly ? slot % 2 * 6 : slot * 3;
            var since = fraction * 60000 / effective;
            var side = waltz ? slot % 3 == 0 : sideOnly || slot % 2 == 0;
            var duration = side ? Math.Min(150, 60000 / effective * 0.35) : Math.Min(110, 60000 / effective * 0.28);
            var remaining = Math.Max(0, 1 - since / duration);
            var anticipationDuration = Math.Min(130, 60000 / effective * 0.24);
            var lift = Math.Max(0, 1 - (1 - fraction) * 60000 / effective / anticipationDuration);
            var strength = side ? 1 : waltz ? 0.42 : swing ? 0.65 : 0.28;
            // Swing leaves the main beat/pose clock steady. Its additional,
            // lighter bounce falls two-thirds of the way through each beat.
            var offbeat = fraction - 2d / 3;
            var swingAccent = !swing ? 0 : offbeat < 0 ? SmoothStep(1 + offbeat / 0.08) :
                1 - SmoothStep(offbeat / 0.14);
            return new PartyMapPose { Beat = beat, Bpm = bpm, Held = held, Frame = frame,
                Impact = held ? 0 : (float)(remaining * remaining * strength + 0.4 * swingAccent),
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
