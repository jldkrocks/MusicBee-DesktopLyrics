// Exercises the actual plugin drawing code off-screen. It cannot prove display
// presentation or MusicBee UI responsiveness; use the live capture for that.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MusicBeePlugin;

class GdiRenderBenchmark
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static object Get(object o, string n) { return o.GetType().GetField(n, Fields).GetValue(o); }
    static void Set(object o, string n, object value) { o.GetType().GetField(n, Fields).SetValue(o, value); }
    static object Call(object o, string n, params object[] args) { return o.GetType().GetMethod(n, Fields).Invoke(o, args); }

    [STAThread] static void Main(string[] args)
    {
        var plugin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mb_DesktopLyrics.dll");
        if (!File.Exists(plugin)) plugin = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "bin", "Release", "mb_DesktopLyrics.dll"));
        Assembly.LoadFrom(plugin);
        Run(args);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Run(string[] args)
    {
        Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "render-baseline");
        Directory.CreateDirectory(output);
        bool gpuMode = args.Length > 1 && args[1] == "gpu";
        bool paced = args.Length > 2 && args[2] == "paced";
        bool freshLyrics = args.Length > 3 && args[3] == "fresh";
        int messageFps = args.Length > 2 && args[2] == "message120" ? 120 :
            args.Length > 2 && args[2] == "message60" ? 60 : 0;
        bool legacyTimer = args.Length > 2 && args[2] == "messageLegacy";
        var assembly = typeof(Plugin).Assembly;
        var formType = assembly.GetType("MusicBeePlugin.FrmLyricsWindow");
        var profileType = assembly.GetType("MusicBeePlugin.RenderProfile");
        var metricType = assembly.GetType("MusicBeePlugin.RenderMetric");
        var dancerType = assembly.GetType("MusicBeePlugin.PartyDancerWindow");
        foreach (var size in new[] { new Size(960, 540), new Size(3840, 2160) })
        for (int run = 1; run <= 3; run++)
        {
            var settings = SettingsObj.GenerateDefault(); settings.PartyMode = true;
            settings.DisableDeezerBpmLookup = true;
            var api = new Plugin.MusicBeeApiInterface {
                Player_GetPosition = () => 10000, Player_GetPlayState = () => Plugin.PlayState.Playing,
                NowPlaying_GetFileUrl = () => null
            };
            var ctor = formType.GetConstructors()[0]; var pars = ctor.GetParameters();
            var parameters = new object[pars.Length]; parameters[0] = settings; parameters[1] = api;
            parameters[2] = Activator.CreateInstance(pars[2].ParameterType, true);
            parameters[10] = Activator.CreateInstance(pars[10].ParameterType, Fields, null, new object[] { output }, null);
            using (var form = (Form)ctor.Invoke(parameters))
            using (var left = (Form)Activator.CreateInstance(dancerType, new object[] { "MusicBeePlugin.PartyRem.png" }))
            using (var right = (Form)Activator.CreateInstance(dancerType, new object[] { "MusicBeePlugin.PartyRam.png" }))
            {
                Set(form,"_gpuOutlineText",!(args.Length>4 && args[4]=="bitmap"));
                // Materialize the HWND before starting diagnostics: handle
                // creation can raise SizeChanged and end an active capture.
                var handle = form.Handle;
                if (size.Width > 1000) form.WindowState = FormWindowState.Maximized;
                form.ClientSize = size;
                if (form.ClientSize != size) throw new Exception("Requested benchmark size was changed by Windows.");
                Set(form, "_songTitle", "Synthetic profiling fixture"); Set(form, "_songArtist", "Rendering benchmark");
                Set(form, "_line1", "Current lyrics stay readable while the music and dancers move");
                Set(form, "_line2", "A translated line with enough words to exercise a large lyric card");
                Set(form, "_nextLine", "The next line is ready to move into place");
                Set(form, "_previousLine1", "An earlier lyric fades smoothly away");
                Set(form, "_previousLine2", "The previous translation fades away");
                Set(form, "_previousNextLine", Get(form, "_line1"));
                Set(form, "_queueExpanded", true);
                var queue = (IList)Get(form, "_queueTracks"); var trackType = queue.GetType().GetGenericArguments()[0];
                for (int i = 0; i < 8; i++) {
                    var track = Activator.CreateInstance(trackType, true);
                    Set(track, "Title", "Upcoming song " + i); Set(track, "Artist", "Artist " + i); Set(track, "Offset", i);
                    queue.Add(track);
                }
                var art = new Bitmap(600, 600);
                using (var g = Graphics.FromImage(art)) using (var b = new LinearGradientBrush(new Rectangle(0, 0, 600, 600), Color.Coral, Color.SteelBlue, 45f))
                    g.FillRectangle(b, 0, 0, 600, 600);
                Set(form, "_albumArtwork", art);
                var metadata = new Dictionary<string, object> {
                    { "mode", gpuMode ? "hardware HWND composition, unpaced; no display FPS claim" : "offscreen actual GDI code; no display FPS claim" }, { "width", size.Width }, { "height", size.Height },
                    { "run", run }, { "workload", "all layers; lyric transition every 2 s, uncached palette path 1 s every 8 s; two hidden layered dancer uploads" },
                    { "process_bits", IntPtr.Size * 8 }, { "logical_processors", Environment.ProcessorCount },
                    { "paced_60_workload", paced },
                    { "fresh_lyrics_every_2s", freshLyrics },
                    { "artwork_crossfades", args.Length>5 && args[5]=="artwork" },
                    { "lyric_outlines", !(args.Length>4 && args[4]=="bitmap") },
                    { "invalidate_at_lyric_change", true },
                    { "frame_target_fps", messageFps }, { "legacy_timer", legacyTimer },
                    { "cadence_note", "Synthetic hidden-window submissions, not physical presentation or MusicBee responsiveness" },
                    { "remote_session", SystemInformation.TerminalServerSession }
                };
                var profile = Activator.CreateInstance(profileType, Fields, null, new object[] { metadata, null, null, 30d, 2d, true }, null);
                Set(form, "_renderProfile", profile); Set(left, "Profile", profile); Set(right, "Profile", profile);
                var bars = (float[])Get(form, "_bars");
                int height = size.Width > 1000 ? 1626 : 454, width = (int)Math.Round(height * 180d / 353);
                bool gpuDancers = gpuMode && size.Width > 1000 && formType.GetField("_partyVisualValid",Fields)!=null;
                metadata["gpu_dancers"]=gpuDancers;
                metadata["workload"]="all layers; fresh lyrics, palette motion; two dancers, identical poses/transforms; GPU composition when supported at 4K";
                var watch = Stopwatch.StartNew();
                double nextFrame = 0;
                int lastVerse = -1;
                string json = null;
                using (var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    Action render = () => {
                        if (paced) {
                            while (watch.Elapsed.TotalSeconds < nextFrame) {
                                if (nextFrame - watch.Elapsed.TotalSeconds > .002) System.Threading.Thread.Sleep(1);
                                else System.Threading.Thread.SpinWait(20);
                            }
                            nextFrame = watch.Elapsed.TotalSeconds + 1d / 60;
                        }
                        double t = watch.Elapsed.TotalSeconds;
                        // Match UpdateLyrics' invalidation without calling its
                        // live artwork/track lookup in this synthetic fixture.
                        int currentVerse = (int)(t / 2);
                        if (currentVerse != lastVerse) {
                            if(args.Length>5 && args[5]=="artwork") {
                                var cover=new Bitmap(256,256,PixelFormat.Format32bppPArgb);
                                using(var cg=Graphics.FromImage(cover))using(var brush=new LinearGradientBrush(new Rectangle(0,0,256,256),
                                    currentVerse%2==0?Color.Coral:Color.MediumPurple,Color.SteelBlue,45f))cg.FillRectangle(brush,0,0,256,256);
                                Call(form,"StartArtwork",cover,Stopwatch.GetTimestamp());
                            }
                            form.Invalidate(); lastVerse = currentVerse;
                        }
                        Call(form,"AdvanceArtwork",Stopwatch.GetTimestamp());
                        if(freshLyrics) {
                            int verse=(int)(t/2);
                            Set(form,"_line1","Current lyrics stay readable while the music and dancers move " + verse);
                            Set(form,"_line2","A translated line with enough words to exercise a large lyric card " + verse);
                            Set(form,"_nextLine","Current lyrics stay readable while the music and dancers move " + (verse+1));
                        }
                        if (t % 2 < .3) {
                            Set(form, "_previousLine1", "An earlier lyric fades smoothly away");
                            Set(form, "_previousLine2", "The previous translation fades away");
                            Set(form, "_previousNextLine", Get(form, "_line1"));
                        }
                        for (int i = 0; i < bars.Length; i++) bars[i] = .45f + .35f * (float)Math.Sin(t * 4 + i);
                        // Let the final transition paint clear the existing
                        // state instead of repeatedly re-starting it when idle.
                        if (t % 2 < .3) Set(form, "_transitionStarted", Stopwatch.GetTimestamp() - (long)(t % 2 * Stopwatch.Frequency));
                        Set(form, "_paletteStarted", t % 8 < 1 ? Stopwatch.GetTimestamp() : 0L);
                        long started = (long)profileType.GetProperty("Stamp", Fields).GetValue(profile);
                        float impact = .5f + .5f * (float)Math.Sin(t * 10);
                        int pose = (int)(t * 2) % 4 * 3;
                        if (gpuDancers) {
                            Set(form,"_partyVisualValid",true);Set(form,"_partyVisualFrame",pose);
                            Set(form,"_partyVisualImpact",impact);Set(form,"_partyVisualSway",(float)Math.Sin(t)*.02f);
                            Set(form,"_partyVisualAnticipation",impact);
                            Set(form,"_leftPartyBounds",form.RectangleToScreen(new Rectangle(0,200,width,height)));
                            Set(form,"_rightPartyBounds",form.RectangleToScreen(new Rectangle(size.Width-width,200,width,height)));
                        }
                        if (messageFps != 0 || legacyTimer) Call(profile, "BeginPaint");
                        if (gpuMode) {
                            if (!(bool)Call(form, "TryDrawGpu")) throw new Exception("GPU failed: " + Get(form, "_gpuFailure"));
                        } else {
                            Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(Point.Empty, size)));
                            g.Flush(FlushIntention.Sync);
                        }
                        long dancers = (long)profileType.GetProperty("Stamp", Fields).GetValue(profile);
                        if (!gpuDancers) {
                            Call(left, "Present", new Rectangle(-20000, -20000, width, height), pose, impact, (float)Math.Sin(t) * .02f, impact);
                            Call(right, "Present", new Rectangle(-18000, -20000, width, height), pose, impact, (float)Math.Sin(t) * .02f, impact);
                        }
                        Call(profile, "End", Enum.Parse(metricType, "Dancers"), dancers);
                        Call(profile, "End", Enum.Parse(metricType, "FrameWork"), started);
                    };
                    if (messageFps != 0 || legacyTimer)
                    {
                        using(var pump = new FramePump(render, (late, skipped) => {
                            if ((long)profileType.GetProperty("Stamp", Fields).GetValue(profile) != 0) {
                                Call(profile,"Add",Enum.Parse(metricType,"FrameWakeLateness"),late);
                                Call(profile,"Add",Enum.Parse(metricType,"SkippedRenderDeadlines"),(double)skipped);
                            }
                        }))
                        using(var end = new System.Windows.Forms.Timer { Interval = 100 })
                        using(var legacy = new System.Windows.Forms.Timer { Interval = 16 })
                        using(var context = new ApplicationContext())
                        {
                            end.Tick += (s,e) => { if(watch.Elapsed.TotalSeconds >= 11.9) context.ExitThread(); };
                            if(legacyTimer) { legacy.Tick += (s,e) => render(); legacy.Start(); }
                            else pump.Start(messageFps);
                            end.Start(); Application.Run(context);
                        }
                    }
                    else while (watch.Elapsed.TotalSeconds < 11.9) render();
                    json = (string)Call(profile, "Finish", "offscreen benchmark completed; presentation unavailable");
                    if (gpuMode) SaveGpuSnapshot(assembly, form, bitmap);
                    bitmap.Save(Path.Combine(output, (gpuMode ? "gpu" : "gdi") + "-fixture-" + size.Width + ".png"), ImageFormat.Png);
                }
                if (json == null) throw new Exception("Benchmark exceeded capture window before saving.");
                File.WriteAllText(Path.Combine(output, (gpuMode ? "gpu" : "gdi") + "-" + size.Width + "-" + run + ".json"), json);
                Set(form, "_renderProfile", null);
                Console.WriteLine("Saved {0}x{1} run {2}", size.Width, size.Height, run);
            }
            settings.Font.Dispose();
        }
    }

    // Same scheduler as the plugin, on a real STA message loop. The workload
    // still draws to hidden HWNDs: cadence is NOT a scan-out/FPS measurement.
    sealed class FramePump : NativeWindow, IDisposable
    {
        const int Frame = 0x8000 + 0x4D1;
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam);
        readonly Action _render;
        readonly Action<double,long> _sample;
        RenderFramePacer _pacer;
        internal FramePump(Action render,Action<double,long> sample)
        {
            _render=render; _sample=sample;
            CreateHandle(new CreateParams { Parent=new IntPtr(-3) });
        }
        internal void Start(int fps)
        {
            string failure; var hwnd=Handle;
            _pacer=RenderFramePacer.TryCreate(token=>PostMessage(hwnd,Frame,new IntPtr(token),IntPtr.Zero),out failure);
            if(_pacer==null)throw new Exception("Native pacing unavailable: "+failure);
            _pacer.Start(fps);
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg!=Frame) { base.WndProc(ref m); return; }
            int token=m.WParam.ToInt32();double late;long skipped;
            if(_pacer.Failure!=null)throw new Exception(_pacer.Failure);
            try { if(_pacer.BeginFrame(token,out late,out skipped)) { _sample(late,skipped); _render(); } }
            finally { _pacer.EndFrame(token); }
        }
        public void Dispose() { _pacer?.Dispose(); DestroyHandle(); }
    }

    static void SaveGpuSnapshot(Assembly assembly, Form form, Bitmap output)
    {
        var type = assembly.GetType("MusicBeePlugin.GpuSceneRenderer");
        using (var renderer = (IDisposable)Activator.CreateInstance(type, Fields, null,
            new object[] { form.Handle, output.Size, true }, null))
        {
            var foreground = (Bitmap)type.GetProperty("Foreground", Fields).GetValue(renderer);
            Call(form, "RasterGpuForeground", renderer, null);
            Call(renderer, "Upload");
            using (var g = Graphics.FromImage(output)) {
                var dc = g.GetHdc();
                try { Call(renderer, "Draw", Get(form, "_palette"), Get(form, "_bars"), true, dc); }
                finally { g.ReleaseHdc(dc); }
            }
        }
    }
}
