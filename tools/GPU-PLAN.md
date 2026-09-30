# GPU rendering checkpoint

Current63: user accepted62 visually/performance-wise and waived further captures. Outlines now defaultON with toggle retained. Artwork crossfades onGPU over550ms sharing the background start; oldart persists until decoding completes, interrupted fades collapse to one bounded snapshot, missingart fades to placeholder. GDI/device-failure fallback preserved. Installed/verified63, fullsuite+hardware botharchitectures pass. All-layer repeated-artwork benchmark4Kp998.85-9.39ms, no>16.667ms intervals. Await normal track-switch feedback, not another mandated capture set. Next agreed item is spectrum polish, then queue/control transitions; not implemented yet.

Current62: optional hardware drawing of existing glyph outlines installed, OFF by default. Paired120Hz tests meet cadence (4Kp998.87-9.00ms, no >16.667ms intervals), but average renderingwork rises0.9to2.5ms and CPUuse rises. This is a quality option, not a speedup. Group opacity, gradients, cache budgets, native failure/GDI recovery and botharchitectures pass. Await user's actual4K visual/scrolling feedback before defaulting or expanding. See RESUME.md and render-check-1.15.62.json.

Latest: user accepted61. The next isolated text experiment exports the existing GDI-shaped paths and rasterizes them through Direct2D, avoiding the DirectWrite font/spacing changes. All40 local fixtures preserve exported geometry bounds within CSV rounding (0.006px). Large moving text appears crisper; small text has different edge weight/pixel coverage. This is a quality feasibility result, not a production text switch or a frame-rate result. See RESUME.md and tools/text-quality-outlines.json. Next: hardware motion comparison, gradient/group-opacity parity and bounded geometry-cache/fallback checks before enabling a live path.

Current follow-up: 1.15.61 addresses the maximize gap and drag-restore freeze reported on60 by retaining restored dancer windows, seeding the GPU from prepared poses, and preparing restored sizes asynchronously. Source sheets for hidden restored windows are released. Tests and Actions pass; actual TV transition feedback remains pending.

Current checkpoint: version 1.15.60 moves maximized dancer preparation off the UI thread and completes an isolated DirectWrite quality investigation. Natural DirectWrite changes spacing and Japanese fallback appearance, so production text stays unchanged; next text experiment should isolate Direct2D drawing of existing shaped outlines. Cold UI preparation falls from204ms to at most4.53ms in the three-run test, while first image readiness stilltakes~0.2s. Await real4K feedback. Details in RENDERING.md and RESUME.md. Version1.15.59 integrates the user-approved refined AI-upscaled dancer poses, with four packed source cells per dancer and unchanged choreography. Validation/installation progress is recorded in RESUME.md. Version 57 GPU dancer live captures showed improved 4K frame tails. The reported NVIDIA116FPS cap is unchanged; see RENDERING.md for measurements and limitations.

User-authorized existing-feature sequence, developed as separate working checkpoints with TV feedback before expanding:
1. Lyric/foreground separation: completed and user-validated in56. Upcoming-text preparation remains a possible follow-up if new-glyph stalls justify it.
2. GPU composition of existing dancer poses/squash/rebound/sway: current57 checkpoint, maximized only. Preserve choreography/clocks and separate restored-window GDI dancers.
3. Investigate DirectWrite text quality/scaling with strict wrapping/font/outline compatibility checks. Do not assume a performance win.
4. Artwork crossfades coordinated with existing background colour transitions.
5. Spectrum visual polish, with bounded optional peak indicators/bar styles/glow and measured cost.
6. Queue expansion and control feedback/fades using retained layers.

Item3 has an isolated20-fixture comparison in60; a production text port remains unimplemented. Items4-6 remain unimplemented. The upscaled dancer sets have completed separate visual review; actual in-plugin 4K validation remains required after installation. Previously planned accent-beat selection and song-programmed celebratory effects remain later work.

## Authorized checkpoint after artwork validation (implemented/investigated in60)

