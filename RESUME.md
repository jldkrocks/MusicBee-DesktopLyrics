# Resumable work: GPU rendering, BPM search, tempo maps

Installed baseline: 1.15.34.0, release code f3352ad. Actions run 36601163777 passed plugin build, smoke checks and GPU-probe build; artifact 11048973758 was downloaded, digest-checked and installed with matching DLL hash. Previous 1.15.33.0 DLL is backed up under backups/MusicBee-20260929-1.15.33.0 outside the repo. Draft PR #1 uses codex/next-lyric-preview.

User authorized these features, with small independently verified checkpoints. Do not overwrite the installed DLL with unfinished work. Build each release through GitHub Actions; back up and verify the DLL before installation. Do not reset or redo completed work. User wants to conserve usage; remaining account allowance is not visible to the agent.

## Current checkpoint
1.15.32.0 fixes dark submenu text (light enabled text/arrows and muted readable disabled text; off-screen menu rendering verified). Copy song and artist replaces the research prompt: clipboard contains only Artist – Song title, or title if artist missing. No success dialog. User's ChatGPT project already has the research context. Do not restore the verbose prompt. Retry supports Deezer-only configuration and preserves saved timing. These menu/copy features remain intact.

Browser BPM search was implemented in 843763b (1.15.30.0), locally built and smoke-checked; Actions run 36593911526 is the release build. Browser BPM search is available to the lyrics flyout. It opens a Google query for the current artist/title only on a click. It does not automatically retrieve a BPM, modify saved timing, or use a paid API. Use existing Adjust Party BPM to save a result.

## Implemented in 1.15.34.0
Tempo-map editor has a dark styled layout, live seek timeline with section diamonds/coloured spans, Play/Pause, +/-5 seconds and Seek to row. Marker clicks select a row and seek; dragging seeks once on release without moving sections. Seeking preserves pause state. Save applies and keeps the editor open; Close confirms discarding later unsaved edits without undoing earlier saves. Navigation is disabled for another song; saving stays attached to the original song. Tests cover seek mapping, markers, scrub release, repeated saves, and track changes. Editor snapshot verified. No changes to main lyrics layout, dancer placement, or GPU integration in this release.

## User-requested follow-ups (one at a time)
- Shift-click the existing PARTY button should open the regular BPM controls; Ctrl-click should open the tempo map. Not implemented yet. Plain click must continue toggling Party Mode.
- Make lyrics the visual focus in maximized mode, especially long current lines; current screenshot shows large empty gaps and the shorter upcoming lyric can appear more prominent. Do not move the dancers. Discuss/implement as a separate checkpoint.
- 4K performance is better but still less smooth than the smaller window. Full GPU integration remains pending; the isolated probe is not the plugin renderer.
User explicitly requested one thing at a time in case usage runs out. This pass completed only the editor workflow.

## Implemented in 1.15.33.0
- Party BPM → Edit tempo map: separate per-song map storage, optional enabled flag, decimal-second starts, BPM, ramp duration, normal/side-to-side/half-speed/hold styles, explicit side-beat alignment. Save sorts and validates; Cancel discards; unchecking the enabled box restores underlying BPM. Existing song timing files are untouched. Tests cover integration, ramps, holds, styles, seeks, persistence and invalid-save preservation.
- The editor captures current playback positions and can seek/play from a row. Saving after a track change still targets the original track. Single-BPM adjustment/lookup are disabled while that song's map is active. Hold freezes all motion until the next section, or to end of song if final. Maps are manual, not automatic change detection.
- GDI+ normal-window drawing now uses opaque background copying, cached lyric rasters and one region-clipped preblended spectrum copy. Transparent text remains vector. Final local 4K drawing profile about 33 ms versus original 57 ms; off-screen timing is not presented FPS. UI snapshots checked compact/large lyrics and tempo editor.
- Isolated Direct2D/DirectWrite hardware probe compiled in Actions and passed locally: 120 off-screen frames each at 1920x1080 and 3840x2160. Initial probe flush outside a drawing batch failed with WRONG_STATE; corrected within batch in 39a3620. No full GPU renderer is installed yet. Probe does not touch MusicBee.

## Next checkpoints
1. Integrate a real opt-in GPU backend with GDI+ fallback, using tools/GPU-PLAN.md. Hardware feasibility is checked; complete scene/controls/DPI/transparency/device-loss handling is not implemented. Do not claim that the current plugin is GPU accelerated.
2. Get real-song feedback on map editing, holds and transitions. Preserve current single-BPM behavior for unmapped songs. Future refinements include map preview/visual timeline and evidence-based tempo-change hints; waltz/swing patterns are not implemented.

## Validation and installation
Use the local MSBuild in F:/Programs/Visual Studio/MSBuild/Current/Bin, existing tests/ParserSmoke.csproj, and GitHub Actions. MusicBee plugin path: C:/Program Files (x86)/MusicBee/Plugins/mb_DesktopLyrics.dll. Check MusicBee is closed before copying. Existing install scripts and backups live outside the repo under F:/Projects/Lyrics Plugin. Preserve them.

## Rendering profile checkpoint
The external diagnostic source F:/Projects/Lyrics Plugin/RenderProfile.cs loads the Release DLL by reflection and repeatedly invokes drawing into a bitmap, with fixed 0.1–0.7 visualizer heights and a transition held at its midpoint. It does not modify plugin settings or timing. Run RenderProfile.exe from that folder after rebuilding the DLL.

Observed local averages (40 draws per measurement, warm caches):
- 1920x1080: background 3.72 ms; spectrum 3.31 ms; whole OnPaint 21.08 ms.
- 3840x2160: background 16.25 ms; spectrum 11.78 ms; whole OnPaint 57.25 ms.

These are off-screen CPU drawing measurements, not MusicBee/compositor/TV frame rates. No real artwork was loaded and queue was disabled. The recording uses maximized dancer gutters; this benchmark uses the whole client area, so do not claim its values equal that layout. CPU improvements and the standalone GPU probe are now complete; integration remains pending as described above. Keep profiling helpers separate from shipped code.
