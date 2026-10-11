using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.SubtitleDetails;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleDetailsEditingUiTests
{
    [AvaloniaFact]
    public async Task DetailsShowsOneNativeEditorAndTheClipAxis()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, true);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        Flush(host);
        Assert.True(UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").IsEffectivelyVisible);
        Assert.True(UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis").IsEffectivelyVisible);
        Assert.DoesNotContain(host.GetVisualDescendants().OfType<Control>(),
            control => control.Name is "SubtitleDetailsTabs" or "SubtitleCodeInput");
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task SharedHighlightStyleTargetRejectsInvalidDraftAndCommitsOnlyVisualOverrides()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, true);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        Flush(host);
        var input = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        input.SetSelection(0, 0);
        var target = UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput");
        Assert.True(target.IsEffectivelyEnabled);
        target.SelectedIndex = 2;
        Flush(host);
        var fill = UiTestActions.Find<ColorDraftInput>(host, "SelectionFillInput");
        Assert.Same(context.Session.Details.HighlightDraft.Fill, fill.Draft);
        Assert.NotSame(context.Session.Details.StyleDraft.Fill, fill.Draft);
        Assert.True(fill.IsEffectivelyEnabled);
        Assert.False(UiTestActions.Find<FontFamilyPicker>(host, "SelectionFontInput").IsEffectivelyEnabled);
        Assert.False(UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput").IsEffectivelyEnabled);
        foreach (var name in new[] { "BoldSelectionButton", "ItalicSelectionButton", "UnderlineSelectionButton", "StrikethroughSelectionButton" })
        {
            Assert.False(UiTestActions.Find<Button>(host, name).IsEffectivelyEnabled);
        }
        foreach (var name in new[] { "ApplyToSelectionButton", "ClearSelectionStyleButton" })
        {
            Assert.True(UiTestActions.Find<Button>(host, name).IsEffectivelyEnabled);
        }
        var blur = UiTestActions.Find<NumericDraftInput>(host, "SelectionShadowBlurInput");
        blur.RawText = "invalid";
        target.SelectedIndex = 0;
        Flush(host);
        Assert.Equal(2, target.SelectedIndex);
        Assert.Equal("invalid", blur.RawText);
        Assert.Equal("invalid", context.Session.Details.HighlightDraft.ShadowBlurText);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(Assert.Single(blur.GetVisualDescendants().OfType<TextBox>()).Focus());
        UiTestActions.Press(host, Key.Escape);
        Flush(host);
        Assert.NotEqual("invalid", blur.RawText);
        blur.RawText = "5.125";
        blur.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Flush(host);
        var edited = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Equal(5.125, Assert.Single(edited.KaraokeStyleSpans).ActiveStyle!.ShadowBlur);
        Assert.Equal(original.Subtitles[0].Style, edited.Style);
        Assert.Equal(original.Subtitles[0].InlineSpans, edited.InlineSpans);
        Assert.Equal(original.Subtitles[0].Karaoke, edited.Karaoke);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        target.SelectedIndex = 0;
        input.SetSelection(0, 1);
        Flush(host);
        Assert.Same(context.Session.Details.StyleDraft.Fill, fill.Draft);
        Assert.True(UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task ClipPopupKeepsPlaybackAndRejectsDismissalUntilItsInvalidDraftIsRestored()
    {
        await using var context = new MainWindowTestContext();
        Prepare(context, true);
        await context.OpenMediaAsync();
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(original.Subtitles[0].Id);
        Assert.Equal(original.Subtitles[0].Id, context.Session.SelectedCueId);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        Flush(host);
        await context.Session.Details.PlayAsync(false, false);
        Assert.True(context.Session.Details.IsPlaying);
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        axis.BringIntoView();
        Flush(host);
        var point = axis.TranslatePoint(axis.GeometryFor(original.Subtitles[0].Karaoke[0].Id).Body.Center, host)!.Value;
        host.MouseDown(point, MouseButton.Left);
        host.MouseUp(point, MouseButton.Left);
        Flush(host);
        var popup = Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(axis));
        Assert.True(popup.IsOpen);
        Assert.True(UiTestActions.Find<Button>(host, "BoldSelectionButton").IsEffectivelyEnabled);
        Assert.True(UiTestActions.Find<WrapPanel>(host, "SelectionStyleFields").IsEffectivelyEnabled);
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        var duration = UiTestActions.Find<NumericDraftInput>(content, "KaraokeDurationInput");
        Assert.Equal("1", duration.RawText);
        Assert.Equal(original.Subtitles[0].Karaoke[0].Id, context.Session.Details.SelectedClipId);
        var durationEditor = Assert.Single(duration.GetVisualDescendants().OfType<TextBox>());
        Assert.True(durationEditor.Focus());
        Dispatcher.UIThread.RunJobs();
        Assert.True(context.Session.Details.IsPlaying);
        CapturePopup(content);
        duration.RawText = "invalid";
        var initialParent = content.GetVisualParent();
        var initialRoot = TopLevel.GetTopLevel(content);
        Assert.NotNull(initialParent);
        Assert.Same(durationEditor, host.FocusManager.GetFocusedElement());
        content.GetLogicalAncestors().OfType<Popup>().Single().Close();
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        Assert.Same(content, popup.Child);
        Assert.Same(initialParent, content.GetVisualParent());
        Assert.Same(initialRoot, TopLevel.GetTopLevel(content));
        Assert.Equal("invalid", duration.RawText);
        Assert.Same(durationEditor, host.FocusManager.GetFocusedElement());
        Assert.True(context.Session.Details.IsPlaying);
        popup.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.True(popup.IsOpen);
        Assert.Equal("invalid", duration.RawText);
        context.Session.SelectCue(original.Subtitles[1].Id);
        Assert.Equal(original.Subtitles[0].Id, context.Session.SelectedCueId);
        Assert.True(durationEditor.Focus());
        var popupRoot = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(durationEditor));
        Flush(host);
        Assert.Same(durationEditor, host.FocusManager.GetFocusedElement());
        popupRoot.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        popupRoot.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        Flush(host);
        Assert.False(popup.IsOpen);
        host.MouseDown(point, MouseButton.Left);
        host.MouseUp(point, MouseButton.Left);
        Flush(host);
        Assert.True(popup.IsOpen);
        Assert.True(durationEditor.Focus());
        Assert.Equal("1", duration.RawText);
        duration.RawText = "1.25";
        duration.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        var edited = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(5, 4), edited.Karaoke[0].End);
        Assert.Equal(original.Subtitles[0].Karaoke[1], edited.Karaoke[1]);
        Assert.Equal(original.Subtitles[0].Start, edited.Start);
        Assert.Equal(original.Subtitles[0].End, edited.End);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task SharedStyleTargetPreservesInvalidBodyInputAndCommitsVisualHighlightOnceOnBlur()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, true);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        Flush(host);
        var rich = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        rich.SetSelection(0, 1);
        var target = UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput");
        var size = UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput");
        size.RawText = "invalid";
        target.SelectedIndex = 2;
        Flush(host);
        Assert.Equal(0, target.SelectedIndex);
        Assert.Equal("invalid", size.RawText);
        Assert.Equal("invalid", context.Session.Details.StyleDraft.FontSizeText);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.TryCommitDrafts());
        Flush(host);
        Assert.Same(Assert.Single(size.GetVisualDescendants().OfType<TextBox>()), host.FocusManager.GetFocusedElement());
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        Assert.False(Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(axis)).IsOpen);
        Assert.True(Assert.Single(size.GetVisualDescendants().OfType<TextBox>()).Focus());
        UiTestActions.Press(host, Key.Escape);
        Assert.Equal("32", size.RawText);
        size.RawText = "48.125";
        target.SelectedIndex = 2;
        Flush(host);
        Assert.Equal(2, target.SelectedIndex);
        var bodySnapshot = context.Session.Editor.Snapshot;
        var body = bodySnapshot.Subtitles[0];
        Assert.Equal(48.125, Assert.Single(body.InlineSpans).Style.FontSize);
        Assert.Equal(original.Subtitles[0].Style, body.Style);
        var stroke = UiTestActions.Find<NumericDraftInput>(host, "SelectionStrokeWidthInput");
        Assert.Single(stroke.GetVisualDescendants().OfType<TextBox>()).Focus();
        stroke.RawText = "4.25";
        Assert.Same(bodySnapshot, context.Session.Editor.Snapshot);
        rich.Focus();
        Flush(host);
        var highlight = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Equal(4.25, Assert.IsType<KaraokeVisualStyleOverride>(KaraokeVisualStyleResolver.StyleAt(highlight.KaraokeStyleSpans, 0, KaraokeVisualState.ACTIVE)).StrokeWidth);
        Assert.Equal(body.KaraokeStyle, highlight.KaraokeStyle);
        Assert.Equal(body.Style, highlight.Style);
        Assert.Equal(body.InlineSpans, highlight.InlineSpans);
        Assert.Equal(body.Karaoke, highlight.Karaoke);
        Assert.Null(KaraokeVisualStyleResolver.StyleAt(highlight.KaraokeStyleSpans, 1, KaraokeVisualState.ACTIVE));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(bodySnapshot, context.Session.Editor.Snapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task FloatingDetailsReusesItsPanelAndActualTypingCommitsOnceOnVisualStateChange()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        var panel = Assert.IsType<SubtitleDetailsPanelView>(context.Window.Panels[WorkbenchPanelIds.SUBTITLE_DETAILS]);
        Flush(host);
        var input = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        input.Focus();
        input.SetSelection(0, original.Subtitles[0].Text.Length);
        var request = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        input.RaiseEvent(request);
        var ime = Assert.IsType<RichSubtitleInputMethodClient>(request.Client);
        ime.SetPreeditText("👩‍💻", 1);
        Assert.Equal("ab", ime.SurroundingText);
        Assert.True(double.IsFinite(ime.CursorRectangle.X));
        Assert.True(ime.CursorRectangle.Height > 0);
        Assert.Null(input.RenderDiagnostic);
        Assert.False(context.Session.Editor.CanUndo);
        ime.SetPreeditText(null);
        host.KeyTextInput("你好👩‍💻");
        Flush(host);
        Assert.Null(input.RenderDiagnostic);
        Assert.Equal("你好👩‍💻", context.Session.Details.Line!.Text);
        Assert.Equal("ab", context.Session.Editor.Snapshot.Subtitles[0].Text);
        Assert.False(context.Session.Editor.CanUndo);
        UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = 1;
        Flush(host);
        Assert.Equal("你好👩‍💻", context.Session.Editor.Snapshot.Subtitles[0].Text);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        host.Close();
        Flush(context.Window);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        Assert.Same(panel, context.Window.Panels[WorkbenchPanelIds.SUBTITLE_DETAILS]);
        Assert.Single(context.Window.Layouts.FloatingWindows);
        var adapter = context.Window.Layouts.PanelAdapters[WorkbenchPanelIds.SUBTITLE_DETAILS];
        var target = context.Window.Layouts.PanelAdapters[WorkbenchPanelIds.SUBTITLES];
        var factory = ((Dock.Avalonia.Controls.DockControl)context.Window.Layouts.Host).Factory!;
        factory.MoveDockable((Dock.Model.Core.IDock)adapter.Owner!, (Dock.Model.Core.IDock)target.Owner!, adapter, target);
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLE_DETAILS);
        Flush(context.Window);
        Assert.Same(context.Window, TopLevel.GetTopLevel(panel));
        Assert.Same(panel, context.Window.Panels[WorkbenchPanelIds.SUBTITLE_DETAILS]);
        await context.Window.Layouts.FlushAsync();
        Assert.Empty(context.Window.Layouts.Capture().Floating);
    }

    [AvaloniaFact]
    public async Task ToolbarClickPreservesSelectionAndStylesOnlySelectedText()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        Flush(host);
        var input = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        input.Focus();
        input.SetSelection(0, 1);
        UiTestActions.Click(host, "BoldSelectionButton");
        Flush(host);
        var line = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.False(line.Style.Bold);
        var span = Assert.Single(line.InlineSpans);
        Assert.Equal(0, span.Utf16Start);
        Assert.Equal(1, span.Utf16Length);
        Assert.True(span.Style.Bold);
        Assert.Equal(0, input.SelectionStart);
        Assert.Equal(1, input.SelectionEnd);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
    }

    [AvaloniaFact]
    public async Task RichViewSelectsAndEditsExistingKaraokeWithoutCommittingEachCharacter()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, true);
        var line = original.Subtitles[0] with
        {
            InlineSpans = [new(0, 1, new() { Bold = true })],
            Karaoke = [original.Subtitles[0].Karaoke[0] with { End = new(1001, 1000) },
                original.Subtitles[0].Karaoke[1] with { Start = new(1001, 1000), End = new(2001, 1000) }]
        };
        original = original with { Subtitles = [line, original.Subtitles[1]] };
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(line.Id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 1200;
        Flush(host);
        var input = UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput");
        input.Focus();
        input.SetSelection(0, 1);
        Assert.Equal(line.Karaoke[0].Id, context.Session.Details.SelectedClipId);
        Assert.Equal(0, UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex);
        Assert.False(Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis"))).IsOpen);
        Assert.Equal("1.001", context.Session.Details.DurationText);
        host.KeyTextInput("x");
        Assert.Equal(new MediaTime(1001, 1000), context.Session.Details.Line!.Karaoke[1].Start);
        host.KeyTextInput("y");
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Details.Line!.Karaoke.Zip(context.Session.Details.Line.Karaoke.Skip(1), (left, right) => left.End <= right.Start).All(value => value),
            string.Join("; ", context.Session.Details.Line.Karaoke.Select(clip => clip.Utf16Start + ": " + clip.Start + " - " + clip.End)));
        input.SetSelection(0, 1);
        context.Session.Details.EditDuration("1.501");
        Assert.True(context.Session.Details.TryCommit(), context.Session.Details.Error);
        var edited = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(1501, 1000), edited.Karaoke[0].End);
        Assert.Equal(new MediaTime(2001, 1000), edited.Karaoke[^1].End);
        Assert.NotEmpty(edited.InlineSpans);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task InvalidNativeDraftBlocksVisualStateSelectionAndFloatingClose()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        Flush(host);
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 1);
        var size = UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput");
        size.RawText = "invalid";
        var state = UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput");
        state.SelectedIndex = 1;
        Assert.Equal(0, state.SelectedIndex);
        context.Session.SelectCue(original.Subtitles[1].Id);
        Assert.Equal(original.Subtitles[0].Id, context.Session.SelectedCueId);
        host.Close();
        Assert.True(host.IsVisible);
        Assert.Same(original, context.Session.Editor.Snapshot);
        context.Session.Details.Restore("FontSize");
        host.Close();
        Assert.False(host.IsVisible);
    }

    [AvaloniaFact]
    public async Task EscapeCancelsKaraokeDragAndReleasesPointerCaptureWithoutAnUndoEntry()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, true);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        Flush(host);
        host.Width = 950;
        host.Height = 1200;
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        Flush(host);
        IPointer? pointer = null;
        axis.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Bubble, true);
        axis.BringIntoView();
        Flush(host);
        var point = axis.TranslatePoint(axis.GeometryFor(original.Subtitles[0].Karaoke[0].Id).Body.Center, host)!.Value;
        host.MouseDown(point, MouseButton.Left);
        Assert.NotNull(pointer);
        Assert.Same(axis, pointer.Captured);
        host.MouseMove(point + new Vector(30, 0));
        UiTestActions.Press(host, Key.Escape);
        Assert.Null(pointer.Captured);
        host.MouseUp(point + new Vector(30, 0), MouseButton.Left);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task KaraokeBodyDragMovesOnlyTheSelectedGroupWithOneUndo()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, true);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        Flush(host);
        host.Width = 950;
        host.Height = 1200;
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        Flush(host);
        var first = original.Subtitles[0].Karaoke[0];
        axis.BringIntoView();
        Flush(host);
        var point = axis.TranslatePoint(axis.GeometryFor(original.Subtitles[0].Karaoke[0].Id).Body.Center, host)!.Value;
        host.MouseDown(point, MouseButton.Left);
        host.MouseMove(point + new Vector((axis.Bounds.Width - 24) / 8, 0));
        Assert.False(context.Session.Editor.CanUndo);
        host.MouseUp(point + new Vector((axis.Bounds.Width - 24) / 8, 0), MouseButton.Left);
        Flush(host);
        var edited = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.True(edited.Karaoke[0].Start > first.Start);
        Assert.Equal(first.End - first.Start, edited.Karaoke[0].End - edited.Karaoke[0].Start);
        Assert.Equal(original.Subtitles[0].Karaoke[1], edited.Karaoke[1]);
        Assert.Equal(original.Subtitles[0].End, edited.End);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static ProjectDocument Prepare(MainWindowTestContext context, bool karaoke = false)
    {
        var first = new SubtitleLine { Text = "ab", End = new(4), Style = new() { FontSize = 32 }, Karaoke = karaoke
            ? [new(0, 1, MediaTime.Zero, new(1), SceneColor.White), new(1, 1, new(1), new(2), SceneColor.White)] : [] };
        var second = new SubtitleLine { Text = "next", Start = new(5), End = new(7) };
        var document = new ProjectDocument { Subtitles = [first, second], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start, End = first.End },
             new() { Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End }] };
        context.Session.Editor.Reset(document);
        context.Session.SelectCue(first.Id);
        Assert.Equal(first.Id, context.Session.SelectedCueId);
        Assert.Equal("32", context.Session.Details.StyleDraft.FontSizeText);
        return document;
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void CapturePopup(Control popupContent)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Assert.True(Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(popupContent));
        root.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = root.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, "details-toolbar-clip-popup.png"), PngBitmapEncoderOptions.Default);
    }
}
