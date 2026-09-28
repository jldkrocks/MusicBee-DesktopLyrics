using System;
using System.Drawing;
using System.Globalization;

namespace MusicBeePlugin
{
    internal struct LyricTextFit
    {
        public string Text;
        public float Points;
        public int Lines;
    }

    internal static class LyricTextLayout
    {
        internal static bool CanPromotePreview(Graphics graphics, string lyric,
            Font font, float previewPoints, float mainPoints, float width,
            float previewHeight, float mainHeight)
        {
            var preview = Fit(graphics, lyric, font, previewPoints, width, previewHeight);
            var main = Fit(graphics, lyric, font, mainPoints, width, mainHeight);
            // A changing row count or large font jump makes the travelling
            // lyric reflow while it moves. Let those lines slide and fade at
            // their fixed sizes instead.
            return preview.Lines == 1 && main.Lines == 1 &&
                preview.Points >= previewPoints * 0.88f &&
                main.Points >= mainPoints * 0.88f &&
                main.Points <= preview.Points * 1.75f;
        }

        internal static LyricTextFit Fit(Graphics graphics, string lyric, Font font,
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
