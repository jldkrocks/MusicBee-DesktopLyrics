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
                    { "mode", "offscreen actual GDI code; no display FPS claim" }, { "width", size.Width }, { "height", size.Height },
                    { "run", run }, { "workload", "all layers; lyric transition every 2 s, uncached palette path 1 s every 8 s; two hidden layered dancer uploads" },
                    { "process_bits", IntPtr.Size * 8 }, { "logical_processors", Environment.ProcessorCount },
                    { "remote_session", SystemInformation.TerminalServerSession }
                };
                var profile = Activator.CreateInstance(profileType, Fields, null, new object[] { metadata, null, null, 30d, 2d, true }, null);
                Set(form, "_renderProfile", profile); Set(left, "Profile", profile); Set(right, "Profile", profile);
                var bars = (float[])Get(form, "_bars");
                int height = size.Width > 1000 ? 1626 : 454, width = (int)Math.Round(height * 180d / 353);
                var watch = Stopwatch.StartNew();
                string json = null;
                using (var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb))
                using (var g = Graphics.FromImage(bitmap))
                {
                    while (watch.Elapsed.TotalSeconds < 11.9)
                    {
                        double t = watch.Elapsed.TotalSeconds;
                        if (t % 2 < .3) {
                            Set(form, "_previousLine1", "An earlier lyric fades smoothly away");
                            Set(form, "_previousLine2", "The previous translation fades away");
                            Set(form, "_previousNextLine", Get(form, "_line1"));
                        }
                        for (int i = 0; i < bars.Length; i++) bars[i] = .45f + .35f * (float)Math.Sin(t * 4 + i);
                        Set(form, "_transitionStarted", Stopwatch.GetTimestamp() - (long)((t % 2 < .3 ? t % 2 : .3) * Stopwatch.Frequency));
                        Set(form, "_paletteStarted", t % 8 < 1 ? Stopwatch.GetTimestamp() : 0L);
                        long started = (long)profileType.GetProperty("Stamp", Fields).GetValue(profile);
                        Call(form, "OnPaint", new PaintEventArgs(g, new Rectangle(Point.Empty, size)));
                        g.Flush(FlushIntention.Sync);
                        long dancers = (long)profileType.GetProperty("Stamp", Fields).GetValue(profile);
                        float impact = .5f + .5f * (float)Math.Sin(t * 10);
                        int pose = (int)(t * 2) % 4;
                        Call(left, "Present", new Rectangle(-20000, -20000, width, height), pose, impact, (float)Math.Sin(t) * .02f, impact);
                        Call(right, "Present", new Rectangle(-18000, -20000, width, height), pose, impact, (float)Math.Sin(t) * .02f, impact);
                        Call(profile, "End", Enum.Parse(metricType, "Dancers"), dancers);
                        Call(profile, "End", Enum.Parse(metricType, "FrameWork"), started);
                    }
                    json = (string)Call(profile, "Finish", "offscreen benchmark completed; presentation unavailable");
                    bitmap.Save(Path.Combine(output, "fixture-" + size.Width + ".png"), ImageFormat.Png);
                }
                if (json == null) throw new Exception("Benchmark exceeded capture window before saving.");
                File.WriteAllText(Path.Combine(output, "gdi-" + size.Width + "-" + run + ".json"), json);
                Set(form, "_renderProfile", null);
                Console.WriteLine("Saved {0}x{1} run {2}", size.Width, size.Height, run);
            }
            settings.Font.Dispose();
        }
    }
}
