using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBeePlugin
{
    // Keeps the original plain lyric lines and spacing, using their usual line ending.
    // Every sung line must be stamped before the new LRC can be saved.
    internal sealed class UntimedTimingDocument
    {
        private static readonly Regex Metadata = new Regex(
            @"^\s*\[(?:ar|al|ti|au|by|length|offset|re|ve):.*\]\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly string[] _lines;
        private readonly string _newline;
        private readonly List<Line> _entries = new List<Line>();

        public sealed class Line
        {
            public string Text { get; internal set; }
            public int LineIndex { get; internal set; }
            public int? TimeMs { get; internal set; }
        }

        public string OriginalLyrics { get; private set; }
        public IList<Line> Entries => _entries.AsReadOnly();
        public bool IsComplete
        {
            get
            {
                if (_entries.Count == 0) return false;
                foreach (var entry in _entries) if (!entry.TimeMs.HasValue) return false;
                return true;
            }
        }
        public bool IsDirty
        {
            get
            {
                foreach (var entry in _entries) if (entry.TimeMs.HasValue) return true;
                return false;
            }
        }

        private UntimedTimingDocument(string source)
        {
            OriginalLyrics = source;
            _newline = source.Contains("\r\n") ? "\r\n" : "\n";
            _lines = Regex.Split(source, "\r\n|\n|\r");
            for (var i = 0; i < _lines.Length; i++)
            {
                var line = _lines[i];
                if (string.IsNullOrWhiteSpace(line) || Metadata.IsMatch(line)) continue;
                _entries.Add(new Line { Text = line.Trim(), LineIndex = i });
            }
        }

        public static bool TryCreate(string source, out UntimedTimingDocument document)
        {
            document = null;
            if (string.IsNullOrWhiteSpace(source)) return false;
            LrcTimingDocument timed;
            if (LrcTimingDocument.TryCreate(source, out timed)) return false;
            var parsed = new UntimedTimingDocument(source);
            if (parsed._entries.Count == 0) return false;
            document = parsed;
            return true;
        }

        public bool Stamp(int index, int milliseconds)
        {
            if (index < 0 || index >= _entries.Count || milliseconds < 0) return false;
            if (index > 0 && (!_entries[index - 1].TimeMs.HasValue ||
                milliseconds <= _entries[index - 1].TimeMs.Value)) return false;
            if (index + 1 < _entries.Count && _entries[index + 1].TimeMs.HasValue &&
                milliseconds >= _entries[index + 1].TimeMs.Value) return false;
            _entries[index].TimeMs = milliseconds;
            return true;
        }

        public bool Adjust(int index, int delta)
        {
            if (index < 0 || index >= _entries.Count || !_entries[index].TimeMs.HasValue)
                return false;
            var target = (long)_entries[index].TimeMs.Value + delta;
            return target >= 0 && target <= int.MaxValue && Stamp(index, (int)target);
        }

        public int ShiftAll(int delta)
        {
            if (!IsDirty || delta == 0) return 0;
            var first = _entries.Find(entry => entry.TimeMs.HasValue).TimeMs.Value;
            var last = _entries.FindLast(entry => entry.TimeMs.HasValue).TimeMs.Value;
            var actual = (int)Math.Max(-first, Math.Min((long)delta,
                (long)int.MaxValue - last));
            foreach (var entry in _entries)
                if (entry.TimeMs.HasValue) entry.TimeMs += actual;
            return actual;
        }

        public void Reset()
        {
            foreach (var entry in _entries) entry.TimeMs = null;
        }

        public string BuildLyrics()
        {
            var times = new Dictionary<int, int>();
            foreach (var entry in _entries)
                if (entry.TimeMs.HasValue) times.Add(entry.LineIndex, entry.TimeMs.Value);
            var result = new StringBuilder();
            for (var i = 0; i < _lines.Length; i++)
            {
                if (i != 0) result.Append(_newline);
                int time;
                if (times.TryGetValue(i, out time))
                    result.Append('[').Append(LrcTimingDocument.FormatTime(time)).Append("] ");
                result.Append(_lines[i]);
            }
            return result.ToString();
        }
    }
}
