using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    // Edits timestamp tokens in the source LRC. Rebuilding from LyricParser's
    // display entries would lose metadata, empty timing markers and line layout.
    internal sealed class LrcTimingDocument
    {
        private static readonly Regex TimedPrefix = new Regex(
            @"(?m)^[ \t]*(?:\[\d+:[0-5]\d(?:\.\d{1,3})?\][ \t]*)+",
            RegexOptions.Compiled);
        private static readonly Regex Timestamp = new Regex(
            @"\[(?<minutes>\d+):(?<seconds>[0-5]\d)(?:\.(?<fraction>\d{1,3}))?\]",
            RegexOptions.Compiled);
        private static readonly Regex Offset = new Regex(
            @"(?im)^[ \t]*\[offset:\s*([+-]?\d+)\s*\]",
            RegexOptions.Compiled);

        private readonly string _source;
        private readonly List<TimingToken> _tokens = new List<TimingToken>();
        private readonly List<TimingEntry> _entries = new List<TimingEntry>();

        public string OriginalLyrics => _source;
        public IList<TimingEntry> Entries => _entries.AsReadOnly();
        public int OffsetMs { get; private set; }
        public bool IsDirty
        {
            get
            {
                foreach (var entry in _entries)
                    if (entry.TimeMs != entry.OriginalTimeMs) return true;
                return false;
            }
        }

        private LrcTimingDocument(string source)
        {
            _source = source;
        }

        public static bool TryCreate(string source, out LrcTimingDocument document)
        {
            document = null;
            if (string.IsNullOrWhiteSpace(source)) return false;
            var parsed = new LrcTimingDocument(source);
            var offset = Offset.Match(source);
            if (offset.Success)
            {
                int milliseconds;
                if (int.TryParse(offset.Groups[1].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out milliseconds))
                    parsed.OffsetMs = milliseconds;
            }

            var byTime = new Dictionary<int, TimingEntry>();
            foreach (Match prefix in TimedPrefix.Matches(source))
            {
                var lyricStart = prefix.Index + prefix.Length;
                var lyricEnd = source.IndexOfAny(new[] { '\r', '\n' }, lyricStart);
                if (lyricEnd < 0) lyricEnd = source.Length;
                var text = source.Substring(lyricStart, lyricEnd - lyricStart).Trim();
                foreach (Match timestamp in Timestamp.Matches(prefix.Value))
                {
                    int minutes, seconds;
                    if (!int.TryParse(timestamp.Groups["minutes"].Value, out minutes) ||
                        !int.TryParse(timestamp.Groups["seconds"].Value, out seconds) ||
                        minutes > (int.MaxValue - 59999) / 60000) continue;
                    var fraction = timestamp.Groups["fraction"].Value;
                    var millis = fraction.Length == 0 ? 0 :
                        int.Parse(fraction, CultureInfo.InvariantCulture) *
                        (fraction.Length == 1 ? 100 : fraction.Length == 2 ? 10 : 1);
                    var time = minutes * 60000 + seconds * 1000 + millis;
                    TimingEntry entry;
                    if (!byTime.TryGetValue(time, out entry))
                    {
                        entry = new TimingEntry(time);
                        byTime.Add(time, entry);
                        parsed._entries.Add(entry);
                    }
                    entry.AddText(text);
                    parsed._tokens.Add(new TimingToken(prefix.Index + timestamp.Index,
                        timestamp.Length, timestamp.Groups["minutes"].Length,
                        fraction.Length, entry));
                }
            }
            if (parsed._tokens.Count == 0) return false;
            parsed._entries.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));
            document = parsed;
            return true;
        }

        public bool ShiftEntry(TimingEntry entry, int milliseconds)
        {
            var index = _entries.IndexOf(entry);
            if (index < 0 || milliseconds == 0) return false;
            var target = (long)entry.TimeMs + milliseconds;
            if (target < 0 || target > int.MaxValue ||
                index > 0 && target <= _entries[index - 1].TimeMs ||
                index + 1 < _entries.Count && target >= _entries[index + 1].TimeMs)
                return false;
            entry.TimeMs = (int)target;
            return true;
        }

        public int ShiftAll(int milliseconds)
        {
            if (milliseconds == 0) return 0;
            var min = _entries[0].TimeMs;
            var max = _entries[_entries.Count - 1].TimeMs;
            var actual = (int)Math.Max(-min, Math.Min((long)milliseconds,
                (long)int.MaxValue - max));
            foreach (var entry in _entries) entry.TimeMs += actual;
            return actual;
        }

        public void Reset()
        {
            foreach (var entry in _entries) entry.TimeMs = entry.OriginalTimeMs;
        }

        public string BuildLyrics()
        {
            if (!IsDirty) return _source;
            var result = new StringBuilder(_source);
            for (var i = _tokens.Count - 1; i >= 0; i--)
            {
                var token = _tokens[i];
                if (token.Entry.TimeMs == token.Entry.OriginalTimeMs) continue;
                result.Remove(token.Index, token.Length);
                result.Insert(token.Index, FormatToken(token.Entry.TimeMs,
                    token.MinuteDigits, token.FractionDigits));
            }
            return result.ToString();
        }

        public static string FormatTime(int milliseconds)
        {
            var fraction = milliseconds % 10 == 0 ?
                (milliseconds % 1000 / 10).ToString("D2", CultureInfo.InvariantCulture) :
                (milliseconds % 1000).ToString("D3", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}.{2}",
                milliseconds / 60000, (milliseconds / 1000) % 60, fraction);
        }

        private static string FormatToken(int time, int minuteDigits, int fractionDigits)
        {
            var millis = time % 1000;
            var required = millis == 0 ? 0 : millis % 100 == 0 ? 1 :
                millis % 10 == 0 ? 2 : 3;
            var digits = Math.Max(fractionDigits, required == 0 ? 0 : Math.Max(2, required));
            var fraction = digits == 0 ? "" : "." +
                (millis / (digits == 1 ? 100 : digits == 2 ? 10 : 1))
                .ToString("D" + digits, CultureInfo.InvariantCulture);
            return "[" + (time / 60000).ToString("D" + Math.Max(2, minuteDigits),
                CultureInfo.InvariantCulture) + ":" +
                ((time / 1000) % 60).ToString("D2", CultureInfo.InvariantCulture) +
                fraction + "]";
        }

        private sealed class TimingToken
        {
            public readonly int Index, Length, MinuteDigits, FractionDigits;
            public readonly TimingEntry Entry;

            public TimingToken(int index, int length, int minuteDigits,
                int fractionDigits, TimingEntry entry)
            {
                Index = index;
                Length = length;
                MinuteDigits = minuteDigits;
                FractionDigits = fractionDigits;
                Entry = entry;
            }
        }

        public sealed class TimingEntry
        {
            private readonly List<string> _texts = new List<string>();
            public int OriginalTimeMs { get; private set; }
            public int TimeMs { get; internal set; }
            public string Text => _texts.Count == 0 ? "(instrumental / pause)" :
                string.Join("  /  ", _texts);

            internal TimingEntry(int time)
            {
                OriginalTimeMs = TimeMs = time;
            }

            internal void AddText(string text)
            {
                if (!string.IsNullOrWhiteSpace(text) && !_texts.Contains(text))
                    _texts.Add(text);
            }
        }
    }
}
