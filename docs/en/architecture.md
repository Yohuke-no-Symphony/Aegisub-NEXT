# Architecture

[English](architecture.md) · [简体中文](../zh-cn/architecture.md) · [All guides](README.md)

## Find the owner

| Location | Responsibility |
|---|---|
| `src/AegiNext.Core` | Rational time, immutable project/scene models, validation, animation, masks, and effect DSL |
| `src/AegiNext.Application` | Editing transactions, Undo/Redo, storage/resources, and applying subtitle imports and projection edits |
| `src/AegiNext.SubtitleFormats` | ASS/SRT/TXT conversion, format diagnostics, ASS text projection, and subtitle timeline mapping |
| `src/AegiNext.Rendering` | UI-independent Skia/HarfBuzz shaping, scene geometry, and linear F16 composition |
| `src/AegiNext.Media` | Probing, frame ownership, native adapters, playback/analysis, and preview |
| `src/AegiNext.Export` | Export capabilities/settings, presets, worker protocol, native burn-in sessions, and independent subtitle muxing |
| `src/AegiNext.Desktop` | Avalonia workspace, panels, controls, menus, preferences, and platform hosts |
| `src/AegiNext.ExportWorker` | Independent `aegn-exporter` host using Application to read snapshots and calling Export |
| `src/AegiNext.Cli` | Spectre.Console aegnc host composing Application, SubtitleFormats, Rendering, and Export without a Desktop reference |
| `native/decoder`, `native/audio`, `native/export` | Separate FFmpeg/SDL3 C ABIs |
| `native/` | Optional macOS HDR diagnostic backend |
| `Tests/`, `scripts/` | Focused verification, build, and packaging tools |

Core has no Avalonia, Dock, FFmpeg, Skia, or filesystem dependency. Application owns business edits; Desktop composes services. Rendering and Media use explicit contracts and resources. Export depends on Core, Media, SubtitleFormats, and Rendering; Desktop and the worker call Export. Media has no reverse dependency on Export and owns neither its native library nor its presets. Business view models do not hold controls, Dock objects, or bitmaps.

SubtitleFormats depends only on Core and returns converted data and diagnostics. Application commits imports and advanced ASS text edits through its editing transactions. Desktop owns file import/export workflows and conversion confirmation, and supplies font-weight and placement adapters through the format library's interfaces.

## Video export boundaries

`AegiNext.Export` is an independent managed module. It defines formats, quality ranges, containers, and hardware candidates shared by settings validation, desktop drafts, and completion checks. CPU uses libx264, libx265, libvpx, libvpx-vp9, or libsvtav1. VP8 CQ saves both quality and bitrate; other formats' CRF modes use quality, while VBR/CBR use an explicit target bitrate. See [Video export](export.md) for user ranges, defaults, and platform support.

Desktop freezes the project and settings, then starts an independent worker. Application retains ownership of tasks, editing transactions, and persistence. The burn-in path checks source-audio/container compatibility before video encoding, calls native export for linear composition and encoding, and muxes final MP4, MKV, or WebM through the same native session. Intermediate NUT retains source presentation timestamps. Static HDR fields reach final muxing through explicit structures; absent temporary-container fields cannot be treated as evidence that source metadata is absent. External tools prepare AAC/Opus tracks; native muxing retains packets for copied audio.

Stream copy uses a separate `SubtitleMuxRequest` and `SubtitleMuxExporter` service without reading burn-in encoding settings or entering video decoding, rendering, or native encoding sessions. Desktop reuses SRT loss analysis and conversion review. The worker combines all project subtitles through SubtitleFormats, maps them to original media presentation times using the binding's `MediaOrigin`, then calls the pinned FFmpeg to copy source media streams, retain chapters, and add a new track. MKV retains SRT; MP4 converts only the new track to `mov_text`. Existing tracks retain their codecs and playback flags, and the new track is never forced. MP4 enables `+write_colr` only when every mapped video stream explicitly declares a `tv` or `pc` range; if any range is unknown, color information is neither inferred nor filled in, and final verification still strictly compares the complete color fields.

