# Desktop Lyrics
Show the lyrics on your desktop from MusicBee!

# Description
This plugin is for showing lyrics on your desktop, just like clients of very many popular online music services does(such as Netease Cloud Music, QQ Music, Kugou Music, etc.).

The lyrics must in LRC format, synchronized. Offset label is supported.

It can work with NeteaseLyrics plugin [https://github.com/cqjjjzr/MusicBee-NeteaseLyrics](https://github.com/cqjjjzr/MusicBee-NeteaseLyrics "(GitHub Repo)") [https://getmusicbee.com/forum/index.php?topic=24313.0](https://getmusicbee.com/forum/index.php?topic=24313.0 "(Forum Topic)") in order to display double-line lyrics, if you can provide lrc splitted with slash "/" it can also correctly handled.

# Download & Installation
Open the latest successful run of **Build Desktop Lyrics** on this repository's **Actions** tab. Download its `DesktopLyrics-<version>` artifact, then use the DLL or packaged ZIP inside it.

Just install this plugin in the Plugins tab in the Settings or put the DLL file into the Plugins of your MusicBee installation, and then enable it, set the lyrics style in the settings, and you're ready to rock!

Also, lyrics style can be modified in the Plugins tab. And you can hide or show the desktop lyrics in the MusicBee Menu(View->Desktop Lyrics).

## Lyrics window

Drag the title bar to move the window. It remembers its size and position. Resizing scales the lyrics; line changes slide and fade. Short previews can travel into the main slot, while long or wrapped lines slide at their final size to avoid zooming and reflowing mid-transition. The current lyric, optional translation, and faded next lyric appear over spectrum bars driven by MusicBee's audio data. When a track has no lyrics, the lyric panel disappears. **Match window colours to album artwork** is enabled on upgrade and can be turned off in plugin settings. MusicBee's built-in visualizers open in their own views; this window draws its own bars so the lyrics stay in front.

If you close the visualizer with **X**, choose **View → Desktop Lyrics** to reopen it, or use **Show lyrics window** in the plugin settings. Closing with X is temporary and the window returns on the next MusicBee launch. Using the View menu to turn Desktop Lyrics off remains a saved preference.

The window shows the song title, album art, lyrics, and previous/play/next controls. The **Queue** panel shows tracks played since this plugin started that are still in MusicBee's Playing Tracks list, the current song, and the upcoming playback order. Removing a track from Playing Tracks removes it from the plugin's history at the next queue refresh. Scroll with the mouse wheel or arrows and click a track to play it. At medium window sizes the queue uses shorter rows; at the smallest sizes a **QUEUE** tab opens a scrollable queue over the right side of the window. The panel shows each file once when a recently played song also appears in the upcoming order. MusicBee's plugin API has no history query, so earlier sessions cannot be shown. MusicBee can only select a queued file by URL; the plugin explains when it cannot distinguish duplicate entries. The **BG** button switches between the visualizer background and a borderless transparent canvas. The lyric card, art, title, controls, queue, BG and menu buttons remain visible; LRCLIB and TIMING stay available in the menu. Drag the title or lyric card to move the borderless window, and use its small lower-right grip to resize it. The menu has a **Close lyrics window** action; the MusicBee toolbar command can reopen it. These choices are saved for the next MusicBee launch.

The **PARTY** button near the lower-left corner shows the supplied Rem and Ram dancers beside the window. Click it again to hide them; the choice is remembered on the next launch. They grow with the window and remain outside it in normal mode, including across connected monitors. If there is no space outside either side of the whole desktop, that dancer hides instead of covering the lyrics. When maximized, the window reserves a dedicated gutter on each side and moves the artwork, lyrics, queue and controls into the centre; the dancers occupy those gutters. They follow movement and resizing, freeze when playback pauses, and remain click-through. The dancers use four key drawings: the opposite side pose (frame 0), centred pose (frame 3), raised-arm side pose (frame 6), and other centred pose (frame 9). Each drawing holds for one beat; the side pose lands every other beat from 40 to 240 BPM. Every pose lifts slightly just before the next beat, then lands with a brief grounded squash. The middle landing is gentler than the side hit, so both beats register while the side remains the accent. On slower songs the held pose sways slightly between beats and returns to centre on the next beat; the sway fades away by 120 BPM. The dancer clock advances smoothly between MusicBee’s playback-position updates, so coarse position steps do not make the poses hesitate. It gradually corrects late startup samples against fresh updates, reducing timing differences when returning to a saved song. Dancer poses are scaled once per window size and drawn directly into the layered-window buffer to reduce large-screen rendering work. The display also leads the sampled position slightly to compensate for window update latency; saved beat positions remain unchanged. The PARTY button shows `TAG 120` for a BPM tag, `AUTO 120` when live detection first locks a tempo, `SAVED 120` when it loads a previously saved tempo, `WEB 120` for a new GetSongBPM match, or `SET 120` for a manual adjustment. With online lookup enabled it shows `SEARCHING...`, `NO MATCH`, or `API ERROR` as appropriate; with online lookup disabled it shows `LISTENING...` or `NO SIGNAL` during live detection. Songs without a saved BPM or tag can be searched by artist and title using the online sources described below. Matching results are saved per song, and ambiguous versions are skipped. If online lookup is disabled, the plugin samples MusicBee's live low-frequency spectrum instead. It saves that BPM and dance phase per song in MusicBee's plugin storage, so later plays start at the saved speed without listening again. A manual BPM overrides both a tag and an automatic value. If a double-time song looks too frantic, try half its BPM in **Adjust Party BPM…** (for example, 110 instead of 220); this changes only the dance and does not edit music tags. Choose **Adjust Party BPM…** from the window's flyout. Type a BPM from 40 to 240 or tap the large **Tap beat** button along with the song; the BPM readout updates after the second tap and steadies over subsequent taps. Tap or click **Align to this beat** as you hear an audible beat to immediately preview the raised-arm pose timing at the entered BPM. You can click again to retry, or adjust the BPM in 0.1 steps while previewing. **Save** remembers the preview for this song; **Cancel** discards it and restores the underlying timing. A preview never carries over to another song. **Forget saved BPM** returns the song to its tag, online lookup, or live detection. An ordinary manual BPM adjustment without alignment keeps the current animation frame. Live onset detection places the raised-arm pose on its detected beat when it locks, which may cause a one-time phase jump. While it searches, or when no confident online match exists, it uses a fixed 1.68-second loop. With online lookup disabled, this loop also runs while live detection learns. This works with visualizer bars turned off and does not edit the songs or require filling in thousands of BPM tags. Live detection can be imprecise on tracks with weak or irregular percussion. A BPM tag sets the speed but cannot identify the first beat; Align to this beat supplies a manual beat position for that song. BG already has a dedicated button, so its duplicate flyout switch is removed; LRCLIB and TIMING appear in the flyout only when their buttons are hidden by the compact layout.

To add a MusicBee toolbar button, right-click its toolbar and choose **Configure Toolbar**. Add a button and choose **View: Toggle Desktop Lyrics Window** from the command dropdown. MusicBee's toolbar layout remains yours to arrange; the command also appears in Hotkeys.

### Party BPM menu

The lyrics flyout groups adjustment/alignment, browser search, song copying, online lookup settings and retry under **Party BPM**. **Copy song and artist** copies only `Artist – Song title` for pasting into your existing ChatGPT project or elsewhere (just the title if artist is missing). It copies silently without an extra dialog. It does not contact ChatGPT or replace saved timing. Enabled menu text is light; unavailable actions use a readable muted colour.


Hold **Shift** while clicking **PARTY** to open BPM/alignment controls, or **Ctrl** to open the tempo map. Plain click still toggles Party Mode. While a map is enabled, the single-BPM controls are read-only and explain how to return to them.

### Per-song tempo map

Choose **Party BPM → Edit tempo map…** to mark tempo changes or beatless passages. The first row starts at zero and is seeded from the current BPM (120 if none is known). Each row lasts until the next start time. Times are seconds with optional decimals; Save sorts rows by start time and rejects duplicates.

- **BPM** sets the section's musical tempo, from 40 to 240.
- **BPM ramp (s)** gradually reaches that BPM from the previous tempo: for example, 120 to 150 over 4 seconds. Equal BPM values have no ramp effect. This controls BPM only; switching dance styles (including Half speed to Normal) still happens at the row start. Zero changes BPM immediately while preserving phase. A ramp must finish before the next section; the first row cannot ramp.
- **Dance** selects Normal, Side to side (no centre poses), Half speed, or Hold pose. Hold freezes the pose and movement until the next row; its BPM is ignored. A final Hold lasts to the end of the song.
- **Align** starts a raised-arm side pose exactly at the section boundary (with the existing display-latency lead). Leave it unchecked to keep phase continuous. Hold cannot align or ramp.
- **4/4 - accent on 4** adds three smaller centre bops on one, two, three, a preparatory rise before FOUR, then a stronger side landing on FOUR. The next bar lands on the opposite side. BPM counts all four beats. For this rhythm, **Align** puts the strong FOUR at the section start; place the marker on that hit. Without Align, the ongoing dance phase is preserved. Half speed slows the pattern; Side to side omits centre drawings but retains the fourth-beat accent.
- **Rhythm** selects Straight (existing motion), Waltz (3/4), Swing, or 4/4 - accent on 4. Waltz uses **side - centre bop - centre bop**, keeping the same centre drawing for beats 2 and 3 with a separate hit on each, then a small rise before the opposite side lands on beat 1. Use **Align** at a bar's first beat to place the strong accent. Swing holds a side pose through the long part of each beat, briefly hits the middle on the late subdivision, then lands on the opposite side on the next main beat. **Swing %** controls that split per section: 50 is even, 60 is light swing, 66.67 is approximately 2:1 (the default), and 75 is strong swing. The amount changes pose spacing, not BPM or saved beat phase. Half speed slows the selected pattern; Hold stops all motion; explicit Side to side omits middle drawings. Existing maps without a rhythm stay Straight; older Swing maps default to 66.67%. These are manual visual presets, not automatic time-signature detection.
- **Bob count-in** is optional on a Normal row immediately after Half speed. It adds up to four strong lead-in bobs, then one final bop on the first actual beat at or after the Normal row starts. The final bop recovers into regular movement, including during BPM ramps. Each dip lands on the incoming beat alignment, including an explicit Align setting; the section marker itself is not assumed to be a beat. The cue replaces the half-speed squash/lift temporarily, while preserving saved phase, poses and section starts. Short half-speed sections fit fewer bobs; less than one incoming beat gives no cue. Tick the box on the returning Normal row, then Save. Existing maps keep it off. It cues a style change; it does not gradually accelerate the dance.

The editor has a live timeline with section diamonds and coloured spans. Click a diamond to select its table row and seek to its start. Click or drag elsewhere on the timeline to seek; dragging commits one seek on release and does not move section boundaries. Arrow keys seek five seconds; Home/End go to the song bounds. Unknown-duration streams cannot use the timeline.

**Play/Pause** and **±5 seconds** let you replay a passage without closing the editor. **Add at playhead** captures a new row; edit its values in the table. **Seek to row** jumps to that start. Seeking preserves the current play/pause state. Edits take effect only after **Save**, which now applies them and keeps the editor open for listening and further edits. **Close** leaves prior saves applied and asks before discarding later unsaved edits. Uncheck **Use this map for this song** and Save to restore the original single-BPM timing without deleting the map. The Party button reads **TEMPO MAP** while a map is active; single-BPM adjustment is disabled until the map is disabled. Maps override tags and lookup only for their own song and are stored separately from saved BPMs and music files. If the song changes during editing, Save still applies to the original song; capture/seek require that original song to be playing.

Maps are manual: they do not detect tempo changes, silence, waltz or swing automatically. Quiet passages can still contain beats, so holding is an explicit choice. Seeking and replay compute the pose from the song timeline rather than accumulated animation ticks.

### Rendering and GPU investigation

Normal-window rendering caches lyric rasters and preblends spectrum colours against the opaque cached background. This reduces repeated text shaping/drawing and per-frame blending. Transparent mode retains its vector text path. The Direct2D/DirectWrite feasibility probe under `tools` is separate from the plugin: it requires a hardware render target and draws synthetic 1080p/4K scenes off-screen. It neither replaces the installed renderer nor measures presentation frame rate. See `tools/GPU-PLAN.md` for the migration checkpoint.

### Saved data and library size

MusicBee provides a persistent storage folder (normally `%APPDATA%/MusicBee`). `DesktopLyrics-English` stores one JSON translation file per song; `DesktopLyrics-PartyTempo` stores small BPM/alignment JSON files and separate `.map` files. Saving replaces the existing data for that song atomically. These files do not become part of the plugin DLL, and playback reads only the current song's data rather than scanning or loading the whole saved library.

`DesktopLyrics-TimingBackups` keeps a timestamped `.lrc` copy before each lyric-tag edit/import. These backups accumulate with edits. Saved data is keyed by the music file's path, so moving, renaming or deleting songs can leave older records behind; automatic cleanup is not implemented. Back up the data folders when moving MusicBee or migrating to another computer. The actual edited original lyrics are stored in MusicBee's Lyrics tag; imported English translations remain in the plugin's separate folder.

### Browser BPM search

For catalog misses, open **Party BPM → Search Google…** in the lyrics flyout. This opens your browser with the current artist and title; nothing is sent until you click. Review the recording/version and enter the result in **Adjust Party BPM…**, then align and save as usual. This uses no paid API and never replaces timing automatically.

### Online BPM source

Party mode can look up missing BPMs using [GetSongBPM](https://getsongbpm.com/) and [Deezer](https://www.deezer.com/). Deezer lookup is enabled by default and needs no API key; switch **Use Deezer too (no key needed)** off in **Online Party BPM…** to disable it. If a GetSongBPM key is configured, that service is tried first; Deezer is used when it has no match or cannot be reached. Only the song title and artist are sent to Deezer. Returned title, artist, recording version and duration are checked locally, with album matches preferred; conflicting BPMs are rejected. The provider's decimal BPM is kept and the original lookup source is saved with the song. **Adjust Party BPM…** shows that source link and offers **½ speed / 2× speed** without rounding to whole BPM (170.84 becomes 85.42). Click **Align to this beat** to preview, then **Save** to remember both speed and alignment. A catalog BPM is not a beat offset or a tempo map; it can still be inaccurate for a particular recording.

Existing saved timing—including previous automatic results—and BPM tags are protected from new lookups. To replace one deliberately, use **Forget saved BPM**; a song BPM tag still takes priority. **Retry online BPM for this song** retries missing values without replacing saved ones. When both online sources are disabled, the existing live spectrum detector is available. Failed online lookups show their details in **Online Party BPM…** and leave the fallback animation running. Request an optional GetSongBPM key on its [API page](https://getsongbpm.com/api); it is encrypted for the current Windows account and is never stored in the project or music files. The project links back to GetSongBPM as required for its API.

For a library with niche songs, [Mixxx](https://mixxx.org/download/) can batch-analyze the actual audio instead of relying on an online catalog. Import the music folder, leave **Fast Analysis** off in its BPM preferences for full-track analysis, then choose **Analyze → All → Analyze**. First try a few songs and review the results, including half/double tempo mistakes. Mixxx keeps these results in its own library and does not alter audio files by default. **Track Metadata Synchronisation** and **Metadata → Export to File Tags** write metadata back into the audio files; do not use them on the whole collection if preserving all existing tags matters. The plugin currently reads MusicBee BPM tags, not Mixxx's private database. If you choose to export BPM tags, test on copies of a few tracks and compare all tags before applying it to originals. A BPM tag sets speed; use **Align to this beat** for the first-beat position.

The cover scales with window height, and the lyric panel and song title are centred above playback controls. Long current, translation and preview lyrics use two centred rows when that makes them easier to read; the lyric card gives them more vertical room when the window has space. Short lyrics stay on one row. Upcoming lyrics use their final wrapped shape and grow smoothly into the active position, without fading through blank text. English changes separately above them, with the card expanding or contracting smoothly as translation appears or disappears. The outgoing original fades in its own row instead of scrolling through the English. Small translated windows reclaim artwork space and can use extra text rows; text that still cannot fit ends with an ellipsis. Enlarge the window to give long text more room. On narrow windows the LRCLIB and TIMING buttons move into the flyout to leave room for the title. The plugin settings' lyric colour, border colour, and single/two/three-colour gradient choices apply in the normal window. Transparent mode uses opaque neutral cards to avoid purple colour-key fringes while lyrics animate. Artwork colour matching uses several cover hues for a soft background glow and the spectrum. Background and lyric shapes are cached between animation frames. Animation continues while dragging and resizing; the timer also keeps the spectrum fade smooth when playback pauses. Windows still schedules paints during native window resizing, so the actual visible frame rate depends on its compositor and the window size.

### Add English meaning beside timed romaji

Play a song with timed romaji in MusicBee's Lyrics field, open the lyrics window's flyout menu, and choose **Add English meaning from Genius…**. The importer guides you through four steps:

1. Click **Open Genius search**. The search uses only the song title and artist. Choose an English translation if one exists; otherwise copy the song's romaji and translate it line by line yourself, for example in ChatGPT.
2. Paste the English text into the importer. If the line breaks differ, you can also paste the corresponding Genius romaji into the optional box; that helps the importer match phrases to your timed romaji. **Source URL** is optional: it saves the page link with this song and shows it again when you reopen the importer. It does not download lyrics.
3. Click **Align and review**. Compare each English line with the MusicBee lyric and timestamp. Edit the English cells directly. **Repeat previous English here** handles one English phrase spanning two timed lines; **Join next English here** handles two English phrases spanning one timed line. Highlighted pairs and the empty/extra counts flag places to check.
4. Click **Save English**. The English appears above the active romaji immediately. The flyout's **Show English / translation** switch hides or restores it. Reopen the importer to replace or remove it.

The plugin opens Genius in your browser; **you choose and copy the translation**. It does not retrieve full lyrics from Genius's API, scrape the page, or translate them automatically. The alignment uses your existing MusicBee timestamps and needs your review, especially when a translation paraphrases or combines lines. The English is saved per song in `DesktopLyrics-English` under MusicBee's plugin storage, separately from its Lyrics tag. Timing-only edits keep the English; changes to the romaji lines require you to review and import it again. The feature also works without a Genius page if you have an English translation from another source.

Existing bilingual LRC in MusicBee still works: a romaji line and English line at the **same timestamp** are displayed together, with the English smaller. A separately imported English line takes precedence when present. Repeated English phrases are shown once even if punctuation, casing, or a small spelling difference varies; short phrases still require an exact match. English sits above the centred current lyric and the upcoming line remains below it; compact windows allocate separate rows for all three.

At the end of the playing queue, the Next button leaves the window open and shows a brief message. The message also appears when MusicBee signals the queue has ended; it clears when another song starts.

### Edit lyric timing

Click **TIMING** or choose **Edit lyric timing…** in the flyout. If the MusicBee **Lyrics** field already has LRC timestamps, the existing editor opens: click a lyric to play from that line, adjust individual lines with the arrows, or shift the whole song. If it has only plain lyrics, a separate **Create timing** mode opens automatically. Play the song and press **Space** (or **Stamp next**) at the start of each line; click a stamped lyric to replay it, use the arrows to fine tune it, and shift all stamped lines with the whole-song controls. All lines must be stamped before Save is enabled. If there are no lyrics, paste them into MusicBee's Lyrics field or import a plain LRCLIB result first.

Both editors preview edits in the lyric window and write the completed LRC to that song's MusicBee Lyrics tag. The original lyrics are backed up in `DesktopLyrics-TimingBackups` under MusicBee's plugin storage before saving. If the song changes while editing, completed edits can still be saved to the previous song or discarded.

### Find timed lyrics on LRCLIB

Click **LRCLIB** beside TIMING. The picker searches for the current song and sorts results by closeness to its duration. Inspect the preview before saving. Timed results can be used immediately; a plain result can be saved to MusicBee's Lyrics tag and stamped later with **TIMING**. Results with no duration show an unknown length rather than breaking the search, and the picker asks you to check them before saving. The picker also asks before replacing existing lyrics or choosing a version with a substantially different duration. No separate `.lrc` file is created. The search sends title and artist to LRCLIB, uses an identifying User-Agent, and respects its retry limit.

# Chinese version

## MusicBee 桌面歌词
让 MusicBee 支持桌面歌词！

## 说明
本插件用于在你的桌面上显示歌词，就像 QQ 音乐、酷狗音乐和网易云音乐等做的那样。

MusicBee 中歌曲关联的歌词必须为 LRC 格式的同步歌词。支持 `offset` 标签。

本插件能和网易云音乐歌词插件[https://github.com/cqjjjzr/MusicBee-NeteaseLyrics](https://github.com/cqjjjzr/MusicBee-NeteaseLyrics "(GitHub Repo)") [https://getmusicbee.com/forum/index.php?topic=24313.0](https://getmusicbee.com/forum/index.php?topic=24313.0 "(论坛帖)") 联动，显示双行歌词。

双行歌词的两行之间用正斜杠“/”分割。

## 下载 & 安装
在本仓库的 **Actions** 页面打开最新成功的 **Build Desktop Lyrics** 运行，下载 `DesktopLyrics-<version>` 构建产物，其中包含 DLL 和 ZIP 压缩包。

下载后从 MusicBee 设置的“插件”标签页安装或直接将DLL文件复制到 MusicBee 安装目录下的 “`Plugins`” 目录，启动之，在设置中设置好歌词外观，就 OK。

设置项从上至下分别是 字体，渐变色 1，渐变色 2，边框颜色和渐变类型（单色——仅色 1，双色——色1-色2，三色——色1-色2-色1）。

# Pics

![](https://i.imgur.com/o0aYax7.png)

![](https://i.imgur.com/KnHdZzI.png)

Tempo-map precision controls: choose a **Step (s)** from 0.01 to 5 (default 0.10), then use **- step / + step**. Or enter an exact **Seek to (s)** value with three decimal places and press Seek/Enter. These seek playback without moving section markers; edit a row's Start (s) to move its boundary. Pause when placing precise boundaries. **Align uses that row's Start time**, not the time you click Align or Save. Save applies the edited map. Dance/Rhythm dropdowns open with one click. Playback seek accuracy still depends on MusicBee and the audio decoder.

Window controls and dance speed (1.15.41):
- The framed lyrics window has native minimise/maximise/close controls and a taskbar entry for restoring it. It stays on top while restored; minimising hides its dancers and stops its animation timer.
- Tempo-map **Dance** chooses Normal, Side to side, or Hold pose. Separate **Speed** chooses Half (0.5x), Normal (1x), or Double (2x), with every rhythm supported. Speed multiplies dance timing without changing the song BPM. Holds remain still. Count-in can accompany a return from Half to Normal speed with either moving dance style.
- Older Half speed rows load as Normal dance + Half speed, preserving their beat phase, ramps and count-in. Existing files are not rewritten on load; Save writes the updated map format (version 2).
- Maximised Party Mode extends the song colours and spectrum behind both dancers; foreground layout and dancer positions are unchanged.
- The compact Queue tab shares the top toolbar instead of overlapping lyrics. Its expanded list remains a temporary overlay.
- Play/pause clicks update the icon and clear paused spectrum immediately, then reconcile with MusicBee's reported state. Rejected commands revert; a delayed report has a short grace period.

Maximised lyrics and pause animation (1.15.42):
- Pause still updates the icon immediately, but releases spectrum targets to zero instead of erasing the bar heights. The existing falling animation starts on the next frame, without waiting for MusicBee's delayed state report.
- On sufficiently large maximised windows, larger artwork sits at the upper-left of the centre area and the queue sits at the upper-right. Lyrics use the wider space below, while dancers retain their existing positions.
- The current lyric uses a larger font and measured row spacing. English and upcoming lyrics have smaller visual emphasis; preview size is capped relative to the fitted current line, so short previews do not overpower long current lyrics. Transition endpoints keep stable text layouts as the lyric card resizes.
- Smaller/restored windows keep their compact layout. Selectable accent beats and GPU integration remain planned.
