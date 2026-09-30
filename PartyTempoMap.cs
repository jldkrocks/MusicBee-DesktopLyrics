using System;
using System.Collections.Generic;

namespace MusicBeePlugin
{
    internal enum PartyDanceStyle { Normal, SideToSide, HalfSpeed, Hold, Rest }
    internal enum PartyRhythm { Straight, Waltz, Swing, AccentFour }

    internal sealed class PartyTempoSection
    {
        public double StartSeconds;
        public double Bpm = 120;
        public double RampSeconds;
        public bool RampToNext;
        public double? RampStartBpm;
        public PartyDanceStyle Style;
        public PartyRhythm Rhythm;
        public double Speed = 1;
        internal double EffectiveSpeed => Style == PartyDanceStyle.HalfSpeed ? 0.5 : Speed;
        public double SwingPercent = 66.67;
        public bool AlignBeat;
        public bool CountIn;
    }

    internal enum PartyAccentMotion { Bop, Rebound, Left, Right, Alternate }

    internal enum PartyAccentPose { Current, Left, Right, Alternate }

    internal sealed class PartyAccentCue
    {
        public PartyAccentMotion Motion;
        public PartyAccentPose? Pose;
        internal PartyAccentPose EffectivePose => Pose ?? (Motion == PartyAccentMotion.Left ? PartyAccentPose.Left :
            Motion == PartyAccentMotion.Right ? PartyAccentPose.Right : Motion == PartyAccentMotion.Alternate ? PartyAccentPose.Alternate : PartyAccentPose.Current);
        public double TimeSeconds;
        public double Strength = 1.7;
        public double PrepareSeconds = 0.1;
        public double HoldSeconds;
        public double? RecoverySeconds;
        internal double EffectiveRecovery => RecoverySeconds ?? (Motion == PartyAccentMotion.Bop ? .22 : .42);
        internal const double ReleaseSeconds = 0.22;
    }

    internal sealed class PartyTempoMap
    {
        public int Version = 1;
        public bool Enabled = true;
        public string TrackUrl;
        public double InitialBeat;
        public List<PartyAccentCue> Accents = new List<PartyAccentCue>();
        public List<PartyTempoSection> Sections = new List<PartyTempoSection>();

