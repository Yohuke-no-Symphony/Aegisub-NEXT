# Workspace integration

[English](composable-workspace.md) · [简体中文](../zh-cn/composable-workspace.md) · [All guides](README.md)

## Put changes in the owning layer

Paths below are under `src/AegiNext.Desktop/`.

| Directory | Owner |
|---|---|
| `Workspace/` | One WorkbenchSession, editor/controllers, selection, drafts, and awaitable workflows |
| `Layouts/` | Dock tree, floating hosts, layout snapshots, and presets |
| `Panels/` | Feature Views and ViewModels |
| `Controls/Common/`, `Controls/Editing/`, `Editing/` | Reusable controls and field/gesture editing behavior |
| `Settings/` | Preferences, encoding/color-tag libraries, and their drafts |
| `EffectLibrary/` | Style/script drafts, independent window, and sharing workflows |
| `Views/`, `Windowing/` | Hosts, platform chrome, menus, and input coordination |
| `I18n/` | Language service, JSON packs, and live bindings |

Read the consuming panel and closest shared control before adding an API. Shared controls expose values, drafts, commands, and completion/cancel events; they do not acquire a session or depend on Dock. ViewModels contain business state rather than controls/pixels.

## Integrate a panel or control

1. Add the View/ViewModel in the feature owner and compose dependencies in Workspace.
2. Supply one fixed View per stable `WorkbenchPanelIds` entry to the layout controller; docking/hiding never recreates it.
3. Route real input through shared commands and preserve local text/IME behavior.
4. Commit valid drafts before changing editing targets; invalid raw input retains the target and Esc restores one field.
5. Give each completed gesture one transaction; cancel on capture loss or target changes. Close drains asynchronous work and disposes owned resources.

Layouts control space without owning editor, Undo, playback, or export. Current layout and named presets are independent. Persistence validates app-owned snapshots, writes atomically, and constrains restored windows to available monitors. Floating close hides panels; main close flushes layouts and drains the session.

Use `Controls/Common/PropertySection` for collapsible property categories. Its type theme in `WorkbenchTheme.axaml` supplies a compact 28-DIP header, transparent background, muted typography, and a separator matching shortcut settings. Consumers provide Header, Content, and IsExpanded; the control does not store folding preferences or commit edits.

Numeric fields use `NumericDraftInput` without spinner buttons and `NumericDragLabel` with Input bound to the field. `VectorDraftInput` supplies X/Y drag titles internally. DragStarted/DragCompleted/DragCanceled bubble from the title and carry the input identity and original/final raw text. During IsTitleDragging, consumers suppress lost-focus and immediate numeric commits. Freeze the complete business target, source, and time at start; commit once on completion and cancel before replacing a draft or globally submitting unfinished gestures. Personal preset editors retain their existing Save/Apply boundary. Invalid raw text cannot start a drag, and cancellation restores the exact original text.

Font fields share `FontFamilyPicker`: the current family stays first, multiple variants use a submenu, and a single variant commits directly. Menu matching ignores optional PostScript metadata while committed selections retain the complete font identity. Popup input belongs to its panel through both visual and logical ancestry.

Hosts inject `IFontNamePreviewProvider` from the shared font service. `Controls/Media/FontNamePreviewPresenter` requests visible names and owns its bitmap; detach cancels its waiter and rejects stale results. Rendering opens the exact font face or named variable instance and returns a theme-independent Alpha8 mask. `DesktopApplicationContext` owns the bounded preview cache under `PreferencesStore.DirectoryPath/caches/fonts/v1`, keyed by font content and instance, name, size, and DPI; controls never access disk or create their own cache.

## Window appearance and input

Reuse shared typography, spacing, panel corners, and chrome. Layout owns outer frames; panels avoid duplicate borders. Register floating/settings hosts for the same menu/shortcut policy.

macOS defaults to system menus; window-menu mode shows menus on the main workbench. Windows chrome integrates native caption actions and resizing. Theme/shortcut refreshes preserve command identity; language regrouping waits for safe menu lifecycle points.

## Effect library storage and sharing

`EffectLibrary/` composes style and script editors in one nonmodal window, available from the welcome screen or **Edit → Effect Library**. The application context owns a single shared `EffectLibraryRepository`; the window does not require a project. Existing subtitle application remains in Workspace workflows.

The managed `effect-library/` directory contains a versioned manifest plus one immutable file per style/script. The manifest publishes ordered entries, active font-family mappings, and a revision in one atomic replacement; uncommitted files never become visible. Original fonts use content-addressed files in the sibling `fonts/` directory; family replacements and styles become effective in the same commit. Legacy single-file libraries are ignored and preserved. On first loading a legacy directory library, the entire directory is moved intact to a sibling `effect-library.legacy-<id>/` backup, then a new empty library starts without a legacy-library error. Legacy exchange files remain unsupported, and corrupt current-format manifests still report errors. A corrupt existing manifest does not fall back to legacy data. The application owns these files and does not watch external edits.

