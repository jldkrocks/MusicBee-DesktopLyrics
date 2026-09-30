# Precise accent editing (1.15.65)

1. Open the song's tempo map. Hover over the desired point of the detail timeline and scroll to zoom around that point. Drag the selected window in the thin overview bar, or scroll over that bar, to pan without changing zoom or seeking. Clicking outside the selected window centres the same-sized view there. Whole song resets the view.
2. Create accents as before, including by right-clicking a section row to copy its timestamp to Accent cues. Gold circles represent accents; diamonds represent sections.
3. Drag a gold circle to change its timestamp. This does not seek playback. With an accent selected and the detail timeline focused, Left/Right nudges it by 10 ms; Shift+Left/Right uses 1 ms. Escape cancels an unfinished drag.
4. Shift-drag the detail timeline to mark a loop, enter its two times in Loop (s), or choose Use view. Loop preview plays from 0.5 seconds before the start and repeats at the end. It uses MusicBee pause/seek/play commands, so boundaries are not gapless.
5. During preview, valid unsaved changes are auditioned in memory. Invalid edits leave the last valid preview running and show an error. Enable Use this map for this song to hear the mapped choreography.
6. Stop preview returns paused to the range start and restores the last saved map. Save commits both tabs without closing, including while previewing. Closing still asks before discarding unsaved edits. Changing songs cancels preview and keeps editing tied to the original song.

For opening hits, loop a few seconds around the phrase, place one accent per intended hit, then adjust timestamps and existing preparation/hold settings while auditioning. Millisecond editing is available, but audible seek precision still depends on MusicBee and the audio file. Waveform editing and visual preparation/hold handles are not included in this checkpoint.

Shift-click the title/artist area at the top of the lyrics window to copy the current artist and song title. Hover there to see the shortcut.
