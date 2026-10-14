# Subtitle editing

[English](subtitle-editing.md) · [简体中文](../zh-cn/subtitle-editing.md) · [All guides](README.md)

## Tracks and compositing

A track can contain subtitles, images, and shapes. Clips on the same track cannot overlap; put overlapping clips on separate tracks. The top track appears in front in both preview and export.

Drag a track header to change its order. Its clip and animation rows move together; the insertion line shows the destination. Release commits one undoable change, which is saved with the project. Escape cancels the drag. Solo and fold buttons keep their own actions.

## Timeline clip timing

Move subtitle clips and trim either edge in 1 ms increments by default; trimming keeps at least 1 ms of duration. **Step** aligns edits to the visible ruler divisions. With snapping enabled, either clip edge can snap to another clip boundary or the current red playback line, using the target's exact time. Hold Alt to temporarily bypass Step and snapping.

Extending a clip to the left preserves existing effect keyframes at their original project times. You can move or add keyframes throughout the expanded clip. Each completed drag commits once and can be undone.

With a clip selected, press **Shift+Q** to seek to its start or **Shift+W** to seek to its end. Multiple selection uses the primary clip. Seeking preserves playback state and zoom, and brings an offscreen target into the visible timeline. Text inputs retain uppercase Q/W entry. Rebind or disable these commands in **Settings → Shortcuts → Playback and audition**.

Select multiple rows in the subtitle list or clips in the timeline, right-click **Move**, enter an integer number of milliseconds, and click **Confirm**. Positive values move later; negative values move earlier. The same offset shifts both boundaries of every target, preserving duration, spacing, and internal animation time. The subtitle list moves selected subtitles; the timeline moves the entire selected clip set. Cancel or 0 leaves timing unchanged. Moving any target before zero or creating overlapping clips on the same track rejects the whole batch. One Undo restores the batch.

## Rich text and karaoke

Select a subtitle and open **Subtitle Details** from View or the subtitle row. The dockable panel edits native text, visual ranges, and timing together. ASS tags belong to import and export. The square appearance button at the top opens the three editing states: its lower-right gray ring, amber clock, and green check identify Normal, Inactive, and Active appearance. Its tooltip names the current state. Playback, loop, snapping, duration-label, and chain-shaped linked-timing controls share the timing-editing toolbar above the axis. The **Reset** arrow appears last, separated from the other actions by a divider.

1. **Normal** edits selected text typography, fill, outline, and shadow. **Inactive (INACTIVE)** and **Active (ACTIVE)** preview and edit the two highlight appearances. Untimed and disabled text can also retain both range styles. With no text selection, highlight edits cover the whole line; an ACTIVE whole-line preset can provide its default appearance.
2. Select complete untimed text, choose **Time selected text**, and explicitly enter its start and end. Opening the panel and ordinary text editing do not generate timing. First **Enable highlight** with no timing data divides the visible cue duration among complete graphemes. Existing whole groups stay intact and disabled caches are restored first. **Reset** in the timing toolbar regenerates all grapheme timing, replacing the line's enabled and cached groups.
3. A timing group can contain multiple Unicode graphemes. Import, open, save, and everyday edits preserve the whole group. Each character can have its own highlight appearance without splitting timing. Only **Split into characters** divides the selected group equally at complete Unicode grapheme boundaries using exact times. Ctrl/Meta-click toggles multiple groups on the axis; Shift-click selects a text-order range. **Merge selected timing groups** requires contiguous text, touching time boundaries, and the same highlight mode. Gaps, overlaps, and backward timing prevent merging; the disabled tooltip explains why. One Undo restores the entire merge.
4. With **Linked timing** disabled (the default), drag the left or right handle to change only that endpoint; drag the group body to move both endpoints while retaining duration. Groups can have gaps, overlapping times, or a time order different from their text order. Neighboring groups do not move automatically. Displayed times are relative to the cue's visible start and may be negative; content times must remain nonnegative and each group must have positive duration.
5. **Linked timing** in the karaoke toolbar controls both numeric input and dragging. When enabled, start edits or the left handle move preceding groups in text order; end/duration edits or the right handle move following groups; body drags move both sides. Other groups keep their durations and existing gaps. When disabled, edits affect only the current group. **Earliest start** moves all enabled groups from the earliest time, preserving durations, gaps, and relative order. **Restore saved timing** restores disabled groups. Disabling karaoke retains timing and independent visual ranges.

