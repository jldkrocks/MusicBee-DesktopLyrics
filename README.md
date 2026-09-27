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

Drag the title bar to move the window. It remembers its size and position. Resizing scales the lyrics; line changes slide and fade. The current lyric, optional translation, and faded next lyric appear over spectrum bars driven by MusicBee's audio data. When a track has no lyrics, the lyric panel disappears. **Match window colours to album artwork** is enabled on upgrade and can be turned off in plugin settings. MusicBee's built-in visualizers open in their own views; this window draws its own bars so the lyrics stay in front.

If you close the visualizer with **X**, choose **View → Desktop Lyrics** to reopen it, or use **Show lyrics window** in the plugin settings. Closing with X is temporary and the window returns on the next MusicBee launch. Using the View menu to turn Desktop Lyrics off remains a saved preference.

The window shows the song title, album art, lyrics, and previous/play/next controls. The **Queue** panel shows tracks played since this plugin started that are still in MusicBee's Playing Tracks list, the current song, and the upcoming playback order. Removing a track from Playing Tracks removes it from the plugin's history at the next queue refresh. Scroll with the mouse wheel or arrows and click a track to play it. At medium window sizes the queue uses shorter rows; at the smallest sizes a **QUEUE** tab opens a scrollable queue over the right side of the window. The panel shows each file once when a recently played song also appears in the upcoming order. MusicBee's plugin API has no history query, so earlier sessions cannot be shown. MusicBee can only select a queued file by URL; the plugin explains when it cannot distinguish duplicate entries. The **BG** button switches between the visualizer background and a borderless transparent canvas. The lyric card, art, title, controls, queue, BG and menu buttons remain visible; LRCLIB and TIMING stay available in the menu. Drag the title or lyric card to move the borderless window, and use its small lower-right grip to resize it. The menu has a **Close lyrics window** action; the MusicBee toolbar command can reopen it. These choices are saved for the next MusicBee launch.

The **PARTY** button near the lower-left corner shows the supplied Rem and Ram dancers beside the window. Click it again to hide them; the choice is remembered on the next launch. They grow with the window and remain outside it in normal mode, including across connected monitors. If there is no space outside either side of the whole desktop, that dancer hides instead of covering the lyrics. When maximized, the window reserves a dedicated gutter on each side and moves the artwork, lyrics, queue and controls into the centre; the dancers occupy those gutters. They follow movement and resizing, freeze when playback pauses, and remain click-through. Each complete dance loop takes four beats at the song's BPM, so different tempos produce visibly different speeds. The PARTY button shows `TAG 120` for a BPM tag, `AUTO 120` when live detection first locks a tempo, `SAVED 120` when it loads a previously detected tempo, or `SET 120` for a manual adjustment. It shows `LISTENING...` while searching and `NO SIGNAL` if MusicBee supplies no spectrum. Without a BPM tag or saved tempo, the plugin samples MusicBee's live low-frequency spectrum until it has a consistent tempo. It saves that BPM and dance phase per song in MusicBee's plugin storage, so later plays start at the saved speed without listening again. A manual BPM overrides both a tag and an automatic value. Choose **Adjust Party BPM…** from the window's flyout, type a BPM from 40 to 240 (or use the arrows), and click **Save**; **Forget saved BPM** returns the song to its tag or live detection. Changes preserve the current animation frame. While it learns or when no steady pulse is detectable, it runs at its original 140 ms per frame. This works with visualizer bars turned off and does not edit the songs or require filling in thousands of BPM tags. Live detection can be imprecise on tracks with weak or irregular percussion and does not identify an exact beat grid; a BPM tag also gives tempo rather than an exact first beat. BG already has a dedicated button, so its duplicate flyout switch is removed; LRCLIB and TIMING appear in the flyout only when their buttons are hidden by the compact layout.

To add a MusicBee toolbar button, right-click its toolbar and choose **Configure Toolbar**. Add a button and choose **View: Toggle Desktop Lyrics Window** from the command dropdown. MusicBee's toolbar layout remains yours to arrange; the command also appears in Hotkeys.

The cover scales with window height, and the lyric panel and song title are centred above playback controls. On narrow windows the LRCLIB and TIMING buttons move into the flyout to leave room for the title. The plugin settings' lyric colour, border colour, and single/two/three-colour gradient choices apply in the normal window. Transparent mode uses opaque neutral cards to avoid purple colour-key fringes while lyrics animate. Artwork colour matching uses several cover hues for a soft background glow and the spectrum. Background and lyric shapes are cached between animation frames. Animation continues while dragging and resizing; the timer also keeps the spectrum fade smooth when playback pauses. Windows still schedules paints during native window resizing, so the actual visible frame rate depends on its compositor and the window size.

### Add English meaning beside timed romaji

Play a song with timed romaji in MusicBee's Lyrics field, open the lyrics window's flyout menu, and choose **Add English meaning from Genius…**. The importer guides you through four steps:

1. Click **Open Genius search**. The search uses only the song title and artist. Choose an English translation if one exists; otherwise copy the song's romaji and translate it line by line yourself, for example in ChatGPT.
2. Paste the English text into the importer. If the line breaks differ, you can also paste the corresponding Genius romaji into the optional box; that helps the importer match phrases to your timed romaji. **Source URL** is optional: it saves the page link with this song and shows it again when you reopen the importer. It does not download lyrics.
3. Click **Align and review**. Compare each English line with the MusicBee lyric and timestamp. Edit the English cells directly. **Repeat previous English here** handles one English phrase spanning two timed lines; **Join next English here** handles two English phrases spanning one timed line. Highlighted pairs and the empty/extra counts flag places to check.
4. Click **Save English**. The English appears above the active romaji immediately. The flyout's **Show English / translation** switch hides or restores it. Reopen the importer to replace or remove it.

The plugin opens Genius in your browser; **you choose and copy the translation**. It does not retrieve full lyrics from Genius's API, scrape the page, or translate them automatically. The alignment uses your existing MusicBee timestamps and needs your review, especially when a translation paraphrases or combines lines. The English is saved per song in `DesktopLyrics-English` under MusicBee's plugin storage, separately from its Lyrics tag. Timing-only edits keep the English; changes to the romaji lines require you to review and import it again. The feature also works without a Genius page if you have an English translation from another source.

Existing bilingual LRC in MusicBee still works: a romaji line and English line at the **same timestamp** are displayed together, with the English smaller. A separately imported English line takes precedence when present. Repeated English phrases are shown once even if punctuation, casing, or a small spelling difference varies; short phrases still require an exact match. English sits above the centred current lyric and the upcoming line remains below it; only an extremely short lyric area hides the upcoming line to keep the active lyric legible.

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
