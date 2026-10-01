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

The blue waveform now shows an average-energy body inside thin peak outlines. The orange lane below it highlights increases in low-, mid- and high-frequency band energy. It uses relative contrast scaling within the visible range, so spike height can change when you zoom. The tallest outliers are capped visually to keep other attacks readable; the underlying values used to locate peaks are retained. Sustained volume is less prominent, but instrument changes and noise can still create spikes. They are visual candidates, not automatic BPM analysis or guaranteed musical beats. Snap hits uses nearby local maxima in this lane; leave it off to place hits freely.

In Accent cues, enable **New hit wins** on a hit that should interrupt earlier accents. The interruption occurs at that hit's exact timestamp. Its preparation before the hit retains existing overlap behavior; after the interruption, older holds/recoveries do not return. Future cues still follow their own settings. The setting defaults off and never changes beat integration or song data by itself. Use Save to persist it, or Loop preview to audition without saving.

For the recorded 99.9 opening, the first two saved accents are 70ms apart. Try New hit wins on the second accent so the stronger first recovery cannot suppress it. A shorter first recovery may still give a clearer movement; this option does not invent an extra upward bounce between closely spaced hits.


## Accent continuity and drag navigation (1.15.68)

Enable **Flow accent sequences** beside Use this map, then Save (or audition with Loop preview). It is saved per song and defaults off for existing maps. During a continuous Hold/Rest passage, the first Alternate cue lands on the starting side; later Alternate cues switch from the preceding cue. Current and explicit Left/Right cues participate too, so changing the first cue to Current does not reverse the remaining alternation. If the entry pose is central, Alternate starts on the right-hit pose. A new Hold/Rest passage starts a new sequence. Accents outside Hold/Rest retain their original behavior.

When dancing resumes with Align off, an accent recovery that would jump directly to the opposite side uses a centre pose for that release slot, then rejoins the scheduled dance. This is a pose bridge, not a fade or a BPM/beat-phase adjustment. Explicit Align takes precedence. Existing recovery durations and New hit wins still control overlapping accents.

For the 99.9 opening, try Flow accent sequences ON, Alternate sides on all opening hits, New hit wins on each subsequent closely spaced hit, and Align OFF on the returning dance section. Keep your carefully placed times. First-current/rest-alternate also works. Save keeps the editor open; turning Flow off restores the old choreography.

Drag empty space in the detailed timeline or waveform to move the visible range, like dragging a sheet of paper. Drag right to reveal earlier time, left for later time. The same pixel distance moves fewer seconds when zoomed in. A click without dragging still seeks. Dragging the playhead still seeks; gold markers move accents; white handles edit durations; Shift-drag selects a loop. Wheel zoom remains centred under the pointer. Panning never edits song data or seeks playback. Waveform decoding refreshes after navigation settles.


## Opposite-side return and retained waveform (1.15.69)

With **Flow accent sequences** enabled, the final accent recovers through centre and the next scheduled side landing is opposite that accent. Subsequent side landings keep alternating. This only changes the drawing's left/right order, never the beat times, BPM, ramps or stored hit timestamps. The chosen order continues through ordinary BPM points, until a new Hold/Rest passage, explicit Align, or a change of dance/rhythm. Side-to-side mode stays on its last side until the next opposite landing instead of adding a centre pose. Existing maps with Flow disabled retain their behavior. No need to retime the 99.9 opening or resave it if Flow is already enabled.

The waveform now loads a surrounding buffer, normally three times the visible span, capped at 60 seconds and 16,384 bins. Nearby panning reuses the same samples and attack contrast without clearing or decoding them again. When panning beyond the buffer, its overlapping portion stays visible while a new buffer loads after navigation settles. Newly exposed audio cannot be displayed until decoded. Zooming requests an appropriately sized buffer. Only one completed buffer is retained, with one background decoder; no disk cache or whole-library analysis. The old buffer remains alongside the in-flight result until replacement, so transient memory includes both buffers plus decoder scratch space. Attack contrast may change on buffer replacement or zoom, but stays fixed while panning within it.

The yellow loop range and playhead now stay inside the timeline's drawing bounds at any zoom. Editing surfaces no longer show tooltips. Hover the instruction text above the overview for timeline help; individual settings retain their tooltips.


## Cached navigation, section dragging and song following (1.15.70)

Waveforms use up to sixteen cached 30-second chunks (about 3 MiB maximum sample-array storage), with one decoder. Visible chunks load first, then two neighbouring chunks in each direction. Panning and zooming reuse the same samples. Views up to five minutes can fill in progressively; longer views require zooming in. Jumps outside the cache still need loading. Changing songs clears the cache; no waveform files are stored on disk. Decoder scratch space and one pending result are additional to the sample cache. Attack contrast is fixed per chunk, so background prefetch does not rescale existing spikes.

Drag a section diamond to move its start, just like an accent. The move commits on release without seeking. A click still selects and seeks. The first section stays at zero and other sections cannot cross their neighbours. Ramp to next follows the moved boundary automatically. If a custom saved ramp would become invalid, the move is rejected and the original start retained. Escape cancels an unfinished drag.

The tempo editor is now modeless: you can drag and use the main lyrics window while it is open. Opening Tempo map again focuses the existing editor. A saved editor follows the newly playing song automatically once its metadata is ready, keeping the editor's position and size. Unsaved edits stay attached to the original song instead of being discarded or applied to the wrong song. The status explains this; Save commits them to that song and then permits following. Close offers the existing discard confirmation. Song changes safely end audition before replacing the editor. Follow does not modify any saved map by itself.

Layout alternatives are previews only and are not included in this build.