The added track always requests `disposition=0`. Final verification permits `default=1` only for a new `mov_text` track when the source has no subtitle tracks; `forced` and every other flag must remain zero. MKV and MP4 with existing subtitles require all flags on the new track to remain zero, while original playback flags are always strictly preserved. The pinned FFmpeg enables the sole MP4 subtitle track to keep QuickTime's subtitle menu available; enabling does not imply default rendering, and initial display depends on the player. See [FFmpeg n9.0.2 `enable_tracks`](https://raw.githubusercontent.com/FFmpeg/FFmpeg/n9.0.2/libavformat/movenc.c).

MP4/MOV chapters may use a dedicated `bin_data` track. A track is recognized as a chapter carrier only when chapters exist in a MOV-family container and the track is `data/bin_data`, its handler is `SubtitleHandler`, its codec tag is `text`, its frame count matches the chapter count, and probing exposes no packets. Chapter mapping rebuilds the chapters in the target container instead of copying the source carrier as ordinary data; every other source stream remains explicitly mapped. Output chapter start/end times and tags must match, and any extra output stream must pass the same chapter-carrier identification.

Before commit, verification reads back source-stream codecs and parameters, video color, audio format, playback flags, languages, and titles, and checks first-packet presentation times and chapters. A streaming scan with bounded buffers also compares each audio track's packet count, maximum packet end time, `initial_padding`, and sums of per-packet `skip_samples` and `discard_padding`. Packet end times allow a quantization tolerance of 1 ms plus one source and one output time-base tick; the other values must match exactly. It extracts the added track to SRT and compares every cue's text and start/end times within 1 ms. Containers that cannot preserve source streams, changed audio sample trimming, or MP4 outputs that truncate overlapping cues prevent commit. Source-container metadata is copied where possible, without claiming lossless preservation of arbitrary tags across containers. Both methods share worker process management, cancellation, and atomic output transactions, committing a new file only after validating the completion receipt for the requested operation.

Native results read back the encoded video and final container to verify actual codec, bit depth, color, and static HDR. The parent also checks decoding backend, encoder, rate mode, parameter precision, and output evidence against the request before atomically committing the file. Rate-control evidence distinguishes `ConfiguredAndInitialized` from `RuntimeQueried`; QSV currently uses the former. The pinned Windows FFmpeg statically links its oneVPL dispatcher, so its session must not be passed to another dispatcher or inspected through private layouts to claim runtime-query evidence.

Current native export **ABI 6** uses an **88-byte** request, **488-byte** result, **96-byte** HDR structure, and **32-byte** muxing request. The subtitle foreground structure remains **24 bytes**. Worker protocol is **4**, explicitly separating burn-in and stream-copy operations and rejecting burn-in parameters on a stream-copy request. Rebuild and deploy the application, worker, native library, and contract fingerprint together with the same configuration. Export presets use version **2**; strict version 1 migration renames legacy `crf` to `quality` while preserving settings and identities. Unknown, missing, duplicate fields and batch conflicts still reject loading or import.

Shared speed names have explicit CPU mappings. x264/x265 use their named presets, libvpx/VP9 use `cpu-used`, and SVT-AV1 uses a numeric preset. Hardware backends map to their supported levels.

| Speed | libvpx / VP9 `cpu-used` | SVT-AV1 `preset` |
|---|---|---|
| ultrafast | 8 | 9 |
| superfast | 7 | 9 |
| veryfast | 6 | 8 |
| faster | 5 | 8 |
| fast | 4 | 7 |
| medium | 3 | 6 |
| slow | 2 | 4 |
| slower | 1 | 2 |
| veryslow | 0 | 0 |

## Time and storage

Time is rational `MediaTime`; Clip visibility is `[Start, End)`. Decoding and VFR use actual presentation timestamps. Subtitle-format exchange maps confirmed playback origin only at the boundary; relative animation/karaoke time is retained.

New `.aeginext` files use **v17**; `ProjectStore` also reads v3–v16 and upgrades them in memory. Migration maps the old scalar margin equally to Left, Right, and Vertical, preserving existing placement and subtitle/layer times; an unknown playback origin remains unknown until confirmed. Missing/null legacy masks are accepted, but unsupported nonempty legacy local masks reject loading with the affected object identified. v1/v2 are unsupported. Style libraries use **v7** and read v1–v6 with the same margin migration. Applications supporting only older schemas cannot read newly saved v17 projects or v7 style libraries.