        internal void Validate()
        {
            if ((Version != 1 && Version != 2 && Version != 3 && Version != 4 && Version != 5 && Version != 6 && Version != 7) || string.IsNullOrWhiteSpace(TrackUrl) || Sections == null ||
                Sections.Count == 0 || Sections.Count > 500 || !Finite(InitialBeat))
                throw new ArgumentException("The map needs a song and 1-500 sections.");
            if (Accents == null || Accents.Count > 1000)
                throw new ArgumentException("Use at most 1000 accent cues.");
            double lastCue = -1;
            foreach (var cue in Accents)
            {
                if (cue == null || !Enum.IsDefined(typeof(PartyAccentMotion), cue.Motion) || (cue.Pose.HasValue && !Enum.IsDefined(typeof(PartyAccentPose), cue.Pose.Value)) || !Finite(cue.TimeSeconds) || cue.TimeSeconds < 0 || cue.TimeSeconds > 604800 ||
                    cue.TimeSeconds <= lastCue || !Finite(cue.Strength) || cue.Strength < 0.5 || cue.Strength > 2.5 ||
                    !Finite(cue.PrepareSeconds) || cue.PrepareSeconds < 0 || cue.PrepareSeconds > 1 ||
                    !Finite(cue.HoldSeconds) || cue.HoldSeconds < 0 || cue.HoldSeconds > 5 || !Finite(cue.EffectiveRecovery) || cue.EffectiveRecovery < .02 || cue.EffectiveRecovery > 2)
                    throw new ArgumentException("Accent times must be distinct and increasing; strength 0.5-2.5, lead-in 0-1 s, hold 0-5 s, recovery 0.02-2 s.");
                lastCue = cue.TimeSeconds;
            }
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
                if (s.RampToNext && (i + 1 >= Sections.Count || s.Style == PartyDanceStyle.Hold ||
                    s.RampSeconds != 0 || s.RampStartBpm.HasValue))
                    throw new ArgumentException("Ramp to next needs a following point, cannot start on Hold, and cannot also use a custom ramp.");
                if (s.Speed != 0.5 && s.Speed != 1 && s.Speed != 2)
                    throw new ArgumentException("Choose Half, Normal or Double dance speed.");
                if (!Finite(s.SwingPercent) || s.SwingPercent < 50 || s.SwingPercent > 75)
                    throw new ArgumentException("Swing % must be between 50 (even) and 75 (strong swing).");
                if (s.RampStartBpm.HasValue && (!Finite(s.RampStartBpm.Value) ||
                    s.RampStartBpm < 40 || s.RampStartBpm > 240 || s.RampSeconds <= 0))
                    throw new ArgumentException("From BPM needs a positive ramp and BPM 40-240; leave it blank for the preceding tempo.");
                if (i == 0 && (s.StartSeconds != 0 || (s.RampSeconds != 0 && !s.RampStartBpm.HasValue)))
                    throw new ArgumentException("The first section must start at 0; a ramp here also needs From BPM.");
                if (s.Style == PartyDanceStyle.Hold && (s.RampSeconds != 0 || s.AlignBeat))
                    throw new ArgumentException("Hold sections cannot have tempo ramps or beat alignment.");
                if (s.Style == PartyDanceStyle.Rest && s.AlignBeat)
                    throw new ArgumentException("Rest keeps counting; use Align only on the next dancing row if you need a new beat origin.");
                if (s.CountIn && (i == 0 || s.Style == PartyDanceStyle.Hold || s.Style == PartyDanceStyle.Rest || s.EffectiveSpeed != 1 ||
                    (Sections[i - 1].Style != PartyDanceStyle.Hold && Sections[i - 1].Style != PartyDanceStyle.Rest && Sections[i - 1].EffectiveSpeed != 0.5)))
                    throw new ArgumentException("Count-in belongs on a dancing Normal-speed row immediately after Half speed, Hold or Rest. It adds up to four lead-in bobs and a final bop on the return beat.");
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
            var pose = CoreAt(seconds);
            PartyAccentCue strongest = null;
            double weight = 0, amount = 0;
            int alternateIndex = 0, chosenSide = 0;
            var heldStart = -1d;
            if (pose.Held)
                foreach (var section in Sections)
                {
                    if (section.StartSeconds > seconds) break;
                    if (section.Style != PartyDanceStyle.Rest && section.Style != PartyDanceStyle.Hold) heldStart = -1;
                    else if (heldStart < 0) heldStart = section.StartSeconds;
                }
            foreach (var cue in Accents)
            {
                var side = cue.EffectivePose == PartyAccentPose.Left ? -1 : cue.EffectivePose == PartyAccentPose.Right ? 1 :
                    cue.EffectivePose == PartyAccentPose.Alternate ? ((alternateIndex++ % 2 == 0) ? -1 : 1) : 0;
                if (pose.Held && side != 0 && cue.TimeSeconds >= heldStart && cue.TimeSeconds <= seconds)
                    pose.Frame = side < 0 ? 6 : 0;
                var relative = seconds - cue.TimeSeconds;
                if (relative < -cue.PrepareSeconds || relative >= cue.HoldSeconds + cue.EffectiveRecovery) continue;
                var release = cue.EffectiveRecovery;
                var envelope = relative < 0 ? SmoothStep(1 + relative / cue.PrepareSeconds) :
                    relative <= cue.HoldSeconds ? 1 : 1 - SmoothStep((relative - cue.HoldSeconds) / release);
                if (envelope * cue.Strength <= amount) continue;
                strongest = cue; weight = envelope; amount = envelope * cue.Strength; chosenSide = side;
            }
            if (strongest != null)
            {
                pose.Impact = (float)(pose.Impact * (1 - weight) + amount);
                pose.Anticipation *= (float)(1 - weight);
                pose.Sway *= (float)(1 - weight);
                if (strongest.Motion != PartyAccentMotion.Bop)
                {
                    var relative = seconds - strongest.TimeSeconds;
                    var recovery = (relative - strongest.HoldSeconds) * .42 / strongest.EffectiveRecovery;
                    // Deep landing, then a smaller second crouch for a visible rebound.
                    // Keep the feet planted: stretching above the source bitmap would clip.
                    var hit = relative <= strongest.HoldSeconds ? weight : 1 - SmoothStep(recovery / .15);
                    var rebound = recovery <= .15 ? 0 : Math.Sin(Math.PI * Math.Min(1, (recovery - .15) / .27));
                    pose.Impact = (float)(pose.Impact * (1 - weight) + strongest.Strength * (1.35 * hit + .32 * rebound));
                    if (chosenSide == 0 && relative >= 0 && !pose.Held)
                        pose.Frame = CoreAt(strongest.TimeSeconds).Frame;
                    // Use the side drawing itself for lateral emphasis, without translating
                    // outside the existing dancer surface or changing its screen position.
                    pose.Anticipation *= (float)(1 - weight);
                }
                if (chosenSide != 0)
                {
                    if (seconds >= strongest.TimeSeconds) pose.Frame = chosenSide < 0 ? 6 : 0;
                    else if (!pose.Held)
                        pose.Frame = CoreAt(Math.Max(0, strongest.TimeSeconds - strongest.PrepareSeconds)).Frame;
                }
                // A cue never modifies beat integration. Only an explicit post-hit
                // hold pins the drawing; release returns to the running timeline.
                if (chosenSide == 0 && strongest.Motion == PartyAccentMotion.Bop && strongest.HoldSeconds > 0 && seconds >= strongest.TimeSeconds &&
                    seconds < strongest.TimeSeconds + strongest.HoldSeconds)
                    pose.Frame = CoreAt(strongest.TimeSeconds).Frame;
            }
            if (strongest == null && !pose.Held) EaseRestExit(ref pose, seconds);
            return pose;
        }

