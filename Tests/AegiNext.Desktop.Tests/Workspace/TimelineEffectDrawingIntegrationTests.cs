using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Timeline;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineEffectDrawingIntegrationTests
{
    [Theory]
    [InlineData(false, "CROP", false)]
    [InlineData(true, "CROP", false)]
    [InlineData(false, "SHAPE_PATH", false)]
    [InlineData(true, "SHAPE_PATH", false)]
    [InlineData(false, "CROP", true)]
    [InlineData(true, "CROP", true)]
    [InlineData(false, "SHAPE_PATH", true)]
    [InlineData(true, "SHAPE_PATH", true)]
    public async Task AcceptedSelectionOnlyResetsGraphicModesThatDoNotApplyToTheNewParent(bool keyframe,
        string modeName, bool sameParent)
    {
        var mode = Enum.Parse<CanvasEditMode>(modeName);
        var source = Document();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        var original = source.Layers[mode == CanvasEditMode.CROP ? 0 : 1];
        var target = sameParent ? original : source.Layers[mode == CanvasEditMode.CROP ? 1 : 0];
        session.SelectLayer(original.Id, [original.Id]);
        Assert.True(session.SetGraphicCanvasMode(mode));

        Assert.True(Select(session.ViewModel.Timeline, target.Id, keyframe));

        Assert.Equal(target.Id, session.SelectedLayerId);
        Assert.Equal(sameParent ? mode : CanvasEditMode.POSITION, session.SceneEditing.Mode);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedSelectionPreservesInvalidImageDraftAndItsGraphicMode(bool keyframe)
    {
        var source = Document();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        var image = source.Layers[0];
        session.SelectLayer(image.Id, [image.Id]);
        Assert.True(session.SetGraphicCanvasMode(CanvasEditMode.CROP));
        session.ViewModel.Styles.Image.PlaybackSpeed.RawText = "7e-";

        Assert.False(Select(session.ViewModel.Timeline, source.Layers[1].Id, keyframe));

        Assert.Equal(image.Id, session.SelectedLayerId);
        Assert.Equal(CanvasEditMode.CROP, session.SceneEditing.Mode);
        Assert.Equal("7e-", session.ViewModel.Styles.Image.PlaybackSpeed.RawText);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false, "CROP")]
    [InlineData(true, "CROP")]
    [InlineData(false, "SHAPE_PATH")]
    [InlineData(true, "SHAPE_PATH")]
    public async Task SameParentSelectionCancelsTheSessionGestureBeforeALateResult(bool keyframe, string modeName)
    {
        var mode = Enum.Parse<CanvasEditMode>(modeName);
        var source = Document();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        var layer = source.Layers[mode == CanvasEditMode.CROP ? 0 : 1];
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.SetGraphicCanvasMode(mode));
        Assert.True(session.Graphics.BeginGesture());
        Assert.NotNull(session.SceneEditing.GestureTarget);

        Assert.True(Select(session.ViewModel.Timeline, layer.Id, keyframe));
        session.Graphics.CommitGesture(new(layer.Id,
            layer.Image is { } image ? image with { Crop = new(0.1, 0.2, 0.5, 0.6) } : null,
            layer.Shape is { } shape ? ShapeGeometry.Resize(shape, 300, 200) : null));

        Assert.Null(session.SceneEditing.GestureTarget);
        Assert.Equal(mode, session.SceneEditing.Mode);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false, "CREATE_SHAPE")]
    [InlineData(true, "CREATE_SHAPE")]
    [InlineData(false, "CREATE_PATH")]
    [InlineData(true, "CREATE_PATH")]
    public async Task SameParentSelectionCancelsPendingCreationWhileKeepingTheCreationTool(bool keyframe,
        string modeName)
    {
        var mode = Enum.Parse<CanvasEditMode>(modeName);
        var source = Document();
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        var layer = source.Layers[0];
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.SetGraphicCanvasMode(mode));
        Assert.True(session.Graphics.BeginCreation());

        Assert.True(Select(session.ViewModel.Timeline, layer.Id, keyframe));
        var path = mode == CanvasEditMode.CREATE_PATH ? ShapeGeometry.CreatePath(new(ShapeKind.RECTANGLE, 40, 30)) : null;
        session.Graphics.CreateShape(new(path is null ? ShapeKind.RECTANGLE : ShapeKind.PATH, new(20, 30), 40, 30, path));

        Assert.Equal(mode, session.SceneEditing.Mode);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static bool Select(TimelinePanelViewModel model, Guid layerId, bool keyframe) => keyframe
        ? model.SelectKeyframe(new(layerId, AnimationProperty.OPACITY, new(1), new(1)))
        : model.SelectEffectClip(new(new(layerId, new(AnimationProperty.OPACITY))));

    private static ProjectDocument Document()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/fixture.png");
        var shape = new LayerShape(ShapeKind.RECTANGLE, 100, 60);
        return new()
        {
            Assets = [asset],
            Layers =
            [
                new()
                {
                    Kind = LayerKind.IMAGE, Image = new(asset.Id, 120, 80) { Playback = new() }, End = new(10),
                    Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])]
                },
                new()
                {
                    Kind = LayerKind.SHAPE, Shape = shape with { Kind = ShapeKind.PATH, Path = ShapeGeometry.CreatePath(shape) },
                    Start = new(12), End = new(22), Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])]
                }
            ]
        };
    }
}
