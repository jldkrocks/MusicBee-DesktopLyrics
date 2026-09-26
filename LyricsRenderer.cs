using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public static class LyricsRenderer
    {
        private static readonly Brush ShadowBrush = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        private const int ShadowOffset = 2;
        private const int BackgroundOffsetH = 10;
        private const int BackgroundOffsetV = 6;
        private const float PreviewOpacity = 0.55f;

        private static readonly StringFormat Format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near
        };

        private static Pen _borderPen;
        private static Font _mainFont, _subFont;
        private static Color _color1, _color2;
        private static GradientType _type;
        private static int _width = 1024;
        private static int _dpi = 72;
        private static int _backgroundOpacity = 40;

        // Thread unsafe!!!
        public static void UpdateFromSettings(SettingsObj settings, int width)
        {
            _mainFont = settings.Font;
            _subFont = new Font(_mainFont.FontFamily, _mainFont.Size * 0.8f, _mainFont.Style, _mainFont.Unit, _mainFont.GdiCharSet);
            _borderPen?.Dispose();
            _borderPen = new Pen(settings.BorderColor, 2f)
            {
                LineJoin = LineJoin.Round,
                EndCap = LineCap.Round,
                StartCap = LineCap.Round
            };
            _type = (GradientType)settings.GradientType;
            _color1 = settings.Color1;
            _color2 = settings.Color2;
            _backgroundOpacity = settings.BackgroundOpacity;
            switch ((AlignmentType)settings.AlignmentType)
            {
                case AlignmentType.Left:
                    Format.Alignment = StringAlignment.Near;
                    break;
                case AlignmentType.Right:
                    Format.Alignment = StringAlignment.Far;
                    break;
                case AlignmentType.Center:
                default:
                    Format.Alignment = StringAlignment.Center;
                    break;
            }

            _width = width;
        }

        public static void SetDpi(int deviceDpi)
        {
            _dpi = deviceDpi;
        }

        public static Bitmap Render1LineLyrics(string line1, IDeviceContext dc)
        {
            return RenderLyrics(line1, _mainFont, dc);
        }

        public static Bitmap Render2LineLyrics(string line1, string line2, bool isPreview, IDeviceContext dc)
        {
            using (Bitmap line1Bitmap = RenderLyrics(line1, _mainFont, dc),
                   line2Bitmap = RenderLyrics(line2, _subFont, dc))
            {
                var bitmap = new Bitmap(Math.Max(line1Bitmap.Width, line2Bitmap.Width), line1Bitmap.Height + line2Bitmap.Height);
                bitmap.SetResolution(_dpi, _dpi);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.DrawImage(line1Bitmap, new PointF(0, 0));
                    DrawSubLine(g, line2Bitmap, (int)(line1Bitmap.Height * 0.9f), isPreview);
                }
                return bitmap;
            }
        }

        public static Bitmap Render3LineLyrics(string line1, string translation, string nextLine, IDeviceContext dc)
        {
            using (var line1Bitmap = RenderLyrics(line1, _mainFont, dc))
            using (var translationBitmap = RenderLyrics(translation, _subFont, dc))
            using (var previewBitmap = RenderLyrics(nextLine, _subFont, dc))
            {
                var bitmap = new Bitmap(_width, line1Bitmap.Height + translationBitmap.Height + previewBitmap.Height);
                bitmap.SetResolution(_dpi, _dpi);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.DrawImage(line1Bitmap, new PointF(0, 0));
                    DrawSubLine(g, translationBitmap, (int)(line1Bitmap.Height * 0.9f), false);
                    DrawSubLine(g, previewBitmap, (int)(line1Bitmap.Height * 0.9f + translationBitmap.Height * 0.9f), true);
                }
                return bitmap;
            }
        }

        private static void DrawSubLine(Graphics g, Bitmap line, int y, bool isPreview)
        {
            if (!isPreview)
            {
                g.DrawImage(line, 0, y);
                return;
            }

            // Fade the entire rendered line, including its border, shadow and background.
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = PreviewOpacity });
                g.DrawImage(line, new Rectangle(0, y, line.Width, line.Height),
                    0, 0, line.Width, line.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        private static Bitmap RenderLyrics(string lyric, Font font, IDeviceContext dc)
        {
            // TODO: bounds returned by `TextRenderer.MeasureTex` differs greatly
            //       from bounds created by `GraphicsPath.AddString`, need further
            //       investigation.
            var fontBounds = TextRenderer.MeasureText(dc, lyric, font);
            var height = fontBounds.Height;
            if (height <= 0) height = 1;
            var bitmap = new Bitmap(_width, height + BackgroundOffsetV * 2);
            bitmap.SetResolution(_dpi, _dpi);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.High;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.CompositingQuality = CompositingQuality.HighQuality;

                var fontEmSize = font.SizeInPoints * _dpi / 72;

                var initialRect = new RectangleF(0, BackgroundOffsetV, _width, height);
                using (var stringPath = new GraphicsPath(FillMode.Alternate))
                {
                    stringPath.AddString(lyric, font.FontFamily, (int)font.Style, fontEmSize, initialRect, Format);
                    var bounds = stringPath.GetBounds();

                    if (_backgroundOpacity > 0 && bounds.Width > 0)
                    {
                        using (var brush = new SolidBrush(Color.FromArgb(_backgroundOpacity, 0, 0, 0)))
                        {
                            g.FillRectangle(brush, new RectangleF(
                                bounds.Left - BackgroundOffsetH, 0,
                                bounds.Width + BackgroundOffsetH * 2, height + 30));
                        }
                    }

                    // Using matrix to translate the path
                    using (var mat = new Matrix())
                    {
                        mat.Translate(-ShadowOffset, -ShadowOffset);
                        stringPath.Transform(mat);

                        g.FillPath(ShadowBrush, stringPath);

                        mat.Translate(ShadowOffset * 2, ShadowOffset * 2);
                        stringPath.Transform(mat);
                    }


                    if (_borderPen != null)
                        g.DrawPath(_borderPen, stringPath);

                    using (var stringBrush = CreateGradientBrush(initialRect))
                    {
                        g.FillPath(stringBrush, stringPath);
                    }
                }
            }
            
            return bitmap;
        }

        private static Brush CreateGradientBrush(RectangleF dstRect)
        {
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (_type)
            {
                case GradientType.DoubleColor:
                    return new LinearGradientBrush(dstRect.Location, new PointF(dstRect.X, dstRect.Y + dstRect.Height), _color1, _color2)
                    {
                        WrapMode = WrapMode.TileFlipXY
                    };
                case GradientType.TripleColor:
                    return new LinearGradientBrush(dstRect.Location, new PointF(dstRect.X, dstRect.Y + dstRect.Height / 3 * 2), _color1, _color2)
                    {
                        WrapMode = WrapMode.TileFlipXY
                    };
                case GradientType.NoGradient:
                default:
                    return new SolidBrush(_color1);
            }
        }
    }
}