        private void EaseRestExit(ref PartyMapPose pose, double seconds)
        {
            for (int i = 1; i < Sections.Count; i++)
            {
                var section = Sections[i];
                if (section.StartSeconds > seconds) break;
                if (seconds >= section.StartSeconds + .12 || section.AlignBeat ||
                    section.Style == PartyDanceStyle.Rest || section.Style == PartyDanceStyle.Hold ||
                    Sections[i - 1].Style != PartyDanceStyle.Rest) continue;
                var restStart = Sections[i - 1].StartSeconds;
                for (int j = i - 2; j >= 0 && (Sections[j].Style == PartyDanceStyle.Rest || Sections[j].Style == PartyDanceStyle.Hold); j--)
                    restStart = Sections[j].StartSeconds;
                if (!Accents.Exists(c => c.EffectivePose != PartyAccentPose.Current && c.TimeSeconds >= restStart && c.TimeSeconds < section.StartSeconds)) return;
                // Suppress only a tiny intervening pose that returns to the held drawing.
                // Integration is untouched, so the next full pose lands on the same beat.
                var heldFrame = At(section.StartSeconds - .000001).Frame;
                var boundaryFrame = CoreAt(section.StartSeconds).Frame;
                var lookahead = i + 1 < Sections.Count ? Math.Min(section.StartSeconds + .12, Sections[i + 1].StartSeconds - .000001) : section.StartSeconds + .12;
                if (lookahead <= section.StartSeconds) return;
                var nextFrame = CoreAt(lookahead).Frame;
                if (boundaryFrame != heldFrame && nextFrame == heldFrame && pose.Frame == boundaryFrame)
                    pose.Frame = heldFrame;
                return;
            }
        }

