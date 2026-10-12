# Quickstart

[English](quick-start.md) · [简体中文](../zh-cn/quick-start.md) · [All guides](README.md)

## Open a project

1. Open `AegiNext.app` on macOS or `AegiNext/aegi-next.exe` on Windows. Keep the complete package together.
2. In the welcome window, choose **New Project**, enter a name and parent directory, and create it. The target directory must not already exist.
3. Choose **File → Open Video**. Wait for the picture; audio analysis runs in the background.

Use **Open Project** for an existing `.aeginext` file. To run from source, follow [Building](building.md). Check the package manifest for its minimum OS version.

## Make a subtitle

1. Click a timeline track heading to choose the destination.
2. Press **Space** to play, **F8** at the sentence start, and **F9** at its end.
3. Enter the sentence in **Subtitles**. Adjust start/end times with seconds or `00:00:12.350`.
4. Drag the clip to move it, or its edges to trim. Clips on the same track cannot overlap.

For manual creation, seek to a start and press **Cmd/Ctrl + Enter**. Use **Format → Aegisub** or **Format → SRT** to import existing subtitles. Imports place overlapping cues on separate tracks.

## Style and review

- **Styles:** choose font, size, fill, stroke, alignment, and position; drag the text in Preview to place it.
- **Effects:** choose a preset and apply it. Expand the track triangle to edit keyframes.
- **Subtitle Details:** edit rich text and karaoke, then audition a selected segment.
- **Masks:** create a rectangle or closed Bézier mask for the selected subtitle.

Use **View** to reopen a hidden panel and **Layout → Standard** to restore the initial arrangement. Preview quality affects editing only; Smooth 540p is the default.

## Save and export

1. Press **Cmd/Ctrl + S** to save the project. Video remains an external reference.
2. Open **Encoding**, select codec and audio options, and choose a new destination file.
3. Wait for completion and review the exported picture, subtitles, and audio. **Cancel** stops the job.

Use **Format → Aegisub/SRT → Export** for subtitle files. See [Video export](export.md) for HDR and GPU options, and [Workbench](workbench.md) for autosave and backup recovery.

## Essential shortcuts

Use Cmd on macOS and Ctrl on Windows. Settings → Shortcuts shows current bindings.

| Action | Default |
|---|---|
| New / open project | Cmd/Ctrl + N / O |
| Open video | Cmd/Ctrl + Shift + O |
| Save / Save As | Cmd/Ctrl + S / Shift + S |
| Play / pause | Space |
| Timing start / end | F8 / F9 |
| Add subtitle | Cmd/Ctrl + Enter |
| Undo / redo | Cmd/Ctrl + Z / Shift + Z |
| Export video | Cmd/Ctrl + E |

Text fields keep their text-entry behavior. If function keys are captured by the OS, use the Subtitle menu or rebind them.

### Classic Aegisub controls

Select a subtitle first. Q/W/E/R work with Timeline or a subtitle row focused, outside text editing. Short audition ranges default to 500 ms; adjust their duration in **Settings → Preview**.

| Action | Default key / mouse |
|---|---|
| Audition a short range before the subtitle starts | Q |
| Audition a short range after the subtitle ends | W |
| Audition a short range from the subtitle's start | E |
| Audition the whole subtitle | R |
| Set the primary selected subtitle's start at the clicked time | Left click in the timeline body |
| Set the primary selected subtitle's end at the clicked time | Right click in the timeline body |

For mouse timing, enable **Classic Aegisub timing** using the mouse icon at the timeline's bottom left; it is off by default. While enabled, left and right clicks in the body edit the primary selected subtitle without changing selection. Turn it off to restore normal selection, dragging, and clip context menus.
