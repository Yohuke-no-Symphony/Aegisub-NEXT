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
    [InlineData(LayerKind.SUBTITLE)]
    public async Task SpatialPivotPresetPreservesOnlyTheCurrentPoseAndKeepsEveryAnimationTrack(LayerKind kind)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/pivot.png");
        var subtitle = new SubtitleLine
        {
            Text = "AB", End = new(4), Style = new()
            {
                FontFamily = "sans-serif", Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0.1, 0.8), Offset = new(20, -30) }
            }
        };
        var layer = new ProjectLayer
        {
            Kind = kind, End = new(4), Transform = new(10, 20, 1.3, 0.8, 35)
            {
                RotationX = 10, RotationY = -20, PerspectiveDistance = 500, ProjectionTranslation = new(2, 3, 5)
            },
            Position = kind == LayerKind.SUBTITLE ? null : new() { Anchor = new(0.25, 0.75), Pivot = new(0.1, 0.8), Offset = new(20, -30) },
            SubtitleId = kind == LayerKind.SUBTITLE ? subtitle.Id : null,
            Image = kind == LayerKind.IMAGE ? new(asset.Id, 200, 100) : null,
            Shape = kind == LayerKind.SHAPE ? new(ShapeKind.RECTANGLE, 200, 100) : null,
            Tracks =
            [
                new(AnimationProperty.ROTATION_X, [new(new(0), 10d), new(new(2), 50d)]),
                new(AnimationProperty.ROTATION_Y, [new(new(0), -20d), new(new(2), 10d)]),
                new(AnimationProperty.SCALE, [new(new(1), new ScenePoint(1.75, 0.65))]),
                new(AnimationProperty.OPACITY, [new(new(0), 0.8d)])
            ]
        };
        var source = new ProjectDocument
        {
            Width = 1000, Height = 600, Assets = kind == LayerKind.IMAGE ? [asset] : [],
            Subtitles = kind == LayerKind.SUBTITLE ? [subtitle] : [], Layers = [layer]
        };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(layer.Id, [layer.Id]);
        Assert.True(session.SelectKeyframe(new(layer.Id, AnimationProperty.SCALE, new(1), new(1))));
        var draft = kind == LayerKind.SUBTITLE ? session.ViewModel.Styles.Subtitle.Position :
            Assert.IsAssignableFrom<GraphicStyleEditorViewModel>(session.ViewModel.Styles.ActiveEditor).Position;
        Assert.Equal(30, draft.Geometry!.Transform.RotationX, 8);
        Assert.Equal(-5, draft.Geometry.Transform.RotationY, 8);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(context.DirectoryPath));
        var before = renderer.GetLayerGeometry(source, new(1), layer.Id)!;
        var otherFrameBefore = renderer.GetLayerGeometry(source, new(0), layer.Id)!;

        Assert.True(draft.SelectPreset(new(0.5, 0.5), setPivot: true));
        Assert.True(session.TryCommitDrafts(false));

        var changed = context.Editor.Snapshot.Layers[0];
        Assert.Equal(layer.Tracks, changed.Tracks);
        Assert.Equal(layer.Transform.Position, changed.Transform.Position);
        Assert.Equal(layer.Transform.RotationX, changed.Transform.RotationX);
        Assert.Equal(layer.Transform.RotationY, changed.Transform.RotationY);
        Assert.NotEqual(layer.Transform.ProjectionTranslation, changed.Transform.ProjectionTranslation);
        var after = renderer.GetLayerGeometry(context.Editor.Snapshot, new(1), layer.Id)!;
        Assert.Equal(before.BasePosition, after.BasePosition);
        Assert.Equal(before.LocalBounds, after.LocalBounds);
        AssertCornersEqual(before, after);
        var otherFrameAfter = renderer.GetLayerGeometry(context.Editor.Snapshot, new(0), layer.Id)!;
        Assert.Contains(otherFrameBefore.WorldCorners.Zip(otherFrameAfter.WorldCorners),
            pair => Math.Abs(pair.First.X - pair.Second.X) > 0.01 || Math.Abs(pair.First.Y - pair.Second.Y) > 0.01);
        var reloaded = AegiNext.Application.ProjectStore.Deserialize(AegiNext.Application.ProjectStore.Serialize(context.Editor.Snapshot));
        Assert.Equal(changed.Transform.ProjectionTranslation, reloaded.Layers[0].Transform.ProjectionTranslation);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

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
        SkiaSharp.SKPoint[] localCorners =
        [
            new(before.LocalBounds.Left, before.LocalBounds.Top),
            new(before.LocalBounds.Right, before.LocalBounds.Top),
            new(before.LocalBounds.Right, before.LocalBounds.Bottom),
            new(before.LocalBounds.Left, before.LocalBounds.Bottom)
        ];
        foreach (var point in localCorners)
        {
            Assert.True(before.TryMapLocalPoint(point, out var previous));
            Assert.True(after.TryMapLocalPoint(point, out var changed));
            Assert.InRange(Math.Abs(previous.X - changed.X), 0, 0.001);
            Assert.InRange(Math.Abs(previous.Y - changed.Y), 0, 0.001);
        }
    }
}
