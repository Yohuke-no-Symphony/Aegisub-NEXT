# Workbench

[English](workbench.md) · [简体中文](../zh-cn/workbench.md) · [All guides](README.md)

## Panels and layouts

The workbench has eleven panels: Video preview, Timeline, Subtitles, Styles, Effects, Subtitle details, Masks, Encoding, Log, Project assets, and Drawing. Drag their headings to dock, split, tab, or float. **View** reopens hidden panels, including **Project assets** and **Drawing tools**; **Layout** selects Standard, Timing, Effects, Encoding, or a personal preset. The current layout saves automatically; named presets update only when explicitly saved.

## Tracks and navigation

- Click a track heading or empty track area to select the destination for new subtitles and timing. Track context menus create, rename, reorder, and delete empty tracks.
- Drag a clip to move it; drag an edge to trim, or Ctrl + edge to stretch its content time. Same-track overlaps reject the edit.
- **Snap** aligns with other clip boundaries; **Step** aligns with ruler divisions. Alt temporarily bypasses both. Without Step, dragging uses millisecond precision.
- Wheel scrolls tracks, Shift + wheel pans, and Cmd/Ctrl + wheel zooms around the pointer. Touchpads support pinch and horizontal scrolling. The minimap changes the viewport without seeking playback.
- With Timeline focused, **Cmd/Ctrl + C/V** copies clips and pastes at the pointer. Multi-track copies retain their spacing relative to the track selected when copied; missing destination tracks or collisions reject the whole paste.
- Expanding a track shows each clip's effect blocks. Expand a block to edit its keyframes and curves; blocks start collapsed. Track collapse and individual block expansion save with the project, including autosave, without entering content Undo. The left toolbar expands or collapses all tracks while retaining individual block states.
- The **S** button in a subtitle track heading shows only that track in the timeline. Toggle it off, or select another track in the subtitle list, to show all tracks again.

Subtitle list selection centers the primary clip without changing zoom. Enter commits the current text and advances; at the last row, it can create the next cue from that row's end to the playhead. Shift + Enter inserts a newline. Invalid inputs and IME confirmation retain editing focus.

## Playback and timing

Space plays/pauses outside text inputs. F8 creates a new timing segment; F9 finishes that valid segment. Seeking, selection changes, or other editing can invalidate it.

Classic Aegisub audition keys target the primary selected subtitle: **Q** plays a short range before its start, **W** a short range after its end, **E** a short range from its start, and **R** the whole subtitle. Timeline or a subtitle row must have focus; text inputs keep normal typing. Short ranges default to 500 ms, configurable in **Settings → Preview**. Ranges stay within media boundaries, and E also stops at the subtitle's end.

