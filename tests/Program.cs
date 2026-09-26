using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace MusicBeePlugin
{
    internal static class Program
    {
        private static void Main()
        {
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
            Console.WriteLine("LRC parser checks passed.");

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
                if (!ArtworkPalette.TryLoad(imageBytes, out embeddedPalette) ||
                    embeddedPalette.BarTop.ToArgb() != palette.BarTop.ToArgb())
                    throw new Exception("Embedded artwork bytes should produce the same palette.");
                ArtworkPalette urlPalette;
                if (!ArtworkPalette.TryLoad(new Uri(imagePath).AbsoluteUri, out urlPalette) ||
                    urlPalette.BarBottom.ToArgb() != palette.BarBottom.ToArgb())
                    throw new Exception("File URLs should resolve to the artwork.");
                ArtworkPalette encodedPalette;
                if (!ArtworkPalette.TryLoad(Convert.ToBase64String(imageBytes), out encodedPalette) ||
                    encodedPalette.Left.ToArgb() != palette.Left.ToArgb())
                    throw new Exception("Encoded artwork should produce the same palette.");

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
    }
}
