# Rendering checkpoints and acceptance criteria

## Checkpoint 1: GDI baseline, version 1.15.52

The plugin still renders with GDI+. This checkpoint adds opt-in measurements, not a new renderer. The existing background, preblended spectrum, text-raster and dancer-pose caches remain in use. Playback, beat/lyric timing and saved song data are outside this change.

Before GPU implementation, capture three 30-second measured runs at each size on the same display, song passage, settings and refresh rate. Each capture excludes a 3-second warmup. Enable lyrics, English translation, upcoming lyrics, spectrum, artwork, queue and both dancers. Include several lyric transitions. Also test a song/palette change separately, since it invalidates caches. Keep the window size fixed during a capture. Scroll MusicBee for part of every run and report perceived sluggishness. Note display refresh rate, scaling, whether screen recording is running and any background load. A resized capture is marked invalid for comparison.

Open the lyrics window menu and choose **Capture rendering performance (33 s)**. It stops automatically. The next menu opening reports whether saving succeeded. Repeat three times restored, then three times maximized on the 4K TV. Reports are in `%APPDATA%\MusicBee\DesktopLyrics-Rendering` for this installation, as `gdi-restored.json` and `gdi-maximized.json`, each retaining at most three summaries. Other MusicBee installations use their plugin storage root. There are no song paths, titles, lyrics or audio in these reports. A capture cannot queue more than one MusicBee UI heartbeat. Closing or resizing ends it early and records the reason.

Metrics include mean, p50, p95, p99, maximum and counts over 20, 33.333 and 50 ms. FrameInterval measures WM_PAINT cadence; PaintDispatch includes WinForms processing and buffer copying; Scene and individual layers are nested inside it. Tick and Dancers are likewise nested. Never sum every metric. DancerRaster and DancerUpload are per individual changed dancer, while Dancers covers the pair plus state/layout work. CPU is for the whole host process, both as one-core-equivalent percent and percent of all logical processors. Memory reports host working set and private memory. MainUiLatency is a posted callback delay, a useful proxy but not a measure of scrolling quality. Empty metrics are unavailable, not evidence of zero cost. FrameInterval is not confirmed physical display presentation or scan-out.

Normal use has no profiler timer, sample allocation, CPU sampling or report writing. During capture each metric holds at most 16,384 samples; overflow is reported. The maximum raw sample payload is about 2.6 MiB, plus lists and temporary summary arrays. Samples are discarded after completion. Reports retain summaries only. The profiling build adds no dependencies or native helper. Packaging remains the existing MusicBee plugin DLL; the separate benchmark/probe EXEs are developer tools and are not installed into MusicBee.

## Acceptance gates proposed before GPU implementation

| Measure | Initial target |
| --- | --- |
| Sustained cadence on the real 4K output | At least 58 measured paints/s for a 60 FPS target, subject to confirmed presentation and user feedback |
| Frame-interval tails | p95 <=20 ms, p99 <=33.333 ms; fewer than 1% over 33.333 ms |
| Draw budget | Combined relevant drawing work should fit inside 16.667 ms, leaving room for dispatch/presentation; report p95/p99, not just averages |
| First GPU-layer benefit | At least 40% lower mean background+spectrum work in a matched benchmark, with lower p95 for each layer; total frame work and live cadence must also improve |
| Host impact | No perceived MusicBee slowdown; UI heartbeat p95 <=50 ms, p99 <=100 ms and no repeated >250 ms stalls; whole-process CPU must not rise at a matched frame rate |
| Stability | No crashes, blank persistent canvas, lost controls or appearance changes through resize, minimize/restore, monitor changes, remote session and injected device-failure paths |
| Memory | Aim for <=128 MiB additional GPU resources at 4K; report actual allocation/host memory and no sustained growth after repeated resize/device recreation |
| Functional preservation | Full regression suite and Actions pass; animation remains driven by existing song position/monotonic-time state, never a GPU clock |