Classic mouse timing is off by default. Select a subtitle, then enable **Classic Aegisub timing** using the mouse icon at the timeline's bottom left. In the timeline body, **left click** sets the primary selected subtitle's start and **right click** sets its end at the clicked time. These clicks keep the primary selection; track headings and the top ruler retain their normal behavior. Turn the toggle off to restore normal selection, dragging, and clip context menus. See the [Quickstart control table](quick-start.md#classic-aegisub-controls).

Right-clicking an effect keyframe or curve still opens its effect menu while classic mouse timing is enabled.

Preview qualities are Low 320p, Smooth 540p (default), Standard 720p, and High 1080p. Scrubbing temporarily caps quality at 540p without exceeding the chosen level; release restores it. Small videos are not enlarged, and export is independent.

Settings → Media selects Auto/CPU/GPU preview decoding and saves additional audio delay per output device. A positive delay means audible output arrives later. Device-clock loss temporarily disables timing until recovery.

## Styles, animation, and masks

Set font, size, line height, fill, stroke, shadow color/offset/blur, and alignment in Styles, including its floating window. Text sizes to its content. Position uses normalized Anchor/Pivot and pixel Offset; preset clicks preserve position, Shift also changes Pivot, and Alt also clears Offset. **Restore Automatic Position** clears manual positioning and position/path animation while preserving other effects.

**Mask placement** in Masks and Effects shares anchor presets, a placement diagram, and Anchor/Pivot/Offset inputs. Anchor is relative to the project canvas; Pivot is relative to the mask's local reference frame captured at creation. Zero selects the left/top edge and one the right/bottom edge; pivots can also lie outside the frame. Node editing and morph animation do not recalculate it. Preset clicks preserve placement, Shift also sets Pivot, and Alt clears pixel Offset. Direct Pivot edits compensate Offset to preserve the current editing frame; frames with different rotation or scale can change. Static placement is separate from **Mask translation** animation, and placement edits retain existing tracks. Both panels share unfinished input; Esc restores only the current field.

Assign a style preset from a track context menu. On a nonempty track, choose whether to update existing clips or only its default for new clips. The project stores a style snapshot, so later personal-library edits do not change it. Manage reusable materials in **Edit → Effect Library → Style Library**; system fonts still need installation on another machine unless embedded in the style.

**Effect Library → Style Library** renders editable sample text. Cmd/Ctrl + wheel zooms around the pointer; left drag pans the preview. These gestures change only the preview view. Style, script, export, and layout libraries support selecting multiple personal presets and confirming their deletion.

Select a clip to edit Effects, or a keyframe to seek and edit its value/interpolation. Drag Preview text, motion-path nodes, or mask handles for visual editing. Motion-path curves and handles also remain visible and draggable in the preview letterbox. In motion-path and vector-mask editing, Shift + left-click a curve to insert a node, or Ctrl + left-click an anchor to delete it; motion paths retain at least two endpoints. Insertion preserves the curve shape, while motion-path playback continues to use its existing progress mapping across segments. Node morph animation locks mask topology until those tracks are cleared. See [Subtitle editing](subtitle-editing.md) and [Effect scripts](effect-dsl.md).

The clip context menu clears all animation tracks from selected clips. Right-click an effect block, keyframe, or curve to clear that block's animation, including its keyframes and ordered transforms. A keyframe's menu also deletes that individual keyframe. Clearing preserves static styles, base placement, masks, motion paths, and karaoke, and forms one undo operation.

Valid numeric/color drafts preview immediately and commit on Enter or blur; invalid text remains editable. Esc restores the current field. A completed gesture or committed edit forms one undo operation.

Numeric inputs use editable text without spinner buttons. Hold a numeric field's heading and drag horizontally to adjust its draft; vector fields use their X/Y headings. Release commits once. Esc, capture loss, or changing the editing target cancels the gesture and restores its original text. Invalid text does not start a drag. Collapsible property groups use the shared compact `PropertySection` control with subdued headings and a separator matching shortcut settings.

The Effects panel uses collapsible property tables for clip, transform, typography, fill, stroke, shadow, composite, path, and animation categories, using existing workbench styles and draft controls. Presets stay at the bottom. Rows expose the current value, animation toggle, add-keyframe action, and reset action. Detailed keyframe editing remains in the panel; timing and curves use the main timeline.

Select text in Subtitle Details, then create an animation range in Effects. The scope selector switches between the whole subtitle and text ranges; the state selector switches normal, active, and inactive appearance. Ranges can be deleted or reordered. Active/inactive states edit painting properties and share normal font-size, spacing, and transform geometry. Disabling animation clears only the complete target. Range identities follow text editing, splitting, and duplication.

For a Normal text range, Position edits its local pixel offset; Scale and Rotation also act on that group's laid-out geometry without changing its layout occupancy. Whole-subtitle Position still moves the layer. Apply **Letter bounce** or **Letter pulse** through the existing preset selector to generate editable grapheme ranges. Version 2 scripts can also group pairs, words, explicit lines, paragraphs, or literal-separated text. Reapplying the same named generated block resets manual changes to its ranges and tracks; editing text remaps the existing result, and reapplication rebuilds its grouping and stagger. See [Effect scripts](effect-dsl.md#version-2-grouped-text) for the complete rules.

Category folding is a personal preference and creates no project undo entry. Folding or refreshing theme/language does not submit drafts. Invalid input keeps its raw text and opens its category when validation fails. Scope/state/property changes handle the current draft first; deferred commits retain the complete target that originally owned the draft.

### 3D rotation and perspective

Subtitles, images, and shapes can rotate as flat planes. In **Effects → Transform**, the Rotation Vector3 shows **X**, **Y**, and **Z** in degrees. The axes share one track and keyframe times, just like Position Vector2; each component's value and interpolation curve can be edited separately. **Perspective distance** is measured in project pixels and defaults to 312.5. A smaller positive distance increases perspective; it does not change the project's output resolution. Angles keep complete turns: an animation from 0° to 720° makes two turns.

After selecting a keyframe, choose All or X/Y/Z in the curve component selector, then set the interpolation type and power exponent. Vector2 offers All or X/Y, and colors offer R/G/B/A; changing one component's curve preserves the others. Compact numeric inputs clip long decimal displays while retaining the full original value for editing and saving.

1. Select a Clip. To edit a text group, select its animation range in Effects first.
2. In **Drawing tools**, choose **3D transform**. Drag the red **X** handle vertically, the green **Y** handle horizontally, or the blue **Z** handle around the pivot. Hold Shift to snap rotations to 15°.
3. Drag the gold **D** handle horizontally to adjust perspective distance. Moving right increases the distance; moving left strengthens perspective.
4. Release to commit one Undo operation. Esc, capture loss, or changing the target discards the temporary edit. Enable the corresponding property's animation or select a keyframe in Effects to edit its value with the same handles.

The tool uses the selected range's Normal geometry even when Active or Inactive appearance is selected. The Select/Move tool continues to edit the whole layer. Imported ordered ASS rotation transforms can be edited by operation in Effects; clear that rotation track before direct handle editing. Perspective distance and other ranges have their own tracks.

Anchor chooses the canvas position; Pivot chooses the local rotation center. Changing Pivot preserves the projected picture at the current editing frame through native compensation, retaining existing tracks. Other frames may change because they use different rotations or scales. ASS `\org` is converted into this native anchor/pivot model at import/export; it is not a separate absolute-origin setting. Use `.aeginext` to preserve adjustable perspective, compensation, and nested range transforms. ASS exchange reports the combinations it cannot represent.

## Share styles and scripts

Open **Edit → Effect Library** and choose **Style Library**, **Effect Scripts**, or **Import & Export**. The welcome screen also opens the same three-page library without a project. Switching pages or closing handles unsaved material drafts; existing workbench apply/capture actions remain available.

**Effect Scripts** previews the current draft with adjustable text, duration, and personal styles with embedded fonts. Play/pause, loop, and scrubbing work without saving or opening a project. See [Preview a draft](effect-dsl.md#preview-a-draft).

Select one or more entries, click **Export**, then choose a format in **Import & Export**. Independent files use `.aegistyles` for styles and `.aegieffect` for personal scripts; a single item uses a file picker, while multiple items use a destination folder. A package uses `.aegilibrary`. **Script source** exports `.aegifx`, including read-only builtins. **Export all personal materials** creates one package containing personal styles, embedded fonts, and scripts.

**Import** accepts multiple independent files and packages together. Review new, identical, and conflicting rows, choose **Import**, **Replace**, or **Skip** where offered, then confirm. Identical content skips automatically. Protected identities cannot be replaced; only one conflicting input version may be selected. A changed library requires another preview and confirmation. Settings packages are imported separately in Settings and never supply effect-library materials.

## Save and recover

Settings → Projects sets the workspace root, initially `Documents/AegiNext/Workspace`. Creating `Example` makes `Example/Example.aeginext` and `Example/backup/`; an existing target directory rejects creation.

Autosave and backups are independently enabled by default: autosave every 2 minutes, backup every 5 minutes, retaining 20 copies. They save committed state without committing drafts or changing Undo. A title dot indicates unsaved changes.

To recover, close the project, copy `backup/Example-yyyyMMdd-HHmmssfff.aeginext` into the project root, and open that copy. Keep media and managed resources alongside the project; backups do not duplicate them.

Videos inside the project directory use relative references; external videos stay absolute and are never copied. Save As reclassifies references and relocates managed resources. Move the whole directory to retain relative references. Storage compatibility is described in [Architecture](architecture.md).

## Preferences and logs

Settings manages language, theme, accent/audio/timeline colors, shortcuts, media, projects, preview, and timing options. Shortcut recording captures a real chord; Esc cancels, and Clear disables it. macOS can choose system or window menus.

Log supports filtering, copying, and clearing up to 2,000 session records. Errors mark View without stealing focus; records are not retained after restart.
