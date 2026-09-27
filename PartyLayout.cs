using System;
using System.Drawing;

namespace MusicBeePlugin
{
    internal static class PartyLayout
    {
        private const double Aspect = 180d / 353d;

        internal static Rectangle Place(Rectangle window, Rectangle workArea,
            bool left, bool maximized)
        {
            if (window.Width < 1 || window.Height < 1 ||
                workArea.Width < 1 || workArea.Height < 1) return Rectangle.Empty;

            // Size the character with the lyrics window, while leaving room at
            // the top and bottom of the monitor for the transparent frame.
            var desiredHeight = Math.Min(workArea.Height - 16,
                (int)Math.Round(window.Height * 0.84));
            if (desiredHeight < 80) return Rectangle.Empty;
            var desiredWidth = (int)Math.Round(desiredHeight * Aspect);
            var outsideSpace = Math.Max(0, left
                ? window.Left - workArea.Left - 8
                : workArea.Right - window.Right - 8);
            var insideSpace = Math.Min(desiredWidth, Math.Min(
                Math.Max(76, (int)Math.Round(window.Width * 0.20)),
                Math.Min(window.Width / 2 - 16, workArea.Width / 2 - 16)));

            // When the lyric window fills the desktop, put the dancers in its
            // outer side strips rather than losing them beyond the monitor.
            var inside = maximized || insideSpace > outsideSpace;
            var width = inside ? insideSpace : Math.Min(desiredWidth, outsideSpace);
            if (width < 35) return Rectangle.Empty;
            var height = (int)Math.Round(width / Aspect);
            var y = Math.Max(workArea.Top, Math.Min(workArea.Bottom - height,
                window.Top + (window.Height - height) / 2));
            var x = inside
                ? (left ? window.Left + 8 : window.Right - width - 8)
                : (left ? window.Left - width - 8 : window.Right + 8);
            x = Math.Max(workArea.Left, Math.Min(workArea.Right - width, x));
            return new Rectangle(x, y, width, height);
        }
    }
}