If the hardware or presentation path cannot meet these gates, report the measured ceiling and limiting operation. Do not mask stalls by reporting only average FPS. A smaller improvement can be a useful checkpoint but is not a claim that the final target was met. Physical 4K feedback is required before declaring success or expanding scope.

## Existing architecture and smallest proposed GPU stage

The main window uses a 16 ms WinForms timer, full-window invalidation and an optimized double buffer on its own STA thread. A single background reader samples playback. Background gradients are cached except during palette changes; spectrum bars copy clipped strips from a preblended background; lyric geometry/raster caches avoid repeated glyph construction. Artwork and queue/control drawing remain GDI+. Each dancer is a separate layered window with cached scaled poses, a GDI+ DIB raster and UpdateLayeredWindow upload.

The existing `GpuRenderProbe.cpp` creates a hardware Direct2D target and DirectWrite format, then draws 120 off-screen frames at 1080p and 4K. It passed on this machine. It has no frame-time benchmark, real window presentation, device-loss recovery or plugin integration. Its success does not establish 60 FPS.

First recommendation: a GPU-composited background and spectrum, retaining the existing foreground GDI rasterization and appearance. Integration needs a measured surface/composition boundary; drawing onto the GPU and copying a whole frame back to the CPU every tick is not an acceptable shortcut. Prefer reusable foreground textures/regions and a hardware presentation surface. This is an architectural proposal, not implemented behavior. Compare it with the live GDI baseline before proceeding.

Use Windows Direct2D/DirectWrite and, if the presentation boundary requires them, Direct3D11/DXGI (DirectComposition may be needed for transparent overlays). These are Windows components. A small native helper could expose a narrow C ABI, built for MusicBee's process architecture with static CRT linkage so no separate VC runtime installer is needed. If used, measure its actual file sizes and package the helper beside the ordinary plugin DLL. The current standalone x64 EXE cannot be loaded as a plugin helper, and process bitness must be checked. No NuGet graphics framework or additional service is proposed.

The GDI renderer remains available. Remote sessions initially select GDI. Failed creation, missing/incorrect helper architecture, native HRESULT failures, device removal and render exceptions must release GPU resources and restore GDI promptly. Test failures through injection, not by resetting the user's live graphics driver. Bound retries and allocations; no re-creation loop or crash may escape into MusicBee. Prefer preserving GDI text rasters initially because DirectWrite can change glyph metrics, wrapping, antialiasing and outlines. Input/hit-testing and dancer placement must stay identical.