Optional duration labels show every group's length; otherwise they appear during dragging. Dense labels can overlay elements within their panel, but are clipped at its boundary to avoid covering other panels or tabs. A red overflow region marks times outside the cue without changing its boundaries. **Fit all timing** changes the timing viewport without editing the project.

Valid text and style drafts commit on Enter or blur. Leaving the current input commits that field's valid value or restores its invalid value, retaining other field drafts. Moving focus between Start, End, and Duration within the timing popup retains the timing drafts for joint validation; Enter, normal popup dismissal, or an external command commits valid timing drafts. **Reset** at the top of the panel discards pending text, style, and timing; the timing toolbar's **Reset** separately regenerates timing. Cancelling manual creation discards only its popup inputs; cancelling the group timing popup discards only timing drafts. A completed range gesture and existing valid drafts form one Undo. Target, content, timing-origin, or linkage-mode changes cancel an unfinished gesture.

Text edits preserve unaffected group identities and times. Insertion inside a group shares its whole timing; insertion at a group boundary is untimed. Deletion leaves a time gap rather than transferring duration to a neighbor. Cross-group replacement follows the existing text mapping; new text without an existing owner stays untimed. Combining characters and emoji must remain complete graphemes with one timing owner; edits violating that boundary are rejected. Explicitly split a group before splitting a cue through its interior.

