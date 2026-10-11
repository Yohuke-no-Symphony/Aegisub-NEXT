using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MainWindowSeekSchedulingUiTests
{
    [AvaloniaFact]
    public async Task TimelineShowsLatestTargetImmediatelyAndOldSeekCompletionCannotReplaceReleasedTarget()
    {
        await using var context = new SeekSchedulingTestContext();
        await WaitAsync(context.Window.Session.ApplicationContext.Initialization);
        await context.OpenMediaAsync();
        var window = context.Window;
        var first = context.Source.BlockNextSeek(new(5));
        var final = context.Source.BlockNextSeek(new(10));
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline");
        window.UpdateLayout();
        var firstPoint = TimelinePoint(window, timeline, 5);
        var finalPoint = TimelinePoint(window, timeline, 10);

        window.MouseDown(firstPoint, MouseButton.Left);
        AssertDisplayedTarget(window, new(5));
        Assert.Equal(MediaTime.Zero, context.Controller.Snapshot.Position);
        await WaitAsync(first.Entered);
        var obsolete = context.Controller.SeekInteractiveAsync(first.Target);
        window.MouseMove(finalPoint);
        window.MouseUp(finalPoint, MouseButton.Left);
        Assert.False(timeline.HasActiveDrag);
        AssertDisplayedTarget(window, new(10));
        var current = context.Controller.CompleteInteractiveSeekAsync(final.Target, false);

        first.Release();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WaitAsync(obsolete));
        await WaitAsync(final.Entered);
        await FlushUiAsync();
        AssertDisplayedTarget(window, new(10));
        Assert.Equal(MediaTime.Zero, context.Controller.Snapshot.Position);
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_ENTER).CanExecute(null));
        Assert.Single(context.Source.SeekTargets, target => target == new MediaTime(5));
        Assert.Single(context.Source.SeekTargets, target => target == new MediaTime(10));

        final.Release();
        await WaitAsync(current);
        await FlushUiAsync();
        Assert.Equal(new MediaTime(10), context.Controller.Snapshot.Position);
        AssertDisplayedTarget(window, new(10));
        Assert.True(window.GetCommand(WorkbenchCommand.TIMING_ENTER).CanExecute(null));
    }

    [AvaloniaFact]
    public async Task RepeatedRelativeCommandsAccumulateFromPendingTargetBeforeDecodeCompletes()
    {
        await using var context = new SeekSchedulingTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        var first = context.Source.BlockNextSeek(new(5));
        var final = context.Source.BlockNextSeek(new(10));
        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline").Focus());
        UiTestActions.Press(window, Key.Right);
        await WaitAsync(first.Entered);
        AssertDisplayedTarget(window, new(5));
        var obsolete = context.Controller.SeekAsync(first.Target);
        UiTestActions.Press(window, Key.Right);
        AssertDisplayedTarget(window, new(10));
        var current = context.Controller.SeekAsync(final.Target);

        first.Release();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WaitAsync(obsolete));
        await WaitAsync(final.Entered);
        await FlushUiAsync();
        AssertDisplayedTarget(window, new(10));
        final.Release();
        await WaitAsync(current);
        await FlushUiAsync();
        Assert.Equal(new MediaTime(10), context.Controller.Snapshot.Position);
        AssertDisplayedTarget(window, new(10));
        Assert.Single(context.Source.SeekTargets, target => target == new MediaTime(10));
    }

    [AvaloniaFact]
    public async Task EditingStyleDuringPendingSeekKeepsRequestedPositionAndPersistedStyle()
    {
        await using var context = new SeekSchedulingTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        var addSubtitle = Assert.IsAssignableFrom<IAsyncRelayCommand>(window.GetCommand(WorkbenchCommand.ADD_SUBTITLE));
        Assert.True(addSubtitle.CanExecute(null));
        await WaitAsync(addSubtitle.ExecuteAsync(null));
        await WaitAsync(context.Controller.SeekAsync(MediaTime.Zero));
        Assert.False(Assert.Single(window.DocumentSnapshot.Subtitles).Style.Bold);
        var request = context.Source.BlockNextSeek(new(5));
        UiTestActions.Find<Slider>(window, "PositionSlider").Value = 5;
        await WaitAsync(request.Entered);
        var pending = context.Controller.SeekAsync(request.Target);
        AssertDisplayedTarget(window, new(5));
        Assert.Equal(MediaTime.Zero, context.Controller.Snapshot.Position);

        UiTestActions.Find<ToolbarToggleButton>(window, "BoldCheck").IsChecked = true;
        Assert.True(Assert.Single(window.DocumentSnapshot.Subtitles).Style.Bold);
        AssertDisplayedTarget(window, new(5));
        Assert.False(pending.IsCompleted);
        request.Release();
        await WaitAsync(pending);
        await FlushUiAsync();
        Assert.Equal(new MediaTime(5), context.Controller.Snapshot.Position);
        AssertDisplayedTarget(window, new(5));
        Assert.True(Assert.Single(window.DocumentSnapshot.Subtitles).Style.Bold);
        Assert.Single(context.Source.SeekTargets, target => target == new MediaTime(5));
        Assert.Equal(new MediaTime(5), context.Source.SeekTargets.Last());
    }

    private static Point TimelinePoint(MainWindow window, SubtitleTimelineControl timeline, double seconds)
    {
        return timeline.TranslatePoint(new(timeline.HeaderWidth + (seconds - timeline.ViewStart) * timeline.PixelsPerSecond,
            timeline.RulerHeight / 2), window)
            ?? throw new InvalidOperationException("Timeline is not attached to the test window.");
    }

    private static void AssertDisplayedTarget(MainWindow window, MediaTime target)
    {
        Assert.Equal(target, UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline").Position);
        Assert.Equal((double)target.Numerator / target.Denominator, UiTestActions.Find<Slider>(window, "PositionSlider").Value);
    }

    private static async Task WaitAsync(Task task)
    {
        await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static async Task FlushUiAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, TestContext.Current.CancellationToken);
    }
}
