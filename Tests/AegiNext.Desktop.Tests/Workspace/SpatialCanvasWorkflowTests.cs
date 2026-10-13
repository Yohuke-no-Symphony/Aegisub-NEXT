using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SpatialCanvasWorkflowTests
{
    [Theory]
    [InlineData(AnimationProperty.ROTATION_X, 55)]
    [InlineData(AnimationProperty.ROTATION_Y, -65)]
    [InlineData(AnimationProperty.PERSPECTIVE_DISTANCE, 640)]
    public async Task SpatialRangeGestureCommitsOnlyItsFrozenNormalTargetAndUndoesOnce(AnimationProperty property, double value)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var other = new SubtitleAnimationRange(Guid.NewGuid(), 2, 2) { RotationX = 12 };
        var line = new SubtitleLine { Text = "ABCD", End = new(4), AnimationRanges = [range, other] };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Transform = new() { RotationX = 20 } };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        session.ViewModel.Effects.Target = new(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE);
        Assert.True(session.SetGraphicCanvasMode(CanvasEditMode.SPATIAL_TRANSFORM));
        var target = new AnimationTrackTarget(property == AnimationProperty.PERSPECTIVE_DISTANCE ? property :
            AnimationProperty.ROTATION_3D, TextRangeId: range.Id);
        var editValue = property == AnimationProperty.PERSPECTIVE_DISTANCE ? AnimationValue.FromScalar(value) :
            AnimationValue.FromVector3(property == AnimationProperty.ROTATION_X ? new(value, 0, 0) : new(0, value, 0));

        Assert.True(session.BeginSpatialGesture(new(layer.Id, target)));
        Assert.Equal(target, session.SceneEditing.GestureTarget!.Target);
        Assert.Same(source, context.Editor.Snapshot);
        await session.CommitSpatialAsync(new(layer.Id, target, editValue));

        Assert.Null(session.LastError);
        var changed = context.Editor.Snapshot;
        var rangeValue = AegiNext.Core.Editing.SubtitleAnimationEvaluation.GetBaseValue(changed.Layers[0], changed.Subtitles[0], target);
        Assert.Equal(editValue, rangeValue);
        Assert.Same(other, changed.Subtitles[0].AnimationRanges[1]);
        Assert.Equal(layer.Transform, changed.Layers[0].Transform);
        Assert.Empty(changed.Layers[0].Tracks);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SpatialKeyframeGestureRetainsClockCurveAndOtherAxis()
    {
        var frame = new Keyframe(new(1), new SceneVector3(45, 80, 10), KeyframeInterpolation.POWER)
        {
            Exponent = 2.5, Reverse = true,
            ComponentCurves = [new(KeyframeInterpolation.EASE_OUT), new(KeyframeInterpolation.HOLD)]
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 50), Start = new(5), End = new(9),
            Tracks = [new(AnimationProperty.ROTATION_3D, [frame])]
        };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.ROTATION_3D, frame.Time, new(6))));
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION_3D);
        var request = new CanvasSpatialGestureStartingEventArgs(layer.Id, target);

        Assert.True(session.BeginSpatialGesture(request));
        Assert.Equal(frame.Time, request.LocalTime);
        Assert.True(request.IsKeyframe);
        var editedValue = new SceneVector3(765, 80, 10);
        await session.CommitSpatialAsync(new(layer.Id, target, editedValue));

        Assert.Equal(frame with { Value = editedValue }, Assert.Single(Assert.Single(session.SelectedLayer!.Tracks).Keyframes));
        Assert.Equal(layer.Transform, session.SelectedLayer.Transform);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
    }

    [Fact]
    public async Task CancelledAndStaleSpatialGesturesCannotPublishLateResults()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 50) };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION_3D);

        Assert.True(session.BeginSpatialGesture(new(layer.Id, target)));
        session.CancelCanvasGesture();
        await session.CommitSpatialAsync(new(layer.Id, target, new SceneVector3(0, 65, 0)));
        Assert.Same(source, context.Editor.Snapshot);
        Assert.True(session.BeginSpatialGesture(new(layer.Id, target)));
        var replacement = source with { Name = "New snapshot" };
        context.Editor.Reset(replacement);
        await session.CommitSpatialAsync(new(layer.Id, target, new SceneVector3(0, 75, 0)));

        Assert.Same(replacement, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task PositionToolStillEditsTheWholeLayerWhenAnAppearanceRangeIsSelected()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { RotationY = 40 };
        var line = new SubtitleLine { Text = "AB", End = new(4), AnimationRanges = [range] };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Transform = new() { RotationX = 30 } };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        session.ViewModel.Effects.Target = new(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE);

        Assert.True(session.BeginCanvasGesture());
        await session.CommitCanvasAsync(new(layer.Id, layer.Transform with { X = 25, Y = 30 }, null));

        Assert.Equal(new ScenePoint(25, 30), session.SelectedLayer!.Transform.Position);
        Assert.Same(line, session.SelectedCue);
        Assert.Equal(30, session.SelectedLayer.Transform.RotationX);
    }
}
