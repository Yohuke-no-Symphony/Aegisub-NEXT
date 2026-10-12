# Timing post-processor

[English](timing-post-processor.md) · [简体中文](../zh-cn/timing-post-processor.md) · [All guides](README.md)

## Save a style association

1. Open **Settings → Timing processor**.
2. Select a style row to load its saved options, or edit candidate options.
3. Check the target presets and choose **Save association** to apply that configuration to all of them.
4. Use **Unlink** to remove associations from checked presets.

Styles come from the personal library, so no project or video is needed. This page saves configuration.

## Process selected clips

Select subtitle clips in the timeline and click the clock icon at the bottom of the left toolbar. Each subtitle uses its style's saved association. Lead-in/out, adjacency, and keyframe snapping run in that order, with one Undo for the complete batch. Tracks are processed independently; adjacency only joins directly adjacent selected subtitles with identical options. Unselected subtitles and graphics clips remain unchanged.

Subtitles without an association are skipped; the log reports changed and skipped counts. Without video, keyframe snapping is skipped. Probe failures, invalid durations, or final collisions reject the complete batch. Each click is a new processing operation; F8/F9, style application, and export do not run the processor automatically.

The first uncached index scan appears in the title-bar task list and supports explicit cancellation while the project stays editable. Cancellation waits for probe-process exit and cleanup. Before applying, the task rechecks project, media, selection, subtitle input, draft, and gesture revisions. Changed input cancels application of the result, preserving new edits. Index caches remain reusable; requesting cancellation does not immediately remove the task row.

## Default options

| Stage | Default |
|---|---|
| Lead-in / lead-out | Enabled, 100 / 350 ms |
| Adjacency | Enabled, max gap 300 ms, max overlap 50 ms, end bias 90% |
| Keyframe snapping | Enabled; start before/after 200/150 ms, end before/after 200/250 ms |

The engine runs these stages in order. Thresholds are inclusive; 0% bias adjusts the next start, 100% the previous end. Executing keyframe snapping requires a real video-frame index.

Enter or blur commits valid integers; invalid drafts stay editable, and Esc restores the current field. Candidate settings remain independent until **Save association**.

## Persistence and processing

Associations follow stable style IDs: rename preserves them, copy duplicates them, deletion removes them, and `.aegistyles` carries them. Style library v6 reads v1–v5 and maps their scalar margin equally to Left, Right, and Vertical; older applications cannot read v6.

Projects store style preset IDs on subtitle clips. The current format is v14 and the loader accepts v3–v14; see [storage compatibility](architecture.md#time-and-storage). Legacy subtitles without IDs first resolve a track preset with the same saved style name, then an exact personal-library style name. A legacy subtitle still named `Default` also uses its track's association when its style exactly matches the track default, automatic style application is enabled, and the personal library has no preset with that name. Successful processing saves the resolved ID. A missing preset referenced by an existing ID never falls back to a newly created preset with the same name.

The engine uses rational time and per-track adjacency. Invalid durations or final collisions reject the whole result; crop semantics preserve animation phase. Frame indexing uses actual PTS, supports VFR/nonzero origins, and rejects broken timing or changed media/tool identities.
