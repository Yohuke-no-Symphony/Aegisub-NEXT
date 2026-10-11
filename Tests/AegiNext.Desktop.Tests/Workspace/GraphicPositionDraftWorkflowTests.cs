using AegiNext.Core.Projects;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class GraphicPositionDraftWorkflowTests
{
    [Theory]
    [InlineData(LayerKind.IMAGE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task PositionPresetPreservesRotatedPlacementCommitsOnceAndRefreshesInspector(LayerKind kind)
    {
        var document = Document(kind);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var layer = document.Layers[0];
        session.SelectLayer(layer.Id, [layer.Id]);
        var model = Assert.IsAssignableFrom<GraphicStyleEditorViewModel>(session.ViewModel.Styles.ActiveEditor);
        Assert.True(model.Position.IsExplicit);
        Assert.Equal(new ScenePoint(200, 100), model.Position.Geometry!.GlyphSize);
        Assert.True(model.Position.SelectPreset(new(0.5, 0.5), setPivot: true));
        Assert.True(session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot.Layers[0];
        Assert.Equal(new ScenePoint(0.5, 0.5), changed.Position!.Anchor);
        Assert.Equal(new ScenePoint(0.5, 0.5), changed.Position.Pivot);
        Assert.Equal(-650, changed.Position.Offset.X, 10);
        Assert.Equal(-100, changed.Position.Offset.Y, 10);
        Assert.Equal(layer.Transform, changed.Transform);
        Assert.Equal(-140m, session.ViewModel.Effects.PositionX);
        Assert.Equal(220m, session.ViewModel.Effects.PositionY);
        using var resolver = new LayerPlacementResolver();
        Assert.Equal(new ScenePoint(-150, 200), resolver.Resolve(context.Editor.Snapshot, context.DirectoryPath, changed).BasePosition);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("0", model.Position.AnchorX.RawText);
    }

    [Theory]
    [InlineData(LayerKind.IMAGE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task InvalidPositionDraftBlocksSelectionAndEscapeRestoresOnlyItsField(LayerKind kind)
    {
        var document = Document(kind);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id]);
        var model = Assert.IsAssignableFrom<GraphicStyleEditorViewModel>(session.ViewModel.Styles.ActiveEditor);
        model.Position.PivotX.RawText = "1e-";
        model.Position.OffsetY.RawText = "45";
        Assert.False(session.TryCommitDrafts(false));
        Assert.Equal("PivotXInput", session.ViewModel.InvalidFieldKey);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal("1e-", model.Position.PivotX.RawText);
        Assert.True(model.RestoreField("PivotXInput"));
        Assert.Equal("0", model.Position.PivotX.RawText);
        Assert.Equal("45", model.Position.OffsetY.RawText);
        Assert.True(session.TryCommitDrafts(false));
        Assert.Equal(new ScenePoint(0, 45), context.Editor.Snapshot.Layers[0].Position!.Offset);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
    }

    [Theory]
    [InlineData(LayerKind.IMAGE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task SizeEditsUpdatePositionGeometryWithoutAddingUnusedPositionMetadata(LayerKind kind)
    {
        var document = Document(kind);
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(document.Layers[0].Id, [document.Layers[0].Id]);
        var model = Assert.IsAssignableFrom<GraphicStyleEditorViewModel>(session.ViewModel.Styles.ActiveEditor);
        if (model is ImageStyleViewModel image)
        {
            image.Width.RawText = "400";
        }
        else
        {
            ((ShapeStyleViewModel)model).Width.RawText = "400";
        }
        Assert.Equal(400, model.Position.Geometry!.GlyphSize.X);
        Assert.True(session.TryCommitDrafts(false));
        Assert.Null(context.Editor.Snapshot.Layers[0].Position);
        Assert.Equal(400, LayerPositionGeometry.GraphicSize(context.Editor.Snapshot.Layers[0]).X);
    }

    [Fact]
    public async Task PositionPresetUsesTheSelectedAnimationScaleAndRotationWithoutRewritingTracks()
    {
        var document = Document(LayerKind.SHAPE);
        var layer = document.Layers[0] with
        {
            Tracks =
            [
                new(AnimationProperty.SCALE, [new(new(0), new ScenePoint(4, 2))]),
                new(AnimationProperty.ROTATION, [new(new(0), 0d)])
            ]
        };
        document = document with { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.SCALE, new(0), new(0))));
        var position = session.ViewModel.Styles.Shape.Position;
        Assert.Equal(new ScenePoint(4, 2), position.Geometry!.Transform.Scale);
        Assert.Equal(0, position.Geometry.Transform.Rotation);
        Assert.True(position.SelectPreset(new(0.5, 0.5), setPivot: true));
        Assert.True(session.TryCommitDrafts(false));
        var changed = context.Editor.Snapshot.Layers[0];
        Assert.Equal(new ScenePoint(-100, -200), changed.Position!.Offset);
        Assert.Equal(layer.Tracks, changed.Tracks);
        Assert.Equal(layer.Transform, changed.Transform);
    }

    private static ProjectDocument Document(LayerKind kind)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/position.png");
        return new()
        {
            Width = 1000, Height = 600,
            Assets = kind == LayerKind.IMAGE ? [asset] : [],
            Layers = [new()
            {
                Kind = kind,
                Image = kind == LayerKind.IMAGE ? new(asset.Id, 200, 100) : null,
                Shape = kind == LayerKind.SHAPE ? new(ShapeKind.RECTANGLE, 200, 100) : null,
                Transform = new(10, 20, 2, 3, 90)
            }]
        };
    }
}