1. Reduce measured first-use and resize preparation stalls. Release59 cold GPU pair preparation was 204 ms initially, then 61-64 ms for uncached poses, while warm preparation is negligible. Investigate bounded source/pose preparation without growing a per-size/per-song cache or creating a new animation clock. Moderate difficulty; expected benefit at transitions rather than steady FPS. Confirm on the user's 4K display before expanding.
2. Text quality and DirectWrite feasibility. Keep existing layout, wrapping, outlines and English translation behavior; compare glyph quality and new-line preparation cost before deciding what to port. High compatibility risk/difficulty; performance benefit unproven.
3. Artwork crossfades, then spectrum polish, then queue/control transitions. Keep each a separately measured and user-reviewed checkpoint. Crossfades should be modest steady GPU work; glow can be expensive at 4K and should remain optional/bounded.
4. Selectable accent beat 1/2/3/4 and manually programmed celebration effects remain future functional work. Effects need particle caps, lyric readability, reduced/off controls and deterministic seek reconstruction.

These are recommendations, not additional implementation in the sprite release.

## Historical pre-integration notes

The plugin still uses GDI+. Keep that renderer until the complete replacement passes visual and interaction checks. Do not claim that the isolated probe enables GPU rendering in MusicBee.

## Measured CPU improvements
On the same local off-screen harness with fixed bars and a midpoint lyric transition, the earlier 3840x2160 frame was about 57 ms. Opaque background copying, cached lyric rasters, and a single region-clipped copy of preblended spectrum pixels reduced it to about 33 ms (1080p about 11 ms). These are warm drawing measurements, not compositor/TV frame rates. One spectrum copy per bar regressed performance and was discarded.

## Isolated hardware probe
`GpuRenderProbe.vcxproj` builds an independent x64 executable through Actions. Run `GpuRenderProbe.exe` to require a hardware Direct2D target, create compatible off-screen targets, and render 120 synthetic frames at each of 1920x1080 and 3840x2160. It opens no visible window and touches no MusicBee settings. EndDraw/Flush failures return nonzero. A pass proves device/resource/render-call feasibility, not visual equivalence or throughput.

The plugin receives only its normal DLL. The probe is a separate artifact file, not a runtime dependency.

## Next integration checkpoint
1. Build an opt-in native HWND rendering backend behind the existing lyric window, with a runtime GDI+ fallback. Device loss must release target-bound resources and recreate them; persistent failure must restore the existing renderer.
2. Share layout/animation state. Port background, spectrum, lyric outlines/gradients, card shapes and artwork to GPU resources. Preserve existing translations, small-window wrapping, hit testing, menus, queue, DPI, resizing and dancer gutters. Transparent colour-key mode initially stays on GDI+.
3. Avoid rendering the full frame in GDI+ and then uploading it; that keeps the CPU bottleneck. Retain GPU bitmaps/brushes/text layouts between frames.
4. Compare screenshots at compact/1080p/4K sizes and measure presented frames while playing MusicBee. Test resize, display changes and device recreation before enabling it by default.

## Later: per-song visual effect cues
After the GPU renderer is stable, add manually timed flying stars/particles and bottom-edge spark bursts for choruses, builds and tense passages, inspired by the user's Taiko no Tatsujin example description. Plan type/start/duration/intensity controls without crowding the existing tempo editor. Preserve lyric readability and keep effect timing separate from dance/BPM settings. Reuse GPU textures and cap particles; offer reduced/off effects. Reconstruct current effects after seeking without firing past cues again. This is a planned feature, not part of the current renderer or a gameplay scoring system.

References: [Direct2D QuickStart](https://learn.microsoft.com/en-us/windows/win32/direct2d/getting-started-with-direct2d) and [GDI interoperability](https://learn.microsoft.com/en-us/windows/win32/direct2d/direct2d-and-gdi-interoperation-overview).

Verified locally from Actions artifact 11047942054 (run 36598712653): hardware target creation and 120 off-screen frames at both sizes passed. No presented-frame benchmark or plugin integration has been performed.
