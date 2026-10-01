using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    // Matches pasted English to the existing timed lyric rows. Optional
    // romanized Genius text acts as a bridge when its line breaks differ.
    internal sealed class EnglishImportDocument
    {
        internal sealed class Row
        {
            public int OriginalIndex;
            public double TimeMs;
            public string Romaji;
            public string English = "";
            public bool Check;
        }

        private sealed class Section
        {
            public string Heading;
            public readonly List<string> Lines = new List<string>();
        }

        private struct Step
        {
            public int Timed, Source;
            public int PreviousTimed, PreviousSource;
        }

        private static readonly Regex Timestamp = new Regex(
            @"^\[\d+:[0-5]\d(?:\.\d{1,3})?\]\s*", RegexOptions.Compiled);
        private readonly List<string> _extra = new List<string>();
        public readonly List<Row> Rows = new List<Row>();
        public string Note { get; private set; }
        public int ExtraCount => _extra.Count;
        public int EmptyCount => Rows.Count(row => !IsPlaceholder(row.Romaji) &&
                                                  string.IsNullOrWhiteSpace(row.English));

        public EnglishImportDocument(IList<LyricParser.LyricEntry> lyrics,
            string englishText, string geniusRomajiText)
        {
            for (var i = 0; i < lyrics.Count; i++)
                Rows.Add(new Row { OriginalIndex = i, TimeMs = lyrics[i].TimeMs,
                    Romaji = (lyrics[i].LyricLine1 ?? "").Trim() });

            var english = Parse(englishText);
            var active = Rows.Where(row => !IsPlaceholder(row.Romaji)).ToList();
            var pastedRomaji = Parse(geniusRomajiText);
            if (pastedRomaji.Count > 0 &&
                TryAlignThroughRomaji(active, pastedRomaji, english)) return;

            var lines = english.SelectMany(section => section.Lines).ToList();
            for (var i = 0; i < active.Count && i < lines.Count; i++)
                active[i].English = lines[i];
            for (var i = active.Count; i < lines.Count; i++) _extra.Add(lines[i]);
            Note = pastedRomaji.Count > 0
                ? "The pasted romaji did not match MusicBee closely enough. Check the rows before saving."
                : lines.Count == active.Count
                    ? "Line counts match. Check the pairs, especially around section breaks."
                    : "Line counts differ. Use Repeat previous or Join next where line breaks differ.";
        }

        public IList<string> TranslationLines()
        {
            return Rows.Select(row => (row.English ?? "").Trim()).ToList();
        }

        public void RepeatPrevious(int index)
        {
            var active = Rows.Where(row => !IsPlaceholder(row.Romaji)).ToList();
            var position = active.FindIndex(row => row.OriginalIndex == index);
            if (position <= 0) return;
            var displaced = active[active.Count - 1].English;
            for (var i = active.Count - 1; i > position; i--)
                active[i].English = active[i - 1].English;
            active[position].English = active[position - 1].English;
            Rows[index].Check = false;
            if (!string.IsNullOrWhiteSpace(displaced)) _extra.Insert(0, displaced);
        }

        public void JoinNext(int index)
        {
            var active = Rows.Where(row => !IsPlaceholder(row.Romaji)).ToList();
            var position = active.FindIndex(row => row.OriginalIndex == index);
            if (position < 0 || position + 1 >= active.Count) return;
            var first = active[position].English ?? "";
            var second = active[position + 1].English ?? "";
            active[position].English = string.IsNullOrWhiteSpace(first) ? second :
                string.IsNullOrWhiteSpace(second) ? first : first + " / " + second;
            Rows[index].Check = false;
            for (var i = position + 1; i + 1 < active.Count; i++)
                active[i].English = active[i + 1].English;
            active[active.Count - 1].English = _extra.Count > 0 ? _extra[0] : "";
            if (_extra.Count > 0) _extra.RemoveAt(0);
        }

        public void SetEnglish(int index, string text)
        {
            if (index < 0 || index >= Rows.Count) return;
            Rows[index].English = text ?? "";
            Rows[index].Check = false;
        }

        public static bool IsPlaceholder(string text)
        {
            var trimmed = (text ?? "").Trim();
            return trimmed.Length == 0 ||
                trimmed.Trim('.', '…', '♪', '♫', '-', ' ', '　').Length == 0;
        }

        private bool TryAlignThroughRomaji(List<Row> active,
            IList<Section> sourceSections, IList<Section> englishSections)
        {
            var source = sourceSections.SelectMany(section => section.Lines).ToList();
            if (source.Count == 0 || englishSections.Count == 0 || active.Count == 0)
                return false;
            List<int>[] matched;
            if (!MatchRomaji(active, source, out matched)) return false;

            var meanings = new List<string>();
            var uncertain = new List<bool>();
            var matchingSections = sourceSections.Count == englishSections.Count &&
                sourceSections.Zip(englishSections, (a, b) =>
                    string.Equals(a.Heading, b.Heading,
                        StringComparison.OrdinalIgnoreCase)).All(equal => equal);
            if (matchingSections)
            {
                var start = 0;
                for (var i = 0; i < sourceSections.Count; i++)
                {
                    MapSection(sourceSections[i].Lines.Count,
                        englishSections[i].Lines, start, matched, meanings, uncertain);
                    start += sourceSections[i].Lines.Count;
                }
            }
            else
                MapSection(source.Count, englishSections.SelectMany(s => s.Lines).ToList(),
                    0, matched, meanings, uncertain);

            for (var i = 0; i < active.Count; i++)
            {
                if (matched[i] == null || matched[i].Count == 0)
                {
                    active[i].Check = true;
                    continue;
                }
                var distinct = new List<string>();
                foreach (var j in matched[i])
                {
                    if (j < 0 || j >= meanings.Count) continue;
                    var meaning = meanings[j];
                    if (!string.IsNullOrWhiteSpace(meaning) && !distinct.Contains(meaning))
                        distinct.Add(meaning);
                    active[i].Check |= uncertain[j];
                }
                active[i].English = string.Join(" / ", distinct);
            }
            Note = uncertain.Any(value => value) || active.Any(row => row.Check)
                ? "Romaji helped align the sections. Highlighted rows need a quick check."
                : "Genius romaji matched your timed lines. Check the pairs before saving.";
            return true;
        }

        private static void MapSection(int sourceCount, IList<string> english,
            int start, IList<List<int>> matched, List<string> meanings,
            List<bool> uncertain)
        {
            // When two Genius romaji rows match one timed line, and the
            // English has one line per timed phrase, use those matched groups.
            var groups = matched.Where(indices => indices != null)
                .Select(indices => indices.Where(index =>
                    index >= start && index < start + sourceCount).ToList())
                .Where(indices => indices.Count > 0).ToList();
            if (english.Count == groups.Count && english.Count != sourceCount &&
                groups.SelectMany(indices => indices).Distinct().Count() == sourceCount &&
                groups.Sum(indices => indices.Count) == sourceCount)
            {
                var bySource = new string[sourceCount];
                for (var i = 0; i < groups.Count; i++)
                    foreach (var index in groups[i]) bySource[index - start] = english[i];
                for (var i = 0; i < sourceCount; i++)
                {
                    meanings.Add(bySource[i]);
                    uncertain.Add(true);
                }
                return;
            }
            for (var i = 0; i < sourceCount; i++)
            {
                // A translation sometimes combines the final two romanized
                // rows of a verse. Keep that English visible for both rows.
                var index = Math.Min(i, english.Count - 1);
                meanings.Add(index < 0 ? "" : english[index]);
                uncertain.Add(sourceCount != english.Count);
            }
            if (english.Count > sourceCount && sourceCount > 0)
            {
                var extra = english.Skip(sourceCount);
                meanings[meanings.Count - 1] += " / " + string.Join(" / ", extra);
            }
        }

        private static bool MatchRomaji(IList<Row> timed, IList<string> source,
            out List<int>[] matched)
        {
            matched = new List<int>[timed.Count];
            var n = timed.Count;
            var m = source.Count;
            if (n > 400 || m > 400) return false;
            var a = timed.Select(row => Normalize(row.Romaji)).ToArray();
            var b = source.Select(Normalize).ToArray();
            var score = new double[n + 1, m + 1];
            var previous = new Step[n + 1, m + 1];
            for (var i = 0; i <= n; i++)
                for (var j = 0; j <= m; j++) score[i, j] = double.PositiveInfinity;
            score[0, 0] = 0;
            for (var i = 0; i <= n; i++)
                for (var j = 0; j <= m; j++)
                {
                    var current = score[i, j];
                    if (double.IsInfinity(current)) continue;
                    if (i < n && j < m)
                        Update(i + 1, j + 1, current + Distance(a[i], b[j]),
                            i, j, 1, 1, score, previous);
                    if (i < n && j + 1 < m)
                        Update(i + 1, j + 2,
                            current + Distance(a[i], b[j] + b[j + 1]) + 0.12,
                            i, j, 1, 2, score, previous);
                    if (i + 1 < n && j < m)
                        Update(i + 2, j + 1,
                            current + Distance(a[i] + a[i + 1], b[j]) + 0.12,
                            i, j, 2, 1, score, previous);
                    if (i < n) Update(i + 1, j, current + 1.05,
                        i, j, 1, 0, score, previous);
                    if (j < m) Update(i, j + 1, current + 1.05,
                        i, j, 0, 1, score, previous);
                }
            var ti = n;
            var sj = m;
            var covered = 0;
            var similarity = 0.0;
            while (ti > 0 || sj > 0)
            {
                var step = previous[ti, sj];
                if (step.Timed > 0 && step.Source > 0)
                {
                    var timedText = string.Concat(a.Skip(step.PreviousTimed).Take(step.Timed));
                    var sourceText = string.Concat(b.Skip(step.PreviousSource).Take(step.Source));
                    similarity += 1 - Distance(timedText, sourceText);
                    covered += step.Timed;
                    for (var x = step.PreviousTimed; x < ti; x++)
                    {
                        matched[x] = new List<int>();
                        for (var y = step.PreviousSource; y < sj; y++) matched[x].Add(y);
                    }
                }
                if (step.PreviousTimed == ti && step.PreviousSource == sj) return false;
                ti = step.PreviousTimed;
                sj = step.PreviousSource;
            }
            return covered >= Math.Min(5, n) && covered >= n * 0.75 &&
                   similarity / Math.Max(1, covered) >= 0.55;
        }

        private static void Update(int i, int j, double value, int oldI, int oldJ,
            int timed, int source, double[,] scores, Step[,] previous)
        {
            if (value >= scores[i, j]) return;
            scores[i, j] = value;
            previous[i, j] = new Step { PreviousTimed = oldI, PreviousSource = oldJ,
                Timed = timed, Source = source };
        }

        private static double Distance(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0 || b.Length == 0) return 1;
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) previous[j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (var j = 1; j <= b.Length; j++)
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1,
                        previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                var swap = previous;
                previous = current;
                current = swap;
            }
            return (double)previous[b.Length] / Math.Max(a.Length, b.Length);
        }

        private static string Normalize(string text)
        {
            var result = new StringBuilder();
            foreach (var c in (text ?? "").ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) result.Append(c);
            return result.ToString();
        }

        private static List<Section> Parse(string text)
        {
            var result = new List<Section>();
            var current = new Section { Heading = "" };
            foreach (var raw in (text ?? "").Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                var line = Timestamp.Replace(raw.Trim(), "").Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("[") && line.EndsWith("]") &&
                    !Timestamp.IsMatch(line))
                {
                    if (current.Lines.Count > 0) result.Add(current);
                    current = new Section { Heading = line.ToLowerInvariant() };
                    continue;
                }
                current.Lines.Add(line);
            }
            if (current.Lines.Count > 0) result.Add(current);
            return result;
        }
    }
}
