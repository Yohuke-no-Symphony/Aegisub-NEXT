---
name: aeginext-controls
description: Develop, refactor, integrate, or review AegiNext Avalonia reusable controls and their use in panels/settings, including draft inputs, timeline/canvas gestures, bindings, keyboard routing, typography, and disposal. Use for project control architecture and behavior; keep Dock space management and subtitle business workflows in their owning layers.
---

# AegiNext control development and use

Read the actual consuming panel and the closest existing control before designing an interface. Locate the checkout via `git rev-parse --show-toplevel`, or resolve this skill's `.agents/skills/aeginext-controls/` location three directories below it. Read `docs/en/composable-workspace.md` and `references/integration-checks.md` as needed; Chinese counterparts are under `docs/zh-cn/`.

## Choose the owner before editing

| Owner | Responsibility |
|---|---|
| `Controls/Common/` | General visual/input primitives |
| `Controls/Media/` | Frame presentation and bitmap lifetime |
| `Controls/Editing/` | Draft input, drawing, hit tests, local pointer/keyboard gestures |
| `Panels/<feature>/` | Compose controls, local adapters, compiled state/commands |
| `Settings/<page>/` | Settings drafts, validation, page commands |
| `Workspace/` | Shared session/selection, edit coordination, workflows, transaction boundaries |
| `Layouts/` | Dock hosts, tabs, splits, floating windows, layout persistence |

Controls must not access Dock, MainWindow, WorkbenchSession, ProjectEditor, personal libraries, or global services. A drawing control may consume immutable pure model data, but must not own the authoritative project. Business ViewModels must not hold controls, platform events, Dock objects, or bitmaps. Keep C# types top-level, one per file; preserve established namespaces without an unrelated mass rename.

UI text may use the shared `AegiNext.Desktop.I18n.Localization` presentation service; language selection and preference persistence stay with their existing owners. For JSON language packs, live `Loc` bindings, or language refresh, use [aeginext-localization](../aeginext-localization/SKILL.md).

## Design a semantic contract

- Prefer `StyledProperty`/`DirectProperty`, commands, and typed events carrying stable entity IDs, `MediaTime`, complete vectors/colors, or final gesture values. Declare ownership, two-way behavior, and the difference between user edits and synchronization.
- Use `NumericDraftInput`, `VectorDraftInput`, `ColorDraftInput`, and `LayerPositionEditor` before inventing another field implementation. Preserve raw invalid text; parsed `NumericUpDown.Value` alone is insufficient. `ColorDraft` is a pure draft model; it retains untouched linear/HDR values while its input projects sRGB HEX or byte RGBA.
- Validate/commit in the consuming ViewModel or Workspace transaction, not while setting a control property. Initialization, language/theme refresh, focus changes, and rendering synchronization must not write valid-looking old values back into the project.
- Keep stable field identities for error location and Esc restore. For programmatically composed vector fields, use the control's held component references or local descendants; names do not automatically register in a parent's XAML namescope. A View may find its own local controls, never another panel's controls.
- For typed child views, isolate their local `DataContext` before loading XAML where inheritance could expose the host ViewModel. `x:DataType` compiles accessors but does not enforce the runtime context; final-state type assertions alone miss initialization binding exceptions.

## Preserve input and lifetime behavior

- Invalid input remains editable with a field error. Esc restores only that field. Automatic lost-focus validation must not activate a panel or repeatedly force focus; cancel stale queued commits using a revision plus current model/root identity and visual attachment checks.
- Moving/hiding/reparenting a panel cancels pointer gestures, not drafts. Do not commit a business edit solely because a Dock container detached. Views implement their local gesture cancellation/disposal contract; the session owns the unique editor and media controller.
- Freeze target IDs, source snapshot, time, and component set at gesture start. Use one geometry/projection for drawing, hover, hit tests, and dragging. Release creates one semantic edit and one Undo; capture loss, selection replacement, Undo, and closing cancel uncommitted gestures.
- Global shortcuts consume both KeyDown and the matching KeyUp so focused buttons/checkboxes cannot also click on Space. Preserve local text, popup/menu, modal, and shortcut-recording input. Do not bypass the shared router by adding a second global handler to a control.
- Presenters own/dispose bitmaps and reject late preview results using request identity. A quality or viewport change is personal presentation state, not a project edit. Do not perform expensive scene composition in the UI drawing callback.
- Hook local/long-lived model events deliberately and remove subscriptions on final disposal. Do not dispose fixed panel instances on a temporary visual detach; test reattachment and final release separately.

## Match the shared visual system