Version 14 stores the project asset library, managed or linked asset storage, image crop/playback settings, and shape parameters. Legacy images remain managed assets and become library items in the root folder; migration supplies a full-image crop and no explicit playback settings. Library folders organize project metadata rather than changing the source files' directories.

Version 16 adds native X/Y rotation, perspective distance, and static projection compensation to layers and Normal text ranges. Versions before 16 migrate with zero X/Y rotation and compensation and distance 312.5. Version 17 adds the single XYZ `ROTATION_3D` animation target and exact curves for masked ordered operations; older scalar axes migrate without resampling. Static X/Y/Z fields and their stable legacy IDs remain readable. Older schema declarations reject capabilities introduced after their version. Native Anchor/Pivot remains the placement model; ASS origin tags are converted only at exchange boundaries. See [projection](rendering.md#flat-plane-projection).

The flat Track → Clip model permits subtitles, images, and shapes on the same track. Clips cannot overlap within a track, and the track array is the sole compositing order: the top track is drawn last. Migration retains existing tracks where possible; interleaving clips in the legacy layer list does not split tracks. Derived tracks are created only when needed to preserve the order of simultaneously active clips or resolve overlap within a track. Legacy neutral groups can be flattened only when their timing and compositing are preserved; groups with transforms, animation, opacity, masks, blur, or blend isolation reject migration with the affected group identified. Original files remain unchanged until saving.

Loading validates fields, references, overlaps, geometry, and budgets. Saving uses same-directory temporary files and atomic replacement. Videos are referenced, not copied; managed resources use confined relative paths and optional hashes. Save As rebases resources/references; a cross-directory relocation clears history that could restore obsolete paths.

Autosave captures committed content and view state through a serialized persistence coordinator. It does not commit drafts, steal focus, or change Undo. Saving a captured snapshot keeps newer edits dirty.

## Application task service

`DesktopApplicationContext` owns one `AegiTaskService`. Its kernel lives in `Application/Tasks` without Avalonia, Media, or Rendering references. Concrete business tasks declare their scope, execution mode, cancellation, editing restriction, and complete resource set independently.

The shared queue starts tasks in submission order. An unavailable resource at the head holds later submissions. A blocking task waits for all predecessors, runs exclusively, and holds every successor behind its barrier. The default limit is four; Settings → Tasks allows 1–32. Lowering it leaves running operations intact. Project writes, media controllers, personal libraries, and normalized storage paths are exclusive resources acquired together. Internal stages reuse their parent's slot, resources, and cancellation token.

A handle supports awaiting completion; cancelling the wait does not cancel its operation. Explicit cancellation keeps resources and editing leases until subprocess exit and cleanup finish. Atomic commit validates cancellation and input first, then disables cancellation. Queued tasks acquire no editing lease. Scope restrictions include that project's floating panels.

Cancellable parallel work may call `YieldIfWorkIsQueuedAsync` at a checkpoint after all internal workers and stages finish, before commit or editing leases. Yielding retains resources, identity, and start time while releasing only the execution slot. Only the queue's consecutive independent parallel prefix runs first; yielding never crosses a blocking task or resource conflict. Cancelling yielded work still drains its original execution stack, callbacks, and cleanup before a barrier can proceed. Started yielded work cannot be replaced by continuous-write coalescing.

Project creation dialogs own cancellation of their submitted task: closing the dialog explicitly requests cancellation and waits for actual completion. Commit rejects cancellation, and the created project still transfers to the workbench. Application exit prevents activating a new window after commit and cleanup finish. Ordinary callers retain the independent wait-cancellation contract.

| Business entry | Policy |
|---|---|
| Create, open, restore, merge, media replacement | Prepare asynchronously; restrict the owning project while replacing context; preserve rollback |
| Timing post-processing and keyframe indexing | Allow edits during scanning; validate revisions and drafts before one Undo commit |
| Save, Save As, resource relocation | Freeze committed save input at submission; retain newer edits and raw drafts |
| Video/subtitle export; subtitle/font/style/effect preparation | Freeze export input; briefly commit valid prepared results |
| Waveform/spectrum and system fonts | Observable background tasks; retain internal viewport replacement and analysis blocks |
| Autosave, backups, cleanup, libraries, preferences, recent projects, layouts | Serialize storage resources; coalesce compatible pending continuous writes |
| Settings transfer, startup recovery and initialization | Keep recovery barriers, journals, and transaction boundaries |

Use this entry inventory when checking that new business operations use the service. Owning coordinators submit these tasks; resource preparation, keyframe probing, and atomic storage retain their lower-level implementations.

| Module / entry | Task classes or internal stages |
|---|---|
| `Workspace` project workflows | `CreateProjectTask`, `OpenProjectTask`, `MergeProjectsTask`, `SaveProjectTask` (including Save As and resource relocation); failed preparation rolls back inside its parent task |
| `Workspace` media contexts | `SwitchProjectMediaTask`, `SynchronizeProjectMediaTask`, `SwitchPreviewDecodeModeTask` |
| `Workspace` timing | `TimingPostProcessingTask`; keyframe indexing and cache reuse are internal stages of that task |
| `Workspace` subtitle exchange / creation | `ImportSubtitlesTask`, `ExportSubtitlesTask`, `CreateSubtitleClipsTask`, `BeginTimingCueTask` |
| `Workspace` video export | `VideoExportTask` for burn-in, `SubtitleMuxTask` for stream copy; the export panel and task list cancel the same handle |
| `Workspace` font / style / effect preparation | `ImportSubtitleFontTask`, `PrepareSubtitleStyleTask`, `ApplySubtitleStyleTask`, `ApplySubtitleTrackStyleTask`, `CaptureSubtitleStyleTask`, `ApplyEffectScriptTask` |
| `Workspace` image assets | `AssetLibraryTask`; the coordinator prepares imports, placement, refresh, relocation, and collection before committing project changes |
| `Workspace` personal resource exchange | `ImportStylePresetsTask`, `ExportStylePresetsTask`, `ImportEffectScriptsTask`, `ExportEffectScriptTask` |
| `Workspace` audio analysis / devices | `AudioAnalysisBatchTask`, `AudioCacheMigrationTask`, `ApplyAudioCalibrationTask`, `RebuildAudioOutputTask` |
| `Workspace` automatic persistence | `AutomaticProjectPersistenceTask`; backup pruning uses `ProjectPersistenceTask` or a parent stage |
| `Startup` / `Editing` application services | `ApplicationInitializationTask`, `EnumerateSystemFontsTask`, `PreferencesWriteTask`, `RecentProjectsWriteTask`, `PersonalLibraryTask` |
| `Layouts` persistence | `LayoutWriteTask`; the coordinator retains layout interaction orchestration |
| `Settings/Presets` / `Settings/Export` | `SettingsStyleImportTask`, `SettingsStyleExportTask`, `SettingsEffectImportTask`, `SettingsEffectExportTask`, `SettingsExportPresetExportTask`; library mutations use `PersonalLibraryTask` |
| `Settings/Transfer` | `UserSettingsLayoutCaptureTask`, `UserSettingsExportTask`, `UserSettingsImportTask`, `UserSettingsRestoreStageTask`, `UserSettingsRestoreCancelTask` |

Image import is available through File → Import images and the Project assets panel. Project backup recovery uses the manual file workflow described in [Workbench](workbench.md#save-and-recover); settings recovery follows the restoration tasks above. Playback loops, single-frame decoding, drag seeking, the audio clock, and live preview remain internal real-time pipelines whose owning business tasks manage cancellation and completion. Viewport requests and individual analysis blocks do not create separate task records.

Coalescing preserves the original identity, handle, and queue position. It never replaces running work or crosses a barrier or intervening conflicting write. Autosave reads committed content and view state without committing drafts, changing focus, or modifying Undo. Save As separately relocates the frozen save and current editor snapshots; obsolete-path history is cleared while invalid raw input remains editable.

Closing first asks the user. Cancelling that prompt leaves tasks running. A confirmed close seals its scope, requests cancellation, drains cleanup and commit, then submits final persistence through a controlled channel. Failure reopens intake before restarting persistence timers; other projects continue. Application exit cancels optional background work and drains required persistence.

Startup completes asynchronous settings recovery and initialization before constructing and explicitly showing the welcome window, preventing a running application with only a Dock icon.

The task button sits at the right edge of the main project title bar, before the measured native caption-button area on Windows. The generic title bar exposes host-owned content slots and does not access the task service. Each window owns its right-aligned Flyout while sharing task data. Task cards have two rows: name, stage/status, and a square cancel icon on the first; progress on the second. Project names and application context labels are omitted. Unknown totals use indeterminate progress while active; finished progress stays static. The Flyout uses shared typography and card styles, with its width constrained to available space and no horizontal scrolling.

Current items follow submission order; the latest 50 finished records follow reverse completion order. The Clear completed icon removes only history. Progress updates are merged at about 10 Hz; final states notify immediately. History retains only lightweight status and error summaries, without execution closures, native resources, project snapshots, or persistence.

Native macOS/Windows title-bar dragging, scaling, and platform-button hit testing require actual-window acceptance. Measure scan duration separately with real media.

## Editing and presentation

`ProjectEditor` commits immutable snapshots. Multi-Clip edits/imports are atomic; failed validation leaves the project and history unchanged. Layout changes retain one session and fixed panel instances. Personal layouts, styles, scripts, preferences, and logs are separate from project content.

Effect DSL version 2 resolves named scopes and grapheme-safe groups in Core, then `CompileTarget` / `ComposeTarget` returns both prepared subtitle ranges and animation tracks. Application applies the complete frozen result in one transaction. A generated family matches effect ID, scope name, and parent range ID; its stable grouping signature and text span determine identity reuse. Reapplication replaces that family's results. Renderer and Desktop consume native ranges and tracks rather than interpreting scripts during playback. See [Effect scripts](effect-dsl.md).

Range Offset, reversed main/component curves, and generated origins were introduced in project format 13 and remain part of the current format. When loading a schema before v13, these optional values default to zero Offset, false Reverse, and no origin; v12 retains its existing text-animation fields. Older schemas reject the new fields by their owning object, allowing existing subtitle-position Offset fields. Generated parent references must stay within one subtitle and form no cycle; validation does not require child-span containment after manual edits or text remapping.

Native styles store letter spacing, independent fill/outline blur, and wrap mode. Older files migrate with zero for the new numeric values and grapheme wrapping, preserving their existing appearance. Current files require the complete fields; legacy files containing the new fields are rejected. Letter spacing and both blur values are subtitle-only animation and DSL properties. Layer blur and shadow blur retain their independent meanings.

Preview uses the same scene geometry as editing/export and presents SDR derivatives. The renderer retains linear F16 until display/encoding; worker export reads original media frames. Every asynchronous result checks time, revision, request identity, and ownership before delivery.

## Contribute

Use Allman braces, file-scoped namespaces, one top-level type per file, `var`, and target-typed `new()` where suitable. Nullable checking, analyzers, and warnings-as-errors are enabled. Public/protected API docs describe the contract.

UI labels and error prompts use the existing language packs. Subtitle content, Unicode test fixtures, user names, paths, and serialized identifiers retain their business meaning.

Put focused tests in the owning Tests project. Change a native contract together with managed bindings, worker protocol, capability/version checks, and layout tests. Keep commits scoped with short English `feat:`, `fix:`, or `chore:` titles. Exclude generated artifacts and run `git diff --check` plus affected tests.

### Maintain documentation

Keep user workflows in the Quickstart and topic guides, module contracts here and in the integration guides, and executable build/release commands in Building and Publishing. Update existing topics before adding another guide. Keep matching filenames and behavior descriptions in `docs/en/` and `docs/zh-cn/`, with language switches and relative links intact. Verify version numbers against their owning constants and commands against the actual scripts.

Use `docs/assets/` for final images referenced by maintained guides and `docs/en/examples/effects/` for shared runnable effect examples. Keep generated output, logs, temporary verification reports, and AI session artifacts out of the maintained documentation. Check links, anchors, image references, bilingual correspondence, and `git diff --check` after editing; run focused tests when changing executable examples or documenting behavior that needs verification.

Continue with [Workspace integration](composable-workspace.md), [Media](media.md), [Rendering](rendering.md), or [Building](building.md).
