using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    // An owned, click-through layered window keeps each dancer outside the
    // lyric layout and preserves the PNG's soft per-pixel transparent edges.
    internal sealed class PartyDancerWindow : Form
    {
        private const int FrameWidth = 180;
        private const int FrameHeight = 353;
        private const int WsExLayered = 0x80000;
        private const int WsExTransparent = 0x20;
        private const int WsExNoActivate = 0x08000000;
        private readonly Bitmap _sheet;
        private Bitmap _surface;
        private Graphics _graphics;
        private byte[] _pixels;
        private IntPtr _memoryDc, _dib, _oldBitmap, _dibBits;
        private int _lastFrame = -1;
        private int _lastSquashPixels = -1;
        private int _lastSwayQuarterPixels = int.MinValue;
        private int _lastLiftQuarterPixels = int.MinValue;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X, Y; public NativePoint(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int Width, Height; public NativeSize(int width, int height) { Width = width; Height = height; } }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ClrUsed, ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public BitmapInfoHeader Header;
            public uint Colors;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr bitmap);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info,
            uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screenDc,
            ref NativePoint destination, ref NativeSize size, IntPtr sourceDc,
            ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);

        public PartyDancerWindow(string resourceName)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null) throw new InvalidOperationException("Party sprite is missing: " + resourceName);
                using (var image = Image.FromStream(stream)) _sheet = new Bitmap(image);
            }
            if (_sheet.Width != FrameWidth * PartyAnimation.FrameCount ||
                _sheet.Height != FrameHeight)
                throw new InvalidOperationException("The party sprite has an unexpected size.");
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= WsExLayered | WsExTransparent | WsExNoActivate;
                return p;
            }
        }

        public void Present(Rectangle bounds, int frame, float impact, float sway,
            float anticipation)
        {
            if (IsDisposed || bounds.Width < 1 || bounds.Height < 1) return;
            if (Bounds != bounds) Bounds = bounds;
            // Every beat lands; the side poses have the stronger squash.
            // A small lift just before the next pose softens the static hold.
            var squashPixels = (int)Math.Round(bounds.Height * 0.045f * impact);
            var liftQuarterPixels = (int)Math.Round(bounds.Height * 0.009f *
                anticipation * 4);
            // Quarter-pixel steps keep the tiny sway from snapping between
            // whole pixels on slow songs.
            var swayQuarterPixels = (int)Math.Round(bounds.Width * sway * 4);
            if (_lastFrame == frame && _lastSquashPixels == squashPixels &&
                _lastSwayQuarterPixels == swayQuarterPixels &&
                _lastLiftQuarterPixels == liftQuarterPixels &&
                _surface != null && _surface.Size == bounds.Size) return;
            if (_surface == null || _surface.Size != bounds.Size) CreateBuffer(bounds.Size);
            _graphics.Clear(Color.Transparent);
            _graphics.DrawImage(_sheet, new RectangleF(swayQuarterPixels / 4f,
                    squashPixels - liftQuarterPixels / 4f,
                    bounds.Width, bounds.Height - squashPixels),
                new Rectangle(frame * FrameWidth, 0, FrameWidth, FrameHeight), GraphicsUnit.Pixel);

            var data = _surface.LockBits(new Rectangle(Point.Empty, bounds.Size),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                // A top-down 32-bit DIB consumes premultiplied BGRA pixels.
                for (var row = 0; row < bounds.Height; row++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, row * data.Stride), _pixels,
                        row * bounds.Width * 4, bounds.Width * 4);
                }
                Marshal.Copy(_pixels, 0, _dibBits, _pixels.Length);
            }
            finally { _surface.UnlockBits(data); }

            var screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) throw new Win32Exception();
            try
            {
                var destination = new NativePoint(bounds.Left, bounds.Top);
                var size = new NativeSize(bounds.Width, bounds.Height);
                var source = new NativePoint(0, 0);
                var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
                if (!UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                    _memoryDc, ref source, 0, ref blend, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { ReleaseDC(IntPtr.Zero, screenDc); }
            _lastFrame = frame;
            _lastSquashPixels = squashPixels;
            _lastSwayQuarterPixels = swayQuarterPixels;
            _lastLiftQuarterPixels = liftQuarterPixels;
        }

        private void CreateBuffer(Size size)
        {
            ReleaseBuffer();
            _surface = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            _graphics = Graphics.FromImage(_surface);
            _graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            _graphics.CompositingMode = CompositingMode.SourceCopy;
            _pixels = new byte[size.Width * size.Height * 4];
            var screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) throw new Win32Exception();
            try
            {
                _memoryDc = CreateCompatibleDC(screenDc);
                var info = new BitmapInfo
                {
                    Header = new BitmapInfoHeader
                    {
                        Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                        Width = size.Width, Height = -size.Height,
                        Planes = 1, BitCount = 32,
                        SizeImage = (uint)_pixels.Length
                    }
                };
                _dib = CreateDIBSection(screenDc, ref info, 0, out _dibBits,
                    IntPtr.Zero, 0);
                if (_memoryDc == IntPtr.Zero || _dib == IntPtr.Zero || _dibBits == IntPtr.Zero)
                    throw new Win32Exception();
                _oldBitmap = SelectObject(_memoryDc, _dib);
                if (_oldBitmap == IntPtr.Zero) throw new Win32Exception();
            }
            finally { ReleaseDC(IntPtr.Zero, screenDc); }
            _lastFrame = -1;
            _lastSquashPixels = -1;
            _lastSwayQuarterPixels = int.MinValue;
            _lastLiftQuarterPixels = int.MinValue;
        }

        private void ReleaseBuffer()
        {
            _graphics?.Dispose();
            _graphics = null;
            _surface?.Dispose();
            _surface = null;
            if (_oldBitmap != IntPtr.Zero && _memoryDc != IntPtr.Zero)
                SelectObject(_memoryDc, _oldBitmap);
            if (_dib != IntPtr.Zero) DeleteObject(_dib);
            if (_memoryDc != IntPtr.Zero) DeleteDC(_memoryDc);
            _oldBitmap = _dib = _memoryDc = _dibBits = IntPtr.Zero;
            _pixels = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseBuffer();
                _sheet?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
