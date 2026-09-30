using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MusicBeePlugin
{
    // Owned and used only by the lyrics window's UI thread. No song or clock state.
    internal sealed class GpuSceneRenderer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct Scene
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)] public uint[] Colors;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public float[] Bars;
            public int Spectrum;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateFn(IntPtr hwnd, uint w, uint h, int diagnosticReadback, out IntPtr renderer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyFn(IntPtr renderer);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ResizeFn(IntPtr renderer, uint w, uint h);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int UploadFn(IntPtr renderer, IntPtr pixels, uint stride);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DrawFn(IntPtr renderer, ref Scene scene, IntPtr diagnosticOutput);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        private IntPtr _library, _renderer;
        private DestroyFn _destroy;
        private ResizeFn _resize;
        private UploadFn _upload;
        private DrawFn _draw;
        private Scene _scene = new Scene { Colors = new uint[6] };
        internal Bitmap Foreground { get; private set; }

        private T Export<T>(string name) where T : class
        {
            var address = GetProcAddress(_library, name);
            if (address == IntPtr.Zero) throw new EntryPointNotFoundException(name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }
        internal GpuSceneRenderer(IntPtr hwnd, Size size, bool diagnosticReadback = false)
        {
            try
            {
                var path = Path.Combine(Path.GetDirectoryName(typeof(GpuSceneRenderer).Assembly.Location),
                    "DesktopLyricsGpu." + (IntPtr.Size == 4 ? "Win32" : "x64") + ".dll");
                // Absolute plugin path, dependencies restricted to this folder and System32.
                _library = LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x800);
                if (_library == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                _destroy = Export<DestroyFn>("DL_Destroy"); _resize = Export<ResizeFn>("DL_Resize");
                _upload = Export<UploadFn>("DL_Upload"); _draw = Export<DrawFn>("DL_Draw");
                Marshal.ThrowExceptionForHR(Export<CreateFn>("DL_Create")(hwnd, (uint)size.Width, (uint)size.Height, diagnosticReadback ? 1 : 0, out _renderer));
                Resize(size);
            }
            catch { Dispose(); throw; }
        }
        internal bool Resize(Size size)
        {
            if (Foreground != null && Foreground.Size == size) return false;
            if (size.Width <= 0 || size.Height <= 0 || size.Width > 8192 || size.Height > 8192)
                throw new ArgumentOutOfRangeException("size");
            Foreground?.Dispose(); Foreground = null;
            Marshal.ThrowExceptionForHR(_resize(_renderer, (uint)size.Width, (uint)size.Height));
            Foreground = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            return true;
        }
        internal void Upload()
        {
            var bits = Foreground.LockBits(new Rectangle(Point.Empty, Foreground.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.ThrowExceptionForHR(_upload(_renderer, bits.Scan0, (uint)bits.Stride)); }
            finally { Foreground.UnlockBits(bits); }
        }
        internal void Draw(ArtworkPalette palette, float[] bars, bool spectrum, IntPtr diagnosticOutput = default(IntPtr))
        {
            var colors = _scene.Colors;
            colors[0] = (uint)palette.Left.ToArgb(); colors[1] = (uint)palette.Right.ToArgb();
            colors[2] = (uint)palette.BarTop.ToArgb(); colors[3] = (uint)palette.BarBottom.ToArgb();
            colors[4] = (uint)palette.Border.ToArgb(); colors[5] = (uint)palette.Accent.ToArgb();
            _scene.Bars = bars; _scene.Spectrum = spectrum ? 1 : 0;
            Marshal.ThrowExceptionForHR(_draw(_renderer, ref _scene, diagnosticOutput));
        }
        public void Dispose()
        {
            Foreground?.Dispose(); Foreground = null;
            if (_renderer != IntPtr.Zero) { _destroy(_renderer); _renderer = IntPtr.Zero; }
            if (_library != IntPtr.Zero) { FreeLibrary(_library); _library = IntPtr.Zero; }
        }
    }
}
