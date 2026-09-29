using System;
using System.Drawing;
using System.Collections.Generic;
using System.Globalization;

namespace MusicBeePlugin
{
    internal struct LyricTextFit
    {
        public string Text;
        public float Points;
        public int Lines;
        public bool Overflow;
    }

    internal static class LyricTextLayout
    {
        internal static LyricTextFit Fit(Graphics graphics, string lyric, Font font,
            float desiredPoints, float width, float height)
        {
            var fit = FitCore(graphics, lyric, font, desiredPoints, width, height);
            // GDI font hinting can change measured widths slightly when the
            // computed point size is applied. Refine that size before falling
            // back to minimum-size wrapping (also across Windows font versions).
            for (var attempt = 0; attempt < 4; attempt++)
            {
                using (var sample = new Font(font.FontFamily, fit.Points, font.Style, GraphicsUnit.Point))
                {
                    var widest = 1f;
                    foreach (var line in fit.Text.Split('\n'))
                        widest = Math.Max(widest, graphics.MeasureString(line, sample).Width);
                    var ratio = Math.Min(Math.Max(1, height - 2) / (fit.Lines * sample.GetHeight(graphics)),
                        Math.Max(1, width - 8) / widest);
                    if (ratio >= 1) return fit;
                    if (fit.Points <= 10) break;
                    fit.Points = Math.Max(10, fit.Points * ratio * 0.99f);
                }
            }
            // Keep complete words on additional rows before resorting to an
            // explicit ellipsis. The full text remains available in the reader.
            using (var sample = new Font(font.FontFamily, 10, font.Style, GraphicsUnit.Point))
            {
                var rows = Wrap(graphics, lyric, sample, Math.Max(1, width - 8));
                var capacity = Math.Max(1, (int)((height - 2) / sample.GetHeight(graphics)));
                var overflow = rows.Count > capacity || sample.GetHeight(graphics) > height - 2;
                if (rows.Count > capacity) rows.RemoveRange(capacity, rows.Count - capacity);
                if (overflow)
                {
                    var last = rows[rows.Count - 1];
                    while (last.Length > 0 && graphics.MeasureString(last + "…", sample).Width > width - 8)
                    {
                        var elements = StringInfo.ParseCombiningCharacters(last);
                        last = last.Substring(0, elements[elements.Length - 1]).TrimEnd();
                    }
                    rows[rows.Count - 1] = last + "…";
                }
                return new LyricTextFit { Text = string.Join("\n", rows), Points = 10,
                    Lines = rows.Count, Overflow = overflow };
            }
        }

        private static List<string> Wrap(Graphics graphics, string text, Font font, float width)
        {
            var rows = new List<string>();
            var remaining = text.Trim();
            while (remaining.Length > 0)
            {
                var starts = StringInfo.ParseCombiningCharacters(remaining);
                int low = 1, high = starts.Length, count = 1;
                while (low <= high)
                {
                    var mid = (low + high) / 2;
                    var end = mid == starts.Length ? remaining.Length : starts[mid];
                    if (graphics.MeasureString(remaining.Substring(0, end), font).Width <= width)
                    { count = mid; low = mid + 1; }
                    else high = mid - 1;
                }
                var take = count == starts.Length ? remaining.Length : starts[count];
                if (take < remaining.Length)
                {
                    var space = remaining.LastIndexOf(' ', Math.Max(0, take - 1));
                    if (space > 0) take = space;
                }
                rows.Add(remaining.Substring(0, take).TrimEnd());
                remaining = remaining.Substring(take).TrimStart();
            }
            if (rows.Count == 0) rows.Add("");
            return rows;
        }

        private static LyricTextFit FitCore(Graphics graphics, string lyric, Font font,
            float desiredPoints, float width, float height)
        {
            var availableWidth = Math.Max(8f, width - 8f);
            var size = Math.Max(10f, Math.Min(desiredPoints, height * 0.74f));
            float singleWidth;
            using (var sample = new Font(font.FontFamily, size, font.Style, GraphicsUnit.Point))
                singleWidth = graphics.MeasureString(lyric, sample).Width;
            var singlePoints = Math.Max(10f, size *
                Math.Min(1f, availableWidth / Math.Max(1f, singleWidth)));
            var single = new LyricTextFit { Text = lyric, Points = singlePoints, Lines = 1 };
            if (singlePoints >= size * 0.85f || height < 30f) return single;

            string first, second;
            using (var sample = new Font(font.FontFamily, size, font.Style, GraphicsUnit.Point))
                if (!SplitEvenly(graphics, sample, lyric, out first, out second))
                    return single;

            float twoPoints;
            using (var sample = new Font(font.FontFamily, size, font.Style, GraphicsUnit.Point))
                twoPoints = size * Math.Min(1f,
                    Math.Max(0f, height - 4f) / (2f * sample.GetHeight(graphics)));
            if (twoPoints < 10f) return single;
            using (var sample = new Font(font.FontFamily, twoPoints, font.Style, GraphicsUnit.Point))
            {
                var widest = Math.Max(graphics.MeasureString(first, sample).Width,
                    graphics.MeasureString(second, sample).Width);
                twoPoints *= Math.Min(1f, availableWidth / Math.Max(1f, widest));
            }
            // An extra row is worthwhile only if it improves readability.
            if (twoPoints < 10f || twoPoints < singlePoints * 1.2f) return single;
            return new LyricTextFit
            {
                Text = first + "\n" + second, Points = twoPoints, Lines = 2
            };
        }

        private static bool SplitEvenly(Graphics graphics, Font font, string lyric,
            out string first, out string second)
        {
            first = second = null;
            var best = float.MaxValue;
            // Keep words intact whenever the line has spaces. With unspaced
            // scripts, use text elements so combining marks stay with a glyph.
            for (var i = 1; i < lyric.Length; i++)
            {
                if (!char.IsWhiteSpace(lyric[i])) continue;
                Consider(graphics, font, lyric.Substring(0, i).TrimEnd(),
                    lyric.Substring(i).TrimStart(), ref first, ref second, ref best);
            }
            if (first != null) return true;
            var starts = StringInfo.ParseCombiningCharacters(lyric);
            for (var i = 1; i < starts.Length; i++)
                Consider(graphics, font, lyric.Substring(0, starts[i]),
                    lyric.Substring(starts[i]), ref first, ref second, ref best);
            return first != null;
        }

        private static void Consider(Graphics graphics, Font font, string left,
            string right, ref string first, ref string second, ref float best)
        {
            if (left.Length == 0 || right.Length == 0) return;
            var leftWidth = graphics.MeasureString(left, font).Width;
            var rightWidth = graphics.MeasureString(right, font).Width;
            var score = Math.Max(leftWidth, rightWidth) +
                Math.Abs(leftWidth - rightWidth) * 0.12f;
            if (score >= best) return;
            best = score;
            first = left;
            second = right;
        }
    }
}
