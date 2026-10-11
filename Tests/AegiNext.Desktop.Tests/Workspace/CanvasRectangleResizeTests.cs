using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class CanvasRectangleResizeTests
{
    [Fact]
    public async Task ShapeDimensionsAndAnimatedPositionCommitTogetherWithoutChangingScaleTracks()
    {
        var shape = new LayerShape(ShapeKind.RECTANGLE, 100, 50) { CornerRadii = new(4, 8, 12, 16) };
        var position = new AnimationTrack(AnimationProperty.POSITION, [new(new(0), new ScenePoint(100, 50))]);
        var scale = new AnimationTrack(AnimationProperty.SCALE, [new(new(0), new ScenePoint(2, 3))]);
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = shape, Tracks = [position, scale] };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.BeginCanvasGesture());

        await session.CommitCanvasAsync(new(layer.Id, layer.Transform with { X = -20, Y = -30 }, layer.MotionPath,
            shape with { Width = 150, Height = 70 }));

        Assert.Null(session.LastError);
        var changed = session.SelectedLayer!;
        Assert.Equal(shape with { Width = 150, Height = 70 }, changed.Shape);
        Assert.Equal(layer.Transform, changed.Transform);
        Assert.Same(scale, changed.Tracks.Single(track => track.Property == AnimationProperty.SCALE));
        Assert.Equal(new ScenePoint(80, 20), Assert.Single(changed.Tracks.Single(track => track.Property == AnimationProperty.POSITION).Keyframes).Value.Vector);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CancelledOrStaleRectangleResizeCannotCommitItsDimensions()
    {
        var shape = new LayerShape(ShapeKind.RECTANGLE, 100, 50) { CornerRadii = ShapeCornerRadii.Uniform(8) };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = shape };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.BeginCanvasGesture());
        session.CancelCanvasGesture();
        await session.CommitCanvasAsync(new(layer.Id, layer.Transform, layer.MotionPath, shape with { Width = 150 }));
        Assert.Same(source, context.Editor.Snapshot);

        Assert.True(session.BeginCanvasGesture());
        var replacement = source with { Name = "Replacement" };
        context.Editor.Reset(replacement);
        await session.CommitCanvasAsync(new(layer.Id, layer.Transform, layer.MotionPath, shape with { Width = 150 }));
        Assert.Same(replacement, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }
}
