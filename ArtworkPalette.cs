using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace MusicBeePlugin
{
    internal struct ArtworkPalette
    {
        public Color Left;
        public Color Right;
        public Color BarTop;
        public Color BarBottom;
        public Color Border;
        public Color Accent;

        public static ArtworkPalette Default => new ArtworkPalette
        {
            Left = Color.FromArgb(13, 18, 32),
            Right = Color.FromArgb(31, 21, 51),
            BarTop = Color.FromArgb(135, 110, 242),
            BarBottom = Color.FromArgb(57, 193, 221),
            Border = Color.FromArgb(180, 180, 235),
            Accent = Color.FromArgb(98, 150, 196)
        };

        public static ArtworkPalette Blend(ArtworkPalette first, ArtworkPalette second, float amount)
        {
            return new ArtworkPalette
            {
                Left = Mix(first.Left, second.Left, amount),
                Right = Mix(first.Right, second.Right, amount),
                BarTop = Mix(first.BarTop, second.BarTop, amount),
                BarBottom = Mix(first.BarBottom, second.BarBottom, amount),
                Border = Mix(first.Border, second.Border, amount),
                Accent = Mix(first.Accent, second.Accent, amount)
            };
        }

        private static Color Mix(Color first, Color second, float amount)
        {
            return Color.FromArgb(
                (int)(first.R + (second.R - first.R) * amount),
                (int)(first.G + (second.G - first.G) * amount),
                (int)(first.B + (second.B - first.B) * amount));
        }

        public static bool TryLoad(string artworkPath, out ArtworkPalette palette)
        {
            Bitmap cover;
            var loaded = TryLoad(artworkPath, out palette, out cover);
            cover?.Dispose();
            return loaded;
        }

        public static bool TryLoad(string artworkPath, out ArtworkPalette palette, out Bitmap cover)
        {
            palette = Default;
            cover = null;
            if (string.IsNullOrWhiteSpace(artworkPath)) return false;
            try
            {
                if (artworkPath.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = artworkPath.IndexOf(',');
                    if (comma < 0) return false;
                    return TryLoad(Convert.FromBase64String(artworkPath.Substring(comma + 1)), out palette, out cover);
                }
                if (artworkPath.Length > 512 && artworkPath.IndexOf(':') < 0 &&
                    artworkPath.IndexOf('\\') < 0)
                    return TryLoad(Convert.FromBase64String(artworkPath), out palette, out cover);
                Uri uri;
                if (Uri.TryCreate(artworkPath, UriKind.Absolute, out uri) && uri.IsFile)
                    artworkPath = uri.LocalPath;
                if (File.Exists(artworkPath))
                {
                    using (var image = Image.FromFile(artworkPath))
                        return TryCreate(image, out palette, out cover);
                }
                // Older MusicBee artwork methods can return an encoded image.
                if (artworkPath.Length > 64)
                    return TryLoad(Convert.FromBase64String(artworkPath), out palette, out cover);
                return false;
            }
            catch (Exception) { return false; }
        }

        public static bool TryLoad(byte[] artworkData, out ArtworkPalette palette)
        {
            Bitmap cover;
            var loaded = TryLoad(artworkData, out palette, out cover);
            cover?.Dispose();
            return loaded;
        }

        public static bool TryLoad(byte[] artworkData, out ArtworkPalette palette, out Bitmap cover)
        {
            palette = Default;
            cover = null;
            if (artworkData == null || artworkData.Length == 0) return false;
            try
            {
                using (var stream = new MemoryStream(artworkData, false))
                using (var image = Image.FromStream(stream))
                    return TryCreate(image, out palette, out cover);
            }
            catch (Exception) { return false; }
        }

        private static bool TryCreate(Image image, out ArtworkPalette palette, out Bitmap cover)
        {
            palette = Default;
            cover = null;
            using (var thumbnail = new Bitmap(48, 48))
            {
                using (var graphics = Graphics.FromImage(thumbnail))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.DrawImage(image, 0, 0, thumbnail.Width, thumbnail.Height);
                }

                const int binCount = 18;
                var weights = new double[binCount];
                var reds = new double[binCount];
                var greens = new double[binCount];
                var blues = new double[binCount];
                var neutralTotal = 0.0;
                var pixelCount = 0;
                for (var y = 0; y < thumbnail.Height; y += 2)
                for (var x = 0; x < thumbnail.Width; x += 2)
                {
                    var pixel = thumbnail.GetPixel(x, y);
                    if (pixel.A < 32) continue;
                    neutralTotal += (pixel.R + pixel.G + pixel.B) / 3.0;
                    pixelCount++;
                    var maximum = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                    var minimum = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                    var lightness = (maximum + minimum) / 510.0;
                    if (lightness < 0.08 || lightness > 0.93) continue;
                    var saturation = maximum == 0 ? 0 : (maximum - minimum) / (double)maximum;
                    var bin = Math.Min(binCount - 1, (int)(pixel.GetHue() / 20));
                    var weight = (0.2 + saturation) * (0.4 + lightness);
                    if (saturation < 0.12) weight *= 0.18;
                    weights[bin] += weight;
                    reds[bin] += pixel.R * weight;
                    greens[bin] += pixel.G * weight;
                    blues[bin] += pixel.B * weight;
                }

                var primary = -1;
                for (var i = 0; i < binCount; i++)
                    if (weights[i] > 0 && (primary < 0 || weights[i] > weights[primary]))
                        primary = i;

                var secondary = -1;
                for (var i = 0; i < binCount && primary >= 0; i++)
                {
                    var distance = Math.Min(Math.Abs(primary - i), binCount - Math.Abs(primary - i));
                    if (distance < 2 || weights[i] <= 0) continue;
                    if (secondary < 0 || weights[i] > weights[secondary]) secondary = i;
                }

                var tertiary = -1;
                for (var i = 0; i < binCount && secondary >= 0; i++)
                {
                    var fromPrimary = Math.Min(Math.Abs(primary - i), binCount - Math.Abs(primary - i));
                    var fromSecondary = Math.Min(Math.Abs(secondary - i), binCount - Math.Abs(secondary - i));
                    if (fromPrimary < 2 || fromSecondary < 2 || weights[i] <= 0) continue;
                    if (tertiary < 0 || weights[i] > weights[tertiary]) tertiary = i;
                }

                if (pixelCount == 0) return false;
                // Very dark, bright, or monochrome covers still get their own
                // neutral palette instead of the default purple/blue gradient.
                var neutral = Color.FromArgb(Math.Max(65, Math.Min(205,
                    (int)(neutralTotal / pixelCount))),
                    Math.Max(65, Math.Min(205, (int)(neutralTotal / pixelCount))),
                    Math.Max(65, Math.Min(205, (int)(neutralTotal / pixelCount))));
                var first = primary < 0 ? neutral : Average(primary);
                var second = secondary < 0 ? Mix(first, neutral, 0.25f) : Average(secondary);
                var third = tertiary < 0 ? Mix(first, second, 0.5f) : Average(tertiary);
                palette = new ArtworkPalette
                {
                    Left = Mix(Color.FromArgb(8, 12, 20), first, 0.30f),
                    Right = Mix(Color.FromArgb(11, 13, 21), second, 0.34f),
                    BarTop = Mix(first, Color.White, 0.18f),
                    BarBottom = Mix(second, Color.White, 0.18f),
                    Border = Mix(first, Color.White, 0.35f),
                    Accent = third
                };
                var thumbnailCover = new Bitmap(256, 256);
                try
                {
                    using (var graphics = Graphics.FromImage(thumbnailCover))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        var crop = Math.Min(image.Width, image.Height);
                        graphics.DrawImage(image, new Rectangle(0, 0, 256, 256),
                            (image.Width - crop) / 2, (image.Height - crop) / 2,
                            crop, crop, GraphicsUnit.Pixel);
                    }
                }
                catch (Exception) { thumbnailCover.Dispose(); throw; }
                cover = thumbnailCover;
                return true;

                Color Average(int bin)
                {
                    return Color.FromArgb((int)(reds[bin] / weights[bin]),
                        (int)(greens[bin] / weights[bin]), (int)(blues[bin] / weights[bin]));
                }
            }
        }
    }
}
