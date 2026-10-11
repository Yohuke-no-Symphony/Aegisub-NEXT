# Existing controls and regression map

Paths are relative to the AegiNext checkout. Use them as current examples, not as a requirement to copy their entire implementation.

| Need | Existing implementation |
|---|---|
| Shared square toolbar toggles | `src/AegiNext.Desktop/Controls/Common/ToolbarToggleButton.cs` and its type-keyed theme in `WorkbenchTheme.axaml` |
| Raw scalar drafts | `src/AegiNext.Desktop/Controls/Editing/NumericDraftInput.cs` |
| Pure scalar state for vector consumers | `src/AegiNext.Desktop/Editing/NumericValueDraft.cs` |
| X/Y draft composition and local focus | `src/AegiNext.Desktop/Controls/Editing/VectorDraftInput.cs` |
| Color input, picker, field-local Esc | `src/AegiNext.Desktop/Controls/Editing/ColorDraftInput.axaml` and `.axaml.cs` |
| Pure color synchronization/validation | `src/AegiNext.Desktop/Editing/ColorDraft.cs` |
| Anchor/pivot/offset, no rectangle sizing | `src/AegiNext.Desktop/Controls/Editing/LayerPositionEditor.axaml` and `.axaml.cs` |
| Panel-local deferred commit cancellation | `src/AegiNext.Desktop/Panels/Effects/EffectsPanelView.axaml.cs` |
| Pure geometry and timeline interaction | `src/AegiNext.Desktop/Controls/Editing/SubtitleTimelineControl.cs` |
| Shared body line boxes and centered drawing | `src/AegiNext.Desktop/Styling/WorkbenchTheme.axaml` and `WorkbenchTextFormatting.cs` |
| Single video editing surface | `src/AegiNext.Desktop/Controls/Editing/EffectCanvasControl.cs` |
| Presenting/disposal | `src/AegiNext.Desktop/Controls/Media/VideoFrameSurface.cs` |
| Panel cancellation/disposal interface | `src/AegiNext.Desktop/Panels/IWorkbenchPanelView.cs` |
| Fixed panel instances and injection | `src/AegiNext.Desktop/Workspace/WorkbenchCompositionRoot.cs` |
| Consumed press/release and local exceptions | `src/AegiNext.Desktop/Windowing/WorkbenchWindowRegistry.cs` |

Ordinary scalar/vector fields bind both parsed values and raw text. Report failure by stable component field key. A color consumer supplies a `ColorDraft`, configures its commit boundary, and listens to semantic changes; picker refresh must use a synchronization guard. Check that untouched HDR values survive refresh and that invalid HEX/byte text never coerces to a fallback color.

Settings pages use compiled bindings to their own page ViewModels. Watch framework-handled binding exceptions during initialization, not merely the eventual DataContext. Static labels use `{Loc Key=...}` and dynamic text uses the shared `AegiNext.Desktop.I18n.Localization` service. Model selection refresh and translated item lists must preserve stable IDs without firing user actions; language changes must not commit drafts. See [aeginext-localization](../../aeginext-localization/SKILL.md) for language-resource, binding, and subscription contracts.

## Useful scoped test groups

| Behavior | Tests |
|---|---|
| Toolbar toggle themes, input, and panel wiring | `ToolbarToggleButtonUiTests`, `SubtitleDetailsFormattingToggleUiTests`, `TimelineDisplayOptionsUiTests`, `WorkbenchShortcutInputUiTests` |
| Draft ranges and preservation | `NumericDraftEditingUiTests`, `InvalidAnimationDraftUiTests` |
| Shared vector local namescope/Esc | `LayerPositionDiagramUiTests`, `SubtitlePositionEditingUiTests` |
| Color input modes, invalid input, picker | `ColorDraftTests`, `ColorDraftInputUiTests`, `SettingsColorDraftTests` |
| Page contexts and live refresh | `SettingsWindowUiTests` |
| Shortcut full press/release | `WorkbenchShortcutInputUiTests`, `ShortcutSettingsRecordingUiTests` |
| Track/menu/pointer geometry | `TimelineTracksAndNavigationUiTests`, `TimelineTrackManagementUiTests` |
| Mixed-text inputs, headers, overlays and captions | `BusinessTypographyUiTests`, `SubtitleInputTypographyUiTests`, `TimelineHeaderTypographyUiTests`, `PreviewTransportUiTests` |
| Live video while scrubbing, including return to start | `PreviewScrubbingLiveUiTests`, `InteractiveTimelineSeekingUiTests`, `MainWindowSeekSchedulingUiTests` |
| Dock lifetime and fixed instances | `WorkbenchLayoutsUiTests`, `MainDockContentUiTests` |
| Editor caret, IME, completion | `EffectScriptLanguageTests`, `EffectScriptSettingsUiTests` |

For example, run only the affected control group, not every UI test:

```sh
dotnet test Tests/AegiNext.Desktop.Ui.Tests/AegiNext.Desktop.Ui.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ColorDraftInputUiTests|FullyQualifiedName~InvalidAnimationDraftUiTests'
```

The UI test context owns the session and main/floating/settings windows; dispose it or close standalone windows in `finally`. Standalone windows constructed in C# may have no XAML root namescope: find programmatic children through local logical/visual descendants. A MainWindow helper may activate the requested panel; do not mistake that activation for an independent focus test.

Check the actual test project package before using test APIs: Core, Application, and Desktop tests use xUnit v2, while Headless UI uses xUnit v3. `TestContext.Current.CancellationToken` is a v3 API; it is not available in the Desktop v2 project. A media/font integration test must use complete stream color metadata and an actual licensed font fixture, not a header-shaped byte array that fails when selection measures text.

For rendering controls, cover the entire press → move → release with real pointer coordinates, capture cancellation, one Undo, re-layout while hovering, and reattachment. Inspect light/dark pixel colors when the failure is a theme or visible-icon bug. Headless measurements do not establish actual Windows native caption-button drawing or macOS system-menu behavior.
