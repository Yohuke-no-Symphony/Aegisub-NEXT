using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class UnifiedAnimationEditingUiTests
{
    [AvaloniaFact]
    public async Task InspectorKeyboardInputCommitsBeforeSelectingAnotherFrameAndOtherPropertyUsesSameTime()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(1), 0.25));
        session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(2), 0.75));
        session.SelectKeyframe(new(id, AnimationProperty.OPACITY, new(1), new(1)));
        UiTestActions.ExpandEffectsCategory(context.Window, "CompositeCategory");
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "OpacityInput");
        input.BringIntoView();
        context.Window.UpdateLayout();
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(text.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("0.4");
        session.SelectKeyframe(new(id, AnimationProperty.OPACITY, new(2), new(2)));
        Dispatcher.UIThread.RunJobs();
        var layer = session.SelectedLayer!;
        Assert.Equal(0.4, layer.Tracks.Single(track => track.Property == AnimationProperty.OPACITY).Keyframes[0].Value.Scalar);
        Assert.Equal(new MediaTime(2), session.SelectedKeyTime);
        Assert.Equal(0.75m, context.ViewModel.Effects.Opacity);
        Assert.Equal(1, layer.Opacity);
        context.ViewModel.Effects.RotationText = "45";
        Assert.True(session.TryCommitDrafts());
        layer = session.SelectedLayer!;
        Assert.Equal(new Keyframe(new(2), AnimationValue.FromVector3(new(0, 0, 45))),
            Assert.Single(layer.Tracks.Single(track => track.Property == AnimationProperty.ROTATION_3D).Keyframes));
        Assert.Equal(0, layer.Transform.Rotation);
        session.Editor.Undo();
        Assert.DoesNotContain(session.SelectedLayer!.Tracks, track => track.Property == AnimationProperty.ROTATION_3D);
    }

    [AvaloniaFact]
    public async Task ChangingBlendDuringPlaybackDoesNotWriteUntouchedAnimationFields()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.2));
        session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(5), 0.8));
        var original = session.SelectedLayer!.Tracks;
        await context.Controller.PlayAsync();
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        session.Tick();
        context.ViewModel.Effects.Blend = (int)BlendMode.SCREEN;
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        await context.Controller.PauseAsync();
        Assert.True(session.TryCommitDrafts());
        Assert.Equal(BlendMode.SCREEN, session.SelectedLayer!.Blend);
        Assert.Equal(original, session.SelectedLayer.Tracks);
    }

    [AvaloniaFact]
    public async Task InvalidRawDraftPreventsFrameSelectionAndVideoGestureAndRemainsVisible()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(1), 0.25));
        session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(2), 0.75));
        session.SelectKeyframe(new(id, AnimationProperty.OPACITY, new(1), new(1)));
        var before = session.DocumentSnapshot;
        context.ViewModel.Effects.OpacityText = "unfinished";
        session.SelectKeyframe(new(id, AnimationProperty.OPACITY, new(2), new(2)));
        Assert.Equal(new MediaTime(1), session.SelectedKeyTime);
        Assert.Equal("unfinished", context.ViewModel.Effects.OpacityText);
        Assert.Same(before, session.DocumentSnapshot);
        Assert.False(session.BeginCanvasGesture());
        context.ViewModel.Effects.OpacityText = "0.25";
        Assert.True(session.TryCommitDrafts());
    }

    [AvaloniaFact]
    public async Task VideoGestureUpdatesSelectedAnimatedPositionInOneUndoAndDoesNotChangeBase()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var mask = new RectangleClipMask { TopLeft = new(100, 100), BottomRight = new(500, 400) };
        session.Editor.SetClipMask(session.SelectedLayer!.Id, mask);
        var layer = session.SelectedLayer!;
        session.Editor.SetKeyframe(layer.Id, AnimationProperty.POSITION, new(new(1), new ScenePoint(100, 0)));
        session.SelectKeyframe(new(layer.Id, AnimationProperty.POSITION, new(1), new(1)));
        var before = session.DocumentSnapshot;
        Assert.True(session.BeginCanvasGesture());
        await session.CommitCanvasAsync(new(layer.Id, layer.Transform with { X = layer.Transform.X + 60, Y = layer.Transform.Y + 20 }, layer.MotionPath));
        var edited = session.SelectedLayer!;
        Assert.Same(mask, edited.Mask);
        Assert.Equal(new ScenePoint(160, layer.Transform.Y + 20),
            Assert.Single(edited.Tracks.Single(track => track.Property == AnimationProperty.POSITION).Keyframes).Value.Vector);
        Assert.Equal(layer.Transform, edited.Transform);
        session.Editor.Undo();
        Assert.Same(before, session.DocumentSnapshot);
    }
}
