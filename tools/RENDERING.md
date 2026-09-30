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

The current GDI workload does not fit the16.667ms budget at4K. The average drawing-throughput ceiling in this workload is roughly30-33frames/s before real presentation overhead, with much slower palette-transition outliers. No conclusion about actual MusicBee main-window sluggishness or display cadence is justified until the live capture and user feedback are available. Checkpoint1's live baseline remains pending; checkpoints2-4 have not begun.
