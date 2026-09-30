using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Windows.Forms;
using MusicBeePlugin;

// Uses the shipped plugin and actual helper ABI, including its x86 calling
// convention. On headless CI, unavailable hardware must cleanly fall back.
class GpuCompositionChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object Get(object o,string n) => o.GetType().GetField(n,Flags).GetValue(o);
    static void Set(object o,string n,object v) => o.GetType().GetField(n,Flags).SetValue(o,v);
    static object Call(object o,string n,params object[] a) => o.GetType().GetMethod(n,Flags).Invoke(o,a);
    static void Check(bool ok,string why) { if(!ok)throw new Exception(why); }
    [STAThread] static void Main(string[] args)
    {
        Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mb_DesktopLyrics.dll"));
        Run(args);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Run(string[] args)
    {
        var assembly=typeof(Plugin).Assembly;
        var type=assembly.GetType("MusicBeePlugin.FrmLyricsWindow");
        var settings=SettingsObj.GenerateDefault();settings.PartyMode=true;settings.DisableDeezerBpmLookup=true;
        var ctor=type.GetConstructors()[0];var pars=ctor.GetParameters();var values=new object[pars.Length];
        values[0]=settings;values[1]=new Plugin.MusicBeeApiInterface {Player_GetPosition=()=>10000,
            Player_GetPlayState=()=>Plugin.PlayState.Playing,NowPlaying_GetFileUrl=()=>null};
        values[2]=Activator.CreateInstance(pars[2].ParameterType,true);
        var folder=Path.Combine(Path.GetTempPath(),"DesktopLyrics-gpu-check-"+Guid.NewGuid());
        values[10]=Activator.CreateInstance(pars[10].ParameterType,Flags,null,new object[]{folder},null);
        using(var form=(Form)ctor.Invoke(values))
        {
            var hwnd=form.Handle;form.ClientSize=new Size(960,540);
            Set(form,"_line1","Current lyrics, with clear edges");Set(form,"_line2","English translation above the main line");
            Set(form,"_nextLine","Upcoming lyrics remain visible");Set(form,"_songTitle","GPU validation");
            Set(form,"_songArtist","Synthetic fixture");
            var bars=(float[])Get(form,"_bars");for(int i=0;i<bars.Length;i++)bars[i]=.15f+i*.015f;
            bool hardware=(bool)Call(form,"TryDrawGpu");
            if(!hardware) {
                Check((bool)Get(form,"_gpuFailed") || SystemInformation.TerminalServerSession,"Unavailable GPU must latch fallback");
                Console.WriteLine("Hardware unavailable; GDI fallback: "+Get(form,"_gpuFailure"));
                Check(args.Length==0 || args[0]!="require-hardware","Local hardware test did not run");
            }
            else {
                PixelCompare(assembly,form);
                foreach(var size in new[]{new Size(814,272),new Size(3840,2160)}) {
                    form.WindowState=size.Width>1000?FormWindowState.Maximized:FormWindowState.Normal;
                    form.ClientSize=size;
                    Set(form,"_previousLine1","An earlier lyric makes room for the next line");
                    Set(form,"_previousNextLine",Get(form,"_line1"));
                    Set(form,"_previousLine2",null);
                    PixelCompare(assembly,form,.25);
                    Set(form,"_previousLine2","The previous translation fades away");
                    PixelCompare(assembly,form,.65);
                }
                Set(form,"_transitionStarted",0L);
                form.WindowState=FormWindowState.Normal;form.ClientSize=new Size(960,540);
                Call(form,"TryDrawGpu");
                var gpu=Get(form,"_gpu");var bitmap=(Bitmap)gpu.GetType().GetProperty("Foreground",Flags).GetValue(gpu);
                {
                    var generation=(long)Get(gpu,"_capture");
                    Set(form,"_line1","Changed but not invalidated, cache must remain");
                    Check((bool)Call(form,"TryDrawGpu"),"Cached frame failed");
                    Check((long)Get(gpu,"_capture")==generation,"Static foreground and lyric commands should be reused");
                    form.Invalidate();Call(form,"TryDrawGpu");
                    Check((long)Get(gpu,"_capture")>generation,"Invalidated lyrics must refresh");
                    PixelCompare(assembly,form);
                }
                // Exercise resize/release repeatedly, including returning to 4K.
                for(int i=0;i<12;i++) {
                    form.ClientSize=i%2==0?new Size(3840,2160):new Size(814,272);
                    Check((bool)Call(form,"TryDrawGpu"),"Resize failed");
                    Check(((Bitmap)Get(form,"_gpu").GetType().GetProperty("Foreground",Flags).GetValue(Get(form,"_gpu"))).Size==form.ClientSize,"Stale foreground size");
                }
                // Inject the actual HRESULT a lost D2D target returns, at the
                // managed/native boundary. This does not reset the display driver.
                gpu=Get(form,"_gpu");var draw=gpu.GetType().GetField("_draw",Flags);
                var parameters=draw.FieldType.GetMethod("Invoke").GetParameters().Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
                draw.SetValue(gpu,Expression.Lambda(draw.FieldType,Expression.Constant(unchecked((int)0x8899000C)),parameters).Compile());
                Check(!(bool)Call(form,"TryDrawGpu"),"Device-loss HRESULT must fall back");
                Check(Get(form,"_gpu")==null && (bool)Get(form,"_gpuFailed"),"Failed resources must release and latch");
                Check(!(bool)Call(form,"TryDrawGpu"),"Failure must not retry every frame");
                TextureBudget(assembly,form);
                Console.WriteLine("Hardware composition, pixels, cache, repeated resize and injected device-loss fallback passed.");
            }
            using(var fallback=new Bitmap(form.ClientSize.Width,form.ClientSize.Height))
            using(var g=Graphics.FromImage(fallback)) Call(form,"OnPaint",new PaintEventArgs(g,form.ClientRectangle));
            Set(form,"_gpuFailed",false);settings.TransparentCanvas=true;
            Check(!(bool)Call(form,"TryDrawGpu") && Get(form,"_gpu")==null,"Transparent canvas must use GDI");
            settings.TransparentCanvas=false;Set(form,"_gpuDisabled",true);
            Check(!(bool)Call(form,"TryDrawGpu"),"Comparison toggle must use GDI");
        }
        settings.Font.Dispose();
        Console.WriteLine("GPU checks complete, process bits "+(IntPtr.Size*8));
    }
    static bool EqualPixels(Bitmap a,Bitmap b) {
        for(int y=0;y<a.Height;y+=3)for(int x=0;x<a.Width;x+=3)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;
        return true;
    }
    static void TextureBudget(Assembly assembly,Form form)
    {
        using(var renderer=(IDisposable)Activator.CreateInstance(assembly.GetType("MusicBeePlugin.GpuSceneRenderer"),Flags,null,
            new object[]{form.Handle,form.ClientSize,false},null)) {
            for(int frame=0;frame<40;frame++) {
                Call(renderer,"BeginLyrics");
                // Distinct 4 MiB rasters force byte-budget eviction before the
                // 24-slot cap. Disposing these borrowed rasters is safe after upload.
                using(var bitmap=new Bitmap(1024,1024,PixelFormat.Format32bppPArgb))
                    Call(renderer,"AddText",bitmap,new RectangleF(0,0,100,100),new RectangleF(0,0,100,100),1f,true);
                Call(renderer,"CommitLyrics");
                Check((int)Get(renderer,"_textureBytes")<=24*1024*1024,"Text cache exceeded 24 MiB");
            }
            Check(((Bitmap[])Get(renderer,"_textImages")).Count(b=>b!=null)<=6,"Byte-budget eviction failed");
        }
        Console.WriteLine("Lyric texture cache remained bounded through repeated uploads/evictions.");
    }
    static void PixelCompare(Assembly assembly,Form form,double? progress=null)
    {
        using(var expected=new Bitmap(form.ClientSize.Width,form.ClientSize.Height))
        using(var actual=new Bitmap(expected.Width,expected.Height))
        using(var renderer=(IDisposable)Activator.CreateInstance(assembly.GetType("MusicBeePlugin.GpuSceneRenderer"),Flags,null,
            new object[]{form.Handle,form.ClientSize,true},null))
        {
            long instant=Stopwatch.GetTimestamp();
            var previous=new[]{Get(form,"_previousLine1"),Get(form,"_previousLine2"),Get(form,"_previousNextLine")};
            Action reset=()=> {
                Set(form,"_previousLine1",previous[0]);Set(form,"_previousLine2",previous[1]);Set(form,"_previousNextLine",previous[2]);
                Set(form,"_transitionStarted",progress.HasValue?instant-(long)(progress.Value*.3*Stopwatch.Frequency):0L);
            };
            // Warm GDI caches before fixing the comparison phase. Its initial
            // full-4K backdrop allocation can outlast an entire transition.
            for(int warm=0;warm<2;warm++) {
                reset();using(var g=Graphics.FromImage(expected))Call(form,"DrawScene",new PaintEventArgs(g,form.ClientRectangle),false,instant);
            }
            reset();
            using(var g=Graphics.FromImage(expected))Call(form,"DrawScene",new PaintEventArgs(g,form.ClientRectangle),false,instant);
            var foreground=(Bitmap)renderer.GetType().GetProperty("Foreground",Flags).GetValue(renderer);
            reset();
            Call(form,"RasterGpuForeground",renderer,instant);
            Call(renderer,"Upload");
            using(var g=Graphics.FromImage(actual)) {
                var dc=g.GetHdc();
                try {Call(renderer,"Draw",Get(form,"_palette"),Get(form,"_bars"),true,dc);}
                finally{g.ReleaseHdc(dc);}
            }
            double error=0;int count=0;
            int step=actual.Width>1000?2:1;
            for(int y=0;y<actual.Height;y+=step)for(int x=0;x<actual.Width;x+=step) {
                var a=actual.GetPixel(x,y);var b=expected.GetPixel(x,y);
                error+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);count+=3;
            }
            Console.WriteLine("RGB mean absolute error (0-255): "+(error/count).ToString("F3"));
            var output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gpu-check-images");Directory.CreateDirectory(output);
            var label=expected.Width+"-"+(progress.HasValue?progress.Value.ToString("F2",System.Globalization.CultureInfo.InvariantCulture):"still");
            expected.Save(Path.Combine(output,"gdi-"+label+".png"),ImageFormat.Png);actual.Save(Path.Combine(output,"gpu-"+label+".png"),ImageFormat.Png);
            Check(error/count<3,"GPU appearance differs materially from GDI; inspect pixel comparison");
        }
    }
}