Sharing uses `.aegistyles` ZIP for styles, `.aegieffect` JSON for a personal script's library Guid/name/source, and `.aegilibrary` ZIP for selected or all personal materials. Style exports include every locally available face in each required font family, with font files deduplicated by content. Confirmed imports install fonts into `fonts/` beside `caches/` in the user configuration directory, for AegiNext only. Raw UTF-8 `.aegifx` preserves only DSL source and its semantic identifier; builtins export through this format. The package includes neither settings nor repository revisions. Complete exports retain style timing associations and font bytes.

Each **Export…** entry in the library opens the same modal selection dialog over a frozen saved-library snapshot. Its initially unchecked tree contains only personal styles and scripts; editing-page selections and unsaved drafts remain independent. Search matches names and script IDs. Root checkboxes change only visible matches, while hidden checked entries retain their identities. Separate toolbar select-all and deselect-all actions operate on the entire snapshot, including hidden entries; the inline search clear changes only the query. Package is the default; independent files support both material kinds, and source export requires a nonempty script-only selection. Exporting the whole library uses select-all followed by **Export selected** in the chosen format. The separate script-page source action exports selected saved personal scripts and builtin sources. Cancelling a picker preserves dialog state; local errors allow retry. Closing cancels pending picker/task waiters and drains export work before releasing the dialog.

Overwriting the current style requires one saved library style and freezes its target ID. It replaces that style's appearance from the selected subtitle while preserving its name and timing processor association, without adding a preset. System and installed application fonts retain their names, variants, and other parameters without enumerating or recapturing system fonts; project font references preserve their original family. Export collects complete font families and rejects missing source files before publishing. The overwrite operation propagates its own failure to the library window and retains the target selection on success; existing material drafts first use the save/discard/cancel flow.

Import accepts multiple independent files and packages in one request. Analysis freezes validated input and the current repository revision. Identical entries skip; conflicts require a choice from the offered import/replace/skip actions. Crossed identities and builtin replacements are protected, and only one variant in an input conflict group may be selected. Commit merges both material collections atomically while preserving existing local Guids. An outdated preview must be analyzed and confirmed again; importing never performs a destructive whole-library restore. File/task failures remain local to sharing.

Effect script preview composes the unsaved source, sample text/duration, and a personal style using the application font library. The View owns transport, viewport, and presenter lifetimes; the ViewModel retains draft/input state. A dedicated worker creates, renders, and disposes one native scene, with 200 ms debounce, one pending frame, generation checks, and presentation acknowledgements. Hide pauses at the last presented position, while close clears the presenter and awaits worker disposal. Rendering uses the shared subtitle pipeline without touching project state or Undo. Parser/render errors remain local to preview; existing workbench effect cards retain their own preview sizing and behavior.

## User settings backup and migration

Export a version-3 `.aegisettings` bundle from the Settings import/export page. It contains committed preferences, encoding presets, subtitle color tags, the current workspace layout, and personal layout presets. Effect-library materials, builtin resources, recent projects, caches, and project assets are excluded. To share the entire personal effect library, select all in its export dialog, choose **Library package (.aegilibrary)**, and click **Export selected**.

Import validates and previews settings before scheduling restoration at the next launch. Version-1/2 settings packages remain readable, but their embedded styles and scripts are ignored during manual import. The destination project directory stays local by default; audio calibration remains scoped to an exactly matching device chain. Optional missing color tags preserve the local collection. Deferred restoration can be cancelled, or completed by exiting through the normal unsaved-project workflow.

`Settings/Transfer/` composes storage without introducing Desktop dependencies into Application or Media. New restores replace only settings files in a journaled startup transaction. Failure or interruption while applying rolls back original bytes and retains the pending package for retry; an already committed journal finishes cleanup. Pending packages containing legacy styles are rejected and preserved. Existing legacy transaction journals retain their original recovery rules; a managed effect-library manifest prevents an old restore from overwriting newer materials. Builtin resources remain supplied by the application.

## Localization

Language packs remain external UTF-8 JSON in `I18n/Languages/`, copied to application `i18n/`. Each has `LanguageName`, `LanguageID`, and string-valued `Strings`; language IDs and keys must be unique.

- C#: `Localization.Get("Key")` and `Localization.SetLanguage(...)`.
- XAML: `{Loc Key=...}` binds live through the markup extension.
- Lookup: current language → `en-US` → key.
- Discover available packs automatically; preserve system-language matching and preferences.
- Keep subtitle content, preset names, and DSL syntax as business data.

Do not add Tag/tree scanning. Pack changes need restart; publishing validates and hashes them. Portable guidance is in [controls](../../.agents/skills/aeginext-controls/SKILL.md) and [localization](../../.agents/skills/aeginext-localization/SKILL.md) skills.

## Verify integration

Run affected Desktop controller tests and Desktop.Ui Headless tests. Use actual Views, pointer/key input, and deterministic service boundaries; close every created host. Native menus, drag/docking, fullscreen, multiple monitors, DPI, and IME appearance need platform interaction checks.
