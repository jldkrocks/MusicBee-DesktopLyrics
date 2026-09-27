# Desktop Lyrics
Show the lyrics on your desktop from MusicBee!

# Description
This plugin is for showing lyrics on your desktop, just like clients of very many popular online music services does(such as Netease Cloud Music, QQ Music, Kugou Music, etc.).

The lyrics must in LRC format, synchronized. Offset label is supported.

It can work with NeteaseLyrics plugin [https://github.com/cqjjjzr/MusicBee-NeteaseLyrics](https://github.com/cqjjjzr/MusicBee-NeteaseLyrics "(GitHub Repo)") [https://getmusicbee.com/forum/index.php?topic=24313.0](https://getmusicbee.com/forum/index.php?topic=24313.0 "(Forum Topic)") in order to display double-line lyrics, if you can provide lrc splitted with slash "/" it can also correctly handled.

# Download & Installation
To use this plugin, download it from "Release" page of this GitHub repo or from links below:  
For users who are in China:  
[https://pan.baidu.com/s/1UrPo_NF8H3dNwQyNpNEZbg?pwd=1sr3](https://pan.baidu.com/s/1UrPo_NF8H3dNwQyNpNEZbg?pwd=1sr3 "Baidu Netdisk Download"), passcode `1sr3`  
For users who aren't in China:  
[https://1drv.ms/f/s!AicHZ6DLvCtX7B5M4CRdcJfCULCe](https://1drv.ms/f/s!AicHZ6DLvCtX7B5M4CRdcJfCULCe "OneDrive Download")  

Just install this plugin in the Plugins tab in the Settings or put the DLL file into the Plugins of your MusicBee installation, and then enable it, set the lyrics style in the settings, and you're ready to rock!

Also, lyrics style can be modified in the Plugins tab. And you can hide or show the desktop lyrics in the MusicBee Menu(View->Desktop Lyrics).

## Lyrics window

Drag the title bar to move the window. It remembers its size and position. Resizing scales the lyrics; line changes slide and fade. The current lyric, optional translation, and faded next lyric appear over spectrum bars driven by MusicBee's audio data. When a track has no lyrics, the lyric panel disappears. **Match window colours to album artwork** is enabled on upgrade and can be turned off in plugin settings. MusicBee's built-in visualizers open in their own views; this window draws its own bars so the lyrics stay in front.

If you close the visualizer with **X**, choose **View → Desktop Lyrics** to reopen it, or use **Show lyrics window** in the plugin settings. Closing with X is temporary and the window returns on the next MusicBee launch. Using the View menu to turn Desktop Lyrics off remains a saved preference.

The window shows the song title, album art, lyrics, and previous/play/next controls. The **Queue** panel shows tracks played since this plugin started that are still in MusicBee's Playing Tracks list, the current song, and the upcoming playback order. Removing a track from Playing Tracks removes it from the plugin's history at the next queue refresh. Scroll with the mouse wheel or arrows and click a track to play it. At medium window sizes the queue uses shorter rows; at the smallest sizes a **QUEUE** tab opens a scrollable queue over the right side of the window. The panel shows each file once when a recently played song also appears in the upcoming order. MusicBee's plugin API has no history query, so earlier sessions cannot be shown. MusicBee can only select a queued file by URL; the plugin explains when it cannot distinguish duplicate entries. The **BG** button switches between the visualizer background and a borderless transparent canvas. The lyric card, art, title, controls, queue, BG and menu buttons remain visible; LRCLIB and TIMING stay available in the menu. Drag the title or lyric card to move the borderless window, and use its small lower-right grip to resize it. The menu has a **Close lyrics window** action; the MusicBee toolbar command can reopen it. These choices are saved for the next MusicBee launch.

To add a MusicBee toolbar button, right-click its toolbar and choose **Configure Toolbar**. Add a button and choose **View: Toggle Desktop Lyrics Window** from the command dropdown. MusicBee's toolbar layout remains yours to arrange; the command also appears in Hotkeys.

The cover scales with window height, and the lyric panel is centred above playback controls. The plugin settings' lyric colour, border colour, and single/two/three-colour gradient choices apply in the normal window. Transparent mode uses opaque neutral cards to avoid purple colour-key fringes while lyrics animate. Artwork colour matching uses several cover hues for a soft background glow and the spectrum. Background and lyric shapes are cached between animation frames. Animation continues while dragging and resizing; the timer also keeps the spectrum fade smooth when playback pauses. Windows still schedules paints during native window resizing, so the actual visible frame rate depends on its compositor and the window size.

For bilingual lyrics, put a romaji line and its English translation at the **same timestamp** in MusicBee's Lyrics field, for example `[00:52.27] Romaji line` followed by `[00:52.27] English meaning`. The English line appears smaller beneath the active lyric. The flyout's **Show translation** switch hides or restores it. In short windows, a translation takes the space normally used by the next-line preview; in taller windows both can appear. Existing paired LRC lyrics stay in the MusicBee tag. This release does not fetch translations from Genius or translate Japanese text automatically.

At the end of the playing queue, the Next button leaves the window open and shows a brief message. The message also appears when MusicBee signals the queue has ended; it clears when another song starts.

### Edit lyric timing

Click **TIMING** or choose **Edit lyric timing…** in the flyout. If the MusicBee **Lyrics** field already has LRC timestamps, the existing editor opens: click a lyric to play from that line, adjust individual lines with the arrows, or shift the whole song. If it has only plain lyrics, a separate **Create timing** mode opens automatically. Play the song and press **Space** (or **Stamp next**) at the start of each line; click a stamped lyric to replay it, use the arrows to fine tune it, and shift all stamped lines with the whole-song controls. All lines must be stamped before Save is enabled. If there are no lyrics, paste them into MusicBee's Lyrics field or import a plain LRCLIB result first.

Both editors preview edits in the lyric window and write the completed LRC to that song's MusicBee Lyrics tag. The original lyrics are backed up in `DesktopLyrics-TimingBackups` under MusicBee's plugin storage before saving. If the song changes while editing, completed edits can still be saved to the previous song or discarded.

### Find timed lyrics on LRCLIB

Click **LRCLIB** beside TIMING. The picker searches for the current song and sorts results by closeness to its duration. Inspect the preview before saving. Timed results can be used immediately; a plain result can be saved to MusicBee's Lyrics tag and stamped later with **TIMING**. The picker asks before replacing existing lyrics or choosing a version with a substantially different duration. No separate `.lrc` file is created. The search sends title and artist to LRCLIB, uses an identifying User-Agent, and respects its retry limit.

# Chinese version

## MusicBee 桌面歌词
让 MusicBee 支持桌面歌词！

## 说明
本插件用于在你的桌面上显示歌词，就像 QQ 音乐、酷狗音乐和网易云音乐等做的那样。

MusicBee 中歌曲关联的歌词必须为 LRC 格式的同步歌词。支持 `offset` 标签。

本插件能和网易云音乐歌词插件[https://github.com/cqjjjzr/MusicBee-NeteaseLyrics](https://github.com/cqjjjzr/MusicBee-NeteaseLyrics "(GitHub Repo)") [https://getmusicbee.com/forum/index.php?topic=24313.0](https://getmusicbee.com/forum/index.php?topic=24313.0 "(论坛帖)") 联动，显示双行歌词。

双行歌词的两行之间用正斜杠“/”分割。

## 下载 & 安装
要用此插件请从本 GitHub repo 的 “Release” 页面或下列链接下载：  
国内：
[https://pan.baidu.com/s/1UrPo_NF8H3dNwQyNpNEZbg?pwd=1sr3](https://pan.baidu.com/s/1UrPo_NF8H3dNwQyNpNEZbg?pwd=1sr3 "Baidu Netdisk Download"), 提取码 `1sr3`  
国外：  
[https://1drv.ms/f/s!AicHZ6DLvCtX7B5M4CRdcJfCULCe](https://1drv.ms/f/s!AicHZ6DLvCtX7B5M4CRdcJfCULCe "OneDrive Download")  

下载后从 MusicBee 设置的“插件”标签页安装或直接将DLL文件复制到 MusicBee 安装目录下的 “`Plugins`” 目录，启动之，在设置中设置好歌词外观，就 OK。

设置项从上至下分别是 字体，渐变色 1，渐变色 2，边框颜色和渐变类型（单色——仅色 1，双色——色1-色2，三色——色1-色2-色1）。

# Pics

![](https://i.imgur.com/o0aYax7.png)

![](https://i.imgur.com/KnHdZzI.png)