        private PartyMapPose CoreAt(double seconds)
        {
            var beat = InitialBeat;
            var tempo = Sections[0].RampStartBpm ?? Sections[0].Bpm;
            var style = PartyDanceStyle.Normal;
            var rhythm = PartyRhythm.Straight;
            var swingPercent = 66.67;
            var speed = 1d;
            var restFrame = -1;
            seconds = Math.Max(0, seconds);
            for (var i = 0; i < Sections.Count; i++)
            {
                var section = Sections[i];
                if (section.StartSeconds > seconds) break;
                if (section.AlignBeat && section.Style != PartyDanceStyle.Hold)
                    beat = AlignedBeat(beat, section);
                var sectionStartBeat = beat;
                var sectionStartTempo = section.RampToNext ? section.Bpm : section.RampStartBpm ?? tempo;
                var end = i + 1 < Sections.Count ? Sections[i + 1].StartSeconds : seconds;
                var elapsed = Math.Max(0, Math.Min(seconds, end) - section.StartSeconds);
                var held = section.Style == PartyDanceStyle.Hold;
                var rest = section.Style == PartyDanceStyle.Rest;
                if (rest && restFrame < 0)
                    restFrame = MakePose(beat, tempo, style, rhythm, swingPercent, speed, true).Frame;
                if (!rest) restFrame = -1;
                if (!held)
                {
                    if (!rest) style = section.Style;
                    speed = section.EffectiveSpeed;
                    rhythm = section.Rhythm;
                    swingPercent = section.SwingPercent;
                    var ramp = section.RampToNext ? end - section.StartSeconds : section.RampSeconds;
                    var target = section.RampToNext ? (Sections[i + 1].RampStartBpm ?? Sections[i + 1].Bpm) : section.Bpm;
                    var beats = IntegratedBeats(sectionStartTempo, section, elapsed, ramp, target);
                    beat += beats * speed;
                    tempo = ramp > 0 && elapsed < ramp ?
                        sectionStartTempo + (target - sectionStartTempo) * elapsed / ramp : target;
                }
                if (seconds < end || i == Sections.Count - 1)
                {
                    var pose = MakePose(beat, tempo, style, rhythm, swingPercent, speed, held);
                    if (rest)
                    {
                        pose.Frame = restFrame; pose.Held = true;
                        pose.Impact = pose.Anticipation = pose.Sway = 0;
                    }
                    if ((held || rest || speed == 0.5) && i + 1 < Sections.Count && Sections[i + 1].CountIn)
                    {
                        var endBeat = sectionStartBeat + (held ? 0 : IntegratedBeats(sectionStartTempo, section,
                            end - section.StartSeconds, section.RampToNext ? end - section.StartSeconds : section.RampSeconds,
                            section.RampToNext ? (Sections[i + 1].RampStartBpm ?? Sections[i + 1].Bpm) : section.Bpm) * speed);
                        var incoming = Sections[i + 1];
                        if (incoming.AlignBeat) endBeat = AlignedBeat(endBeat, incoming);
                        var incomingBpm = incoming.RampToNext ? incoming.Bpm : incoming.RampSeconds > 0 ? incoming.RampStartBpm ?? (section.RampToNext ? incoming.Bpm : section.Bpm) : incoming.Bpm;
                        var cuePhase = endBeat - (end - seconds) * incomingBpm / 60;
                        ApplyCountIn(ref pose, section, incoming, endBeat, cuePhase);
                    }
                    else if (!held && !rest && section.CountIn && i > 0 && (Sections[i - 1].EffectiveSpeed == 0.5 || Sections[i - 1].Style == PartyDanceStyle.Hold || Sections[i - 1].Style == PartyDanceStyle.Rest))
                        ApplyCountIn(ref pose, Sections[i - 1], section, sectionStartBeat, beat);
                    return pose;
                }
            }
            return MakePose(beat, tempo, style, rhythm, swingPercent, speed, false);
        }

        private static double AlignedBeat(double beat, PartyTempoSection section)
        {
            // In waltz, Align marks the first (strong) beat of a three-beat bar.
            // AccentFour Align marks the strong FOUR, not the first small bop.
            return section.Rhythm == PartyRhythm.AccentFour ? Math.Floor(beat / 8) * 8 + 3 :
                section.Rhythm == PartyRhythm.Swing ? Math.Floor(beat / 2) * 2 :
                section.Rhythm == PartyRhythm.Waltz ? Math.Floor(beat / 6) * 6 :
                Math.Floor(beat / 4) * 4 + (section.Style == PartyDanceStyle.SideToSide ? 3 : 2);
        }

        private static double IntegratedBeats(double fromBpm, PartyTempoSection section, double elapsed, double? duration = null, double? target = null)
        {
            var ramp = duration ?? section.RampSeconds;
            var bpm = target ?? section.Bpm;
            var rampElapsed = Math.Min(elapsed, ramp);
            var beats = ramp > 0 ? (fromBpm * rampElapsed +
                (bpm - fromBpm) * rampElapsed * rampElapsed / (2 * ramp)) / 60 : 0;
            return beats + Math.Max(0, elapsed - ramp) * bpm / 60;
        }

