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

        public static ArtworkPalette Default => new ArtworkPalette
        {
            Left = Color.FromArgb(13, 18, 32),
            Right = Color.FromArgb(31, 21, 51),
            BarTop = Color.FromArgb(135, 110, 242),
            BarBottom = Color.FromArgb(57, 193, 221),
            Border = Color.FromArgb(180, 180, 235)
        };

        public static ArtworkPalette Blend(ArtworkPalette first, ArtworkPalette second, float amount)
        {
            return new ArtworkPalette
            {
                Left = Mix(first.Left, second.Left, amount),
                Right = Mix(first.Right, second.Right, amount),
                BarTop = Mix(first.BarTop, second.BarTop, amount),
                BarBottom = Mix(first.BarBottom, second.BarBottom, amount),
                Border = Mix(first.Border, second.Border, amount)
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
            palette = Default;
            if (string.IsNullOrWhiteSpace(artworkPath) || !File.Exists(artworkPath)) return false;
            try
            {
                using (var image = Image.FromFile(artworkPath))
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
                    for (var y = 0; y < thumbnail.Height; y += 2)
                    for (var x = 0; x < thumbnail.Width; x += 2)
                    {
                        var pixel = thumbnail.GetPixel(x, y);
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
                    if (primary < 0) return false;

                    var secondary = -1;
                    for (var i = 0; i < binCount; i++)
                    {
                        var distance = Math.Min(Math.Abs(primary - i), binCount - Math.Abs(primary - i));
                        if (distance < 2 || weights[i] <= 0) continue;
                        if (secondary < 0 || weights[i] > weights[secondary]) secondary = i;
                    }

                    var first = Average(primary);
                    var second = secondary < 0 ? Mix(first, Color.White, 0.2f) : Average(secondary);
                    palette = new ArtworkPalette
                    {
                        Left = Mix(Default.Left, first, 0.23f),
                        Right = Mix(Default.Right, second, 0.27f),
                        BarTop = Mix(first, Color.White, 0.18f),
                        BarBottom = Mix(second, Color.White, 0.18f),
                        Border = Mix(first, Color.White, 0.35f)
                    };
                    return true;

                    Color Average(int bin)
                    {
                        return Color.FromArgb((int)(reds[bin] / weights[bin]),
                            (int)(greens[bin] / weights[bin]), (int)(blues[bin] / weights[bin]));
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
