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

## Compact lyrics window

The compact visualizer window is enabled by default on upgrade. You can also switch modes with **View → Desktop Lyrics Visualizer Window**, or with the checkbox at the top of Desktop Lyrics plugin settings. The settings title shows the loaded plugin version. Drag the window's title bar to move it; the window remembers its size and position. Resizing the window scales the lyrics, and lyric changes slide and fade smoothly. The current lyric, optional translation, and faded next lyric appear over spectrum bars driven by MusicBee's audio data. When a track has no lyrics, the lyric panel disappears and the visualizer remains. **Match window colours to album artwork** is enabled on upgrade and can be turned off in plugin settings; without artwork, the normal colours are used. Turn compact mode off to return to the transparent desktop overlay. MusicBee's built-in visualizers open in their own views; this window draws its own bars so the lyrics can stay in front of them.

If you close the visualizer with **X**, choose **View → Desktop Lyrics** to reopen it, or use **Show lyrics window** in the plugin settings. Closing with X is temporary and the window returns on the next MusicBee launch. Using the View menu to turn Desktop Lyrics off remains a saved preference.

The window can show a small song title at the top, cover art beside the lyrics, and previous, play/pause, and next controls at the bottom. The middle button changes between pause and play as playback changes. A quiet **Up next** panel shows up to four songs in playback order when the window is wide enough. Open the three-line menu in the upper right (or right-click the window) to toggle the title, cover art, playback controls, upcoming songs, spectrum, artwork colours, and next-lyric preview. **More settings…** opens the font and other plugin settings from the window. These choices are saved for the next MusicBee launch. The lyric panel slides and fades with the text.

The cover scales with the window height, and the lyric panel is centred above the playback controls. The plugin settings' lyric colour, border colour, and single/two/three-colour gradient choices also apply to text in this window. Artwork colour matching uses several cover hues for a soft background glow and the spectrum. Background and lyric shapes are cached between animation frames, and redraws pause while dragging or resizing the window.

At the end of the playing queue, the Next button leaves the window open and shows a brief message. The message also appears when MusicBee signals the queue has ended; it clears when another song starts.

### Edit lyric timing

For a song whose timestamped LRC lyrics are pasted into MusicBee's **Lyrics** field, click **TIMING** at the top right of the compact window (or choose **Edit lyric timing…** in its flyout). The separate editor shows one row per timestamp; lyrics and translations sharing a timestamp move together. Click a lyric or its time to play from just before that line without editing it. Use the arrows beside a row for ±0.1 or ±1 second, or the **Whole song** buttons to shift every timestamp, including empty pause markers. Each adjustment previews the result in the lyrics window and plays from shortly before the adjusted line. **Reset** restores the original timings; **Cancel** discards changes. **Save edits** writes the updated timestamps to that song's MusicBee Lyrics tag and refreshes its panels. The original LRC is copied to `DesktopLyrics-TimingBackups` under MusicBee's plugin storage before saving. If the song changes while editing, the editor keeps unsaved changes available to save to the previous song or discard.

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
