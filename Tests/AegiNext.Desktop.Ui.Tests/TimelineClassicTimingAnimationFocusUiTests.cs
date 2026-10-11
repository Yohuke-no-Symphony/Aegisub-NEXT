using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClassicTimingAnimationFocusUiTests
{
    [AvaloniaFact]
    public async Task ClassicModeAnimationExpanderPreservesSubtitleInputFocusAndOnlyChangesTheRowViewState()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cue = new SubtitleLine { Start = new(8), End = new(10), Text = "字幕 ABC 123" };
        context.Session.Editor.Reset(context.Session.DocumentSnapshot with
        {
            Subtitles = [cue],
            Layers =
            [
                new()
                {
                    Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
                    Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.25), new(new(1), 0.75)])]
                }
            ]
        });
        context.Session.UpdatePreferences(current => current with
        {
            TimelineClassicTimingEnabled = true, TimelineSnapEnabled = false, TimelineStepEnabled = false
        });
        context.ViewModel.Timeline.PixelsPerSecond = 120;
        context.ViewModel.Timeline.ViewStart = 7;
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        Flush(context.Window);
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var subtitleRow = list.Items.OfType<SubtitleRow>().Single(row => row.Id == cue.Id);
        list.ScrollIntoView(subtitleRow);
        Flush(context.Window);
        var input = list.GetVisualDescendants().OfType<TextBox>().Single(box =>
            box.AcceptsReturn && box.DataContext is SubtitleRow row && row.Id == cue.Id);
        input.BringIntoView();
        Flush(context.Window);
        var inputPoint = input.TranslatePoint(new(input.Bounds.Width / 2, input.Bounds.Height / 2), context.Window)!.Value;
        var inputHit = Assert.IsAssignableFrom<Visual>(context.Window.InputHitTest(inputPoint));
        Assert.True(ReferenceEquals(inputHit, input) || inputHit.GetVisualAncestors().Contains(input));
        context.Window.MouseDown(inputPoint, MouseButton.Left);
        context.Window.MouseUp(inputPoint, MouseButton.Left);
        Flush(context.Window);
        Assert.Same(input, context.Window.FocusManager!.GetFocusedElement());
        Assert.Equal(cue.Id, context.Session.SelectedCueId);
        input.CaretIndex = 5;
        input.SelectionStart = 2;
        input.SelectionEnd = 5;
        var caret = input.CaretIndex;
        var lostFocusCount = 0;
        input.LostFocus += (_, _) => lostFocusCount++;
        var original = context.Session.DocumentSnapshot;
        var undoLabel = context.Session.Editor.UndoLabel;
        var redoLabel = context.Session.Editor.RedoLabel;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var animationRow = new TimelineEffectClipId(cue.Id, new(AnimationProperty.OPACITY));
        var requests = new List<TimelineEffectClipCollapseEventArgs>();
        timeline.EffectClipCollapseRequested += (_, e) => requests.Add(e);
        Assert.True(timeline.IsClassicTimingEnabled);
        Assert.True(timeline.IsEffectClipCollapsed(animationRow));
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(context.Session.Editor.CanRedo);

        ClickExpander(context, timeline, animationRow, input, false);

        var collapseRequest = Assert.Single(requests);
        Assert.Equal(animationRow, collapseRequest.Id);
        Assert.False(collapseRequest.IsCollapsed);
        Assert.Equal(animationRow, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(context.Session.Editor.CanRedo);
        Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);
        Assert.Equal(redoLabel, context.Session.Editor.RedoLabel);

        ClickExpander(context, timeline, animationRow, input, true);

        Assert.Equal(2, requests.Count);
        Assert.Equal(animationRow, requests[1].Id);
        Assert.True(requests[1].IsCollapsed);
        Assert.Empty(context.Session.TimelineViewState.ExpandedAnimationClips);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(context.Session.Editor.CanRedo);
        Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);
        Assert.Equal(redoLabel, context.Session.Editor.RedoLabel);
        Assert.False(timeline.HasActiveDrag);
        Assert.Equal(cue.Text, input.Text);
        Assert.Equal(caret, input.CaretIndex);
        Assert.Equal(2, input.SelectionStart);
        Assert.Equal(5, input.SelectionEnd);
        Assert.Equal(0, lostFocusCount);
        Assert.Null(context.Session.LastError);
    }

    private static void ClickExpander(MainWindowTestContext context, SubtitleTimelineControl timeline,
        TimelineEffectClipId row, TextBox input, bool expectedCollapsed)
    {
        Flush(context.Window);
        var rectangle = timeline.GetEffectClipExpanderRectangle(row)!.Value;
        Assert.True(rectangle.Center.Y >= timeline.RulerHeight);
        Assert.True(rectangle.Center.Y < timeline.Bounds.Height);
        var point = timeline.TranslatePoint(rectangle.Center, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        Flush(context.Window);
        Assert.Equal(expectedCollapsed, timeline.IsEffectClipCollapsed(row));
        Assert.Same(input, context.Window.FocusManager!.GetFocusedElement());
        context.Window.MouseUp(point, MouseButton.Left);
        Flush(context.Window);
        Assert.Equal(expectedCollapsed, timeline.IsEffectClipCollapsed(row));
        Assert.Same(input, context.Window.FocusManager.GetFocusedElement());
        Assert.True(input.IsFocused);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
