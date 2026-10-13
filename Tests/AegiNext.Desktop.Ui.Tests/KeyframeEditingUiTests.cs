using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KeyframeEditingUiTests
{
    [AvaloniaFact]
    public async Task AddingAtAnotherPlayheadTimeCreatesAnotherFrameAndUndoRemovesOnlyThatFrame()
    {
        await using var context = new MainWindowTestContext();
        await PrepareSubtitleAsync(context);
        var window = context.Window;
        SetSelectedValue(window, 0.25m);
        await context.Session.SeekFromUserAsync(new(1, 3));
        await AddKeyframeAsync(window, AnimationProperty.OPACITY);
        SetInterpolation(window, AnimationProperty.OPACITY, KeyframeInterpolation.EASE_OUT);
        var first = Assert.Single(OpacityTrack(window.DocumentSnapshot).Keyframes);
        Assert.Equal(new MediaTime(1, 3), first.Time);

        await context.Session.SeekFromUserAsync(new(4, 3));
        await AddKeyframeAsync(window, AnimationProperty.OPACITY);
        var frames = OpacityTrack(window.DocumentSnapshot).Keyframes;
        Assert.Equal(2, frames.Length);
        Assert.Equal(first, frames[0]);
        Assert.Equal(new MediaTime(4, 3), frames[1].Time);
        Assert.Equal(0.25, frames[1].Value.Scalar);
        Assert.Equal(KeyframeInterpolation.LINEAR, frames[1].Interpolation);

        Execute(window, WorkbenchCommand.UNDO);
        Assert.Equal(first, Assert.Single(OpacityTrack(window.DocumentSnapshot).Keyframes));
        Execute(window, WorkbenchCommand.REDO);
        Assert.Equal(frames.ToArray(), OpacityTrack(window.DocumentSnapshot).Keyframes.ToArray());
    }

    [AvaloniaFact]
    public async Task SelectingTimelineFrameLoadsAndEditsItsValueAndInterpolationWithUndoAndPersistence()
    {
        await using var context = new MainWindowTestContext();
        await PrepareSubtitleAsync(context);
        var window = context.Window;
        var valueInput = UiTestActions.PropertyInput(window, AnimationProperty.OPACITY);
        SetSelectedValue(window, 0.25m);
        await context.Session.SeekFromUserAsync(new(1));
        await AddKeyframeAsync(window, AnimationProperty.OPACITY);
        SetInterpolation(window, AnimationProperty.OPACITY, KeyframeInterpolation.EASE_OUT);
        await context.Session.SeekFromUserAsync(new(2));
        await AddKeyframeAsync(window, AnimationProperty.OPACITY);
        SetSelectedValue(window, 0.75m);
        SetInterpolation(window, AnimationProperty.OPACITY, KeyframeInterpolation.HOLD);
        var second = OpacityTrack(window.DocumentSnapshot).Keyframes[1];
        Assert.Equal(new MediaTime(2), second.Time);
        Assert.Equal(0.75, second.Value.Scalar);
        Assert.Equal(KeyframeInterpolation.HOLD, second.Interpolation);
        Assert.Equal(0, second.CurveStart);
        Assert.Equal(1, second.CurveEnd);
        Assert.Equal(1, second.Exponent);
        Assert.False(second.Reverse);
        Assert.Empty(second.ComponentCurves);

        var beforeSelection = window.DocumentSnapshot;
        SelectOpacityFrame(window, new(1), 0.25);
        Assert.Same(beforeSelection, window.DocumentSnapshot);
        Assert.Equal(0.25m, valueInput.Value);
        AssertInterpolation(window, AnimationProperty.OPACITY, KeyframeInterpolation.EASE_OUT);
        SetSelectedValue(window, 0.5m);
        Assert.Equal(new Keyframe(new(1), 0.5, KeyframeInterpolation.EASE_OUT),
            OpacityTrack(window.DocumentSnapshot).Keyframes[0]);
        SetInterpolation(window, AnimationProperty.OPACITY, KeyframeInterpolation.LINEAR);
        Assert.Equal(new Keyframe(new(1), 0.5), OpacityTrack(window.DocumentSnapshot).Keyframes[0]);
        Assert.Equal(second, OpacityTrack(window.DocumentSnapshot).Keyframes[1]);

        Execute(window, WorkbenchCommand.UNDO);
        Assert.Equal(new Keyframe(new(1), 0.5, KeyframeInterpolation.EASE_OUT),
            OpacityTrack(window.DocumentSnapshot).Keyframes[0]);
        AssertInterpolation(window, AnimationProperty.OPACITY, KeyframeInterpolation.EASE_OUT);
        Execute(window, WorkbenchCommand.UNDO);
        Assert.Equal(new Keyframe(new(1), 0.25, KeyframeInterpolation.EASE_OUT),
            OpacityTrack(window.DocumentSnapshot).Keyframes[0]);
        Assert.Equal(0.25m, valueInput.Value);
        Execute(window, WorkbenchCommand.REDO);
        Execute(window, WorkbenchCommand.REDO);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(window.DocumentSnapshot));
        Assert.Equal(new Keyframe(new(1), 0.5), OpacityTrack(restored).Keyframes[0]);
        Assert.Equal(second, OpacityTrack(restored).Keyframes[1]);
    }

    [AvaloniaFact]
    public async Task ChangingPropertyKeepsSelectedTimeAndEditsOnlyTheNewProperty()
    {
        await using var context = new MainWindowTestContext();
        await PrepareSubtitleAsync(context);
        var window = context.Window;
        SetSelectedValue(window, 0.25m);
        await context.Session.SeekFromUserAsync(new(1));
        await AddKeyframeAsync(window, AnimationProperty.OPACITY);
        var before = window.DocumentSnapshot;
        UiTestActions.SelectAnimationProperty(window, AnimationProperty.POSITION);
        Assert.False(UiTestActions.Find<ComboBox>(window, "InterpolationCombo").IsEffectivelyEnabled);
        var positionInput = UiTestActions.PropertyInput(window, AnimationProperty.POSITION);
        var absoluteX = positionInput.Value!.Value - (decimal)Assert.Single(before.Layers).Transform.Position.X + 120;
        SetSelectedValue(window, absoluteX, AnimationProperty.POSITION);
        Assert.NotSame(before, window.DocumentSnapshot);

        await AddKeyframeAsync(window, AnimationProperty.POSITION);
        SetInterpolation(window, AnimationProperty.POSITION, KeyframeInterpolation.HOLD);
        var tracks = Assert.Single(window.DocumentSnapshot.Layers).Tracks;
        Assert.Equal(2, tracks.Length);
        Assert.Equal(0.25, Assert.Single(OpacityTrack(window.DocumentSnapshot).Keyframes).Value.Scalar);
        var position = Assert.Single(tracks.Single(track => track.Property == AnimationProperty.POSITION).Keyframes);
        Assert.Equal(new MediaTime(1), position.Time);
        Assert.Equal(120, position.Value.Vector.X, 10);
        Assert.Equal(0, position.Value.Vector.Y, 10);
        Assert.Equal(KeyframeInterpolation.HOLD, position.GetCurve(0).Interpolation);
        Assert.Equal(KeyframeInterpolation.HOLD, position.GetCurve(1).Interpolation);
    }

    private static async Task PrepareSubtitleAsync(MainWindowTestContext context)
    {
        await context.OpenMediaAsync();
        context.Window.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        await UiTestActions.CreateSubtitleAsync(context);
        UiTestActions.SelectAnimationProperty(context.Window, AnimationProperty.OPACITY);
        context.Window.UpdateLayout();
    }

    private static AnimationTrack OpacityTrack(ProjectDocument document)
    {
        return Assert.Single(document.Layers).Tracks.Single(track => track.Property == AnimationProperty.OPACITY);
    }

    private static void SetSelectedValue(MainWindow window, decimal value, AnimationProperty property = AnimationProperty.OPACITY)
    {
        var input = UiTestActions.PropertyInput(window, property);
        input.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(textBox.Focus());
        textBox.Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Dispatcher.UIThread.RunJobs();
        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline").Focus());
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task AddKeyframeAsync(MainWindow window, AnimationProperty property)
    {
        var button = UiTestActions.PropertyKeyframeButton(window, property);
        UiTestActions.Click(window, button);
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command).ExecutionTask!;
        Dispatcher.UIThread.RunJobs();
    }

    private static void SetInterpolation(MainWindow window, AnimationProperty property, KeyframeInterpolation value)
    {
        var anchor = UiTestActions.OpenAnimationOptions(window, property);
        var input = UiTestActions.Find<ComboBox>(window, "InterpolationCombo");
        Assert.True(input.IsEffectivelyEnabled);
        input.SelectedIndex = (int)value;
        Dispatcher.UIThread.RunJobs();
        anchor.Flyout!.Hide();
    }

    private static void AssertInterpolation(MainWindow window, AnimationProperty property, KeyframeInterpolation value)
    {
        var anchor = UiTestActions.OpenAnimationOptions(window, property);
        Assert.Equal((int)value, UiTestActions.Find<ComboBox>(window, "InterpolationCombo").SelectedIndex);
        anchor.Flyout!.Hide();
    }

    private static void SelectOpacityFrame(MainWindow window, MediaTime time, double value)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline");
        var layer = Assert.Single(window.DocumentSnapshot.Layers);
        var keyPoint = timeline.GetKeyframePoint(layer.Id, time, value)
            ?? throw new InvalidOperationException("The selected clip's animation is not expanded.");
        var point = timeline.TranslatePoint(keyPoint, window)
            ?? throw new InvalidOperationException("Timeline is not attached to the test window.");
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Execute(MainWindow window, WorkbenchCommand command)
    {
        var action = window.GetCommand(command);
        Assert.True(action.CanExecute(null));
        action.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }
}
