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
        [StructLayout(LayoutKind.Sequential)] private struct Rect
        {
            public float Left, Top, Right, Bottom;
            public Rect(RectangleF r) { Left=r.Left; Top=r.Top; Right=r.Right; Bottom=r.Bottom; }
        }
        [StructLayout(LayoutKind.Sequential)] private struct TextCommand
        {
            public int Slot;
            public Rect Destination, Clip;
            public float Opacity;
            public int Nearest;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TextFn(IntPtr renderer, int slot, uint width, uint height, IntPtr pixels, uint stride);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LyricsFn(IntPtr renderer, ref Rect panel, [In] TextCommand[] commands, int count);
        private TextFn _text;
        private LyricsFn _lyrics;
        [StructLayout(LayoutKind.Sequential)] private struct DancerCommand
        {
            public int Slot;
            public Rect Destination, Clip;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DancerTextureFn(IntPtr renderer, int slot, uint width, uint height, IntPtr pixels, uint stride);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DancersFn(IntPtr renderer, [In] DancerCommand[] commands, int count);
        private DancerTextureFn _dancerTexture;
        private DancersFn _dancers;
        private readonly DancerCommand[] _dancerCommands = new DancerCommand[2];
        private readonly Size[] _dancerSizes = new Size[8];
        private DancerPosePreparer _dancerPreparation;
        private int _dancerBytes;
        private int _dancerCount;
        internal void BeginDancers() { _dancerCount = 0; }
        internal void ClearDancers()
        {
            BeginDancers(); CommitDancers();
            for (int i=0;i<8;i++) if (!_dancerSizes[i].IsEmpty) {
                Marshal.ThrowExceptionForHR(_dancerTexture(_renderer,i,0,0,IntPtr.Zero,0));
                _dancerSizes[i]=Size.Empty;
            }
            _dancerBytes=0;
            _dancerPreparation?.Dispose(); _dancerPreparation = null;
        }
        internal void CommitDancers() { Marshal.ThrowExceptionForHR(_dancers(_renderer,_dancerCommands,_dancerCount == 2 ? 2 : 0)); }
        internal void SeedDancer(int character, int frame, Bitmap pose)
        {
            if (pose == null) return;
            if (character < 0 || character > 1 || frame < 0 || frame > 9 || frame % 3 != 0)
                throw new ArgumentOutOfRangeException("dancer");
            int slot = character * 4 + frame / 3;
            if (!_dancerSizes[slot].IsEmpty) return;
            int bytes = checked(pose.Width * pose.Height * 4);
            if (bytes > 8 * 1024 * 1024) return;
            var bits = pose.LockBits(new Rectangle(Point.Empty, pose.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.ThrowExceptionForHR(_dancerTexture(_renderer, slot, (uint)pose.Width, (uint)pose.Height, bits.Scan0, (uint)bits.Stride)); }
            finally { pose.UnlockBits(bits); }
            _dancerSizes[slot] = pose.Size; _dancerBytes += bytes;
        }
        internal void AddDancer(int character, Rectangle bounds, int frame, float impact, float sway, float anticipation)
        {
            if (bounds.Width<=0 || bounds.Height<=0) return;
            if (character<0 || character>1 || frame<0 || frame>9 || frame%3!=0 || _dancerCount>=2)
                throw new ArgumentOutOfRangeException("dancer");
            // Bound the complete four-pose cache before allocating any pixels.
            if ((long)bounds.Width*bounds.Height*4>8*1024*1024) throw new InvalidOperationException("Dancer pose exceeds GPU budget.");
            int slot=character*4+frame/3;
            if (_dancerPreparation == null) _dancerPreparation = new DancerPosePreparer();
            _dancerPreparation.Request(character, bounds.Size, frame);
            // Upload prepared poses on this thread only. Existing textures remain
            // usable during resize; no decode or bicubic raster runs on the UI thread.
            for (int i = character * 4; i < character * 4 + 4; i++) {
                using (var pose = _dancerPreparation.Take(i)) {
                    if (pose == null) continue;
                    var stamp = Profile?.Stamp ?? 0;
                    var bits = pose.LockBits(new Rectangle(Point.Empty, pose.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                    try { Marshal.ThrowExceptionForHR(_dancerTexture(_renderer, i, (uint)pose.Width, (uint)pose.Height, bits.Scan0, (uint)bits.Stride)); }
                    finally { pose.UnlockBits(bits); }
                    _dancerBytes -= _dancerSizes[i].Width * _dancerSizes[i].Height * 4;
                    _dancerSizes[i] = pose.Size;
                    _dancerBytes += pose.Width * pose.Height * 4;
                    Profile?.End(RenderMetric.DancerTextureUpload, stamp);
                }
            }
            // A cold pose is omitted until ready, never substituted with another
            // beat's pose. The next frame always uses the current animation state.
            if (_dancerSizes[slot].IsEmpty) return;
            var destination=PartyDancerWindow.PoseDestination(bounds.Size,impact,sway,anticipation);
            // The cached pose is already the destination width. GDI's nearest
            // sampling rounds exact half-pixel translations toward the lower
            // pixel; some D2D hardware rounds the tie upward. Resolve that tie
            // explicitly so sharper outlines do not shift by one pixel.
            destination.X=(float)Math.Ceiling(destination.X-0.5f);
            destination.Offset(bounds.Location);
            _dancerCommands[_dancerCount++]=new DancerCommand {Slot=slot,Destination=new Rect(destination),Clip=new Rect(bounds)};
        }
        private readonly Bitmap[] _textImages = new Bitmap[24]; // borrowed raster references, not owned
        private readonly long[] _textUsed = new long[24];
        private readonly int[] _textBytes = new int[24];
        private readonly TextCommand[] _commands = new TextCommand[8];
        private long _capture;
        private int _commandCount, _textureBytes;
        private Rect _panel;
        internal RenderProfile Profile;
        internal void BeginLyrics() { _capture++; _commandCount=0; _panel=new Rect(); }
        internal void SetPanel(RectangleF panel) { _panel=new Rect(panel); }
        internal void CommitLyrics() { Marshal.ThrowExceptionForHR(_lyrics(_renderer,ref _panel,_commands,_commandCount)); }
        private int OldestUnused()
        {
            int chosen=-1;
            for(int i=0;i<24;i++) if(_textUsed[i]!=_capture && (chosen<0 || _textUsed[i]<_textUsed[chosen])) chosen=i;
            if(chosen<0) throw new InvalidOperationException("Lyric texture cache is full.");
            return chosen;
        }
        private void Evict(int slot)
        {
            Marshal.ThrowExceptionForHR(_text(_renderer,slot,0,0,IntPtr.Zero,0));
            _textureBytes-=_textBytes[slot];_textBytes[slot]=0;_textImages[slot]=null;_textUsed[slot]=0;
        }
        internal void AddText(Bitmap image, RectangleF destination, RectangleF clip, float opacity, bool nearest)
        {
            if(clip.Width<=0 || clip.Height<=0 || opacity<=0) return;
            if(_commandCount==_commands.Length) throw new InvalidOperationException("Too many lyric layers.");
            int slot=Array.IndexOf(_textImages,image);
            if(slot<0) {
                int bytes=checked(image.Width*image.Height*4);
                if(bytes>24*1024*1024) throw new InvalidOperationException("Lyric texture exceeds budget.");
                // Eviction cannot remove any texture referenced in this frame.
                while(_textureBytes+bytes>24*1024*1024) {
                    int oldest=-1;
                    for(int i=0;i<24;i++) if(_textBytes[i]>0 && _textUsed[i]!=_capture && (oldest<0 || _textUsed[i]<_textUsed[oldest])) oldest=i;
                    if(oldest<0) throw new InvalidOperationException("Active lyric textures exceed budget.");
                    Evict(oldest);
                }
                slot=OldestUnused();Evict(slot);
                var stamp=Profile?.Stamp ?? 0;
                var bits=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                try { Marshal.ThrowExceptionForHR(_text(_renderer,slot,(uint)image.Width,(uint)image.Height,bits.Scan0,(uint)bits.Stride)); }
                finally { image.UnlockBits(bits);Profile?.End(RenderMetric.LyricTextureUpload,stamp); }
                _textImages[slot]=image;_textBytes[slot]=bytes;_textureBytes+=bytes;
            }
            _textUsed[slot]=_capture;
            _commands[_commandCount++]=new TextCommand {Slot=slot,Destination=new Rect(destination),Clip=new Rect(clip),Opacity=opacity,Nearest=nearest?1:0};
        }
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
                // Resolve the entire ABI before creating resources. An older
                // helper safely selects GDI instead of reading a mismatched ABI.
                _text = Export<TextFn>("DL_Text"); _lyrics = Export<LyricsFn>("DL_Lyrics");
                _dancerTexture = Export<DancerTextureFn>("DL_DancerTexture"); _dancers = Export<DancersFn>("DL_Dancers");
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
            if(Profile?.Stamp > 0) Profile.Add(RenderMetric.LyricTextureMiB,_textureBytes/1048576d);
            if(Profile?.Stamp > 0) Profile.Add(RenderMetric.DancerTextureMiB,_dancerBytes/1048576d);
            Marshal.ThrowExceptionForHR(_draw(_renderer, ref _scene, diagnosticOutput));
        }
        public void Dispose()
        {
            Foreground?.Dispose(); Foreground = null;
            Array.Clear(_textImages,0,_textImages.Length);
            _dancerPreparation?.Dispose(); _dancerPreparation = null;
            if (_renderer != IntPtr.Zero) { _destroy(_renderer); _renderer = IntPtr.Zero; }
            if (_library != IntPtr.Zero) { FreeLibrary(_library); _library = IntPtr.Zero; }
        }
    }
}
