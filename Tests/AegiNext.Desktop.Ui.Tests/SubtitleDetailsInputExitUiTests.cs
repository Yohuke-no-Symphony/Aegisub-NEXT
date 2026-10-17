using System.Collections.Immutable;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Core.Projects;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleDetailsInputExitUiTests
{
    [AvaloniaFact]
    public async Task EscapeCommitsCurrentValidFieldOnceAndPreservesOtherInvalidDraft()
    {
        Assert.True(Enum.TryParse<WorkbenchCommand>("END_TEXT_INPUT", out _));
        await using var context = new MainWindowTestContext();
        var host = await OpenAsync(context);
        var original = context.Session.Editor.Snapshot;
        var input = UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput");
        Focus(input, host);
        input.RawText = "88";
        context.Session.Details.StyleDraft.ShadowBlurText = "invalid";
        UiTestActions.Press(host, Key.Escape);
        Flush(host);
        Assert.Equal(88, context.Session.Editor.Snapshot.Subtitles[0].InlineSpans[0].Style.FontSize);
        Assert.Equal("invalid", context.Session.Details.StyleDraft.ShadowBlurText);
        Assert.IsNotType<TextBox>(host.FocusManager.GetFocusedElement());
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task EscapeRestoresOnlyInvalidCurrentFieldAndLeavesTextInput()
    {
        Assert.True(Enum.TryParse<WorkbenchCommand>("END_TEXT_INPUT", out _));
        await using var context = new MainWindowTestContext();
        var host = await OpenAsync(context);
        var original = context.Session.Editor.Snapshot;
        var input = UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput");
        var previous = input.RawText;
        Focus(input, host);
        input.RawText = "invalid";
        context.Session.Details.StyleDraft.StrokeWidthText = "3.25";
        UiTestActions.Press(host, Key.Escape);
        Flush(host);
        Assert.Equal(previous, input.RawText);
        Assert.Equal("3.25", context.Session.Details.StyleDraft.StrokeWidthText);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.IsNotType<TextBox>(host.FocusManager.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task RebindingAndDisablingExitDoNotLeaveHiddenFixedEscapeBehavior()
    {
        Assert.True(Enum.TryParse<WorkbenchCommand>("END_TEXT_INPUT", out var exit));
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var host = await OpenAsync(context);
        var input = UiTestActions.Find<NumericDraftInput>(host, "SelectionFontSizeInput");
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            ShortcutBindings = context.Session.Preferences.ShortcutBindings
                .Select(binding => binding.Command == exit ? binding with { Gesture = "Shift+F2" } : binding).ToImmutableArray()
        });
        var editor = Focus(input, host);
        input.RawText = "invalid";
        UiTestActions.Press(host, Key.Escape);
        Flush(host);
        Assert.Same(editor, host.FocusManager.GetFocusedElement());
        Assert.Equal("invalid", input.RawText);
        host.KeyPress(Key.F2, RawInputModifiers.Shift, PhysicalKey.None, null);
        host.KeyPress(Key.F2, RawInputModifiers.Shift, PhysicalKey.None, null);
        host.KeyRelease(Key.F2, RawInputModifiers.Shift, PhysicalKey.None, null);
        Flush(host);
        Assert.NotEqual("invalid", input.RawText);
        Assert.IsNotType<TextBox>(host.FocusManager.GetFocusedElement());
        Assert.False(context.Session.Editor.CanUndo);
        context.Session.UpdatePreferences(context.Session.Preferences with
        {
            ShortcutBindings = context.Session.Preferences.ShortcutBindings
                .Select(binding => binding.Command == exit ? binding with { Gesture = string.Empty } : binding).ToImmutableArray()
        });
        editor = Focus(input, host);
        input.RawText = "invalid";
        UiTestActions.Press(host, Key.Escape);
        UiTestActions.Press(host, Key.F2, RawInputModifiers.Shift);
        Flush(host);
        Assert.Same(editor, host.FocusManager.GetFocusedElement());
        Assert.Equal("invalid", input.RawText);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingVisualStateCommitsValidNativeDraftOrKeepsInvalidStateAndTarget(bool invalid)
    {
        await using var context = new MainWindowTestContext();
        var host = await OpenAsync(context);
        var original = context.Session.Editor.Snapshot;
        var state = UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput");
        state.SelectedIndex = 2;
        var input = UiTestActions.Find<NumericDraftInput>(host, "SelectionStrokeWidthInput");
        var previous = input.RawText;
        Focus(input, host);
        input.RawText = invalid ? "invalid" : "3.25";
        context.Session.Details.EditText(1, 1, "C");
        state.SelectedIndex = 1;
        Flush(host);
        if (invalid)
        {
            Assert.Equal(2, state.SelectedIndex);
            Assert.Equal("invalid", input.RawText);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.Equal("aC", context.Session.Details.Line!.Text);
            UiTestActions.Press(host, Key.Escape);
            Flush(host);
            Assert.Equal(previous, input.RawText);
            Assert.Equal("aC", context.Session.Details.Line!.Text);
            Assert.False(context.Session.Editor.CanUndo);
        }
        else
        {
            Assert.Equal(1, state.SelectedIndex);
            var changed = context.Session.Editor.Snapshot.Subtitles[0];
            Assert.Equal("aC", changed.Text);
            Assert.Equal(3.25, KaraokeVisualStyleResolver.RangeStyleAt(changed, 0, KaraokeVisualState.ACTIVE)!.StrokeWidth);
            Assert.Null(KaraokeVisualStyleResolver.RangeStyleAt(changed, 0, KaraokeVisualState.INACTIVE));
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
    }

    [AvaloniaFact]
    public async Task SelectingDefaultHighlightClearsExistingSelectionOverrideAndKeepsOtherGlyph()
    {
        await using var context = new MainWindowTestContext();
        var host = await OpenAsync(context);
        Assert.True(context.Session.Details.GenerateAllTiming());
        var id = context.Session.Editor.Snapshot.Subtitles[0].Id;
        context.Session.Editor.UpdateSubtitle(id, line => line with
        {
            KaraokeStyleSpans = [new(0, 1, new() { Fill = new(1, 0, 0) }), new(1, 1, new() { StrokeWidth = 7 })]
        });
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 1);
        UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = 2;
        var presets = UiTestActions.Find<ComboBox>(host, "SelectionStylePresetCombo");
        Assert.NotEqual(Guid.Empty, Assert.IsType<StylePresetListItem>(presets.SelectedItem).Id);
        presets.SelectedItem = presets.Items.OfType<StylePresetListItem>().Single(item => item.Id == Guid.Empty);
        Flush(host);
        var changed = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Null(KaraokeVisualStyleResolver.StyleAt(changed.KaraokeStyleSpans, 0, KaraokeVisualState.ACTIVE));
        Assert.Equal(original.Subtitles[0].KaraokeStyleSpans[1], Assert.Single(changed.KaraokeStyleSpans));
        Assert.Equal(original.Subtitles[0].Karaoke, changed.Karaoke);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static async Task<Window> OpenAsync(MainWindowTestContext context)
    {
        var id = context.Session.Editor.AddSubtitle(new(0), new(4), "ab");
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 900;
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 1);
        Flush(host);
        return host;
    }

    private static TextBox Focus(NumericDraftInput input, Window host)
    {
        input.BringIntoView();
        Flush(host);
        var editor = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(editor.Focus());
        Flush(host);
        return editor;
    }

    private static void Flush(Window host)
    {
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
