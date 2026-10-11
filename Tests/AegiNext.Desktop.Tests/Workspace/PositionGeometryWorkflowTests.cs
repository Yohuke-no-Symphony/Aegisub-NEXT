using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Panels.Styles;
using AegiNext.Rendering.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class PositionGeometryWorkflowTests
{
    [Theory]
    [InlineData(LayerKind.IMAGE)]
    [InlineData(LayerKind.SHAPE)]
    public async Task GraphicPivotPresetPreservesCornersWhenThePathControlsRotation(LayerKind kind)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/pivot.png");
        var layer = new ProjectLayer
        {
            Kind = kind, End = new(4), Transform = new(10, 20, 2, 3, 0),
            Image = kind == LayerKind.IMAGE ? new(asset.Id, 200, 100) : null,
            Shape = kind == LayerKind.SHAPE ? new(ShapeKind.RECTANGLE, 200, 100) : null,
            MotionPath = VerticalPath(),
            Tracks = [new(AnimationProperty.ROTATION, [new(new(1), 30d)])]
        };
        var document = new ProjectDocument
        {
            Width = 1000, Height = 600, Assets = kind == LayerKind.IMAGE ? [asset] : [], Layers = [layer]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.ROTATION, new(1), new(1))));
        var model = Assert.IsAssignableFrom<GraphicStyleEditorViewModel>(session.ViewModel.Styles.ActiveEditor);
        Assert.Equal(120, model.Position.Geometry!.Transform.Rotation, 8);
        Assert.Equal(SceneEvaluator.EvaluateLayer(layer, null, new(1)).Transform, model.Position.Geometry.Transform);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.DirectoryPath));
        var before = renderer.GetLayerGeometry(document, new(1), layer.Id)!;

        Assert.True(model.Position.SelectPreset(new(0.5, 0.5), setPivot: true));
        Assert.True(session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot.Layers[0];
        Assert.Same(layer.MotionPath, changed.MotionPath);
        Assert.Equal(layer.Tracks, changed.Tracks);
        Assert.Equal(layer.Transform, changed.Transform);
        var after = renderer.GetLayerGeometry(context.Editor.Snapshot, new(1), layer.Id)!;
        AssertCornersEqual(before, after);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SubtitlePositionUsesWholeLayerTransformWhenATextRangeIsSelected()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { Scale = new(5, 6), Rotation = 170 };
        var line = new SubtitleLine
        {
            Text = "AB", Start = MediaTime.Zero, End = new(4), AnimationRanges = [range],
            Style = new() { FontFamily = "sans-serif", Position = new() { Anchor = new(0, 0), Pivot = new(0, 0) } }
        };
        var rangeTarget = new AnimationTrackTarget(AnimationProperty.SCALE, TextRangeId: range.Id);
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End, Transform = new(10, 20, 2, 3, 30),
            MotionPath = VerticalPath(),
            Tracks = [new(rangeTarget, [new(new(1), new ScenePoint(8, 9))])]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectCue(line.Id);
        Assert.True(session.SelectKeyframe(new(layer.Id, rangeTarget, new(1), new(1))));
        var draft = session.ViewModel.Styles.Subtitle.Position;
        Assert.Equal(new ScenePoint(2, 3), draft.Geometry!.Transform.Scale);
        Assert.Equal(120, draft.Geometry.Transform.Rotation, 8);
        Assert.Equal(SceneEvaluator.EvaluateLayer(layer, line, new(1)).Transform, draft.Geometry.Transform);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.DirectoryPath));
        var before = renderer.GetLayerGeometry(document, new(1), layer.Id)!;

        Assert.True(draft.SelectPreset(new(0.5, 0.5), setPivot: true));
        Assert.True(session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot;
        Assert.Equal(layer.Tracks, changed.Layers[0].Tracks);
        Assert.Equal(line.AnimationRanges, changed.Subtitles[0].AnimationRanges);
        AssertCornersEqual(before, renderer.GetLayerGeometry(changed, new(1), layer.Id)!);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
    }

    private static MotionPath VerticalPath() => new(new(new(0, 0), [new(new(0, 40), new(0, 80), new(0, 120))]), new(4), true);

    private static void AssertCornersEqual(ProjectLayerGeometry before, ProjectLayerGeometry after)
    {
        for (var index = 0; index < before.WorldCorners.Count; index++)
        {
            Assert.InRange(Math.Abs(before.WorldCorners[index].X - after.WorldCorners[index].X), 0, 0.001);
            Assert.InRange(Math.Abs(before.WorldCorners[index].Y - after.WorldCorners[index].Y), 0, 0.001);
        }
    }
}
