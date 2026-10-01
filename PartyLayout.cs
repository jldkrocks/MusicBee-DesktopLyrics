using System;
using System.Drawing;

namespace MusicBeePlugin
{
    internal static class PartyLayout
    {
        private const double Aspect = 180d / 353d;

        internal static int MaximizedGutter(int clientWidth)
        {
            // Keep enough room for the lyric card, artwork, queue and controls.
            return Math.Max(0, Math.Min((int)Math.Round(clientWidth * 0.22),
                (clientWidth - 620) / 2));
        }

        internal static Rectangle PlaceOutside(Rectangle window,
            Rectangle[] workAreas, bool left)
        {
            if (window.Width < 1 || window.Height < 1 || workAreas == null)
                return Rectangle.Empty;
            var best = Rectangle.Empty;
            var bestGap = int.MaxValue;
            foreach (var area in workAreas)
            {
                if (area.Width < 1 || area.Height < 96) continue;
                var desiredHeight = Math.Min(area.Height - 16,
                    (int)Math.Round(window.Height * 0.84));
                var desiredWidth = (int)Math.Round(desiredHeight * Aspect);
                var edge = left ? Math.Min(window.Left - 8, area.Right) :
                    Math.Max(window.Right + 8, area.Left);
                var available = left ? edge - area.Left : area.Right - edge;
                var width = Math.Min(desiredWidth, available);
                if (width < 35) continue;
                var height = (int)Math.Round(width / Aspect);
                var y = Math.Max(area.Top, Math.Min(area.Bottom - height,
                    window.Top + (window.Height - height) / 2));
                var x = left ? edge - width : edge;
                var gap = left ? window.Left - (x + width) : x - window.Right;
                if (width > best.Width || (width == best.Width && gap < bestGap))
                {
                    best = new Rectangle(x, y, width, height);
                    bestGap = gap;
                }
            }
            return best;
        }

        internal static Rectangle PlaceMaximized(Rectangle clientOnScreen,
            Rectangle workArea, bool left, int gutter)
        {
            if (gutter < 51 || clientOnScreen.Height < 96 || workArea.Height < 96)
                return Rectangle.Empty;
            var desiredHeight = Math.Min(workArea.Height - 16,
                (int)Math.Round(clientOnScreen.Height * 0.84));
            var width = Math.Min(gutter - 16,
                (int)Math.Round(desiredHeight * Aspect));
            if (width < 35) return Rectangle.Empty;
            var height = (int)Math.Round(width / Aspect);
            var x = left ? clientOnScreen.Left + 8 :
                clientOnScreen.Right - width - 8;
            x = Math.Max(workArea.Left, Math.Min(workArea.Right - width, x));
            var y = Math.Max(workArea.Top, Math.Min(workArea.Bottom - height,
                clientOnScreen.Top + (clientOnScreen.Height - height) / 2));
            return new Rectangle(x, y, width, height);
        }
    }
}
