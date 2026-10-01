# Precise accent editing (1.15.67)

1. Open the song's tempo map. Hover over the desired point of the detail timeline and scroll to zoom around that point. Drag the selected window in the thin overview bar, or scroll over that bar, to pan without changing zoom or seeking. Clicking outside the selected window centres the same-sized view there. Whole song resets the view.
2. Create accents as before, including by right-clicking a section row to copy its timestamp to Accent cues. Gold circles represent accents; diamonds represent sections.
3. Drag a gold circle to change its timestamp. This does not seek playback. With an accent selected and the detail timeline focused, Left/Right nudges it by 10 ms; Shift+Left/Right uses 1 ms. Escape cancels an unfinished drag.
4. Shift-drag the detail timeline to mark a loop, enter its two times in Loop (s), or choose Use view. Loop preview plays from 0.5 seconds before the start and repeats at the end. It uses MusicBee pause/seek/play commands, so boundaries are not gapless.
5. During preview, valid unsaved changes are auditioned in memory. Invalid edits leave the last valid preview running and show an error. Enable Use this map for this song to hear the mapped choreography.
6. Stop preview returns paused to the range start and restores the last saved map. Save commits both tabs without closing, including while previewing. Closing still asks before discarding unsaved edits. Changing songs cancels preview and keeps editing tied to the original song.

For opening hits, loop a few seconds around the phrase, place one accent per intended hit, then adjust timestamps and existing preparation/hold settings while auditioning. Millisecond editing is available, but audible seek precision still depends on MusicBee and the audio file. Zoom to a range of 60 seconds or less to load its waveform. Only that local audio range is decoded, in the background; unsupported codecs keep the ordinary editor. Waveform peaks are not automatic beat detection.

Shift-click the title/artist area at the top of the lyrics window to copy the current artist and song title. Hover there to see the shortcut.

## Waveform and accent duration

Select a gold accent marker. Its dotted line extends through the waveform so the hit can be lined up with the audio attack. Purple shows preparation, gold shows the post-hit hold, and green shows recovery. Drag the three white handles, or edit Lead-in, Hold and Recovery in the Accent cues grid. Escape cancels an unfinished handle drag. Save/preview behavior stays the same.

Recovery accepts 0.02-2 seconds. A blank grid value retains the original default: 0.22 seconds for Bop, 0.42 for Rebound. Existing saved accents keep those defaults. Shorter recovery can separate rapid hits; longer recovery softens isolated hits. Recovery scales the rebound motion too.

Snap hits is optional and initially off. It snaps a dragged hit to a nearby attack-strength peak within 40 ms, which can be an instrument attack or noise rather than the intended beat. Arrow nudges bypass snapping for final adjustments. Audition the result.

## Count-in from silence or a rest

On the row where dancing RETURNS, choose Normal (1x) speed and tick Bob count-in. The preceding section can now be Hold pose, Rest (keep counting), or Half speed. It adds up to four bops during that section and one final landing at the first dance beat at/after the return. Short sections fit fewer bops; less than one incoming beat gives none. Hold still freezes the beat clock; Rest still counts. The count-in only adds vertical motion, and does not change the saved alignment. Enable Align on the returning row only if its timestamp should explicitly restart the dance phase. Manual accent cues can take visual priority when they overlap a count-in.

The waveform uses Windows [Media Foundation Source Reader](https://learn.microsoft.com/en-us/windows/win32/medfound/processing-media-data-with-the-source-reader) through the existing native helper. It does not access or change MusicBee's playback stream. Decoding is limited to the visible range (up to 60 seconds), with a bounded peak array, cancellation and no persistent cache. Missing Windows decoder support leaves the rest of the editor usable.

## Attack strength and overlapping hits (1.15.67)

The blue waveform now shows an average-energy body inside thin peak outlines. The orange lane below it highlights increases in low-, mid- and high-frequency band energy. It is normalized to the visible range, so spike height can change when you zoom. Sustained volume is less prominent, but instrument changes and noise can still create spikes. They are visual candidates, not automatic BPM analysis or guaranteed musical beats. Snap hits uses nearby local maxima in this lane; leave it off to place hits freely.

In Accent cues, enable **New hit wins** on a hit that should interrupt earlier accents. The interruption occurs at that hit's exact timestamp. Its preparation before the hit retains existing overlap behavior; after the interruption, older holds/recoveries do not return. Future cues still follow their own settings. The setting defaults off and never changes beat integration or song data by itself. Use Save to persist it, or Loop preview to audition without saving.

For the recorded 99.9 opening, the first two saved accents are 70ms apart. Try New hit wins on the second accent so the stronger first recovery cannot suppress it. A shorter first recovery may still give a clearer movement; this option does not invent an extra upward bounce between closely spaced hits.
