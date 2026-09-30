# GPU rendering checkpoint

Current checkpoint: version1.15.58 installs approved higher-detail GIF-derived dancer sprites. Version57 GPU dancer live captures have been reviewed, showing improved4K frame tails. New artwork awaits TV visual feedback. A separate AI upscale sample is preview-only. The reported NVIDIA116FPS cap is unchanged; see RENDERING.md for measurements and limitations.

User-authorized existing-feature sequence, developed as separate working checkpoints with TV feedback before expanding:
1. Lyric/foreground separation: completed and user-validated in56. Upcoming-text preparation remains a possible follow-up if new-glyph stalls justify it.
2. GPU composition of existing dancer poses/squash/rebound/sway: current57 checkpoint, maximized only. Preserve choreography/clocks and separate restored-window GDI dancers.
3. Investigate DirectWrite text quality/scaling with strict wrapping/font/outline compatibility checks. Do not assume a performance win.
4. Artwork crossfades coordinated with existing background colour transitions.
5. Spectrum visual polish, with bounded optional peak indicators/bar styles/glow and measured cost.
6. Queue expansion and control feedback/fades using retained layers.

Items3-6 remain unimplemented. Original dancer assets are unchanged; optional upscaling needs a separate visual comparison to protect outlines/details and pose consistency. Previously planned accent-beat selection and song-programmed celebratory effects remain later work.

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
