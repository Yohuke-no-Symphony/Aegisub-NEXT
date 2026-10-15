using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineEffectClipDeleteUiTests
{
    [AvaloniaTheory]
    [InlineData("block")]
    [InlineData("key")]
    [InlineData("operation")]
    public async Task PointerSelectionAndDeleteRemoveOnlyTheExactTargetAndRepeatedDeletePreservesTheParent(string selection)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        Assert.True(await context.Window.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.EFFECTS));
        var firstNode = new MaskNode { Position = new(10, 20) };
        var secondNode = new MaskNode { Position = new(30, 40) };
        var target = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, firstNode.Id);
        var otherTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, secondNode.Id);
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(0), new(2), new ScenePoint(50, 60));
        var track = selection == "operation"
            ? new AnimationTrack(target, []) { InitialValue = firstNode.Position, Transforms = [operation] }
            : new AnimationTrack(target, [new(new(0), firstNode.Position), new(new(2), new ScenePoint(50, 60))]);
        var cue = new SubtitleLine { End = new(4), Text = "Target" };
        var layer = new ProjectLayer
        {
            Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
            Mask = new VectorClipMask { Contours = [new() { Nodes = [firstNode, secondNode] }] },
            Tracks = [track, new(otherTarget, [new(new(1), secondNode.Position)])]
        };
        var nextCue = new SubtitleLine { Start = new(5), End = new(9), Text = "Other clip" };
        var nextLayer = layer with { Id = nextCue.Id, SubtitleId = nextCue.Id, Start = nextCue.Start, End = nextCue.End };
        var document = new ProjectDocument { Subtitles = [cue, nextCue], Layers = [layer, nextLayer] };
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(nextLayer.Id, [nextLayer.Id]);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.IsSnapEnabled = false;
        context.ViewModel.Timeline.IsStepEnabled = false;
        context.ViewModel.Timeline.PixelsPerSecond = 60;
        context.ViewModel.Timeline.ViewStart = 0;
        Flush(context.Window);
        var id = new TimelineEffectClipId(layer.Id, target);
        var rectangle = timeline.GetEffectClipRectangle(id)!.Value;
        var local = selection == "block"
            ? new Point(rectangle.Left + 35, rectangle.Top + 10)
            : selection == "key"
                ? timeline.GetKeyframePoint(layer.Id, target, new(2), new ScenePoint(50, 60))!.Value
                : timeline.KeyframeMarkers.Single(marker => marker.Identity.LayerId == layer.Id &&
                    marker.Identity.OperationId == operation.Id && !marker.Identity.IsOperationStart).Position;
        var point = timeline.TranslatePoint(local, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Flush(context.Window);
        Assert.Equal(id, context.ViewModel.Timeline.SelectedEffectClipId);
        Assert.Equal(layer.Id, context.Session.SelectedLayerId);
        Assert.Equal(target, context.Session.SceneEditing.Target);
        Assert.True(timeline.Focus());

        UiTestActions.Press(context.Window, Key.Delete);
        await context.Session.WaitForProjectIdleAsync();
        Flush(context.Window);

        var changed = context.Session.DocumentSnapshot;
        Assert.Equal(document.Subtitles, changed.Subtitles);
        Assert.Same(nextLayer, changed.Layers[1]);
        Assert.Same(layer.Mask, changed.Layers[0].Mask);
        Assert.Same(layer.Tracks[1], changed.Layers[0].Tracks.Single(value => value.Target == otherTarget));
        var remaining = changed.Layers[0].Tracks.SingleOrDefault(value => value.Target == target);
        if (selection == "key")
        {
            Assert.NotNull(remaining);
            Assert.Equal(MediaTime.Zero, Assert.Single(remaining.Keyframes).Time);
        }
        else
        {
            Assert.Null(remaining);
        }

        UiTestActions.Press(context.Window, Key.Delete);
        await context.Session.WaitForProjectIdleAsync();
        Flush(context.Window);
        Assert.Same(changed, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(changed, context.Session.DocumentSnapshot);
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