        // Project the incoming pose clock backwards. A section boundary can be
        // between beats, so it must not become a new beat origin unless Align is on.
        // The cue changes vertical motion only, never the saved phase or poses.
        private static void ApplyCountIn(ref PartyMapPose pose, PartyTempoSection previous,
            PartyTempoSection next, double endBeat, double phase)
        {
            if (next.Style == PartyDanceStyle.Hold || next.EffectiveSpeed != 1) return;
            // A BPM ramp begins at the preceding tempo, not at its final target.
            var incomingBpm = next.RampToNext ? next.Bpm : next.RampSeconds > 0 ? next.RampStartBpm ?? (previous.RampToNext ? next.Bpm : previous.Bpm) : next.Bpm;
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

        private static readonly int[] WaltzFrames = { 6, 3, 3, 0, 9, 9 };

        private static readonly int[] AccentFourFrames = { 3, 3, 3, 0, 9, 9, 9, 6 };

        private static PartyMapPose MakePose(double beat, double bpm, PartyDanceStyle style, PartyRhythm rhythm, double swingPercent, double speed, bool held)
        {
            var waltz = rhythm == PartyRhythm.Waltz;
            var swing = rhythm == PartyRhythm.Swing;
            var accentFour = rhythm == PartyRhythm.AccentFour;
            var cycle = accentFour ? 8 : waltz ? 6 : 4;
            var phase = (beat % cycle + cycle) % cycle;
            var slot = (int)Math.Floor(phase + 1e-9) % cycle;
            var effective = bpm * speed;
            var fraction = phase - Math.Floor(phase + 1e-9);
            fraction = Math.Max(0, fraction);
            var sideOnly = style == PartyDanceStyle.SideToSide;
            if (swing) return MakeSwingPose(beat, bpm, effective, slot, fraction, swingPercent / 100, sideOnly, held);
            var frame = accentFour ? (sideOnly ? (slot % 2 == 0 ? 6 : 0) : AccentFourFrames[slot]) :
                waltz ? (sideOnly ? (slot % 2 == 0 ? 6 : 0) : WaltzFrames[slot]) :
                sideOnly ? slot % 2 * 6 : slot * 3;
            var since = fraction * 60000 / effective;
            var side = accentFour ? slot % 4 == 3 : waltz ? slot % 3 == 0 : sideOnly || slot % 2 == 0;
            var duration = side ? Math.Min(150, 60000 / effective * 0.35) : Math.Min(110, 60000 / effective * 0.28);
            var remaining = Math.Max(0, 1 - since / duration);
            var anticipationDuration = Math.Min(130, 60000 / effective * 0.24);
            var lift = Math.Max(0, 1 - (1 - fraction) * 60000 / effective / anticipationDuration);
            var strength = accentFour ? (side ? 1.3 : 0.45) : side ? 1 : waltz ? 0.55 : 0.28;
            // The repeated centre drawing still lands separately on beats 2 and 3.
            // A slightly larger rise at the end of beat 3 prepares the side hit.
            var rise = (accentFour && slot % 4 == 2) || (waltz && slot % 3 == 2) ? 1.3 : 1;
            return new PartyMapPose { Beat = beat, Bpm = bpm, Held = held, Frame = frame,
                Impact = held ? 0 : (float)(remaining * remaining * strength),
                Anticipation = held ? 0 : (float)(rise * lift * lift * (3 - 2 * lift)),
                Sway = held || effective >= 120 ? 0 : (float)(0.014 * Math.Min(1, (120 - effective) / 20) *
                    Math.Sin(Math.PI * fraction) * (slot % 2 == 0 ? 1 : -1)) };
        }

        private static PartyMapPose MakeSwingPose(double beat, double bpm, double effective,
            int slot, double fraction, double split, bool sideOnly, bool held)
        {
            // Each main beat lands on a side. The late subdivision briefly uses
            // the centre drawing before the opposite side lands on the next beat.
            var late = fraction + 1e-9 >= split;
            var sideFrame = slot % 2 == 0 ? 6 : 0;
            var frame = sideOnly || !late ? sideFrame : slot % 2 == 0 ? 3 : 9;
            var segment = late ? 1 - split : split;
            var progress = Math.Max(0, (fraction - (late ? split : 0)) / segment);
            var segmentMs = 60000 / effective * segment;
            var since = progress * segmentMs;
            var duration = Math.Min(late ? 90 : 150, segmentMs * (late ? 0.4 : 0.35));
            var remaining = Math.Max(0, 1 - since / duration);
            var anticipationMs = Math.Min(100, segmentMs * 0.3);
            var lift = Math.Max(0, 1 - (segmentMs - since) / anticipationMs);
            return new PartyMapPose { Beat = beat, Bpm = bpm, Frame = frame, Held = held,
                Impact = held ? 0 : (float)(remaining * remaining * (late ? 0.4 : 1)),
                Anticipation = held ? 0 : (float)(lift * lift * (3 - 2 * lift)),
                Sway = held || effective >= 120 ? 0 : (float)(0.014 * Math.Min(1, (120 - effective) / 20) *
                    Math.Sin(Math.PI * progress) * (slot % 2 == 0 ? 1 : -1)) };
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
