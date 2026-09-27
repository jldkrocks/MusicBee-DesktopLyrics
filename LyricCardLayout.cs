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
