using System.Drawing;

namespace MusicBeePlugin
{
    // Keep the active lyric in one fixed central slot. English sits above it
    // and the upcoming lyric below, even when only one of those lines exists.
    internal struct LyricCardLayout
    {
        public RectangleF Main;
        public RectangleF English;
        public RectangleF Preview;
        public RectangleF Bounds;

        public static float RequiredHeight(float mainHeight, float subHeight,
            float gap, bool hasSideLine)
        {
            return mainHeight + (hasSideLine ? 2 * (subHeight + gap) : 0);
        }

        public static LyricCardLayout Create(RectangleF content,
            float mainHeight, float subHeight, float gap,
            bool hasEnglish, bool hasPreview, float offsetY)
        {
            if (content.Height < 130 && (hasEnglish || hasPreview))
            {
                // Small windows need asymmetric space: translation can wrap,
                // while the main lyric and upcoming row remain visible.
                var padding = content.Height < 100 ? 4f : 9f;
                var available = System.Math.Max(12f, content.Height - padding * 2);
                gap = System.Math.Min(3f, gap);
                var textHeight = System.Math.Max(3f, available - gap *
                    ((hasEnglish ? 1 : 0) + (hasPreview ? 1 : 0)));
                var previewHeight = hasPreview ? System.Math.Max(16f,
                    textHeight * (hasEnglish ? 0.22f : 0.4f)) : 0;
                var activeHeight = hasEnglish ? System.Math.Max(18f, textHeight * 0.3f) :
                    textHeight - previewHeight;
                var englishHeight = hasEnglish ? System.Math.Max(1f,
                    textHeight - activeHeight - previewHeight) : 0;
                var compactTop = content.Top + padding + offsetY;
                var active = new RectangleF(content.Left,
                    compactTop + (hasEnglish ? englishHeight + gap : 0), content.Width, activeHeight);
                var compactPreview = new RectangleF(content.Left, active.Bottom + gap,
                    content.Width, previewHeight);
                return new LyricCardLayout
                {
                    Main = active,
                    English = new RectangleF(content.Left, compactTop, content.Width, englishHeight),
                    Preview = compactPreview,
                    Bounds = RectangleF.FromLTRB(content.Left, compactTop, content.Right,
                        hasPreview ? compactPreview.Bottom : active.Bottom)
                };
            }
            var mainTop = content.Top + (content.Height - mainHeight) / 2 + offsetY;
            var main = new RectangleF(content.Left, mainTop, content.Width, mainHeight);
            var english = new RectangleF(content.Left, mainTop - subHeight - gap,
                content.Width, subHeight);
            var preview = new RectangleF(content.Left, main.Bottom + gap,
                content.Width, subHeight);
            var top = hasEnglish ? english.Top : main.Top;
            var bottom = hasPreview ? preview.Bottom : main.Bottom;
            return new LyricCardLayout
            {
                Main = main, English = english, Preview = preview,
                Bounds = RectangleF.FromLTRB(content.Left, top, content.Right, bottom)
            };
        }
    }
}
