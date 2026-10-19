using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleListKeyboardUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnterCommitsTextAndFocusesTheNextContentInput(bool floating)
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(3);
        var window = Prepare(context, document, floating);
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        input.SelectAll();
        window.KeyTextInput("编辑字幕 ABC 123");
        UiTestActions.Press(window, Key.Enter);
        Flush(window);

        Assert.Equal("编辑字幕 ABC 123", context.Session.DocumentSnapshot.Subtitles[0].Text);
        Assert.Equal(3, context.Session.DocumentSnapshot.Subtitles.Length);
        AssertFocused(window, document.Subtitles[1].Id);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task EnterScrollsToAnExistingEmptyNextRow()
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(40);
        document = document with { Subtitles = document.Subtitles.SetItem(20, document.Subtitles[20] with { Text = string.Empty }) };
        var window = Prepare(context, document, true);
        window.Height = 260;
        var input = RowInput(window, document.Subtitles[19].Id);
        Assert.True(input.Focus());
        UiTestActions.Press(window, Key.Enter);
        Flush(window);

        AssertFocused(window, document.Subtitles[20].Id);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShiftEnterInsertsANewlineAtTheCaretOrReplacesTheSelection(bool selection)
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(2);
        document = document with { Subtitles = document.Subtitles.SetItem(0, document.Subtitles[0] with { Text = "ABCD" }) };
        var window = Prepare(context, document);
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        input.CaretIndex = 1;
        if (selection)
        {
            input.SelectionStart = 1;
            input.SelectionEnd = 3;
        }
        UiTestActions.Press(window, Key.Enter, RawInputModifiers.Shift);
        Flush(window);

        Assert.Equal(selection ? "A\nD" : "A\nBCD", input.Text);
        AssertFocused(window, document.Subtitles[0].Id);
        Assert.Equal(2, context.Session.DocumentSnapshot.Subtitles.Length);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.True(context.ViewModel.TryCommitDrafts());
        Assert.Equal(input.Text, context.Session.DocumentSnapshot.Subtitles[0].Text);
    }

    [AvaloniaTheory]
    [InlineData(WorkbenchCommand.ADVANCE_SUBTITLE_ROW, RawInputModifiers.None)]
    [InlineData(WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK, RawInputModifiers.Shift)]
    public async Task ClearedListBindingDoesNotFallBackToNativeReturn(WorkbenchCommand command, RawInputModifiers modifiers)
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(2);
        var window = Prepare(context, document);
        SetListBinding(context, command, string.Empty);
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        input.CaretIndex = 1;

        UiTestActions.Press(window, Key.Enter, modifiers);
        Flush(window);

        Assert.Equal(document.Subtitles[0].Text, input.Text);
        AssertFocused(window, document.Subtitles[0].Id);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReboundLineBreakReplacesTheSelectionAndPreservesTextUndo(bool controlReturn, bool floating)
    {
        await using var context = new MainWindowTestContext();
        var source = CreateDocument(2);
        var document = source with { Subtitles = source.Subtitles.SetItem(0, source.Subtitles[0] with { Text = "ABCD" }) };
        var window = Prepare(context, document, floating);
        SetListBinding(context, WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK, controlReturn ? "Control+Enter" : "A");
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        input.SelectionStart = 1;
        input.SelectionEnd = 3;

        UiTestActions.Press(window, controlReturn ? Key.Enter : Key.A, controlReturn ? RawInputModifiers.Control : RawInputModifiers.None);
        Flush(window);

        Assert.Equal("A\nD", input.Text);
        Assert.Equal(2, input.CaretIndex);
        AssertFocused(window, document.Subtitles[0].Id);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.True(input.CanUndo);
        var undo = Avalonia.Application.Current!.PlatformSettings!.HotkeyConfiguration.Undo.First();
        var undoModifiers = RawInputModifiers.None;
        if ((undo.KeyModifiers & KeyModifiers.Control) != 0)
        {
            undoModifiers |= RawInputModifiers.Control;
        }
        if ((undo.KeyModifiers & KeyModifiers.Meta) != 0)
        {
            undoModifiers |= RawInputModifiers.Meta;
        }
        if ((undo.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            undoModifiers |= RawInputModifiers.Shift;
        }
        if ((undo.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            undoModifiers |= RawInputModifiers.Alt;
        }
        UiTestActions.Press(window, undo.Key, undoModifiers);
        Flush(window);
        Assert.Equal("ABCD", input.Text);
        AssertFocused(window, document.Subtitles[0].Id);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReboundAdvanceCommitsAndFocusesTheNextContentInput(bool controlReturn)
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(2);
        var window = Prepare(context, document);
        SetListBinding(context, WorkbenchCommand.ADVANCE_SUBTITLE_ROW, controlReturn ? "Control+Enter" : "A");
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        input.SelectAll();
        window.KeyTextInput("提交后继续");

        UiTestActions.Press(window, controlReturn ? Key.Enter : Key.A, controlReturn ? RawInputModifiers.Control : RawInputModifiers.None);
        Flush(window);

        Assert.Equal("提交后继续", context.Session.DocumentSnapshot.Subtitles[0].Text);
        AssertFocused(window, document.Subtitles[1].Id);
        Assert.Equal(2, context.Session.DocumentSnapshot.Subtitles.Length);
    }

    [AvaloniaTheory]
    [InlineData(WorkbenchCommand.ADVANCE_SUBTITLE_ROW, false)]
    [InlineData(WorkbenchCommand.ADVANCE_SUBTITLE_ROW, true)]
    [InlineData(WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK, false)]
    [InlineData(WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK, true)]
    public async Task ReboundBareLetterListCommandHandlesKeyDownAndAllowsSubsequentTextInput(WorkbenchCommand command, bool floating)
    {
        await using var context = new MainWindowTestContext();
        var source = CreateDocument(2);
        var document = source with { Subtitles = source.Subtitles.SetItem(0, source.Subtitles[0] with { Text = "ABCD" }) };
        var window = Prepare(context, document, floating);
        SetListBinding(context, command, "A");
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        input.SelectionStart = 1;
        input.SelectionEnd = 3;
        KeyEventArgs? observedKeyDown = null;
        EventHandler<KeyEventArgs> observeKeyDown = (_, args) => observedKeyDown = args;
        window.AddHandler(InputElement.KeyDownEvent, observeKeyDown, RoutingStrategies.Bubble, true);
        try
        {
            window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            Assert.NotNull(observedKeyDown);
            Assert.Equal(Key.A, observedKeyDown.Key);
            Assert.True(observedKeyDown.Handled);
            window.KeyRelease(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
            Flush(window);

            var targetId = command == WorkbenchCommand.ADVANCE_SUBTITLE_ROW ? document.Subtitles[1].Id : document.Subtitles[0].Id;
            AssertFocused(window, targetId);
            var target = RowInput(window, targetId);
            Assert.Equal(command == WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK ? "A\nD" : document.Subtitles[1].Text, target.Text);
            Assert.Equal("ABCD", context.Session.DocumentSnapshot.Subtitles[0].Text);
            target.SelectAll();
            observedKeyDown = null;

            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
            Assert.NotNull(observedKeyDown);
            Assert.Equal(Key.B, observedKeyDown.Key);
            Assert.False(observedKeyDown.Handled);
            window.KeyTextInput("b");
            window.KeyRelease(Key.B, RawInputModifiers.None, PhysicalKey.B, "b");
            Flush(window);

            Assert.Equal("b", target.Text);
            AssertFocused(window, targetId);
        }
        finally
        {
            window.RemoveHandler(InputElement.KeyDownEvent, observeKeyDown);
        }
    }

    [AvaloniaTheory]
    [InlineData(WorkbenchCommand.ADVANCE_SUBTITLE_ROW)]
    [InlineData(WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK)]
    public async Task ReboundListCommandDoesNotTakeImeCompositionOrTimeFieldInput(WorkbenchCommand command)
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(2);
        var window = Prepare(context, document);
        SetListBinding(context, command, "A");
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
        presenter.PreeditText = "字幕候选";

        UiTestActions.Press(window, Key.A);
        Flush(window);

        AssertFocused(window, document.Subtitles[0].Id);
        Assert.Equal(document.Subtitles[0].Text, input.Text);
        Assert.Same(document, context.Session.DocumentSnapshot);
        presenter.PreeditText = null;
        var timeInput = RowInput(window, document.Subtitles[0].Id, 1);
        Assert.True(timeInput.Focus());
        var timeText = timeInput.Text;
        UiTestActions.Press(window, Key.A);
        Flush(window);
        AssertFocused(window, document.Subtitles[0].Id, 1);
        Assert.Equal(timeText, timeInput.Text);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task LastRowEnterUsesTheCommittedEndAndExactPlayheadAndCreatesAnUndoableStyledClip()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var document = CreateDocument(1, context);
        var style = new SubtitleStyle { FontSize = 47 };
        document = document with
        {
            Tracks = [ProjectTrack.Default with
            {
                DefaultStyle = style,
                StylePresetId = Guid.NewGuid(),
                StylePresetName = "Track style"
            }]
        };
        var window = Prepare(context, document);
        await context.Controller.SeekAsync(new MediaTime(31, 4));
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        context.ViewModel.Subtitles.Rows[0].EndText = "00:00:03.500";
        input.SelectAll();
        window.KeyTextInput("上一段 edited");
        UiTestActions.Press(window, Key.Enter);
        await context.Session.WaitForProjectIdleAsync();
        Flush(window);

        var created = Assert.Single(context.Session.DocumentSnapshot.Subtitles, line => line.Id != document.Subtitles[0].Id);
        Assert.Equal(new MediaTime(7, 2), created.Start);
        Assert.Equal(new MediaTime(31, 4), created.End);
        Assert.Equal(string.Empty, created.Text);
        Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, context.Session.ClipIndex.GetSubtitleTrackId(created.Id));
        Assert.Equal(style, created.Style);
        var layer = Assert.Single(context.Session.DocumentSnapshot.Layers, layer => layer.SubtitleId == created.Id);
        Assert.Equal(created.Start, layer.Start);
        Assert.Equal(created.End, layer.End);
        AssertFocused(window, created.Id);
        Assert.Null(context.Session.LastError);
        var createdDocument = context.Session.DocumentSnapshot;
        Assert.True(context.Session.Editor.Undo());
        Assert.Equal("上一段 edited", Assert.Single(context.Session.DocumentSnapshot.Subtitles).Text);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(createdDocument, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(1, "en-US")]
    [InlineData(2, "en-US")]
    [InlineData(1, "zh-CN")]
    [InlineData(2, "zh-CN")]
    public async Task PlayheadBeforeOrAtThePreviousEndKeepsContentFocusAndReportsTheReason(int position, string language)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var document = CreateDocument(1, context);
        var window = Prepare(context, document);
        await context.Controller.SeekAsync(new(position));
        Assert.True(RowInput(window, document.Subtitles[0].Id).Focus());
        UiTestActions.Press(window, Key.Enter);
        Flush(window);

        AssertFocused(window, document.Subtitles[0].Id);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.NotNull(context.Session.LastError);
        Assert.Equal(Localization.Get("Workbench.SubtitleContinuationTimeRequired"), context.ViewModel.Error);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidTimeDraftBlocksNavigationAndFocusesItsEditableField(int column)
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(2);
        var window = Prepare(context, document);
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        var row = context.ViewModel.Subtitles.Rows[0];
        if (column == 1)
        {
            row.StartText = "invalid";
        }
        else
        {
            row.EndText = "invalid";
        }
        try
        {
            UiTestActions.Press(window, Key.Enter);
            Flush(window);
            AssertFocused(window, document.Subtitles[0].Id, column);
            Assert.Equal("invalid", RowInput(window, document.Subtitles[0].Id, column).Text);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            row.Accept(document.Subtitles[0]);
        }
    }

    [AvaloniaFact]
    public async Task CandidateConfirmationDoesNotNavigateAndHeldEnterAdvancesOnlyOnce()
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(3);
        var window = Prepare(context, document);
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
        presenter.PreeditText = "字幕候选";
        UiTestActions.Press(window, Key.Enter);
        Flush(window);
        AssertFocused(window, document.Subtitles[0].Id);
        presenter.PreeditText = null;
        input.Text = document.Subtitles[0].Text;
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Flush(window);
        AssertFocused(window, document.Subtitles[1].Id);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Flush(window);
        AssertFocused(window, document.Subtitles[1].Id);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        UiTestActions.Press(window, Key.Enter);
        Flush(window);
        AssertFocused(window, document.Subtitles[2].Id);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Meta)]
    [InlineData(RawInputModifiers.Alt)]
    public async Task OtherEnterModifiersDoNotNavigateOrInsertANewline(RawInputModifiers modifiers)
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(2);
        var window = Prepare(context, document);
        var input = RowInput(window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        UiTestActions.Press(window, Key.Enter, modifiers);
        Flush(window);
        AssertFocused(window, document.Subtitles[0].Id);
        Assert.Equal(document.Subtitles[0].Text, input.Text);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task ContinuationStaysOnTheCurrentTrackAndIgnoresOtherTrackRows()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var otherTrack = new ProjectTrack { Name = "Other" };
        var source = CreateDocument(2, context);
        var document = source with
        {
            Tracks = [ProjectTrack.Default, otherTrack],
            Layers = source.Layers.SetItem(1, source.Layers[1] with { TrackId = otherTrack.Id })
        };
        var window = Prepare(context, document);
        await context.Controller.SeekAsync(new(10));
        Assert.True(RowInput(window, document.Subtitles[0].Id).Focus());
        UiTestActions.Press(window, Key.Enter);
        await context.Session.WaitForProjectIdleAsync();
        Flush(window);

        var created = Assert.Single(context.Session.DocumentSnapshot.Subtitles, line => !document.Subtitles.Any(old => old.Id == line.Id));
        Assert.Equal(new MediaTime(2), created.Start);
        Assert.Equal(new MediaTime(10), created.End);
        Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, context.Session.ClipIndex.GetSubtitleTrackId(created.Id));
        AssertFocused(window, created.Id);
        Assert.Equal(otherTrack.Id, context.Session.ClipIndex.GetSubtitleTrackId(context.Session.DocumentSnapshot.Subtitles[1].Id));
    }

    [AvaloniaFact]
    public async Task NavigationRechecksTheOrderAfterCommittingTimingDrafts()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var document = CreateDocument(2, context);
        var window = Prepare(context, document);
        await context.Controller.SeekAsync(new(15));
        Assert.True(RowInput(window, document.Subtitles[0].Id).Focus());
        context.ViewModel.Subtitles.Rows[0].StartText = "00:00:10.000";
        context.ViewModel.Subtitles.Rows[0].EndText = "00:00:12.000";
        UiTestActions.Press(window, Key.Enter);
        await context.Session.WaitForProjectIdleAsync();
        Flush(window);

        var created = Assert.Single(context.Session.DocumentSnapshot.Subtitles, line => !document.Subtitles.Any(old => old.Id == line.Id));
        Assert.Equal(new MediaTime(12), created.Start);
        Assert.Equal(new MediaTime(15), created.End);
        AssertFocused(window, created.Id);
    }

    [AvaloniaFact]
    public async Task EnterInATimeInputDoesNotAdvanceTheContentRow()
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(2);
        var window = Prepare(context, document);
        Assert.True(RowInput(window, document.Subtitles[0].Id, 1).Focus());
        UiTestActions.Press(window, Key.Enter);
        Flush(window);
        AssertFocused(window, document.Subtitles[0].Id, 1);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortableFontCreationPreservesANewTimeFieldFocusAcrossPreparationAndReattachment(bool reattach)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.Session.Fonts.EnsureLoadedAsync();
        await context.OpenMediaAsync();
        await context.Session.Styles.Completion;
        var bytes = (await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"),
            TestContext.Current.CancellationToken)).ToImmutableArray();
        var font = new EmbeddedSubtitleFont("NotoSans.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes);
        var family = new FontFamilyBundle("Noto Sans", [font]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Portable font", new() { FontFamily = family.FamilyName, FontSize = 61 });
        await context.Session.ApplicationContext.RunStyleOperationAsync(() =>
            context.Session.StyleLibrary.UpsertAsync(new CapturedSubtitleStyle(preset, [family])));
        context.Session.Styles.Refresh(preset.Id);
        var document = CreateDocument(1, context);
        var window = Prepare(context, document);
        await context.Controller.SeekAsync(new MediaTime(41, 4));
        await context.Controller.PlayAsync();
        Assert.True(RowInput(window, document.Subtitles[0].Id).Focus());
        await context.Session.WaitForProjectIdleAsync();
        Flush(window);
        var tasks = context.Session.ApplicationContext.Tasks;
        var concurrency = tasks.MaximumConcurrentTasks;
        tasks.MaximumConcurrentTasks = 1;
        var preparationGate = new TaskCenterTestTask("Subtitle creation preparation gate");
        var gateHandle = tasks.Submit(preparationGate);
        Window targetWindow = window;
        try
        {
            await preparationGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(context.Session.IsProjectBusy);
            Assert.Equal(preset.Id, context.ViewModel.Styles.Subtitle.SelectedPreset!.Id);
            UiTestActions.Press(window, Key.Enter);
            Assert.Contains(tasks.GetSnapshots(), task => task.Name == "Tasks.CreateSubtitleClips" &&
                task.ScopeId == context.Session.TaskScope && task.State == AegiTaskState.Queued);
            Assert.Same(document, context.Session.DocumentSnapshot);
            context.Clock.Advance(TimeSpan.FromSeconds(1));
            if (reattach)
            {
                context.Window.Layouts.Float(WorkbenchPanelIds.SUBTITLES);
                Flush(window);
                targetWindow = Assert.Single(context.Window.Layouts.FloatingWindows);
                targetWindow.Width = 1100;
                targetWindow.Height = 420;
                Flush(targetWindow);
            }
            Assert.True(RowInput(targetWindow, document.Subtitles[0].Id, 1).Focus());
            preparationGate.Finish.TrySetResult();
            preparationGate.Cleanup.TrySetResult();
            await gateHandle.Completion;
            await context.Session.WaitForProjectIdleAsync();
            Flush(targetWindow);
            Assert.Null(context.Session.LastError);
            var created = Assert.Single(context.Session.DocumentSnapshot.Subtitles, line => line.Id != document.Subtitles[0].Id);
            Assert.Equal(new MediaTime(2), created.Start);
            Assert.Equal(new MediaTime(41, 4), created.End);
            Assert.DoesNotContain(context.Session.DocumentSnapshot.Assets, asset => asset.Kind == ProjectAssetKind.FONT);
            Assert.Equal(preset.Style, created.Style);
            Assert.Null(created.Style.FontAssetId);
            AssertFocused(targetWindow, document.Subtitles[0].Id, 1);
        }
        finally
        {
            preparationGate.Finish.TrySetResult();
            preparationGate.Cleanup.TrySetResult();
            await gateHandle.Completion;
            tasks.MaximumConcurrentTasks = concurrency;
        }
    }

    [AvaloniaFact]
    public async Task EnterWithinAMultiselectionPreservesTheSelectionAndAdvancesItsPrimaryInput()
    {
        await using var context = new MainWindowTestContext();
        var document = CreateDocument(3);
        var window = Prepare(context, document);
        var ids = document.Subtitles.Select(line => line.Id).ToArray();
        Assert.True(context.Session.SelectSubtitleRows(ids[0], ids));
        Assert.True(RowInput(window, ids[0]).Focus());
        UiTestActions.Press(window, Key.Enter);
        Flush(window);
        AssertFocused(window, ids[1]);
        Assert.Equal(ids, context.Session.SelectedSubtitleIds);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static ProjectDocument CreateDocument(int count, MainWindowTestContext? context = null)
    {
        var lines = Enumerable.Range(0, count).Select(index => new SubtitleLine
        {
            Text = "字幕 ABC " + index,
            Start = new(index * 3),
            End = new(index * 3 + 2)
        }).ToImmutableArray();
        return new()
        {
            Assets = context?.Session.DocumentSnapshot.Assets ?? [],
            Media = context?.Session.DocumentSnapshot.Media,
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                Start = line.Start,
                End = line.End
            }).ToImmutableArray()
        };
    }

    private static void SetListBinding(MainWindowTestContext context, WorkbenchCommand command, string gesture)
    {
        var bindings = context.Session.Preferences.ShortcutBindings.Select(binding => binding.Command switch
        {
            WorkbenchCommand.ADD_SUBTITLE when gesture == "Control+Enter" => binding with { Gesture = string.Empty },
            _ when binding.Command == command => binding with { Gesture = gesture },
            _ => binding
        }).ToImmutableArray();
        context.Session.UpdatePreferences(context.Session.Preferences with { ShortcutBindings = bindings });
    }

    private static Window Prepare(MainWindowTestContext context, ProjectDocument document, bool floating = false)
    {
        context.Session.Editor.Reset(document);
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        if (floating)
        {
            context.Window.Layouts.Float(WorkbenchPanelIds.SUBTITLES);
            Flush(context.Window);
            var owner = Assert.Single(context.Window.Layouts.FloatingWindows);
            owner.Width = 1100;
            owner.Height = 420;
            Flush(owner);
            return owner;
        }
        Flush(context.Window);
        return context.Window;
    }

    private static TextBox RowInput(Window window, Guid id, int column = 4)
    {
        var list = UiTestActions.Find<ListBox>(window, "SubtitleList");
        list.ScrollIntoView(list.Items.OfType<SubtitleRow>().Single(row => row.Id == id));
        Flush(window);
        return list.GetVisualDescendants().OfType<TextBox>().Single(box =>
            box.DataContext is SubtitleRow row && row.Id == id && Grid.GetColumn(box) == column);
    }

    private static void AssertFocused(Window window, Guid id, int column = 4)
    {
        var focused = Assert.IsType<TextBox>(window.FocusManager?.GetFocusedElement());
        Assert.Equal(id, Assert.IsType<SubtitleRow>(focused.DataContext).Id);
        Assert.Equal(column, Grid.GetColumn(focused));
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