Use dynamic theme resources, shared business typography, and existing compact icon/transport themes. A square icon button must reserve its actual size; slider rows must reserve thumb height. Keep DSL/log multiline layout distinct from ordinary centered inputs. Do not adjust CJK and Latin glyphs separately or alter a subtitle's saved render font to fix application UI typography.

Compose collapsible parameter groups with `Controls/Common/PropertySection.cs`. Use `Controls/Common/PropertyRowControl.cs` for the shared Header/Content/Actions layout, including ordinary dropdown rows; use `Controls/Editing/AnimationPropertyRowControl` to compose scalar, vector, vector3, or color drafts and optional animation actions. Keep typography, responsive layout, and separators in the shared controls/theme instead of rebuilding parameter grids in each panel. Read [references/integration-checks.md](references/integration-checks.md) for the row defaults, consuming panels, and scoped regressions.

Parameter field titles use `Classes="field"` or the shared row's Header presentation: 11 DIP, `PreviewMuted`, `WorkbenchBodyFontFamily`, and `WorkbenchInputLineHeight`. Vector and color titles are plain text; do not wrap them in buttons or add title-click property-selection commands. Scalar titles retain `NumericDragLabel` bound to their actual scalar input, and vector X/Y/Z labels retain component dragging. Property selection and animation editing use the explicit settings/keyframe actions. Collapsible category headers retain the `PropertySection` toggle behavior and its separate theme.

Coordinate the row's padding with neighboring fields. `AnimationPropertyRowControl.RowPadding` controls its inner shell; `SeparatorThickness` controls its divider independently of animation actions. Static mask geometry/node rows align their titles, axis labels, and input columns with `LayerPositionEditor`; their composition settings are in the integration reference. Compare measured coordinates in narrow/wide and light/dark layouts when mixing these controls.

Compact toolbar toggles use `Controls/Common/ToolbarToggleButton.cs`. Its type-keyed `ControlTheme` in `Styling/WorkbenchTheme.axaml` supplies 32×32 DIP sizing, centered content, and the same rounded border and checked background/border/foreground in both themes; consumers do not add styling classes. Use it in XAML or C# with the standard `Content`, `IsChecked`, and `Command` properties. Content can be a vector icon or a letter glyph, including B/I/U/S; icon foregrounds inherit the toggle's foreground, and stroked paths bind their Stroke to it. Subtitle details and timeline's left toolbar are the existing consumers. Keep business state synchronization, localized tooltips, and accessibility names in the consuming panel; the control does not access session/editor services. Synchronize `IsChecked` from selection and Undo/Redo under the view's synchronization guard, and leave field CheckBoxes to their existing field styling.

Ordinary UI text uses `WorkbenchBodyFontFamily` and `WorkbenchInputLineHeight` from `Styling/WorkbenchTheme.axaml`. Center the entire line box in its container; `VerticalAlignment` alone does not normalize fallback-font baselines. Custom drawing must use `WorkbenchTextFormatting` with the same font chain and an explicit line height, then center the measured layout in the actual badge/header rectangle. Never position badge text with a fixed `Y + 2` offset or use `FontFamily.Default` for themed business text. Check Latin, CJK, and mixed digits in both ordinary controls and custom-drawn headers; a TextBox-only regression does not cover timeline badges. Large headings use `.heading` and `WorkbenchHeadingLineHeight`; glyph icons use `.ui-icon` and retain native sizing. Do not force the 20-DIP body line box onto oversized headings/icons. Shared overlays such as quality selectors and SDR/HDR badges should use one layout row and the same actual height, not unrelated margins.

Scrubbing must submit intermediate seek values while the pointer is held, including a return to the drag's starting value. Route those values through the existing interactive seeking coordinator so decoding remains bounded and release restores exact positioning and quality. Scene-gesture cancellation cancels only the canvas gesture, not an active transport scrub; layout/global cancellation may cancel both. Verify presented frame identity and interactive mode before mouse-up; a changing playhead label alone is insufficient evidence of live video.

Read existing Dock and window themes before touching chrome. Space/title buttons remain compact and use visible vector icons; business padding belongs to business surfaces. New controls must work in light/dark themes, narrow docks, floating windows, and translated labels. Inspect actual measured geometry rather than relying on screenshot-like hardcoded offsets.

## Verify and report

Run the affected build, Rider analysis when available, and scoped behavior tests serially. Use real Headless pointer/key/text input for routing/focus/gesture bugs; inspect raw drafts and the committed project, not only commands. Close all test windows. Keep real native drawing, touchpad, multi-DPI, and complex visual acceptance distinct from Headless results.

Deliver the owning paths, public/semantic contract, consumer wiring, tests actually run, and any pending visual acceptance. Preserve unrelated staged changes and follow the user's commit scope; a control task does not imply a commit or dependency upgrade.
