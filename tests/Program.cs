using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace MusicBeePlugin
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            if (PartyAnimation.ReadBpm("120 BPM") != 120 ||
                PartyAnimation.ReadBpm("96,5") != 96.5 ||
                PartyAnimation.ReadBpm("unknown") != 0 ||
                PartyAnimation.FrameAt(0, 0) != 0 ||
                PartyAnimation.FrameAt(1680, 0) != 0 ||
                PartyAnimation.FrameAt(0, 120) != 0 ||
                PartyAnimation.FrameAt(150, 120) != 0 ||
                PartyAnimation.FrameAt(250, 120) != 0 ||
                PartyAnimation.FrameAt(350, 120) != 0 ||
                PartyAnimation.FrameAt(450, 120) != 0 ||
                PartyAnimation.FrameAt(500, 120) != 3 ||
                PartyAnimation.FrameAt(1500, 120) != 9 ||
                PartyAnimation.FrameAt(1000, 120) != 6 ||
                PartyAnimation.FrameAt(1150, 120) != 6 ||
                PartyAnimation.FrameAt(1250, 120) != 6 ||
                PartyAnimation.FrameAt(1350, 120) != 6 ||
                PartyAnimation.FrameAt(2000, 120) != 0 ||
                PartyAnimation.FrameAt(750, 80) != 3 ||
                Math.Abs(PartyAnimation.LoopDurationMs(80) - 3000) > 0.001 ||
                Math.Abs(PartyAnimation.LoopDurationMs(109) - 240000d / 109) > 0.001 ||
                Math.Abs(PartyAnimation.LoopDurationMs(110) - 240000d / 110) > 0.001 ||
                Math.Abs(PartyAnimation.LoopDurationMs(160) - 1500) > 0.001 ||
                Math.Abs(PartyAnimation.LoopDurationMs(240) - 1000) > 0.001)
                throw new Exception("Party dancers should use two beats per side at every BPM.");

            foreach (var bpm in new[] { 40d, 80d, 120d, 240d })
            {
                var beatMs = 60000d / bpm;
                for (var poseIndex = 0; poseIndex < 4; poseIndex++)
                    for (var within = 0; within < 20; within++)
                    {
                        var position = (int)Math.Ceiling((poseIndex + within / 20d) * beatMs);
                        if (PartyAnimation.FrameAt(position, bpm) != poseIndex * 3)
                            throw new Exception("Only the four main drawings should be held between beats.");
                    }
            }

            // The middle and side poses change exactly at the beat, with no
            // in-between sprite frames even at very slow and fast tempos.
            if (PartyAnimation.FrameAt(490, 120) != 0 ||
                PartyAnimation.FrameAt(550, 120) != 3 ||
                PartyAnimation.FrameAt(620, 120) != 3 ||
                PartyAnimation.FrameAt(650, 120) != 3 ||
                PartyAnimation.FrameAt(850, 120) != 3 ||
                PartyAnimation.FrameAt(960, 120) != 3 ||
                PartyAnimation.FrameAt(1490, 120) != 6 ||
                PartyAnimation.FrameAt(1550, 120) != 9 ||
                PartyAnimation.FrameAt(750, 80) != 3 ||
                PartyAnimation.FrameAt(1512, 119) != 6 ||
                PartyAnimation.FrameAt(1513, 119) != 9 ||
                PartyAnimation.FrameAt(250, 240) != 3)
                throw new Exception("The middle and side poses should land on successive beats.");
            if (PartyAnimation.SwayAt(0, 80) != 0 ||
                PartyAnimation.SwayAt(750, 80) != 0 ||
                PartyAnimation.SwayAt(375, 80) < 0.013f ||
                PartyAnimation.SwayAt(1125, 80) > -0.013f ||
                PartyAnimation.SwayAt(250, 100) <= 0 ||
                PartyAnimation.SwayAt(250, 120) != 0 ||
                PartyAnimation.SwayAt(250, 0) != 0)
                throw new Exception("Slow-song sway should stay subtle, return on the beat, and fade at higher BPM.");
            if (PartyAnimation.SideImpactAt(0, 120) < 0.99f ||
                PartyAnimation.SideImpactAt(60, 120) <= 0 ||
                PartyAnimation.SideImpactAt(500, 120) != 0 ||
                PartyAnimation.SideImpactAt(1000, 120) < 0.99f ||
                PartyAnimation.SideImpactAt(0, 0) != 0)
                throw new Exception("The side beats should retain their strong impact.");
            if (PartyAnimation.CentreImpactAt(0, 120) != 0 ||
                PartyAnimation.CentreImpactAt(500, 120) < 0.27f ||
                PartyAnimation.CentreImpactAt(560, 120) <= 0 ||
                PartyAnimation.CentreImpactAt(610, 120) != 0 ||
                PartyAnimation.CentreImpactAt(1000, 120) != 0 ||
                PartyAnimation.CentreImpactAt(1500, 120) < 0.27f ||
                PartyAnimation.CentreImpactAt(500, 0) != 0 ||
                PartyAnimation.CentreImpactAt(500, 120) >=
                    PartyAnimation.SideImpactAt(0, 120))
                throw new Exception("The middle poses should land on beat with a lighter accent.");
            if (PartyAnimation.AnticipationAt(0, 120) != 0 ||
                PartyAnimation.AnticipationAt(350, 120) != 0 ||
                PartyAnimation.AnticipationAt(440, 120) <= 0 ||
                PartyAnimation.AnticipationAt(499, 120) < 0.99f ||
                PartyAnimation.AnticipationAt(500, 120) != 0 ||
                PartyAnimation.AnticipationAt(249, 240) < 0.99f ||
                PartyAnimation.AnticipationAt(250, 240) != 0 ||
                PartyAnimation.AnticipationAt(499, 0) != 0)
                throw new Exception("Each held pose should prepare briefly and land on the next beat.");

            // The display compensates for timer and layered-window latency.
            // Keep the saved origin at the audible beat and test the frame
            // just before and at the compensated arrival at several tempos.
            foreach (var tempo in new[] { 80d, 120d, 240d })
            {
                var origin = PartyAnimation.OriginForBeat(1000, tempo);
                var before = PartyAnimation.DisplayPhaseAt(
                    1000 - PartyAnimation.VisualLeadMs - 1, origin, tempo);
                var arrival = PartyAnimation.DisplayPhaseAt(
                    1000 - PartyAnimation.VisualLeadMs, origin, tempo);
                if (PartyAnimation.FrameAt(before, tempo) != 3 ||
                    PartyAnimation.FrameAt(arrival, tempo) != 6 ||
                    PartyAnimation.SideImpactAt(arrival, tempo) < 0.99f ||
                    PartyAnimation.FrameAt(PartyAnimation.DisplayPhaseAt(
                        1000, origin, tempo), tempo) != 6)
                    throw new Exception("The visual lead must bring the side hit forward without changing its beat origin.");
            }
            if (PartyAnimation.DisplayPhaseAt(250, 10, 0) != 240)
                throw new Exception("The unsynchronized fallback should keep its original loop timing.");

            // A coarse player position should not make the four poses wait
            // for its next 100 ms update and then jump ahead. The monotonic
            // clock keeps the same BPM while making the intervals even.
            var partyClock = new PartyPlaybackClock();
            var poseChanges = new List<int>();
            var previousPose = -1;
            for (var wallMs = 0; wallMs < 5000; wallMs += 17)
            {
                var coarsePosition = wallMs / 100 * 100;
                var smoothPosition = partyClock.PositionAt(coarsePosition,
                    wallMs, 1000, true);
                var currentPose = PartyAnimation.FrameAt(smoothPosition, 220);
                if (previousPose >= 0 && currentPose != previousPose)
                    poseChanges.Add(wallMs);
                previousPose = currentPose;
            }
            if (poseChanges.Count < 17)
                throw new Exception("The playback clock should advance through the whole song.");
            for (var i = 1; i < poseChanges.Count; i++)
                if (Math.Abs(poseChanges[i] - poseChanges[i - 1] - 60000d / 220) > 20)
                    throw new Exception("Coarse MusicBee positions must not jitter the beat poses.");
            if (partyClock.PositionAt(5000, 5100, 1000, false) != 5000 ||
                partyClock.PositionAt(5000, 8000, 1000, false) != 5000 ||
                partyClock.PositionAt(5000, 8017, 1000, true) != 5000 ||
                partyClock.PositionAt(9000, 8034, 1000, true) != 9000 ||
                partyClock.PositionAt(100, 8051, 1000, true) != 100)
                throw new Exception("Pauses and seeks must reset the party clock.");
            partyClock.Reset();
            if (partyClock.PositionAt(750, 9000, 1000, true) != 750)
                throw new Exception("Song changes must clear the old playback position.");

            var beatOrigin = PartyAnimation.OriginForBeat(750, 120);
            if (PartyAnimation.FrameAt(750 - beatOrigin, 120) != 6 ||
                PartyAnimation.FrameAt(1250 - beatOrigin, 120) == 0 ||
                PartyAnimation.FrameAt(1750 - beatOrigin, 120) != 0 ||
                PartyAnimation.FrameAt(2750 - beatOrigin, 120) != 6 ||
                PartyAnimation.FrameAt(-PartyAnimation.OriginForBeat(0, 120), 120) != 6)
                throw new Exception("Fast tracks must alternate sides every other beat.");
            var slowBeatOrigin = PartyAnimation.OriginForBeat(750, 80);
            if (PartyAnimation.FrameAt(750 - slowBeatOrigin, 80) != 6 ||
                PartyAnimation.FrameAt(1500 - slowBeatOrigin, 80) != 9 ||
                PartyAnimation.FrameAt(2250 - slowBeatOrigin, 80) != 0)
                throw new Exception("Slow tracks must also alternate sides every other beat.");

            var workArea = new Rectangle(0, 0, 1920, 1040);
            var monitors = new[] { new Rectangle(-1920, 0, 1920, 1040),
                workArea, new Rectangle(1920, 0, 1920, 1040) };
            var medium = new Rectangle(360, 210, 1200, 570);
            var large = new Rectangle(110, 70, 1700, 900);
            var mediumLeft = PartyLayout.PlaceOutside(medium, monitors, true);
            var largeLeft = PartyLayout.PlaceOutside(large, monitors, true);
            var nearRight = PartyLayout.PlaceOutside(
                new Rectangle(1100, 210, 800, 570), monitors, false);
            var nearLeft = PartyLayout.PlaceOutside(
                new Rectangle(20, 210, 800, 570), monitors, true);
            var withoutNeighbor = PartyLayout.PlaceOutside(
                new Rectangle(1100, 210, 800, 570), new[] { workArea }, false);
            var gutter = PartyLayout.MaximizedGutter(workArea.Width);
            var maximizedLeft = PartyLayout.PlaceMaximized(
                workArea, workArea, true, gutter);
            var maximizedRight = PartyLayout.PlaceMaximized(
                workArea, workArea, false, gutter);
            if (mediumLeft.IsEmpty || largeLeft.Height <= mediumLeft.Height ||
                nearRight.Left < 1920 || nearLeft.Right > 0 ||
                !withoutNeighbor.IsEmpty || workArea.Width - gutter * 2 < 620 ||
                maximizedLeft.IsEmpty || maximizedRight.IsEmpty ||
                !workArea.Contains(maximizedLeft) || !workArea.Contains(maximizedRight) ||
                maximizedLeft.Right > gutter ||
                maximizedRight.Left < workArea.Right - gutter)
                throw new Exception("Party dancers need external monitor space or dedicated maximized gutters.");

            var beat = new PartyBeatTracker();
            var lockedAt = -1;
            for (var ms = 0; ms < 5000; ms += 30)
            {
                beat.Observe(ms, ms >= 510 && (ms - 510) % 510 < 45 ? 9 : 1);
                if (lockedAt < 0 && beat.Bpm > 0)
                {
                    lockedAt = ms;
                    if (PartyAnimation.FrameAt(ms - beat.OriginMs, beat.Bpm) != 6)
                        throw new Exception("A detected beat must select the marked frame.");
                }
            }
            if (lockedAt < 0 || Math.Abs(beat.Bpm - 60000d / 510) > 5)
                throw new Exception("Untagged songs should settle on a steady tempo.");
            var fixedBpm = beat.Bpm;
            var fixedOrigin = beat.OriginMs;
            for (var ms = 5010; ms < 8000; ms += 30)
                beat.Observe(ms, ms % 300 < 45 ? 9 : 1);
            beat.Observe(100, 1); // Seeking retains the song's locked speed.
            if (beat.Bpm != fixedBpm || beat.OriginMs != fixedOrigin)
                throw new Exception("A song's tempo and animation phase must not wander or stutter.");

            var manualOrigin = PartyAnimation.OriginForPhase(1234, 80, 0, 120);
            if (PartyAnimation.FrameAt(1234, 80) !=
                PartyAnimation.FrameAt(1234 - manualOrigin, 120))
                throw new Exception("Manual BPM adjustment must keep the current dance frame.");
            var taps = new PartyTapTempo();
            double tappedBpm;
            if (!taps.Tap(1000, 1000, out tappedBpm) || tappedBpm != 0 ||
                !taps.Tap(1500, 1000, out tappedBpm) || tappedBpm != 120 ||
                !taps.Tap(2010, 1000, out tappedBpm) ||
                !taps.Tap(2510, 1000, out tappedBpm) ||
                !taps.Tap(3510, 1000, out tappedBpm) ||
                Math.Abs(tappedBpm - 120) > 2 || taps.TapCount != 5 ||
                taps.Tap(3600, 1000, out tappedBpm) || taps.TapCount != 5 ||
                !taps.Tap(7000, 1000, out tappedBpm) ||
                tappedBpm != 0 || taps.TapCount != 1 ||
                !taps.Tap(7750, 1000, out tappedBpm) || tappedBpm != 80)
                throw new Exception("Tap BPM must update promptly, ignore double clicks and missed beats, and restart after a pause.");
            var tempoRoot = Path.Combine(Path.GetTempPath(),
                "DesktopLyrics-Tempo-" + Guid.NewGuid().ToString("N"));
            try
            {
                var tempoStore = new PartyTempoStore(tempoRoot);
                tempoStore.Save("first.mp3", beat.Bpm, beat.OriginMs, false);
                var loaded = new PartyTempoStore(tempoRoot).Load("first.mp3");
                if (loaded == null || loaded.Manual || !loaded.TwoBeatPhase ||
                    loaded.BeatPatternVersion != 3 ||
                    loaded.Bpm != beat.Bpm ||
                    loaded.OriginMs != beat.OriginMs ||
                    tempoStore.Load("second.mp3") != null)
                    throw new Exception("A detected BPM must persist for only its song.");
                tempoStore.Save("first.mp3", 132.5, manualOrigin, true);
                loaded = new PartyTempoStore(tempoRoot).Load("first.mp3");
                if (loaded == null || !loaded.Manual || loaded.Bpm != 132.5 ||
                    loaded.OriginMs != manualOrigin)
                    throw new Exception("A manual BPM must replace the saved automatic value.");
                tempoStore.Delete("first.mp3");
                if (new PartyTempoStore(tempoRoot).Load("first.mp3") != null)
                    throw new Exception("Forgetting a BPM must allow fresh detection.");
                tempoStore.Save("old.mp3", 120, 123, false);
                var oldFile = Directory.GetFiles(Path.Combine(tempoRoot,
                    "DesktopLyrics-PartyTempo"), "*.json")[0];
                var oldJson = JObject.Parse(File.ReadAllText(oldFile));
                oldJson.Remove("TwoBeatPhase");
                File.WriteAllText(oldFile, oldJson.ToString());
                loaded = new PartyTempoStore(tempoRoot).Load("old.mp3");
                if (loaded == null || loaded.Bpm != 120 ||
                    loaded.OriginMs != PartyAnimation.OriginForBeat(0, 120))
                    throw new Exception("Older saved BPMs must keep their speed with a new beat phase.");

                tempoStore.Save("last-release.mp3", 120, 500, false);
                string oldPatternFile = null;
                // Locate this particular file by its track URL, independent of
                // directory ordering.
                foreach (var file in Directory.GetFiles(Path.Combine(tempoRoot,
                    "DesktopLyrics-PartyTempo"), "*.json"))
                {
                    if (JObject.Parse(File.ReadAllText(file))["TrackUrl"].ToString() ==
                        "last-release.mp3") oldPatternFile = file;
                }
                if (oldPatternFile == null)
                    throw new Exception("Could not locate the previous tempo entry.");
                var oldPattern = JObject.Parse(File.ReadAllText(oldPatternFile));
                oldPattern.Remove("BeatPatternVersion");
                File.WriteAllText(oldPatternFile, oldPattern.ToString());
                loaded = tempoStore.Load("last-release.mp3");
                if (loaded == null || loaded.OriginMs !=
                    PartyAnimation.OriginForBeat(0, 120) ||
                    PartyAnimation.FrameAt(-loaded.OriginMs, loaded.Bpm) != 6)
                    throw new Exception("Two-beat saves must retain their beat grid at half-time.");
                tempoStore.Save("legacy-slow.mp3", 80, 0, true);
                foreach (var file in Directory.GetFiles(Path.Combine(tempoRoot,
                    "DesktopLyrics-PartyTempo"), "*.json"))
                {
                    var entry = JObject.Parse(File.ReadAllText(file));
                    if (entry["TrackUrl"].ToString() != "legacy-slow.mp3") continue;
                    entry["BeatPatternVersion"] = 2;
                    File.WriteAllText(file, entry.ToString());
                }
                loaded = tempoStore.Load("legacy-slow.mp3");
                if (loaded == null || loaded.OriginMs !=
                    PartyAnimation.OriginForBeat(750, 80) ||
                    PartyAnimation.FrameAt(750 - loaded.OriginMs, 80) != 6)
                    throw new Exception("Old slow-song taps must retain the accented beat.");
                tempoStore.Save("legacy-fast.mp3", 230, 0, true);
                foreach (var file in Directory.GetFiles(Path.Combine(tempoRoot,
                    "DesktopLyrics-PartyTempo"), "*.json"))
                {
                    var entry = JObject.Parse(File.ReadAllText(file));
                    if (entry["TrackUrl"].ToString() != "legacy-fast.mp3") continue;
                    entry["BeatPatternVersion"] = 2;
                    File.WriteAllText(file, entry.ToString());
                }
                loaded = tempoStore.Load("legacy-fast.mp3");
                var oldFastBeat = (int)Math.Round(4 * 60000d / 230);
                if (loaded == null || loaded.OriginMs !=
                    PartyAnimation.OriginForBeat(oldFastBeat, 230) ||
                    PartyAnimation.FrameAt(oldFastBeat - loaded.OriginMs, 230) != 6)
                    throw new Exception("Old fast-song taps must retain the accented beat.");
                tempoStore.Save("online.mp3", 127,
                    PartyAnimation.OriginForBeat(0, 127), false, true);
                loaded = tempoStore.Load("online.mp3");
                if (loaded == null || !loaded.Online || loaded.Manual || loaded.Bpm != 127)
                    throw new Exception("A confirmed online tempo must persist per song.");
                tempoStore.SaveApiKey("local-test-key-not-a-real-credential");
                if (tempoStore.LoadApiKey() != "local-test-key-not-a-real-credential" ||
                    System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(
                        tempoRoot, "DesktopLyrics-PartyTempo", "getsongbpm-key.bin")))
                        .Contains("local-test-key-not-a-real-credential"))
                    throw new Exception("The API key must be recoverable locally but encrypted on disk.");
                tempoStore.SaveApiKey("");
                if (tempoStore.LoadApiKey() != "")
                    throw new Exception("Removing the API key must stop online lookup.");
            }
            finally
            {
                if (Directory.Exists(tempoRoot)) Directory.Delete(tempoRoot, true);
            }

            var bpmResults = "{\"search\":[" +
                "{\"title\":\"~Asterisk~\",\"tempo\":\"127\",\"artist\":{\"name\":\"ORANGE RANGE\"}," +
                "\"album\":{\"title\":\"Natural\"}}," +
                "{\"title\":\"~Asterisk~\",\"tempo\":\"128\",\"artist\":{\"name\":\"ORANGE RANGE\"}," +
                "\"album\":{\"title\":\"Live\"}}]}";
            if (GetSongBpmClient.MatchTempo(bpmResults, "* ~Asterisk~", "Orange Range", "Natural") != 127 ||
                GetSongBpmClient.MatchTempo(bpmResults, "~Asterisk~", "ORANGE RANGE", "") != 0 ||
                GetSongBpmClient.MatchTempo(bpmResults, "~Asterisk~", "Other Artist", "Natural") != 0 ||
                GetSongBpmClient.MatchTempo("{\"search\":[]}", "~Asterisk~", "ORANGE RANGE", "") != 0)
                throw new Exception("Online BPM must require a confident artist, title and version match.");
            var shorterTitle = "{\"search\":[{\"title\":\"Cafe\",\"tempo\":113," +
                "\"artist\":[{\"name\":\"Lead Artist\"}]," +
                "\"album\":[{\"title\":\"Other\"},{\"title\":\"Record\"}]}," +
                "{\"title\":\"Cafe\",\"tempo\":118," +
                "\"artist\":{\"name\":\"Lead Artist\"}," +
                "\"album\":{\"title\":\"Another Record\"}}]}";
            if (GetSongBpmClient.MatchTempo(shorterTitle, "Café (feat. Guest)",
                    "Lead Artist feat. Guest", "Record") != 113 ||
                GetSongBpmClient.MatchTempo(shorterTitle, "Café - 2011 Remaster",
                    "Lead Artist; Guest", "Record") != 113 ||
                GetSongBpmClient.MatchTempo(shorterTitle, "Café",
                    "Lead Artist", "") != 0 ||
                GetSongBpmClient.MatchTempo(shorterTitle, "Café (Live)",
                    "Lead Artist", "") != 0 ||
                GetSongBpmClient.MatchTempo(shorterTitle, "Café (Remix)",
                    "Lead Artist", "") != 0 ||
                GetSongBpmClient.MatchTempo(shorterTitle, "Café feat. Guest - Live",
                    "Lead Artist", "") != 0 ||
                GetSongBpmClient.MatchTempo(shorterTitle, "Café (feat. Guest)",
                    "Another Artist feat. Guest", "") != 0)
                throw new Exception("Fallback matches must allow tag formatting without changing the recording or artist.");
            var otherFeature = "{\"search\":[{\"title\":\"Cafe (feat. Other)\"," +
                "\"tempo\":113,\"artist\":{\"name\":\"Lead Artist\"}}]}";
            if (GetSongBpmClient.MatchTempo(otherFeature, "Café (feat. Guest)",
                    "Lead Artist", "") != 0)
                throw new Exception("Different featured versions need review.");
            var searches = GetSongBpmClient.BuildQueries("Café (feat. Guest)",
                "Lead Artist feat. Guest");
            if (searches.Count != 3 || searches[0].Item1 != "both" ||
                searches[1].Item2 != "song:Café artist:Lead Artist" ||
                searches[2].Item1 != "song" || searches[2].Item2 != "Café")
                throw new Exception("Try a cleaned combined search before a broader title search.");
            var decoratedSearches = GetSongBpmClient.BuildQueries(
                "* ~Asterisk~", "ORANGE RANGE");
            if (decoratedSearches.Count != 3 ||
                decoratedSearches[1].Item2 != "song:Asterisk artist:ORANGE RANGE" ||
                decoratedSearches[2].Item2 != "Asterisk" ||
                GetSongBpmClient.BuildQueries("~dust bunnies~", "lando!")[1].Item2 !=
                    "song:dust bunnies artist:lando!")
                throw new Exception("Decorative title characters must not block catalog searches.");
            var ostSearches = GetSongBpmClient.BuildQueries(
                "Kamado Tanjirō no Uta -OST ver.-", "椎名豪 featuring 中川奈美");
            if (ostSearches.Count != 3 ||
                ostSearches[1].Item2 != "song:Kamado Tanjirō no Uta artist:椎名豪" ||
                GetSongBpmClient.MatchTempo(
                    "{\"search\":[{\"title\":\"Kamado Tanjirō no Uta\"," +
                    "\"tempo\":105,\"artist\":{\"name\":\"椎名豪\"}}]}",
                    "Kamado Tanjirō no Uta -OST ver.-",
                    "椎名豪 featuring 中川奈美", "") != 0)
                throw new Exception("Broad OST searches must still verify the recording version.");

            const string lrc = "[00:09.68] First line\n" +
                               "[00:17.30] \n" +
                               "[00:17.30] Second line\n" +
                               "[00:24.68] \n" +
                               "[00:31.38] Third line\n" +
                               "[00:40.00] Original\n" +
                               "[00:40.00] Translation";

            var lyrics = LyricParser.ParseLyric(lrc);
            if (lyrics == null || lyrics.Entries.Count != 4)
                throw new Exception("Empty timestamps must not become timed lyrics.");
            if (lyrics.Entries[1].LyricLine1 != " Second line" || lyrics.Entries[1].LyricLine2 != null)
                throw new Exception("An empty duplicate timestamp must not become a translation.");
            if (lyrics.Entries[2].LyricLine1 != " Third line")
                throw new Exception("An empty pause must not replace the active lyric.");
            if (lyrics.Entries[3].LyricLine2 != " Translation")
                throw new Exception("A real translation must remain on the second line.");
            var bilingual = new LyricsController(new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileUrl = () => "bilingual.mp3",
                NowPlaying_GetFileTag = field => "Y",
                Library_GetFileTag = (url, field) => lrc,
                Player_GetPosition = () => 41000
            });
            if (bilingual.UpdateLyrics(false)?.LyricLine2 != " Translation")
                throw new Exception("The translation must share its romaji timestamp.");
            bilingual.ShowTranslation = false;
            if (bilingual.UpdateLyrics(false)?.LyricLine2 != null)
                throw new Exception("The flyout toggle must hide only the translation.");
            var lyricArea = new RectangleF(45, 43, 480, 166);
            var withBoth = LyricCardLayout.Create(lyricArea, 50, 30, 6,
                true, true, 0);
            var onlyEnglish = LyricCardLayout.Create(lyricArea, 50, 30, 6,
                true, false, 0);
            var onlyPreview = LyricCardLayout.Create(lyricArea, 50, 30, 6,
                false, true, 0);
            if (withBoth.English.Bottom >= withBoth.Main.Top ||
                withBoth.Preview.Top <= withBoth.Main.Bottom ||
                withBoth.Main.Top != onlyEnglish.Main.Top ||
                withBoth.Main.Top != onlyPreview.Main.Top ||
                withBoth.Bounds.Top != withBoth.English.Top ||
                withBoth.Bounds.Bottom != withBoth.Preview.Bottom ||
                LyricCardLayout.RequiredHeight(50, 30, 6, true) > lyricArea.Height)
                throw new Exception("English must sit above a centred lyric with the next line below.");
            using (var canvas = new Bitmap(1100, 200))
            using (var graphics = Graphics.FromImage(canvas))
            using (var font = new Font("Arial", 26, FontStyle.Bold))
            {
                var english = "A cry reflected brightly in someone's eyes, feelings carried " +
                    "on the wind, wishes entrusted to the moon, living on with all " +
                    "the strength we have, even today";
                var longLine = LyricTextLayout.Fit(graphics, english, font,
                    26, 960, 72);
                var shortLine = LyricTextLayout.Fit(graphics, "The moon above",
                    font, 26, 960, 72);
                var cjk = "星が降る夜に君の声を思い出してもう一度遠い空を見上げた";
                var unspaced = LyricTextLayout.Fit(graphics, cjk, font, 28, 360, 82);
                var screenshotTranslation =
                    "Brain rot, higher and higher till I turn to ash, high, high, high, ah (ooh, ooh)";
                var crampedTranslation = LyricTextLayout.Fit(graphics,
                    screenshotTranslation, font, 19, 560, 38);
                var roomyTranslation = LyricTextLayout.Fit(graphics,
                    screenshotTranslation, font, 19, 560, 60);
                var shortPromotion = LyricTextLayout.CanPromotePreview(graphics,
                    "The moon above", font, 19, 30, 560, 60, 76);
                var longPromotion = LyricTextLayout.CanPromotePreview(graphics,
                    "I'd fly far into space as long as I am with you",
                    font, 19, 36, 450, 60, 90);
                if (longLine.Lines != 2 || !longLine.Text.Contains("\n") ||
                    longLine.Points < 12 || shortLine.Lines != 1 ||
                    unspaced.Lines != 2 ||
                    unspaced.Text.Replace("\n", "") != cjk ||
                    roomyTranslation.Lines != 2 ||
                    roomyTranslation.Points <= crampedTranslation.Points * 1.15f ||
                    !shortPromotion || longPromotion)
                    throw new Exception("Long lyrics should wrap within their row without shrinking short lines.");
                using (var format = new StringFormat(StringFormatFlags.NoWrap)
                       { Alignment = StringAlignment.Center,
                         LineAlignment = StringAlignment.Center })
                using (var path = new GraphicsPath())
                {
                    path.AddString(longLine.Text, font.FontFamily, (int)font.Style,
                        longLine.Points * graphics.DpiY / 72f,
                        new RectangleF(0, 0, 960, 72), format);
                    var bounds = path.GetBounds();
                    if (bounds.Width > 952 || bounds.Height > 70 ||
                        bounds.Height < longLine.Points * graphics.DpiY / 72f * 1.4f)
                        throw new Exception("Wrapped lyric outlines must fit their card row.");
                }
            }
            Console.WriteLine("LRC parser checks passed.");

            const string romajiLrc = "[00:00.00] ...\n" +
                "[00:09.00] The moon above\n" +
                "[00:12.00] An alley flickers\n" +
                "[00:16.00] The city sleeps";
            var romaji = LyricParser.ParseLyric(romajiLrc).Entries;
            var sequential = new EnglishImportDocument(romaji,
                "[Verse 1]\nMoonlight\nThe lights\nThe city is quiet", null);
            if (sequential.Rows[0].English != "" ||
                sequential.Rows[1].English != "Moonlight" ||
                sequential.Rows[3].English != "The city is quiet" ||
                sequential.EmptyCount != 0 || sequential.ExtraCount != 0)
                throw new Exception("English lines must skip instrumental placeholders.");
            var bridge = new EnglishImportDocument(romaji,
                "[Verse 1]\nMoonlight\nThe lights shine\nThe city is quiet",
                "[Verse 1]\nThe moon above\nAn alley\nflickers\nThe city sleeps");
            if (bridge.Rows[1].English != "Moonlight" ||
                bridge.Rows[2].English != "The lights shine" ||
                bridge.Rows[3].English != "The city is quiet" ||
                !bridge.Rows[2].Check)
                throw new Exception("Optional romaji must bridge different line breaks and flag uncertain pairs.");
            var oneMeaning = new EnglishImportDocument(romaji,
                "Moonlight\nThe city is quiet", null);
            oneMeaning.RepeatPrevious(2);
            if (oneMeaning.Rows[0].English != "" ||
                oneMeaning.Rows[1].English != "Moonlight" ||
                oneMeaning.Rows[2].English != "Moonlight" ||
                oneMeaning.Rows[3].English != "The city is quiet" ||
                oneMeaning.EmptyCount != 0)
                throw new Exception("Repeating a meaning must shift only active lyric rows.");
            var splitMeaning = new EnglishImportDocument(romaji,
                "Moonlight\nThrough the alley\nThe lights shine\nThe city is quiet", null);
            splitMeaning.JoinNext(1);
            if (splitMeaning.Rows[0].English != "" ||
                splitMeaning.Rows[1].English != "Moonlight / Through the alley" ||
                splitMeaning.Rows[2].English != "The lights shine" ||
                splitMeaning.Rows[3].English != "The city is quiet" ||
                splitMeaning.ExtraCount != 0)
                throw new Exception("Joining two meanings must use an extra line and skip placeholders.");

            var englishRoot = Path.Combine(Path.GetTempPath(), "LyricsEnglish-" + Guid.NewGuid());
            try
            {
                var store = new EnglishTranslationStore(englishRoot);
                store.Save("romaji.mp3", romaji, sequential.TranslationLines(),
                    "https://genius.com/example");
                var saved = store.Load("romaji.mp3", romaji);
                if (saved == null || saved[1] != "Moonlight" ||
                    store.Load("another.mp3", romaji) != null ||
                    store.LoadSourceUrl("romaji.mp3", romaji) !=
                        "https://genius.com/example")
                    throw new Exception("Saved English must belong only to its song.");
                var geniusQuery = FrmEnglishImport.BuildGeniusSearchUrl(
                    "Black Rover", "Vickeblanka");
                if (Uri.UnescapeDataString(new Uri(geniusQuery).Query.Substring(3)) !=
                    "Black Rover Vickeblanka")
                    throw new Exception("Genius search must use the song and artist without forcing a translation result.");
                var shifted = LyricParser.ParseLyric(romajiLrc.Replace("[00:09.00]",
                    "[00:10.00]")).Entries;
                if (store.Load("romaji.mp3", shifted)?[1] != "Moonlight")
                    throw new Exception("Changing only LRC timing must preserve saved English.");
                var revisedRomaji = LyricParser.ParseLyric(romajiLrc.Replace("moon", "sun")).Entries;
                if (store.Load("romaji.mp3", revisedRomaji) != null)
                    throw new Exception("Changed romaji must require a new English review.");

                var englishApi = new Plugin.MusicBeeApiInterface
                {
                    NowPlaying_GetFileUrl = () => "romaji.mp3",
                    NowPlaying_GetFileTag = field => "Y",
                    Library_GetFileTag = (url, field) => romajiLrc,
                    Player_GetPosition = () => 10000
                };
                var englishController = new LyricsController(englishApi, store);
                if (englishController.UpdateLyrics(false)?.LyricLine2 != "Moonlight")
                    throw new Exception("The saved English must follow the current timed lyric.");
                var replacement = new[] { "", "New meaning", "The lights", "The city" };
                store.Save("romaji.mp3", romaji, replacement, "");
                englishController.InvalidateImportedEnglish();
                if (englishController.UpdateLyrics(false)?.LyricLine2 != "New meaning")
                    throw new Exception("Saved English must refresh without switching songs.");
                englishController.ShowTranslation = false;
                if (englishController.UpdateLyrics(false)?.LyricLine2 != null)
                    throw new Exception("The flyout switch must hide imported English.");
                store.Delete("romaji.mp3");
                englishController.InvalidateImportedEnglish();
                englishController.ShowTranslation = true;
                if (englishController.UpdateLyrics(false)?.LyricLine2 != null)
                    throw new Exception("Removing saved English must clear the visible meaning.");

                const string mixed = "[00:01.00] Back up, get far of the sky, black rover\n" +
                    "[00:02.00] Nankai mo Play";
                var mixedEntries = LyricParser.ParseLyric(mixed).Entries;
                store.Save("mixed.mp3", mixedEntries,
                    new[] { "Back up, get far of the sky / Black rover",
                        "Play it again and again" }, "");
                var mixedPosition = 1500;
                var mixedApi = new Plugin.MusicBeeApiInterface
                {
                    NowPlaying_GetFileUrl = () => "mixed.mp3",
                    NowPlaying_GetFileTag = field => "Y",
                    Library_GetFileTag = (url, field) => mixed,
                    Player_GetPosition = () => mixedPosition
                };
                var mixedController = new LyricsController(mixedApi, store)
                {
                    NextLineWhenNoTranslation = true
                };
                var repeatedEnglish = mixedController.UpdateLyrics(false);
                if (repeatedEnglish?.LyricLine2 != null ||
                    repeatedEnglish.NextLine?.Trim() != "Nankai mo Play")
                    throw new Exception("An English chorus must not display itself twice or hide the next lyric.");
                store.Save("mixed.mp3", mixedEntries,
                    new[] { "Back up, gets far of the sky / Black rover",
                        "Play it again and again" }, "");
                mixedController.InvalidateImportedEnglish();
                if (mixedController.UpdateLyrics(false)?.LyricLine2 != null)
                    throw new Exception("A near-identical English line must not repeat the sung English.");
                store.Save("mixed.mp3", mixedEntries,
                    new[] { "A different meaning for the next day",
                        "Play it again and again" }, "");
                mixedController.InvalidateImportedEnglish();
                if (mixedController.UpdateLyrics(false)?.LyricLine2 !=
                    "A different meaning for the next day")
                    throw new Exception("A distinct long translation must remain visible.");
                mixedPosition = 2500;
                if (mixedController.UpdateLyrics(false)?.LyricLine2 !=
                    "Play it again and again")
                    throw new Exception("Meaning must remain visible under romaji with English loanwords.");
            }
            finally
            {
                if (Directory.Exists(englishRoot)) Directory.Delete(englishRoot, true);
            }
            Console.WriteLine("English alignment and storage checks passed.");

            const string editable = "[ti:Test]\r\n[offset:+150]\r\n" +
                "[00:52.27] Main\r\n[00:52.27] Translation\r\n" +
                "[00:52.27] \r\n[00:54.00][01:10.5] Repeated\r\n";
            LrcTimingDocument timing;
            if (!LrcTimingDocument.TryCreate(editable, out timing) ||
                timing.Entries.Count != 3 || timing.OffsetMs != 150 ||
                timing.BuildLyrics() != editable)
                throw new Exception("The timing editor must read the original LRC unchanged.");
            if (!timing.ShiftEntry(timing.Entries[0], 1000) ||
                timing.ShiftEntry(timing.Entries[0], 1000))
                throw new Exception("A fine-tuned line must not overtake the next line.");
            var changed = timing.BuildLyrics();
            if (!changed.Contains("[00:53.27] Main\r\n[00:53.27] Translation\r\n" +
                "[00:53.27] \r\n") || !changed.Contains("[ti:Test]\r\n[offset:+150]"))
                throw new Exception("Paired lyrics, gap markers and metadata must survive line edits.");
            if (timing.ShiftAll(100) != 100 ||
                !timing.BuildLyrics().Contains("[00:54.10][01:10.60] Repeated"))
                throw new Exception("Whole-song offsets must change every timestamp.");
            timing.Reset();
            if (timing.IsDirty || timing.BuildLyrics() != editable)
                throw new Exception("Cancel/reset must restore the original LRC exactly.");
            Console.WriteLine("Timing edit checks passed.");

            const string plainSource = "[ar:Someone]\r\nFirst line\r\n\r\nSecond line";
            UntimedTimingDocument plain;
            if (!UntimedTimingDocument.TryCreate(plainSource, out plain) ||
                plain.Entries.Count != 2 || plain.IsComplete ||
                UntimedTimingDocument.TryCreate(editable, out plain))
                throw new Exception("Creation mode must only accept untimed lyric lines.");
            UntimedTimingDocument.TryCreate(plainSource, out plain);
            if (!plain.Stamp(0, 1000) || plain.Stamp(1, 900) ||
                !plain.Stamp(1, 2200) || !plain.Adjust(1, -100) ||
                !plain.IsComplete || plain.ShiftAll(100) != 100 ||
                plain.BuildLyrics() != "[ar:Someone]\r\n[00:01.10] First line\r\n\r\n[00:02.20] Second line")
                throw new Exception("New timings must be ordered and preserve plain lyrics and spacing.");
            if (LyricParser.ParseLyric(plain.BuildLyrics())?.Entries.Count != 2)
                throw new Exception("The completed document must be valid timed lyrics.");
            plain.Reset();
            if (plain.IsDirty || plain.IsComplete || plain.BuildLyrics() != plainSource)
                throw new Exception("Reset must leave the original plain lyrics intact.");
            Console.WriteLine("Untimed lyric creation checks passed.");

            var backupRoot = Path.Combine(Path.GetTempPath(), "LyricsTiming-" + Guid.NewGuid());
            try
            {
                var storedLyrics = editable;
                var panelRefreshes = 0;
                var api = new Plugin.MusicBeeApiInterface
                {
                    Setting_GetPersistentStoragePath = () => backupRoot,
                    Library_GetFileTag = (url, field) => storedLyrics,
                    Library_SetFileTag = (url, field, value) =>
                    {
                        if (url != "track.mp3" || field != Plugin.MetaDataType.Lyrics)
                            throw new Exception("Timing edits must target this song's Lyrics field.");
                        storedLyrics = value;
                        return true;
                    },
                    Library_CommitTagsToFile = url => true,
                    MB_RefreshPanels = () => panelRefreshes++
                };
                string saveError;
                var edited = editable.Replace("[00:52.27]", "[00:53.27]");
                if (!TimingTagStore.Save(api, "track.mp3", editable, editable,
                    edited, out saveError) || storedLyrics != edited || panelRefreshes != 1)
                    throw new Exception("Saving must update MusicBee's Lyrics field and its panels: " + saveError);
                var backups = Directory.GetFiles(Path.Combine(backupRoot,
                    "DesktopLyrics-TimingBackups"), "*.lrc");
                if (backups.Length != 1 || File.ReadAllText(backups[0]) != editable)
                    throw new Exception("Saving must retain an exact backup of the old lyrics.");
                if (TimingTagStore.Save(api, "track.mp3", editable, editable,
                    "unexpected", out saveError) || storedLyrics != edited)
                    throw new Exception("A stale editor must not overwrite updated lyrics.");
                api.Library_CommitTagsToFile = url => false;
                if (TimingTagStore.Save(api, "track.mp3", edited, edited,
                    "another change", out saveError) || storedLyrics != edited)
                    throw new Exception("Failed file writes must restore the old in-memory lyrics.");

                var seekPosition = -1;
                var resumeCalls = 0;
                var previewCalls = 0;
                api.NowPlaying_GetFileUrl = () => "track.mp3";
                api.Player_GetPlayState = () => Plugin.PlayState.Paused;
                api.Player_SetPosition = position => { seekPosition = position; return true; };
                api.Player_PlayPause = () => { resumeCalls++; return true; };
                using (var editor = new FrmTimingEditor(api, "track.mp3", "Test song",
                    editable, timing, (url, text) => previewCalls++, url => { },
                    (url, text) => { }))
                {
                    var grid = FindGrid(editor.Controls);
                    if (grid == null || grid.Rows.Count != timing.Entries.Count)
                        throw new Exception("The timing mode must display every timestamped row.");
                    var click = typeof(FrmTimingEditor).GetMethod("LyricClicked",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    click.Invoke(editor, new object[] { grid,
                        new DataGridViewCellEventArgs(1, 0) });
                    if (seekPosition != 51320 || resumeCalls != 1 ||
                        previewCalls != 0 || timing.IsDirty)
                        throw new Exception("Clicking a lyric must seek with a short lead-in without editing it.");
                }
            }
            finally
            {
                if (Directory.Exists(backupRoot)) Directory.Delete(backupRoot, true);
            }
            Console.WriteLine("Lyrics tag save checks passed.");

            var noTrack = new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileTag = field => null,
                NowPlaying_GetLyrics = () => null
            };
            if (new LyricsController(noTrack).UpdateLyrics(false) != null)
                throw new Exception("No current track must not produce lyrics.");

            var currentTrack = "track.mp3";
            string currentLyricsTag = null;
            var playback = new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileUrl = () => currentTrack,
                NowPlaying_GetFileTag = field => "Y",
                NowPlaying_GetLyrics = () => "[00:52.27] Original\n[00:54.00] Later",
                Library_GetFileTag = (url, field) => url == "track.mp3" ?
                    currentLyricsTag : null,
                Player_GetPosition = () => 53500
            };
            var controller = new LyricsController(playback);
            controller.PreviewLyrics(currentTrack,
                "[00:53.27] Adjusted\n[00:54.00] Later");
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Adjusted")
                throw new Exception("Unsaved timing changes must preview in the lyric window.");
            controller.CancelPreview(currentTrack);
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Original")
                throw new Exception("Cancel must restore MusicBee's original timings.");
            currentLyricsTag = "[00:52.27] Original\n[00:54.00] Later";
            controller.KeepSavedLyrics(currentTrack,
                "[00:53.27] Adjusted\n[00:54.00] Later");
            controller.CancelPreview(currentTrack);
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Adjusted")
                throw new Exception("Saved timings must remain visible if MusicBee caches old lyrics.");
            currentLyricsTag = "[00:53.27] Adjusted\n[00:54.00] Later";
            controller.InvalidateTag();
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Adjusted")
                throw new Exception("The newly saved tag must confirm the live lyrics.");
            currentLyricsTag = "[00:53.27] New tag\n[00:54.00] Later";
            controller.InvalidateTag();
            if (controller.UpdateLyrics(false)?.LyricLine1 != " New tag")
                throw new Exception("A newly saved MusicBee Lyrics tag must appear without switching songs.");
            currentTrack = "different.mp3";
            if (controller.UpdateLyrics(false)?.LyricLine1 != " Original")
                throw new Exception("Saved lyric preview must not leak to a different song.");
            Console.WriteLine("Live timing preview checks passed.");

            var nextCalls = 0;
            var atEnd = new Plugin.MusicBeeApiInterface
            {
                Player_GetButtonEnabled = button => true,
                NowPlayingList_IsAnyFollowingTracks = () => false,
                Player_GetRepeat = () => Plugin.RepeatMode.None,
                Player_GetAutoDjEnabled = () => false,
                Player_PlayNextTrack = () => { nextCalls++; return true; }
            };
            if (QueueNavigation.TryPlayNext(atEnd) || QueueNavigation.TryPlayNext(atEnd) ||
                nextCalls != 0)
                throw new Exception("Repeated Next at the end must not call MusicBee.");
            atEnd.NowPlayingList_IsAnyFollowingTracks = () => true;
            if (!QueueNavigation.TryPlayNext(atEnd) || nextCalls != 1)
                throw new Exception("Next must still advance when tracks remain.");
            atEnd.Player_GetButtonEnabled = button => false;
            if (QueueNavigation.TryPlayNext(atEnd) || nextCalls != 1)
                throw new Exception("A disabled Next button must not advance.");
            Console.WriteLine("End-of-queue checks passed.");

            var upcoming = new Plugin.MusicBeeApiInterface
            {
                NowPlayingList_GetCurrentIndex = () => 5,
                NowPlayingList_GetNextIndex = offset =>
                    offset == 1 ? 10 : offset == 2 ? 7 : -1,
                NowPlayingList_GetFileTag = (index, field) =>
                    field == Plugin.MetaDataType.Artist ? "Artist " + index :
                    index == 10 ? "Queued first" : null,
                NowPlayingList_GetListFileUrl = index => index == 10 ?
                    @"C:\Music\Queued first.mp3" : @"C:\Music\Other title.mp3"
            };
            var queue = UpcomingQueue.Read(upcoming);
            if (queue.Count != 2 || queue[0].Title != "Queued first" ||
                queue[1].Title != "Other title" || queue[1].Artist != "Artist 7")
                throw new Exception("The queue must use playback order and fall back to a file name.");
            upcoming.NowPlayingList_GetNextIndex = offset => offset == 1 ? 5 : 7;
            if (UpcomingQueue.Read(upcoming).Count != 0)
                throw new Exception("The current track must not appear in the upcoming queue.");
            upcoming.NowPlayingList_GetNextIndex = offset =>
                offset == 1 ? 10 : offset == 2 ? 7 : -1;
            var page = UpcomingQueue.Read(upcoming, 2, 1);
            if (page.Count != 1 || page[0].Index != 7 || page[0].Offset != 2)
                throw new Exception("Scrolling must fetch the correct queue position.");
            var played = "";
            upcoming.NowPlaying_GetFileUrl = () => @"C:\Music\Current.mp3";
            upcoming.NowPlayingList_PlayNow = url => { played = url; return true; };
            string queueError;
            if (!QueueNavigation.TryPlayQueuedTrack(upcoming, page[0], out queueError) ||
                played != page[0].FileUrl)
                throw new Exception("Clicking a queue song must play that file: " + queueError);
            upcoming.NowPlayingList_GetNextIndex = offset =>
                offset == 1 ? 10 : offset == 2 ? 9 : -1;
            if (QueueNavigation.TryPlayQueuedTrack(upcoming, page[0], out queueError))
                throw new Exception("A stale queue row must not play a different song.");
            upcoming.NowPlayingList_GetNextIndex = offset =>
                offset == 1 ? 10 : offset == 2 ? 7 : -1;
            upcoming.NowPlayingList_GetListFileUrl = index => @"C:\Music\Repeat.mp3";
            var repeated = UpcomingQueue.Read(upcoming, 2, 1)[0];
            if (QueueNavigation.TryPlayQueuedTrack(upcoming, repeated, out queueError) ||
                !queueError.Contains("duplicate"))
                throw new Exception("MusicBee cannot address the second copy of a queued file.");
            Console.WriteLine("Upcoming queue checks passed.");

            var history = new PlaybackHistory();
            history.Observe("first.mp3", "First", "Artist");
            history.Observe("second.mp3", "Second", "Artist");
            history.Observe("third.mp3", "Third", "Artist");
            var previous = history.Snapshot();
            if (previous.Count != 3 || previous[0].Offset != -2 ||
                previous[1].Offset != -1 || previous[2].Offset != 0 ||
                history.Observe("third.mp3", "Third", "Artist"))
                throw new Exception("The queue must retain play history around the current track.");
            upcoming.NowPlaying_GetFileUrl = () => "third.mp3";
            if (!QueueNavigation.TryPlayQueuedTrack(upcoming, previous[0], out queueError) ||
                played != "first.mp3" ||
                QueueNavigation.TryPlayQueuedTrack(upcoming, previous[2], out queueError))
                throw new Exception("A played track must be clickable while current is not replayed.");
            Console.WriteLine("Queue history checks passed.");

            history.Observe("first.mp3", "First", "Artist");
            var timeline = history.Timeline(new[]
            {
                new UpcomingQueue.Track { FileUrl = "second.mp3", Offset = 1 },
                new UpcomingQueue.Track { FileUrl = "second.mp3", Offset = 2 },
                new UpcomingQueue.Track { FileUrl = "fourth.mp3", Offset = 3 }
            });
            if (timeline.Count != 4 || timeline[0].FileUrl != "third.mp3" ||
                timeline[1].FileUrl != "first.mp3" ||
                timeline[2].FileUrl != "second.mp3" || timeline[2].Offset != 1 ||
                timeline[3].FileUrl != "fourth.mp3")
                throw new Exception("Clicking around must not duplicate played or upcoming songs.");

            var playingUrls = new[] { "first.mp3", "fourth.mp3" };
            upcoming.NowPlayingList_QueryFilesEx = (string query, out string[] files) =>
            {
                files = playingUrls;
                return true;
            };
            history.RetainPlayingList(UpcomingQueue.ReadPlayingListUrls(upcoming));
            var revised = history.Timeline(new[]
            {
                new UpcomingQueue.Track { FileUrl = "fourth.mp3", Offset = 1 }
            });
            if (revised.Count != 2 || revised[0].FileUrl != "first.mp3" ||
                revised[1].FileUrl != "fourth.mp3")
                throw new Exception("Removed MusicBee tracks must leave the plugin history.");
            upcoming.NowPlayingList_QueryFilesEx = (string query, out string[] files) =>
            {
                files = null;
                return false;
            };
            history.RetainPlayingList(UpcomingQueue.ReadPlayingListUrls(upcoming));
            if (history.Snapshot().Count != 1)
                throw new Exception("A failed MusicBee query must not discard history.");
            playingUrls = new string[0];
            upcoming.NowPlayingList_QueryFilesEx = (string query, out string[] files) =>
            {
                files = playingUrls;
                return true;
            };
            history.RetainPlayingList(UpcomingQueue.ReadPlayingListUrls(upcoming));
            if (history.Snapshot().Count != 0)
                throw new Exception("Clearing the Playing Tracks list must clear old history and current track.");

            var searchJson = "[{\"id\":1,\"trackName\":\"Anytime Anywhere\",\"artistName\":\"milet\",\"duration\":50," +
                "\"syncedLyrics\":\"[00:01.00] Short\"},{\"id\":2,\"trackName\":\"Anytime Anywhere\"," +
                "\"artistName\":\"milet\",\"duration\":230,\"syncedLyrics\":\"[00:01.00] Correct\"}," +
                "{\"id\":3,\"duration\":225,\"plainLyrics\":\"No timings\"}]";
            var records = LrcLibClient.SortByDuration(LrcLibClient.ParseResults(searchJson), 230000);
            if (records.Count != 3 || records[0].Id != 2 || !records[0].HasTimedLyrics ||
                records[1].HasTimedLyrics || LrcLibClient.RetryDelay("12").TotalSeconds != 12)
                throw new Exception("LRCLIB results must show the matching timed version first and honor Retry-After.");
            var missingDurationJson = "[{\"id\":4,\"trackName\":\"Boku no Sensou\"," +
                "\"duration\":null,\"syncedLyrics\":\"[00:01.00] Missing length\"}," +
                "{\"id\":5,\"duration\":280,\"syncedLyrics\":\"[00:01.00] Known length\"}]";
            var nullableResults = LrcLibClient.SortByDuration(
                LrcLibClient.ParseResults(missingDurationJson), 280000);
            if (nullableResults.Count != 2 || nullableResults[0].Id != 5 ||
                nullableResults[1].Id != 4 || nullableResults[1].DurationSeconds != 0 ||
                !nullableResults[1].HasTimedLyrics)
                throw new Exception("LRCLIB must keep usable results when a record has null duration.");
            var importedTag = "[00:01.00] Previous";
            var importApi = new Plugin.MusicBeeApiInterface
            {
                NowPlaying_GetFileUrl = () => "track.mp3",
                Library_GetFileTag = (url, field) => importedTag,
                Library_SetFileTag = (url, field, text) =>
                {
                    if (field != Plugin.MetaDataType.Lyrics || url != "track.mp3")
                        throw new Exception("Import must only change this song's Lyrics field.");
                    importedTag = text;
                    return true;
                },
                Library_CommitTagsToFile = url => true,
                MB_RefreshPanels = () => { }
            };
            string importError;
            if (!ImportedLyricsTagStore.Save(importApi, "track.mp3", importedTag,
                    records[0].SyncedLyrics, out importError) ||
                importedTag != records[0].SyncedLyrics)
                throw new Exception("Timed lyrics must save in MusicBee's Lyrics field: " + importError);
            if (ImportedLyricsTagStore.Save(importApi, "track.mp3", "stale",
                    "[00:02.00] Wrong", out importError) ||
                importedTag != records[0].SyncedLyrics)
                throw new Exception("Import must not overwrite a tag changed since search opened.");
            if (!ImportedLyricsTagStore.Save(importApi, "track.mp3", importedTag,
                    records[1].PlainLyrics, out importError) ||
                importedTag != records[1].PlainLyrics)
                throw new Exception("Plain LRCLIB lyrics must be saved for later timing: " + importError);
            importApi.Library_CommitTagsToFile = url => false;
            if (ImportedLyricsTagStore.Save(importApi, "track.mp3", importedTag,
                    "[00:02.00] Failed", out importError) ||
                importedTag != records[1].PlainLyrics)
                throw new Exception("A failed MusicBee commit must restore the old Lyrics field.");
            using (var picker = new FrmLrcLibPicker(importApi, "track.mp3",
                "Anytime Anywhere", "milet", 230000, importedTag, (url, text) => { }))
            {
                if (picker.TrackUrl != "track.mp3")
                    throw new Exception("The LRCLIB picker must remain tied to the selected song.");
            }
            Console.WriteLine("LRCLIB import checks passed.");

            var imagePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            try
            {
                using (var image = new Bitmap(48, 48))
                {
                    using (var graphics = Graphics.FromImage(image))
                    {
                        graphics.Clear(Color.FromArgb(210, 45, 55));
                        graphics.FillRectangle(Brushes.RoyalBlue, 36, 0, 12, 48);
                    }
                    image.Save(imagePath, ImageFormat.Png);
                }
                ArtworkPalette palette;
                if (!ArtworkPalette.TryLoad(imagePath, out palette))
                    throw new Exception("Album artwork should produce a palette.");
                if (palette.Left.R <= palette.Left.B || palette.Right.B <= palette.Right.R)
                    throw new Exception("The palette should reflect both artwork colours.");
                var imageBytes = File.ReadAllBytes(imagePath);
                ArtworkPalette embeddedPalette;
                Bitmap cover;
                if (!ArtworkPalette.TryLoad(imageBytes, out embeddedPalette, out cover))
                    throw new Exception("Embedded artwork should produce a palette and cover.");
                using (cover)
                {
                    if (embeddedPalette.BarTop.ToArgb() != palette.BarTop.ToArgb() ||
                        cover.Width != 256 || cover.Height != 256 ||
                        cover.GetPixel(16, 128).R <= cover.GetPixel(16, 128).B ||
                        cover.GetPixel(240, 128).B <= cover.GetPixel(240, 128).R)
                        throw new Exception("The displayed cover should match the artwork colours.");
                }
                ArtworkPalette urlPalette;
                if (!ArtworkPalette.TryLoad(new Uri(imagePath).AbsoluteUri, out urlPalette) ||
                    urlPalette.BarBottom.ToArgb() != palette.BarBottom.ToArgb())
                    throw new Exception("File URLs should resolve to the artwork.");
                ArtworkPalette encodedPalette;
                if (!ArtworkPalette.TryLoad(Convert.ToBase64String(imageBytes), out encodedPalette) ||
                    encodedPalette.Left.ToArgb() != palette.Left.ToArgb())
                    throw new Exception("Encoded artwork should produce the same palette.");

                File.Delete(imagePath);
                using (var colourful = new Bitmap(48, 48))
                {
                    using (var graphics = Graphics.FromImage(colourful))
                    {
                        graphics.Clear(Color.FromArgb(210, 45, 55));
                        graphics.FillRectangle(Brushes.RoyalBlue, 26, 0, 14, 48);
                        graphics.FillRectangle(Brushes.LimeGreen, 40, 0, 8, 48);
                    }
                    colourful.Save(imagePath, ImageFormat.Png);
                }
                ArtworkPalette colourfulPalette;
                if (!ArtworkPalette.TryLoad(imagePath, out colourfulPalette) ||
                    colourfulPalette.Accent.G <= colourfulPalette.Accent.R ||
                    colourfulPalette.Accent.G <= colourfulPalette.Accent.B)
                    throw new Exception("A third artwork hue should colour the background lights.");

                File.Delete(imagePath);
                using (var neutralImage = new Bitmap(48, 48))
                {
                    using (var graphics = Graphics.FromImage(neutralImage))
                        graphics.Clear(Color.White);
                    neutralImage.Save(imagePath, ImageFormat.Png);
                }
                ArtworkPalette neutralPalette;
                if (!ArtworkPalette.TryLoad(imagePath, out neutralPalette) ||
                    neutralPalette.BarTop.R != neutralPalette.BarTop.G)
                    throw new Exception("Monochrome artwork should produce a neutral palette.");
            }
            finally
            {
                File.Delete(imagePath);
            }
            Console.WriteLine("Artwork palette checks passed.");
        }

        private static DataGridView FindGrid(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                var grid = control as DataGridView;
                if (grid != null) return grid;
                grid = FindGrid(control.Controls);
                if (grid != null) return grid;
            }
            return null;
        }
    }
}
