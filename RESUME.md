# Resumable work: GPU rendering, BPM search, tempo maps

Installed baseline: 1.15.29.0, commit 9b797d2. Draft PR #1 uses codex/next-lyric-preview.

User authorized these features, with small independently verified checkpoints. Do not overwrite the installed DLL with unfinished work. Build each release through GitHub Actions; back up and verify the DLL before installation. Do not reset or redo completed work. User wants to conserve usage; remaining account allowance is not visible to the agent.

## Current checkpoint
Browser BPM search is being added to the lyrics flyout. It opens a Google query for the current artist/title only on a click. It does not automatically retrieve a BPM, modify saved timing, or use a paid API. Use existing Adjust Party BPM to save a result.

## Next checkpoints
1. Profile the current lyric renderer at 4K, distinguishing text drawing, background/visualizer drawing and frame scheduling. Prototype Direct2D separately only if rendering is the bottleneck. Keep the existing renderer available; no GPU migration is implemented yet.
2. Design an optional per-song tempo map with timestamped BPM, beat alignment and dance style: normal, side-to-side, half-speed, hold pose. Defaults must preserve existing single-BPM songs and saved origins.
3. Hold sections are explicitly marked beatless/quiet passages, not automatic volume-based pauses. Resume at a defined aligned beat. Support abrupt tempo boundaries and optional gradual tempo changes while maintaining continuous pose phase.
4. Before implementation settle section editing, seek/pause behavior, validation and migration. Add deterministic timing tests before connecting the editor. No tempo-map implementation exists yet.

## Validation and installation
Use the local MSBuild in F:/Programs/Visual Studio/MSBuild/Current/Bin, existing tests/ParserSmoke.csproj, and GitHub Actions. MusicBee plugin path: C:/Program Files (x86)/MusicBee/Plugins/mb_DesktopLyrics.dll. Check MusicBee is closed before copying. Existing install scripts and backups live outside the repo under F:/Projects/Lyrics Plugin. Preserve them.
