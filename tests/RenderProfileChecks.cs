using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MusicBeePlugin;
using Newtonsoft.Json.Linq;

internal static class RenderProfileChecks
{
    internal static void Run()
    {
        var summary = JObject.FromObject(RenderProfile.Summarize(new double[] { 50, 10, 20, 40, 30 }, 2));
        if ((double)summary["average"] != 30 || (double)summary["p50"] != 30 ||
            (double)summary["p95"] != 50 || (int)summary["over_33_333"] != 2 || (int)summary["dropped"] != 2)
            throw new Exception("Profiling percentiles/delayed-frame counts must report real tails.");
        var warm = new RenderProfile(new Dictionary<string, object>(), null, null, 30, 60, false);
        if (warm.Stamp != 0) throw new Exception("Warmup must be excluded.");
        warm.Dispose();
        int callbacks = 0;
        var capture = new RenderProfile(new Dictionary<string, object>(), action => callbacks++, null, 30, 0, false);
        for (int i = 0; i < RenderProfile.SampleLimit + 10; i++) capture.Add(RenderMetric.Scene, i);
        var poll = typeof(RenderProfile).GetMethod("Poll", BindingFlags.NonPublic | BindingFlags.Instance);
        for (int i = 0; i < 5; i++) poll.Invoke(capture, new object[] { null });
        capture.FrameActivity(true, false, true, true, false);
        capture.FrameActivity(true, true, false, false, true);
        if (callbacks != 1) throw new Exception("Blocked MusicBee must have at most one heartbeat queued.");
        string report = capture.Finish("test");
        var json = JObject.Parse(report);
        if ((int)json["activity"]["frames"] != 2 || (int)json["activity"]["lyrics"] != 2 ||
            (int)json["activity"]["gpu"] != 1 || (int)json["activity"]["foreground_redrawn"] != 1)
            throw new Exception("Activity must record actual measured frames, including fallback.");
        if ((int)json["metrics"]["Scene"]["count"] != RenderProfile.SampleLimit ||
            (int)json["metrics"]["Scene"]["dropped"] != 10 || capture.Stamp != 0 || capture.Finish("again") != null)
            throw new Exception("Capture must be bounded, stop sampling and complete only once.");
        var folder = Path.Combine(Path.GetTempPath(), "DesktopLyrics-render-profile-" + Guid.NewGuid());
        string path = Path.Combine(folder, "restored.json");
        try
        {
            for (int i = 0; i < 7; i++) RenderProfile.Save(path, report);
            if (JArray.Parse(File.ReadAllText(path)).Count != 3 || new FileInfo(path).Length > 100000)
                throw new Exception("Repeated profiling must replace old reports with bounded summaries.");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
        using (var finished = new System.Threading.ManualResetEventSlim())
        using (var automatic = new RenderProfile(new Dictionary<string, object>(), null,
            text => finished.Set(), .03, 0, true))
        {
            if (!finished.Wait(2000) || automatic.Stamp != 0)
                throw new Exception("Opt-in profiling must stop automatically without a UI callback.");
        }
        Console.WriteLine("Rendering profile percentiles, bounds, warmup, completion and single UI probe checks passed.");
    }
}