Projects save as v14 and read v3–v14. Reading v3–v10 performs the legacy per-grapheme division once in memory to preserve earlier playback, moving legacy segment ACTIVE/INACTIVE appearances to independent text ranges. Reading v11–v14 never divides groups automatically or repeats the range-style migration. Opening an older file does not rewrite it; saving writes the current format. See [storage compatibility](architecture.md#time-and-storage).

Set alignment in the **Styles** panel using two groups of icon buttons: left/center/right and top/middle/bottom. Changing one direction preserves the other, and the same control is available in the style library. The combined value matches ASS `\an1`–`\an9`. Explicit position stays controlled by the position editor. Older projects retain their independent text alignment until you actively choose an alignment, which clears that legacy override.

The Styles panel and Settings → Styles use the same four groups in the same order: **Typography**, **Fill and outline**, **Shadow**, and **Layout and position**. All groups stay expanded. Font selection shares a row with Bold and Italic icon toggles on its right, followed by Size and Line height together; fill and outline each have their own row. Layout uses one diagram for margins and position. Settings also retains the preset name, sample preview, and library toolbar.

**Letter spacing** adjusts the gaps between shaped text clusters and accepts negative values while keeping combining characters and ligatures intact. **Fill blur** and **Stroke blur** control the two edges independently of shadow blur and whole-layer blur. Edit these values in Styles, selected text, or the style library; they also support native animation and effect scripts. Highlight styles expose the two edge blurs, while body typography controls spacing. Animated spacing updates canvas bounds, text hit testing, and placement together.

**Wrapping** applies to the whole line: **Grapheme** retains the existing layout; **Natural** prefers Unicode line-break opportunities and preserves nonbreaking-space joins; **No wrap** breaks only at explicit newlines. Migrated projects and style libraries retain grapheme wrapping, zero spacing, and zero fill/outline blur, preserving their existing appearance.

Choose **Automatic** or **Custom anchor** in the positioning row. Automatic layout hides the anchor, pivot, and offset inputs while keeping the preset grid and diagram visible; choosing a preset enters custom mode. Switching modes preserves layer position effects. The Styles panel's reset icon restores automatic layout and clears position offsets, motion paths, and position animation in one undoable edit. Settings uses the mode selection without a duplicate reset action.

**Margins** has separate **Left**, **Right**, and **Vertical** values in canvas pixels, in both the Styles panel and Settings → Styles. Left and Right define the wrapping region; horizontal centering uses the center of that region, so unequal values shift centered text. Vertical controls top/bottom placement and is ignored for middle alignment. Explicit position controls placement and disables the Vertical field, while Left and Right still limit wrapping. Margins describe text layout; stroke and shadow may extend beyond it, and existing vertical baseline placement is preserved. The shared position diagram shows the layout region and automatic/explicit position. Invalid input stays editable and blocks a new preview, save, or apply; Esc restores only the focused field.

## Audition a segment

Use the header play/pause button to audition the selected clip, or the whole cue if none is selected. **Loop** repeats the range and defaults off. Changing Loop while paused does not start playback.

Leaving the whole panel or deactivating its floating window pauses playback. Changing the target, closing the panel, or seeking in the main window cancels the previous range. Audio and video play together.

## Import and export

| Menu | Format |
|---|---|
| Format → Aegisub → Import / Export | ASS styling, karaoke, and supported masks |
| Format → SRT → Import / Export | Text and timing |

Imports create independent tracks; overlapping cues keep their times on additional tracks. One Undo removes the batch. ASS PlayRes maps to the existing project canvas. Invalid structure, times, dimensions, UTF-8, or resource limits reject the import.

ASS supports base/local styles, resets, colors/alpha, outline/shadow, alignment/position, whole-line static scale and Z rotation, `\move`, `\fad`/`\fade`, `\k`, `\kf`/`\K`, `\ko`, `\kt`, rectangle/vector clips, and supported rectangle transforms. Imported position and opacity animation uses native tracks, editable with Undo through the canvas, Effects panel, and timeline. Ordinary fades use keyframes; envelopes with instantaneous jumps use ordered transforms, edited through the Effects panel's transform operations.

Export preserves representable transform parameters, linear motion, and opacity envelopes with up to two transitions. Recognized variable-speed straight motion and eased fades report their constant-speed/linear approximation. Unsupported animation is omitted from the output without modifying the project. Opacity envelopes preserve each fill, outline, and shadow alpha. The project applies opacity after composing a layer, while ASS applies fading within the text, so overlapping pixels can differ. Font metrics, scaled wrapping, and nonuniform outlines also have visual differences; parameter correspondence does not imply pixel equivalence.

Import resolves omitted-parameter resets and scales outline/shadow according to `ScaledBorderAndShadow` and valid `LayoutRes`. Anisotropic outlines use the geometric mean of both axes with a warning; missing original video dimensions also produce a warning. Positive timing gaps use empty waiting `\k` tags. Negative relative starts, overlaps, and backward timing require `\kt`, which some tools do not support. Export never divides a multi-character group to encode range styles; style runs within a group may change the target player's activation or sweep behavior.

Conversion review identifies each subtitle by sequence, time, text, and specific loss, with copyable details. Cancelling an import preserves the project; cancelling an export preserves an existing destination. No compatibility mode is required.

`Spacing` / `\fsp` maps to native letter spacing, including inline overrides. `\blur` selects fill or stroke blur from the final stroke width and converts to native Gaussian blur units. ASS also couples shadow blur, so independent edge/shadow settings cannot all round-trip; the report identifies these differences. Complete `LayoutRes` controls blur resampling, otherwise PlayRes is used with a report. Native fill, stroke, and shadow blur use their own UI fields, which ordinary text edits preserve.

`\q2` maps to no wrap and `\q1` to natural wrapping. Balanced `\q0/3` imports as natural with a report. Each soft `\n` becomes a hard break while q2 is active, otherwise a space; the final q sets the whole line's wrapping mode. Native grapheme wrapping exports as q1 with a line-break warning. See [ASS interoperability](ass-compatibility.md) for the boundaries and [ASS tag audit](ass-tag-audit.md) for the four-dimensional per-tag assessment.

Supported whole-line or inline `\t` converts font size, spacing, color/alpha, shadow offset, edge blur, stroke width, scale, and Z rotation. Directly representable continuous segments become power keyframes, while overlaps and jumps become ordered transforms. Per-operation clamping, font resets, or coupled coordinates require sampling with error-limit diagnostics. Ordinary fields show evaluated ordered values and an editing hint; other properties and canvas position movement remain available. External ASS negative scale and border values become zero, shad clamps nonnegative after interpolation, and blur clamps to 0–100 after interpolation; x/yshad retain signed values. Native mirrored scale remains supported, and export reports components the current adapter cannot represent. Custom pivots, stroke/shadow coupling, and some easing have specific conversion reports.

ASS style `MarginL`, `MarginR`, and `MarginV` map independently to these three margins. Nonzero dialogue values override the corresponding style value; zero inherits it. Import scales Left/Right by the PlayRes width ratio and Vertical by the height ratio. Export writes the three values separately, keeping ordinary margin placement automatic and preserving explicit `\pos` placement. Export uses `WrapStyle: 1`; font metrics and wrapping can still differ from an ASS player.

Exports include all subtitle tracks. SRT reports loss of rich styling, highlighting, masks, and animation. ASS uses track order for layers; project times remain exact until millisecond SRT or centisecond ASS output. Karaoke endpoints quantize independently; a collapsed positive duration keeps at least one centisecond with an overlap warning. Values outside the player's 32-bit millisecond range reject export. Files use UTF-8 and atomic writes.

## Merge projects from multiple contributors

When contributors time separate sections against the complete original video, choose **File → Merge other projects…** in the main project and select one or more `.aeginext` files.

The merge preserves source times and adds independent “project name / track name” tracks, retaining their order and default styles. Subtitles, rich text, karaoke, shapes, images, animations, masks, and project effect presets are imported together. Each source is placed above the main project, and later selected sources appear in front; each source retains its internal track order.

The main project keeps its video, canvas, frame rate, reference white, and timeline view. Sources must have matching canvas dimensions and reference white; differing frame rates do not change exact subtitle times. This workflow expects projects already aligned to the full original video and does not infer or shift segment times.

Managed fonts and images are copied into the main project's `assets/` or `sources/`, reusing identical content. Linked images retain their source references with paths rebased for the main project; videos are not copied. Asset-library images are included even when no clip uses them. Deliver the source project with its managed resources and keep linked image files accessible; system fonts still need to be installed on the merging computer. Missing resources, hash mismatches, invalid projects, or exceeded budgets reject the entire batch. Copy backups back into their source project root before importing them.

One Undo removes the entire selected batch, and Redo restores it. Resource files remain available for history. Save the main project after review to retain the merged content when reopening it.

## Masks

Create rectangle or closed multi-contour Bézier geometry in **Masks**, then edit it in Preview. Native Anchor/Pivot/Offset places the mask on the project canvas, cropping only its Clip, including text, stroke, shadow, karaoke, and its blur. Subtitle transforms do not move the mask; its own transform does. Inversion and nonzero-winding holes are supported.

Use **Mask placement** in Masks or Effects to adjust anchor presets, the diagram, and numeric fields. Pivot refers to the fixed local frame captured at creation, retained during node editing and morph animation. Presets preserve placement, Shift also adjusts Pivot, and Alt clears Offset. Pivot edits compensate Offset to preserve the current frame while keeping existing tracks. **Mask translation** edits animation independently. Rectangle corners and node positions use mask-local coordinates; handles are pixel offsets relative to their nodes. ASS screen coordinates are converted only at import/export boundaries.

Edit mask geometry and animation with the native mask tools. Clear affected node tracks before changing topology when node morph animation exists.

ASS writes exactly representable rectangle animation as `\t`. Other mask animation expands to frame-sampled static events, with diagnostics for sampling and coordinate rounding. Karaoke is rebased against each actual output event start, using `\kt` when required; the target player must support it. Export is limited to 100,000 dialogues.
