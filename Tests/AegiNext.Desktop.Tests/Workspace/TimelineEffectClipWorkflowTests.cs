using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Styles;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineEffectClipWorkflowTests
{
    [Fact]
    public async Task SelectingEffectSelectsItsParentAndTypedInspectorWithoutSeeking()
    {
        var first = Shape(0);
        var second = Shape(12);
        var source = new ProjectDocument { Layers = [first, second] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(first.Id, [first.Id]);
        var position = context.Session.ProjectPosition;
        var effect = new TimelineEffectClipId(second.Id, new(AnimationProperty.OPACITY));

        Assert.True(context.Session.ViewModel.Timeline.SelectEffectClip(new(effect)));

        Assert.Equal(second.Id, context.Session.SelectedLayerId);
        Assert.Equal(second.Id, Assert.Single(context.Session.ViewModel.Timeline.SelectedLayerIds));
        Assert.Equal(effect.Target, context.Session.SceneEditing.Target);
        Assert.Equal(effect, context.Session.ViewModel.Timeline.SelectedEffectClipId);
        Assert.IsType<ShapeStyleViewModel>(context.Session.ViewModel.Styles.ActiveEditor);
        Assert.Equal(position, context.Session.ProjectPosition);
        Assert.Null(context.Session.SelectedKeyTime);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task InvalidImageDraftBlocksEffectSelectionAndDeleteButDoesNotBlockCollapse()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/fixture.png");
        var image = Shape(0) with { Kind = LayerKind.IMAGE, Shape = null, Image = new(asset.Id, 120, 80) };
        var shape = Shape(12);
        var source = new ProjectDocument { Assets = [asset], Layers = [image, shape] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        var imageEffect = new TimelineEffectClipId(image.Id, new(AnimationProperty.OPACITY));
        var shapeEffect = new TimelineEffectClipId(shape.Id, new(AnimationProperty.OPACITY));
        Assert.True(model.SelectEffectClip(new(imageEffect)));
        context.Session.ViewModel.Styles.Image.CropWidth.RawText = "7e-";

        model.SetEffectClipCollapsed(new(imageEffect, false));
        Assert.False(model.SelectEffectClip(new(shapeEffect)));
        await model.DeleteTimelineSelectionAsync();

        Assert.Equal(image.Id, context.Session.SelectedLayerId);
        Assert.Equal("7e-", context.Session.ViewModel.Styles.Image.CropWidth.RawText);
        Assert.IsType<ImageStyleViewModel>(context.Session.ViewModel.Styles.ActiveEditor);
        Assert.Contains(imageEffect, context.Session.TimelineViewState.ExpandedAnimationClips);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task EffectDeletePreservesOtherRangesAndStatesAndCannotFallThroughToParent()
    {
        var firstRange = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var secondRange = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var line = new SubtitleLine { Text = "AB", End = new(4), AnimationRanges = [firstRange, secondRange] };
        var target = new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, TextRangeId: firstRange.Id);
        var layer = new ProjectLayer
        {
            Id = line.Id, SubtitleId = line.Id, End = line.End,
            Tracks =
            [
                new(target, [new(new(0), 2)]),
                new(target with { TextRangeId = secondRange.Id }, [new(new(0), 3)]),
                new(target with { State = SubtitleAnimationState.INACTIVE }, [new(new(0), 4)]),
                new(AnimationProperty.STROKE_WIDTH, [new(new(0), 5)])
            ]
        };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        Assert.True(model.SelectEffectClip(new(new(layer.Id, target))));
        Assert.Equal(target, context.Session.SceneEditing.Target);

        await model.DeleteTimelineSelectionAsync();
        await model.DeleteTimelineSelectionAsync();

        var remaining = Assert.Single(context.Editor.Snapshot.Layers);
        Assert.Equal(3, remaining.Tracks.Length);
        Assert.DoesNotContain(remaining.Tracks, track => track.Target == target);
        Assert.Equal(source.Subtitles, context.Editor.Snapshot.Subtitles);
        Assert.Null(model.SelectedEffectClipId);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task KeyframeDeleteUsesExactTimeAndExplicitParentSelectionRestoresClipDelete()
    {
        var layer = Shape(0);
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        Assert.True(model.SelectKeyframe(new(layer.Id, AnimationProperty.OPACITY, new(1), new(1))));

        await model.DeleteTimelineSelectionAsync();
        await model.DeleteTimelineSelectionAsync();

        var remaining = Assert.Single(context.Editor.Snapshot.Layers);
        Assert.Equal(new AegiNext.Core.Timing.MediaTime(2), Assert.Single(Assert.Single(remaining.Tracks).Keyframes).Time);
        Assert.True(model.SelectLayers(new(layer.Id)));
        await model.DeleteTimelineSelectionAsync();
        Assert.Empty(context.Editor.Snapshot.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Equal(remaining, Assert.Single(context.Editor.Snapshot.Layers));
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task SelectingRichTextKeyframeFromAnotherParentKeepsTheEntireTarget()
    {
        var shape = Shape(0);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "AB", Start = new(12), End = new(16), AnimationRanges = [range] };
        var target = new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH,
            TextRangeId: range.Id, State: SubtitleAnimationState.INACTIVE);
        var subtitle = new ProjectLayer
        {
            SubtitleId = line.Id, Start = line.Start, End = line.End,
            Tracks = [new(target, [new(new(1), 2), new(new(2), 3)])]
        };
        var source = new ProjectDocument { Subtitles = [line], Layers = [shape, subtitle] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        context.Session.SelectLayer(shape.Id, [shape.Id]);
        var model = context.Session.ViewModel.Timeline;

        Assert.True(model.SelectKeyframe(new(subtitle.Id, target, new(1), new(1))));

        Assert.Equal(target, context.Session.SceneEditing.Target);
        Assert.Equal(new TimelineEffectClipId(subtitle.Id, target), model.SelectedEffectClipId);
        Assert.IsType<SubtitleStyleViewModel>(context.Session.ViewModel.Styles.ActiveEditor);
        await model.DeleteTimelineSelectionAsync();
        Assert.Same(shape, context.Editor.Snapshot.Layers[0]);
        Assert.Equal(new AegiNext.Core.Timing.MediaTime(2), Assert.Single(context.Editor.Snapshot.Layers[1].Tracks[0].Keyframes).Time);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransformEndpointDeleteUsesOperationIdentity(bool isStart)
    {
        var first = new AnimationTransformOperation(Guid.NewGuid(), new(1), new(3), 0.5);
        var second = new AnimationTransformOperation(Guid.NewGuid(), new(1), new(3), 0.75);
        var target = new AnimationTrackTarget(AnimationProperty.OPACITY);
        var layer = Shape(0) with
        {
            Tracks = [new(target, []) { InitialValue = 1, Transforms = [first, second] }]
        };
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var time = isStart ? second.Start : second.End;
        var model = context.Session.ViewModel.Timeline;
        Assert.True(model.SelectKeyframe(new(layer.Id, target, time, time) { OperationId = second.Id, IsOperationStart = isStart }));

        await model.DeleteTimelineSelectionAsync();
        await model.DeleteTimelineSelectionAsync();

        Assert.Equal(first, Assert.Single(Assert.Single(Assert.Single(context.Editor.Snapshot.Layers).Tracks).Transforms));
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task DocumentChangesAndUndoInvalidateOldEffectSelectionAndContext()
    {
        var layer = Shape(0);
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        var id = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        Assert.True(model.SelectEffectClip(new(id)));
        model.SetEffectClipContext(new(id));
        context.Editor.UpdateLayer(layer.Id, value => value with { Name = "Changed" });

        await model.DeleteTimelineSelectionAsync();
        await model.ClearEffectClipCommand.ExecuteAsync(null);
        Assert.Single(context.Editor.Snapshot.Layers[0].Tracks);
        Assert.True(context.Editor.Undo());
        await model.DeleteTimelineSelectionAsync();
        Assert.False(model.ClearEffectClipCommand.CanExecute(null));
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task KeyframeContextDeletesOnlyTheFrozenMarkerAfterParentSelectionChanges()
    {
        var first = Shape(0);
        var second = Shape(12);
        var source = new ProjectDocument { Layers = [first, second] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        var id = new TimelineEffectClipId(first.Id, new(AnimationProperty.OPACITY));
        model.SetEffectClipContext(new(id, new(1)));
        context.Session.SelectLayer(second.Id, [second.Id]);

        await model.DeleteContextKeyframeCommand.ExecuteAsync(null);

        Assert.Equal(new AegiNext.Core.Timing.MediaTime(2), Assert.Single(context.Editor.Snapshot.Layers[0].Tracks[0].Keyframes).Time);
        Assert.Same(second, context.Editor.Snapshot.Layers[1]);
        Assert.Equal(second.Id, context.Session.SelectedLayerId);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(model.DeleteContextKeyframeCommand.CanExecute(null));
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ExternalMultiselectionInvalidatesTheOldEffectDeleteTarget()
    {
        var first = Shape(0);
        var second = Shape(12);
        var source = new ProjectDocument { Layers = [first, second] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        Assert.True(model.SelectEffectClip(new(new(first.Id, new(AnimationProperty.OPACITY)))));

        context.Session.SelectLayer(first.Id, [first.Id, second.Id]);
        await model.DeleteTimelineSelectionAsync();

        Assert.Null(model.SelectedEffectClipId);
        Assert.Same(source, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ShapeConversionKeepsTheExpandedEffectIdentity()
    {
        var layer = Shape(0);
        var source = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(source);
        await context.InitializeAsync();
        var id = new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY));
        Assert.True(context.Session.ViewModel.Timeline.SelectEffectClip(new(id)));
        context.Session.SetTimelineEffectClipCollapsed(id, false);

        await context.Session.Graphics.ConvertSelectedShapeToPathAsync();

        Assert.NotNull(context.Session.SelectedLayer!.Shape!.Path);
        Assert.Equal(id, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
        Assert.Equal(layer.Tracks, context.Session.SelectedLayer.Tracks);
        Assert.True(context.Editor.Undo());
        Assert.Same(source, context.Editor.Snapshot);
        Assert.Equal(id, Assert.Single(context.Session.TimelineViewState.ExpandedAnimationClips));
    }

    private static ProjectLayer Shape(int start) => new()
    {
        Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 60), Start = new(start), End = new(start + 10),
        Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5), new(new(2), 1)])]
    };
}
