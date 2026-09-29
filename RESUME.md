# Resumable work: GPU rendering, BPM search, tempo maps

Installed baseline: 1.15.31.0, code commit dd0b71d. Actions run 36595767943 passed; artifact 11045887260 was downloaded, digest-checked and installed with matching DLL hash. Previous 1.15.30.0 DLL is backed up under backups/MusicBee-20260929-1.15.30.0 outside the repo. Draft PR #1 uses codex/next-lyric-preview.

User authorized these features, with small independently verified checkpoints. Do not overwrite the installed DLL with unfinished work. Build each release through GitHub Actions; back up and verify the DLL before installation. Do not reset or redo completed work. User wants to conserve usage; remaining account allowance is not visible to the agent.

## Current checkpoint
1.15.31.0 groups BPM tools under Party BPM and adds Copy ChatGPT research prompt (artist/title/album/duration, source comparison, recording checks, half/double time, confidence, no invented precision or audio measurements). User pastes into their own chat and manually reviews/saves the result. Retry availability now follows enabled providers and the existing saved-timing protection predicate. Local build and smoke checks plus GitHub Actions passed. GPU and tempo-map implementation remains pending.

Browser BPM search was implemented in 843763b (1.15.30.0), locally built and smoke-checked; Actions run 36593911526 is the release build. Browser BPM search is available to the lyrics flyout. It opens a Google query for the current artist/title only on a click. It does not automatically retrieve a BPM, modify saved timing, or use a paid API. Use existing Adjust Party BPM to save a result.

## Next checkpoints
1. Profile the current lyric renderer at 4K, distinguishing text drawing, background/visualizer drawing and frame scheduling. Prototype Direct2D separately only if rendering is the bottleneck. Keep the existing renderer available; no GPU migration is implemented yet.
2. Design an optional per-song tempo map with timestamped BPM, beat alignment and dance style: normal, side-to-side, half-speed, hold pose. Defaults must preserve existing single-BPM songs and saved origins.
3. Hold sections are explicitly marked beatless/quiet passages, not automatic volume-based pauses. Resume at a defined aligned beat. Support abrupt tempo boundaries and optional gradual tempo changes while maintaining continuous pose phase.
4. Before implementation settle section editing, seek/pause behavior, validation and migration. Add deterministic timing tests before connecting the editor. No tempo-map implementation exists yet.

## Validation and installation
Use the local MSBuild in F:/Programs/Visual Studio/MSBuild/Current/Bin, existing tests/ParserSmoke.csproj, and GitHub Actions. MusicBee plugin path: C:/Program Files (x86)/MusicBee/Plugins/mb_DesktopLyrics.dll. Check MusicBee is closed before copying. Existing install scripts and backups live outside the repo under F:/Projects/Lyrics Plugin. Preserve them.

## Rendering profile checkpoint
The external diagnostic source F:/Projects/Lyrics Plugin/RenderProfile.cs loads the Release DLL by reflection and repeatedly invokes drawing into a bitmap, with fixed 0.1–0.7 visualizer heights and a transition held at its midpoint. It does not modify plugin settings or timing. Run RenderProfile.exe from that folder after rebuilding the DLL.

Observed local averages (40 draws per measurement, warm caches):
- 1920x1080: background 3.72 ms; spectrum 3.31 ms; whole OnPaint 21.08 ms.
- 3840x2160: background 16.25 ms; spectrum 11.78 ms; whole OnPaint 57.25 ms.

These are off-screen CPU drawing measurements, not MusicBee/compositor/TV frame rates. No real artwork was loaded and queue was disabled. The recording uses maximized dancer gutters; this benchmark uses the whole client area, so do not claim its values equal that layout. GPU migration has NOT started. First compare an opaque cached background copy and cached text rasterization with the current path, then decide whether a Direct2D prototype justifies the dependency and fallback complexity. Keep profiling helpers separate from shipped code.