Microsoft references: [Direct2D overview](https://learn.microsoft.com/en-us/windows/win32/direct2d/direct2d-overview), [render targets](https://learn.microsoft.com/en-us/windows/win32/direct2d/render-targets-overview), [layered-window interoperability and costly readbacks](https://learn.microsoft.com/en-us/archive/msdn-magazine/2009/december/windows-with-c-layered-windows-with-direct2d).

## Prioritized opportunities, not additional implementation scope

| Priority | Existing or future feature | Likely cost and benefit | Difficulty |
| --- | --- | --- | --- |
| 1 | Full-window background and spectrum | Large CPU-copy/fill savings; several 4K surfaces can cost tens of MiB each; palette-transition spikes are a key target | High for the first safe presentation/fallback boundary, moderate for the drawing itself |
| 2 | Lyric-card composition and moving/fading cached text | Reuse textures and transform/blend them on GPU; frequent full-surface uploads must be avoided | Medium after composition exists; high for a later DirectWrite migration with matching wrapping/outlines |
| 3 | Dancer rasterization and transparent presentation | Reuse pose textures and transform them; potentially lower per-frame scaling/upload cost | High because layered-window transparency, ownership and restored/maximized placement must be preserved |
| 4 | Artwork, queue and control composition | Lower measured cost; mostly cache/invalidation improvements rather than urgent GPU work | Low to medium; do only if further measurements justify it |
| 5 | Timed stars, particles and bottom spark bursts | Additional blend/fill cost; bounded particle counts and pooled textures essential, particularly at 4K | Medium to high; build only after the performance gates pass and cue behavior is designed |
| 6 | Optional glows or richer background effects | Potentially expensive overdraw and off-screen passes; requires explicit quality limits | Medium; lowest priority because these add work rather than fixing current performance |

Selectable accent beats and tempo-map features are separate functionality; GPU rendering does not improve their timing logic.

## Reproducing the off-screen baseline

Build the plugin, then `msbuild tools/GdiRenderBenchmark.csproj /p:Configuration=Release`. Run `tools/bin/Release/GdiRenderBenchmark.exe <output-folder>`. It uses isolated synthetic metadata/artwork/queue and the actual plugin OnPaint/PartyDancerWindow paths. Three approximately 10-second measured runs at each size follow a 2-second warmup. It animates spectrum, recurring lyric transitions and periodic uncached palette paths. Dancer windows stay hidden and off-screen, so their upload cost excludes visible compositor work. CPU is expected to approach one full core because this throughput test is unpaced. Screenshots and summary JSON are saved only in the selected folder.

The benchmark cannot measure MusicBee UI sluggishness, real WM_PAINT cadence, refresh synchronization, visible layered-window composition or perceived smoothness. Its FrameWork is combined drawing duration, not a display frame interval. Compare like-for-like results and use the live capture for the missing dimensions.

## Measured off-screen baseline

Final three-run results are preserved in `render-baseline-1.15.52.json`. This machine reports a Ryzen 7 9800X3D (16 logical processors), RTX 5090 and virtual/1440p displays. The 4K workload uses a 3840x2160 bitmap and the maximized layout, not a physical 4K presentation test.

| Combined scene plus dancer work | Mean per run | p95 per run | p99 per run | Work samples over 33.333 ms |
| --- | --- | --- | --- | --- |
| Restored 960x540 | 4.70-4.78 ms | 8.43-8.56 ms | 9.34-10.11 ms | 0% |
| Maximized-layout 3840x2160 | 29.92-33.69 ms | 34.82-41.07 ms | 87.65-90.58 ms | 7.6-15.3% |

At 4K the average costs are background 7.90-9.95 ms, spectrum 6.56-8.06 ms, lyric card/text/layout 8.69-8.85 ms, both dancers 3.38-3.43 ms, artwork 1.88-1.89 ms and queue about0.48 ms. Background p99 is62.8-66.2ms during the deliberately exercised uncached palette path. The background and spectrum together account for about48-53% of combined drawing work. Removing that cost alone may still leave little room for presentation and lyric-transition tails; do not promise60FPS from the first GPU layer.

The unpaced benchmark consumes approximately98-101% of one logical core, about6.1-6.3% of this16-thread host. This is throughput-test saturation, not MusicBee's measured CPU load at60FPS. Average benchmark-process working set is65-78MiB restored and234-242MiB at4K, with a4K maximum near263MiB. These totals include the test bitmap and caches, not just the installed plugin.

The current GDI workload does not fit the16.667ms budget at4K. The average drawing-throughput ceiling in this workload is roughly30-33frames/s before real presentation overhead, with much slower palette-transition outliers. No conclusion about actual MusicBee main-window sluggishness or display cadence is justified until the live capture and user feedback are available. Subsequent live captures are analysed below. Checkpoints2-4 have not begun.

## Live 4K baseline, 2026-09-30, version 1.15.52

The user requested discarding the first four trials with no visible lyrics and identified the following six as three small-window runs followed by three TV-maximized runs. The bounded storage retains three captures per mode. Those retained runs completed at13:24:06,13:24:47,13:25:36 UTC (restored) and13:26:19,13:26:58,13:27:45 UTC (maximized). Read-only copies are in ../render52-live outside the repository. Use these six only. All completed30 measured seconds, with no sample overflow. MusicBee is a32-bit process; both modes report120Hz,96DPI and a non-remote session. These are paint-dispatch measurements, not confirmed display scan-out.

| Live metric, ranges across three captures | Restored814x272 | Maximized3840x2137 |
| --- | --- | --- |
| Mean paint cadence | 40.1-40.2 paints/s | 23.6-26.5 paints/s |
| Frame interval mean | 24.88-24.91ms | 37.77-42.35ms |
| Frame interval p95 | 32.99-33.07ms | 46.07-50.55ms |
| Frame interval p99 | 35.51-36.29ms | 49.88-62.22ms |
| Intervals over33.333ms | 3.3-3.8% | 82.8-98.3% |
| PaintDispatch mean | 2.77-2.93ms | 35.90-40.48ms |
| Scene mean | 2.22-2.37ms | 25.75-29.56ms |
| PaintDispatch minus Scene means | 0.53-0.58ms | 10.00-10.92ms |
| Background mean | 0.19-0.22ms | 6.38-8.32ms |
| Spectrum mean | 0.34-0.38ms | 5.54-6.84ms |
| Lyric-region work mean | 0.85-0.90ms | 11.22-11.68ms |
| Dancers mean per update | 0.21ms | 1.56-1.69ms |
| MusicBee UI callback p95 | 4.33-4.72ms | 14.64-18.49ms |
| MusicBee UI callback p99 | 5.33-6.04ms | 23.06-29.42ms |
| Whole-process mean CPU, one-core equivalent | 14.2-20.9% | 103.2-110.6% |
| Whole-process average working set | 117-118MiB | 290-297MiB |

User feedback: MusicBee scrolling was fine with the small window. Maximized on the4K TV, scrolling was very sluggish and album artwork did not load quickly enough to keep up. This is a failure of the host-responsiveness acceptance criterion even though the lightweight callback probe still passed its numerical thresholds. A queued callback is not a measurement of artwork decoding, thumbnail loading, scrolling paint throughput, or GPU/DWM contention. Do not override the user's observed failure using the callback numbers, and do not claim the mechanism of album-art slowness is established by these measurements alone.

All six initial metadata snapshots say lyrics/English/preview were absent at capture start, despite the user's confirmation that these were the trials with lyrics visible. The current metadata is only captured once, before the three-second warmup, and does not record their state throughout the measured period. The measured lyric-region work is nonzero, but is not independently proof of which text was visible. Preserve this uncertainty, accept the user's observation, and add per-frame active-layer counts in the next rendering checkpoint so future matched comparisons can verify exposure without recording text. Do not silently discard these six or substitute earlier no-lyrics trials.

The live data adds two important constraints. First, about10-11ms lies outside the instrumented scene but inside WinForms WM_PAINT handling, consistent with expensive double-buffer/paint overhead. That difference is not an isolated BitBlt measurement. A GPU implementation that draws the backdrop then copies the entire frame back into the existing GDI double buffer may retain this cost. The first stage should establish GPU presentation/composition, retain the existing GDI foreground as reusable textures, and avoid full-frame readback and unconditional full-frame uploads. Background+spectrum average11.9-15.2ms; the lyric layer remains a substantial11.2-11.7ms and is the next candidate after the first-stage comparison.

Second, even the inexpensive restored paints average only40 paints/s, while timer intervals average25ms and p95 is about32.7ms. This suggests frame scheduling/message delivery is another limit. A16ms WinForms timer is a request, not a guarantee of60Hz presentation. GPU conversion alone cannot be assumed to fix it. Measure and improve render wake-up pacing separately, with one outstanding frame request and no changes to song-position/beat/lyric clocks. Do not change system-wide timer resolution as an unmeasured shortcut.

Both modes miss the proposed60FPS numerical cadence/tail gates; only the small mode passed the user's perceived-responsiveness check. The live baseline is now available. User authorization for the staged GPU implementation remains in place. Checkpoint2 (the first GPU composition layer, diagnostics activity counts and safe fallback) is next; no GPU plugin renderer has been implemented yet. Keep32-bit native packaging in scope, preserve the verified1.15.52 GDI fallback and backup, and compare against these retained live runs as well as the synthetic baseline before expanding to other layers.
