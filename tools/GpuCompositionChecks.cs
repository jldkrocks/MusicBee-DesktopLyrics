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
    static void WaitPreparationIdle(object preparation)
    {
        var wait = Stopwatch.StartNew();
        while (true)
        {
            // The worker clears _running and releases its bitmaps inside this
            // lock. Reading the flag without it can observe partial cleanup.
            lock (Get(preparation, "_gate"))
                if (!(bool)Get(preparation, "_running")) return;
            Check(wait.ElapsedMilliseconds < 5000, "Dancer preparation cleanup timed out");
            System.Threading.Thread.Sleep(1);
        }
    }
    static void WaveformChecks(Assembly assembly)
    {
        var file=Path.Combine(Path.GetTempPath(),"DesktopLyrics-wave-"+Guid.NewGuid()+".wav");
        try {
            const int rate=44100,count=rate*2;
            using(var w=new BinaryWriter(File.Create(file))) {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));w.Write(count*2);
                for(int n=0;n<count;n++)w.Write((short)((n==rate/2 || n==rate*3/2)?30000:0));
            }
            var read=assembly.GetType("MusicBeePlugin.TimelineWaveform").GetMethod("Read",BindingFlags.Static|BindingFlags.NonPublic);
            Func<string,double,double,System.Threading.CancellationToken,object> decode=(path,start,length,token)=>
                System.Threading.Tasks.Task.Run(()=>read.Invoke(null,new object[]{path,start,length,token})).Result;
            var result=decode(file,.4,.3,System.Threading.CancellationToken.None);
            var peaks=(float[])Get(result,"Peaks");
            Check(peaks!=null,"WAV decode failed: "+Get(result,"Error"));
            int highest=Array.IndexOf(peaks,peaks.Max());
            Check(Math.Abs(.4+highest*.3/peaks.Length-.5)<.002 && peaks.Max()>.9,"Waveform timestamp or amplitude shifted");
            var rms=(float[])Get(result,"Rms");var attacks=(float[])Get(result,"Attacks");
            Check(rms!=null && rms.Length==peaks.Length && rms.Max()>0 && rms.Max()<peaks.Max(),"RMS must retain energy without filling to peak amplitude");
            Check(attacks!=null && attacks.Max()>.9 && Math.Abs(.4+Array.IndexOf(attacks,attacks.Max())*.3/attacks.Length-.5)<.01,"Attack lane must locate the known impulse");
            Check(Get(decode(file,0,61,System.Threading.CancellationToken.None),"Peaks")==null,"Oversized waveform range accepted");
            Check(Get(decode(file,0,1,new System.Threading.CancellationToken(true)),"Peaks")==null,"Cancelled waveform decoded");
            Check(Get(decode(file+".missing",0,1,System.Threading.CancellationToken.None),"Peaks")==null,"Missing audio must fail safely");
            Console.WriteLine("Native waveform timing, amplitude, bounds, cancellation and missing-file checks passed.");
        }finally{if(File.Exists(file))File.Delete(file);}
    }
    [STAThread] static void Main(string[] args)
    {
        Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mb_DesktopLyrics.dll"));
        Run(args);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Run(string[] args)
    {
        var assembly=typeof(Plugin).Assembly;
        WaveformChecks(assembly);
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
                Check((bool)Get(form,"_gpuOutlineText"),"User-approved outlines must default on");
                ArtworkChecks(assembly,form);
                OutlineChecks(assembly,form);
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
                RetainedForegroundChecks(assembly,form);
                DancerHandoffChecks(assembly,form);
                DancerChecks(assembly,form);
                // A raster-worker error must select the same reliable fallback.
                form.WindowState=FormWindowState.Maximized;Set(form,"_partyVisualValid",true);
                Check((bool)Call(form,"TryDrawGpu"),"Preparation failure fixture could not start");
                var failedPreparation=Get(Get(form,"_gpu"),"_dancerPreparation");
                lock(Get(failedPreparation,"_gate"))Set(failedPreparation,"_failure",new InvalidOperationException("injected raster failure"));
                Check(!(bool)Call(form,"TryDrawGpu") && Get(form,"_gpu")==null && (bool)Get(form,"_gpuFailed"),"Preparation exception must release GPU and latch GDI fallback");
                WaitPreparationIdle(failedPreparation);
                Check(((Bitmap[])Get(failedPreparation,"_sources")).All(b=>b==null),"In-flight disposal retained source bitmaps");
                Check(((Bitmap[])Get(failedPreparation,"_ready")).All(b=>b==null),"In-flight disposal retained pose bitmaps");
                Set(form,"_partyVisualValid",false);Set(form,"_gpuFailed",false);
                Check((bool)Call(form,"TryDrawGpu"),"Independent device-loss fixture could not restart");
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
            PacingLifecycle(form,settings);
        }
        settings.Font.Dispose();
        Console.WriteLine("GPU checks complete, process bits "+(IntPtr.Size*8));
    }

    static void Pump(int milliseconds)
    {
        var clock=Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<milliseconds) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
    }
    static void PacingLifecycle(Form form,SettingsObj settings)
    {
        settings.PartyMode=false;
        form.WindowState=FormWindowState.Normal;form.ClientSize=new Size(420,190);
        form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-24000,-24000);
        form.ShowInTaskbar=false;form.TopMost=false;
        Set(form,"_gpuDisabled",false);Set(form,"_gpuFailed",false);
        form.Show();Pump(180);
        var timer=(System.Windows.Forms.Timer)Get(form,"_animationTimer");
        var pacer=Get(form,"_framePacer");
        if(pacer!=null && !(bool)Get(form,"_gpuFailed")) {
            Check(!timer.Enabled,"GPU pacing must replace rather than duplicate the old timer");
            form.Hide();Pump(40);
            Check(!timer.Enabled && !(bool)Get(pacer,"_active"),"Hidden window must stop frame requests");
            form.Show();Pump(50);
            Check((bool)Get(pacer,"_active"),"Showing the window must resume frame requests");
            Set(form,"_renderTargetFps",60);Call(form,"ConfigureFramePacing");Pump(40);
            Check((int)Get(pacer,"_fps")==60,"Target change must update presentation scheduling");
            Call(form,"RecreateHandle");Pump(60);
            Check(!ReferenceEquals(pacer,Get(form,"_framePacer")),"Recreated HWND must have a new destination/pacer");
            pacer=Get(form,"_framePacer");
            Set(pacer,"_failure","injected timer failure");Call(form,"OnFrameMessage",0);
            Check(timer.Enabled,"Worker failure must recover with compatibility timer");
        }
        Set(form,"_gpuDisabled",true);Call(form,"ConfigureFramePacing");
        Check(timer.Enabled,"GDI rendering must retain compatibility timer");
        form.Hide();Check(!timer.Enabled,"Hiding must also stop the compatibility timer");
        Console.WriteLine("Window pacing lifecycle, target selection, HWND replacement and timer/GDI fallback passed.");
    }
    static bool EqualPixels(Bitmap a,Bitmap b) {
        for(int y=0;y<a.Height;y+=3)for(int x=0;x<a.Width;x+=3)if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;
        return true;
    }
    static void RetainedForegroundChecks(Assembly assembly,Form form)
    {
        // Isolate lyric motion from palette/notice/UI invalidations. Production
        // TryDrawGpu must update commands without uploading the full overlay.
        form.WindowState=FormWindowState.Maximized;form.ClientSize=new Size(3840,2160);
        Set(form,"_transitionStarted",0L);Set(form,"_paletteStarted",0L);
        form.Invalidate();Check((bool)Call(form,"TryDrawGpu"),"Initial retained overlay failed");
        var profileType=assembly.GetType("MusicBeePlugin.RenderProfile");
        var profile=Activator.CreateInstance(profileType,Flags,null,new object[]{
            new System.Collections.Generic.Dictionary<string,object>(),null,null,30d,0d,false},null);
        Set(form,"_renderProfile",profile);
        var gpu=Get(form,"_gpu");
        var foreground=(Bitmap)gpu.GetType().GetProperty("Foreground",Flags).GetValue(gpu);
        using(var original=(Bitmap)foreground.Clone()) {
            var party=Get(form,"_partyButton");var queue=Get(form,"_queueCard");
            var generation=(long)Get(gpu,"_capture");
            for(int i=0;i<12;i++) {
                Set(form,"_transitionStarted",Stopwatch.GetTimestamp()-(long)(.02*Stopwatch.Frequency));
                Check((bool)Call(form,"TryDrawGpu"),"Lyric-only frame failed");
            }
            Check((long)Get(gpu,"_capture")==generation+12,"Each lyric frame must update GPU commands");
            Check(EqualPixels(original,foreground),"Lyric-only motion changed retained overlay pixels");
            Check(party.Equals(Get(form,"_partyButton")) && queue.Equals(Get(form,"_queueCard")),"Retained hit targets drifted across frames");
        }
        var samples=(Array)Get(profile,"_samples");
        var metric=assembly.GetType("MusicBeePlugin.RenderMetric");
        Func<string,int> count=name=>(samples.GetValue(Convert.ToInt32(Enum.Parse(metric,name))) as System.Collections.ICollection)?.Count ?? 0;
        Check(count("ForegroundUpload")==0 && count("ForegroundRaster")==0 && count("LyricCompose")==12,
            "Lyric-only frames must eliminate full foreground redraws/uploads");
        Call(profile,"Finish","retained transition check");
        Set(form,"_renderProfile",null);
        // Final layout cleanup plus explicit UI invalidation still refresh.
        Set(form,"_transitionStarted",0L);Call(form,"TryDrawGpu");
        Check(!(bool)Get(form,"_foregroundTransition"),"Completed transition must finalize retained layout");
        form.Invalidate();Check((bool)Call(form,"TryDrawGpu") && !(bool)Get(form,"_foregroundDirty"),"UI invalidation must refresh retained foreground");
        Console.WriteLine("Lyric-only frames retained overlay pixels/hit targets and eliminated all 12 full uploads.");
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
    static void DancerHandoffChecks(Assembly assembly, Form form)
    {
        var type=assembly.GetType("MusicBeePlugin.PartyDancerWindow");
        var pair=new Form[2];
        try {
            for(int c=0;c<2;c++)pair[c]=(Form)Activator.CreateInstance(type,new object[]{c==0?"MusicBeePlugin.PartyRem.png":"MusicBeePlugin.PartyRam.png",true});
            double worstCall=0;
            foreach(int height in new[]{454,500,454}) {
                int width=(int)Math.Round(height*180d/353);
                var watch=Stopwatch.StartNew();
                while(true) {
                    var call=Stopwatch.StartNew();
                    for(int c=0;c<2;c++)Call(pair[c],"Present",new Rectangle(-20000+c*1000,-20000,width,height),0,1f,0f,0f);
                    worstCall=Math.Max(worstCall,call.Elapsed.TotalMilliseconds);
                    if(pair.All(d=>new[]{0,3,6,9}.All(f=>((Bitmap)Call(d,"CachedPose",f))?.Size==new Size(width,height))))break;
                    Check(watch.ElapsedMilliseconds<10000,"Restored dancer preparation timed out");
                    System.Threading.Thread.Sleep(1);
                }
                foreach(var d in pair) {
                    Check(Get(d,"_sheet")==null,"Production GDI constructor decoded a sheet synchronously");
                    var prep=Get(d,"_preparation");
                    WaitPreparationIdle(prep);
                    Check(((Bitmap[])Get(prep,"_sources")).All(b=>b==null),"Hidden restored cache retained source sheets");
                }
            }
            form.WindowState=FormWindowState.Maximized;form.ClientSize=new Size(3840,2160);
            using(var renderer=(IDisposable)Activator.CreateInstance(assembly.GetType("MusicBeePlugin.GpuSceneRenderer"),Flags,null,new object[]{form.Handle,form.ClientSize,true},null)) {
                for(int c=0;c<2;c++)foreach(int f in new[]{0,3,6,9})Call(renderer,"SeedDancer",c,f,Call(pair[c],"CachedPose",f));
                foreach(int f in new[]{0,3,6,9}) {
                    Call(renderer,"BeginDancers");
                    for(int c=0;c<2;c++)Call(renderer,"AddDancer",c,new Rectangle(c*2000,0,829,1626),f,1f,0f,0f);
                    Check((int)Get(renderer,"_dancerCount")==2,"Maximize handoff temporarily omitted a dancer");
                    Call(renderer,"CommitDancers");
                }
                Check((int)Get(renderer,"_dancerBytes")<=64*1024*1024,"Seeded dancer cache exceeded its bound");
            }
            // Production handoff retains (hides) the same windows instead of
            // decoding new sheets when restored, including native drag restore.
            Set(form,"_leftDancer",pair[0]);Set(form,"_rightDancer",pair[1]);
            Set(form,"_partyVisualValid",true);Set(form,"_partyVisualFrame",0);
            Set(form,"_leftPartyBounds",form.RectangleToScreen(new Rectangle(0,200,829,1626)));
            Set(form,"_rightPartyBounds",form.RectangleToScreen(new Rectangle(3011,200,829,1626)));
            Check((bool)Call(form,"TryDrawGpu"),"Handoff render failed");
            Check(ReferenceEquals(Get(form,"_leftDancer"),pair[0]) && !pair[0].IsDisposed && !pair[0].Visible,"Maximize discarded or exposed restored dancer window");
            Check((int)Get(Get(form,"_gpu"),"_dancerCount")==2,"Production maximize did not seed both dancers");
            Set(form,"_partyVisualValid",false);
            Console.WriteLine("Async restored poses and immediate seeded maximize handoff passed; maximum pair Present call ms: "+worstCall.ToString("F3"));
        } finally {
            Set(form,"_leftDancer",null);Set(form,"_rightDancer",null);
            foreach(var d in pair)d?.Dispose();
        }
    }
    static void WaitDancers(object renderer,Rectangle[] bounds,int frame,float impact,float sway,float anticipation)
    {
        var watch=Stopwatch.StartNew();
        while(true) {
            Call(renderer,"BeginDancers");
            for(int i=0;i<2;i++)Call(renderer,"AddDancer",i,bounds[i],frame,impact,sway,anticipation);
            var sizes=(Size[])Get(renderer,"_dancerSizes");
            if(sizes[frame/3]==bounds[0].Size && sizes[4+frame/3]==bounds[1].Size)return;
            Check(watch.ElapsedMilliseconds<10000,"Asynchronous dancer preparation timed out");
            System.Threading.Thread.Sleep(1);
        }
    }
    static void DancerChecks(Assembly assembly,Form form)
    {
        var dancerType=assembly.GetType("MusicBeePlugin.PartyDancerWindow");
        form.WindowState=FormWindowState.Maximized;form.ClientSize=new Size(3840,2160);
        using(var left=(Form)Activator.CreateInstance(dancerType,new object[]{"MusicBeePlugin.PartyRem.png"}))
        using(var right=(Form)Activator.CreateInstance(dancerType,new object[]{"MusicBeePlugin.PartyRam.png"}))
        using(var renderer=(IDisposable)Activator.CreateInstance(assembly.GetType("MusicBeePlugin.GpuSceneRenderer"),Flags,null,
            new object[]{form.Handle,form.ClientSize,true},null))
        using(var expected=new Bitmap(3840,2160))
        using(var actual=new Bitmap(3840,2160)) {
            Set(form,"_transitionStarted",0L);Set(form,"_paletteStarted",0L);
            Call(form,"RasterGpuForeground",renderer,null);Call(renderer,"Upload");
            double worst=0;
            foreach(int height in new[]{454,1626})
            foreach(int frame in new[]{0,3,6,9})
            foreach(float impact in new[]{0f,1f,2.5f})
            foreach(float translation in new[]{float.NaN,-.5f,.5f}) {
                if(!float.IsNaN(translation) && (frame!=0 || height!=1626 || impact!=0))continue;
                int width=(int)Math.Round(height*180d/353);
                var bounds=new[]{new Rectangle(10,200,width,height),new Rectangle(3830-width,200,width,height)};
                float sway=impact==0?-.014f:.025f,anticipation=impact==0?.8f:0;
                if(!float.IsNaN(translation))sway=translation/width;
                using(var g=Graphics.FromImage(expected)) {
                    Call(form,"DrawScene",new PaintEventArgs(g,form.ClientRectangle),false,null);
                    foreach(var pair in new[]{new{Dancer=left,Index=0},new{Dancer=right,Index=1}}) {
                        Call(pair.Dancer,"Present",new Rectangle(-20000,-20000,width,height),frame,impact,sway,anticipation);
                        g.DrawImageUnscaled((Bitmap)Get(pair.Dancer,"_surface"),bounds[pair.Index].Location);
                    }
                }
                WaitDancers(renderer,bounds,frame,impact,sway,anticipation);
                Call(renderer,"CommitDancers");
                using(var g=Graphics.FromImage(actual)) {
                    var dc=g.GetHdc();try{Call(renderer,"Draw",Get(form,"_palette"),Get(form,"_bars"),true,dc);}finally{g.ReleaseHdc(dc);}
                }
                double error=0;int count=0;
                foreach(var b in bounds)for(int y=b.Top;y<b.Bottom;y+=2)for(int x=b.Left;x<b.Right;x+=2) {
                    var a=actual.GetPixel(x,y);var e=expected.GetPixel(x,y);
                    error+=Math.Abs(a.R-e.R)+Math.Abs(a.G-e.G)+Math.Abs(a.B-e.B);count+=3;
                }
                worst=Math.Max(worst,error/count);
                if(error/count>=3) {
                    expected.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gpu-check-images","dancer-failure-gdi.png"));
                    actual.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gpu-check-images","dancer-failure-gpu.png"));
                    Console.WriteLine("Dancer mismatch height="+height+" frame="+frame+" impact="+impact);
                }
                Check(error/count<3,"Dancer region differs from existing GDI transform: "+error/count);
                Check((int)Get(renderer,"_dancerBytes")<=64*1024*1024,"Dancer texture budget exceeded");
            }
            actual.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gpu-check-images","dancers-gpu.png"));
            expected.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gpu-check-images","dancers-gdi.png"));
            // Rapid obsolete requests must converge to only the latest size.
            for(int n=0;n<40;n++) {
                Call(renderer,"BeginDancers");
                for(int c=0;c<2;c++)Call(renderer,"AddDancer",c,new Rectangle(0,0,400+n,800+n),n%4*3,0f,0f,0f);
            }
            var latest=new[]{new Rectangle(0,0,500,980),new Rectangle(600,0,500,980)};
            foreach(int frame in new[]{0,3,6,9})WaitDancers(renderer,latest,frame,0,0,0);
            Check(((Size[])Get(renderer,"_dancerSizes")).All(s=>s==latest[0].Size),"Obsolete resize result replaced current poses");
            var preparation=Get(renderer,"_dancerPreparation");
            Call(renderer,"ClearDancers");Check((int)Get(renderer,"_dancerBytes")==0,"Restore must release dancer cache");
            WaitPreparationIdle(preparation);
            Check(((Bitmap[])Get(preparation,"_sources")).All(b=>b==null),"Restore must release worker sources");
            Check(((Bitmap[])Get(preparation,"_ready")).All(b=>b==null),"Restore must release pending poses");
            Check(Get(renderer,"_dancerPreparation")==null,"Restore retained preparation owner");
            Console.WriteLine("Dancer-region RGB mean error, worst case: "+worst.ToString("F3"));
        }
        // Production path: no clock calls and no foreground refresh for a
        // changing pose. The same stored snapshot is reused after failure.
        Set(form,"_partyVisualValid",true);Set(form,"_partyVisualFrame",0);
        Set(form,"_partyVisualImpact",1f);
        Set(form,"_leftPartyBounds",form.RectangleToScreen(new Rectangle(0,200,829,1626)));
        Set(form,"_rightPartyBounds",form.RectangleToScreen(new Rectangle(3011,200,829,1626)));
        form.Invalidate();Check((bool)Call(form,"TryDrawGpu"),"GPU dancers failed");
        var gpu=Get(form,"_gpu");var generation=Get(gpu,"_capture");
        foreach(int frame in new[]{0,3,6,9})WaitDancers(gpu,
            new[]{new Rectangle(0,200,829,1626),new Rectangle(3011,200,829,1626)},frame,1f,0,0);
        for(int i=0;i<12;i++) {
            Set(form,"_partyVisualFrame",i%4*3);Set(form,"_partyVisualImpact",i*.1f);
            Check((bool)Call(form,"TryDrawGpu"),"Moving GPU dancer failed");
            Check(generation.Equals(Get(gpu,"_capture")),"Dancers forced a foreground refresh");
        }
        Check((int)Get(gpu,"_dancerCount")==2,"Both dancers must be composed");
        form.WindowState=FormWindowState.Normal;Call(form,"TryDrawGpu");
        Check((int)Get(gpu,"_dancerBytes")==0 && (int)Get(gpu,"_dancerCount")==0,"Restored scene retained maximized dancers");
        Set(form,"_partyVisualValid",false);
        Console.WriteLine("Dancer poses, strong rebound/sway, resize cache bounds and retained foreground passed.");
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
            long prime=progress.HasValue?instant-(long)(progress.Value*.15*Stopwatch.Frequency):instant;
            Call(form,"RasterGpuForeground",renderer,prime);
            Call(renderer,"Upload");
            if(progress.HasValue) {
                using(var retained=(Bitmap)foreground.Clone()) {
                    Call(form,"ComposeGpuLyrics",renderer,instant);
                    Check(EqualPixels(retained,foreground),"Lyric composition repainted the overlay");
                }
            }
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
            var label=((bool)Get(form,"_gpuOutlineText")?"outline-":"")+expected.Width+"-"+(progress.HasValue?progress.Value.ToString("F2",System.Globalization.CultureInfo.InvariantCulture):"still");
            expected.Save(Path.Combine(output,"gdi-"+label+".png"),ImageFormat.Png);actual.Save(Path.Combine(output,"gpu-"+label+".png"),ImageFormat.Png);
            Check(error/count<3,"GPU appearance differs materially from GDI; inspect pixel comparison");
        }
    }
    static void OutlineChecks(Assembly assembly,Form form) {
        var rendererType=assembly.GetType("MusicBeePlugin.GpuSceneRenderer");
        using(var renderer=(IDisposable)Activator.CreateInstance(rendererType,Flags,null,new object[]{form.Handle,form.ClientSize,true},null))
        using(var path=new System.Drawing.Drawing2D.GraphicsPath()) {
            path.AddRectangle(new RectangleF(0,0,100,100));
            Func<float,int,Bitmap> draw=(alpha,gradient)=> {
                Call(renderer,"BeginLyrics");
                Call(renderer,"AddOutline",path,path.GetBounds(),new RectangleF(0,0,960,540),100f,100f,1f,3f,alpha,
                    Color.Red,Color.Blue,Color.Black,gradient);
                Call(renderer,"CommitLyrics");
                var image=new Bitmap(960,540);
                using(var g=Graphics.FromImage(image)){var dc=g.GetHdc();try{Call(renderer,"Draw",Get(form,"_palette"),Get(form,"_bars"),false,dc);}finally{g.ReleaseHdc(dc);}}
                return image;
            };
            using(var full=draw(1,0))using(var faded=draw(.4f,0))using(var bg=draw(0,0)) {
                // Includes pixels where shadow, border and fill overlap. A brush-
                // opacity implementation incorrectly darkens these intersections.
                for(int y=99;y<203;y++)for(int x=99;x<203;x++){
                    var a=full.GetPixel(x,y);var b=bg.GetPixel(x,y);var c=faded.GetPixel(x,y);
                    Check(Math.Abs(c.R-(a.R*.4+b.R*.6))<4 && Math.Abs(c.G-(a.G*.4+b.G*.6))<4 && Math.Abs(c.B-(a.B*.4+b.B*.6))<4,
                        "Outline opacity must composite the whole glyph group");
                }
            }
            using(var doubleColor=draw(1,1))using(var triple=draw(1,2)) {
                Check(doubleColor.GetPixel(150,110).R>doubleColor.GetPixel(150,190).R,"Double gradient direction changed");
                Check(triple.GetPixel(150,150).B>triple.GetPixel(150,110).B && triple.GetPixel(150,190).R>triple.GetPixel(150,150).R,"Triple gradient lost middle colour");
            }
            for(int i=0;i<40;i++)using(var p=new System.Drawing.Drawing2D.GraphicsPath()) {
                p.AddRectangle(new RectangleF(0,0,30+i,30));Call(renderer,"BeginLyrics");
                Call(renderer,"AddOutline",p,p.GetBounds(),new RectangleF(0,0,960,540),0f,0f,.63f,2f,1f,Color.White,Color.White,Color.Black,0);
                Call(renderer,"CommitLyrics");
            }
            Check(((Array)Get(renderer,"_paths")).Cast<object>().Count(x=>x!=null)<=24 && (int)Get(renderer,"_pointCount")<=262144,"Outline cache grew past budget");
            var points=Enumerable.Range(0,60000).Select(i=>new PointF(i%100,(i/100)%100)).ToArray();
            var heavy=new System.Drawing.Drawing2D.GraphicsPath[5];
            try {
                for(int i=0;i<5;i++){
                    heavy[i]=new System.Drawing.Drawing2D.GraphicsPath();heavy[i].AddLines(points);
                    Call(renderer,"BeginLyrics");Call(renderer,"AddOutline",heavy[i],heavy[i].GetBounds(),new RectangleF(0,0,960,540),0f,0f,1f,2f,1f,Color.White,Color.White,Color.Black,0);
                    Check((int)Get(renderer,"_pointCount")<=262144,"Point-budget eviction failed");
                }
                Call(renderer,"BeginLyrics");bool blocked=false;
                try {for(int i=0;i<5;i++)Call(renderer,"AddOutline",heavy[i],heavy[i].GetBounds(),new RectangleF(0,0,960,540),0f,0f,1f,2f,1f,Color.White,Color.White,Color.Black,0);}
                catch(TargetInvocationException e){blocked=e.InnerException is InvalidOperationException;}
                Check(blocked,"Point budget evicted an active path instead of falling back");
            }finally{foreach(var p in heavy)p?.Dispose();}
            Call(renderer,"BeginLyrics");Call(renderer,"CommitLyrics");Check((int)Get(renderer,"_outlineCount")==0,"Old outline commands survived reset");
        }
        Set(form,"_gpuOutlineText",true);
        Call(form,"ReleaseGpu");Check((bool)Call(form,"TryDrawGpu"),"Outline failure fixture failed to start");
        var gpu=Get(form,"_gpu");var upload=gpu.GetType().GetField("_outline",Flags);
        var parameters=upload.FieldType.GetMethod("Invoke").GetParameters().Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
        upload.SetValue(gpu,Expression.Lambda(upload.FieldType,Expression.Constant(unchecked((int)0x8899000c)),parameters).Compile());
        Set(form,"_line1","A fresh path tests native outline failure");form.Invalidate();
        Check(!(bool)Call(form,"TryDrawGpu") && Get(form,"_gpu")==null && (bool)Get(form,"_gpuFailed"),"Outline upload error failed to restore GDI");
        Set(form,"_gpuFailed",false);Set(form,"_line1","Current lyrics, with clear edges");
        foreach(var size in new[]{new Size(814,272),new Size(3840,2160)}) {
            form.WindowState=size.Width>1000?FormWindowState.Maximized:FormWindowState.Normal;form.ClientSize=size;
            Set(form,"_previousLine1","Café a\u0301 flowing text");Set(form,"_previousNextLine",Get(form,"_line1"));
            PixelCompare(assembly,form,.25);PixelCompare(assembly,form,.65);
        }
        Call(form,"ReleaseGpu");Set(form,"_gpuOutlineText",false);
        form.WindowState=FormWindowState.Normal;form.ClientSize=new Size(960,540);Set(form,"_transitionStarted",0L);
        Check((bool)Call(form,"TryDrawGpu"),"Bitmap rendering could not resume after outlines");
        Console.WriteLine("Outline group opacity, gradients, cache bounds, transitions and bitmap switch passed.");
    }
    static void ArtworkChecks(Assembly assembly,Form form) {
        Func<Color,Bitmap> cover=color=>{var b=new Bitmap(256,256,PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(b))g.Clear(color);return b;};
        long now=Stopwatch.GetTimestamp();long duration=(long)(Stopwatch.Frequency*.55);
        Call(form,"StartArtwork",cover(Color.Red),now);Call(form,"AdvanceArtwork",now+duration+1);
        Check(Get(form,"_previousArtwork")==null && (float)Get(form,"_artworkProgress")==1,"Completed art fade retained history");
        now+=duration+1;Call(form,"StartArtwork",cover(Color.Blue),now);Call(form,"AdvanceArtwork",now+duration/2);
        Check(Math.Abs((float)Get(form,"_artworkProgress")-.5)<.001,"Artwork duration disagrees with background duration");
        PixelCompare(assembly,form);
        var gpu=Get(form,"_gpu");form.Invalidate();Call(form,"TryDrawGpu");gpu=Get(form,"_gpu");
        var generation=(long)Get(gpu,"_capture");var overlay=(Bitmap)gpu.GetType().GetProperty("Foreground",Flags).GetValue(gpu);
        using(var saved=(Bitmap)overlay.Clone()) {
            Call(form,"AdvanceArtwork",now+duration*3/4);Call(form,"TryDrawGpu");
            Check((long)Get(gpu,"_capture")==generation && EqualPixels(saved,overlay),"Artwork opacity forced a foreground raster/upload");
        }
        // Redirect at the midpoint: the new outgoing image must equal the
        // currently visible red/blue blend, not jump to either original cover.
        Call(form,"AdvanceArtwork",now+duration/2);Call(form,"StartArtwork",cover(Color.Green),now+duration/2);
        var snapshot=(Bitmap)Get(form,"_previousArtwork");var pixel=snapshot.GetPixel(128,128);
        Check(snapshot.Size==new Size(256,256) && Math.Abs(pixel.R-127)<3 && Math.Abs(pixel.B-127)<3,"Interrupted art fade jumped or grew its snapshot");
        Call(form,"AdvanceArtwork",now+duration*2);Check(Get(form,"_previousArtwork")==null,"Old snapshot survived fade completion");
        now+=duration*2;Call(form,"StartArtwork",null,now);Call(form,"AdvanceArtwork",now+duration/2);PixelCompare(assembly,form);
        Call(form,"AdvanceArtwork",now+duration+1);Check(Get(form,"_albumArtwork")==null && Get(form,"_previousArtwork")==null,"Missing artwork retained old covers");
        form.Invalidate();Call(form,"TryDrawGpu");
        gpu=Get(form,"_gpu");var artCommand=gpu.GetType().GetField("_artwork",Flags);
        var arguments=artCommand.FieldType.GetMethod("Invoke").GetParameters().Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
        artCommand.SetValue(gpu,Expression.Lambda(artCommand.FieldType,Expression.Constant(unchecked((int)0x8899000c)),arguments).Compile());
        Check(!(bool)Call(form,"TryDrawGpu") && Get(form,"_gpu")==null && (bool)Get(form,"_gpuFailed"),"Artwork native failure did not restore GDI");
        Set(form,"_gpuFailed",false);
        Console.WriteLine("Artwork fade, interruption snapshot, missing cover, cleanup and retained overlay passed.");
    }
}
