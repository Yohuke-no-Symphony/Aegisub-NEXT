using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineEffectReorderWorkflowTests
{
    [Fact]
    public async Task InstanceReorderChangesRealCompositionOnlyAfterOneAtomicCommit()
    {
        var layer = Shape() with
        {
            Transform = new() { Position = new(1, 0) },
            Effects = [Instance("offset(10, 0)"), Instance("factor(2, 1)"), Instance("offset(3, 0)")]
        };
        var document = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        var id = new TimelineEffectClipId(layer.Id, new(AnimationProperty.POSITION), layer.Effects[0].Id);
        Assert.True(model.SelectEffectClip(new(id)));
        var position = context.Session.ProjectPosition;
        Assert.Equal(25, SceneEvaluator.EvaluateLayer(layer, null, new(1)).Transform.Position.X);

        var anchor = new TimelineEffectClipId(layer.Id, new(AnimationProperty.POSITION), layer.Effects[^1].Id);
        await model.CommitEffectClipReorderAsync(new(id, anchor, document));

        var reordered = context.Editor.Snapshot;
        var result = reordered.Layers[0];
        Assert.Equal([layer.Effects[1].Id, layer.Effects[2].Id, layer.Effects[0].Id], result.Effects.Select(instance => instance.Id));
        Assert.Equal(15, SceneEvaluator.EvaluateLayer(result, null, new(1)).Transform.Position.X);
        Assert.Equal(layer.Tracks, result.Tracks);
        Assert.Equal(layer.Start, result.Start);
        Assert.Equal(layer.End, result.End);
        Assert.Equal(layer.TrackId, result.TrackId);
        Assert.Equal(document.Tracks, reordered.Tracks);
        Assert.All(layer.Effects, instance => Assert.Same(instance, result.Effects.Single(value => value.Id == instance.Id)));
        Assert.Equal(id, model.SelectedEffectClipId);
        Assert.Equal(position, context.Session.ProjectPosition);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(reordered, context.Editor.Snapshot);
    }

    [Fact]
    public async Task RangePropertyReorderMovesOnlyItsRowAndPreservesTracksHiddenRangesAndViewStateThroughUndo()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 2, 2);
        var hidden = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "ABCD", End = new(4), AnimationRanges = [first, hidden, second] };
        var target = new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: first.Id);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
            Tracks =
            [
                new(target, [new(new(0), 30d)]),
                new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id), [new(new(0), new SceneColor(1, 0, 0, 1))]),
                new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id, State: SubtitleAnimationState.ACTIVE), [new(new(0), new SceneColor(0, 1, 0, 1))]),
                new(new AnimationTrackTarget(AnimationProperty.SCALE, TextRangeId: second.Id), [new(new(0), new ScenePoint(2, 2))])
            ]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        var id = new TimelineEffectClipId(layer.Id, target);
        Assert.True(model.SelectEffectClip(new(id)));
        context.Session.SetTimelineEffectClipCollapsed(id, false);
        var originalOrder = TimelineEffectRowOrdering.GetOrder(layer, line);

        await model.CommitEffectClipReorderAsync(new(id, new(layer.Id, layer.Tracks[^1].Target), document));

        var reordered = context.Editor.Snapshot;
        Assert.Equal(line.AnimationRanges, reordered.Subtitles[0].AnimationRanges);
        Assert.Equal(document.Subtitles, reordered.Subtitles);
        var orderedRows = TimelineEffectRowOrdering.GetOrder(reordered.Layers[0], line);
        Assert.Equal(new(target), orderedRows[0]);
        Assert.Equal(originalOrder.Where(row => row.Target != target), orderedRows.Skip(1));
        Assert.All(layer.Tracks, track => Assert.Same(track, reordered.Layers[0].Tracks.Single(value => value.Target == track.Target)));
        Assert.Same(hidden, reordered.Subtitles[0].AnimationRanges[1]);
        Assert.Equal(id, model.SelectedEffectClipId);
        Assert.Contains(id, context.Session.TimelineViewState.ExpandedAnimationClips);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Contains(id, context.Session.TimelineViewState.ExpandedAnimationClips);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("other-parent")]
    [InlineData("missing")]
    [InlineData("same-item")]
    [InlineData("same-position")]
    [InlineData("selection")]
    public async Task InvalidOrNoOpAnchorNeverChangesTheProjectOrHistory(string mode)
    {
        var first = Shape() with { Effects = [Instance("offset(10, 0)"), Instance("factor(2, 1)")] };
        var second = Shape() with { Start = new(4), End = new(8), Effects = [Instance("offset(20, 0)")] };
        var document = new ProjectDocument { Layers = [first, second] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        var id = new TimelineEffectClipId(first.Id, new(AnimationProperty.POSITION), first.Effects[0].Id);
        Assert.True(model.SelectEffectClip(new(id)));
        var expected = document;
        var anchor = mode switch
        {
            "other-parent" => new TimelineEffectClipId(second.Id, new(AnimationProperty.POSITION), second.Effects[0].Id),
            "missing" => new TimelineEffectClipId(first.Id, new(AnimationProperty.POSITION), Guid.NewGuid()),
            "same-item" => id,
            "same-position" => new TimelineEffectClipId(first.Id, first.Tracks[0].Target),
            _ => null
        };
        if (mode == "stale")
        {
            expected = document with { Name = "New source" };
            context.Editor.Reset(expected);
        }
        else if (mode == "selection")
        {
            context.Session.SelectLayer(second.Id, [second.Id]);
        }

        await model.CommitEffectClipReorderAsync(new(id, anchor, document));

        Assert.Same(expected, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task InvalidInstanceParameterDraftBlocksReorderWithoutLosingTheRawInput()
    {
        var first = Instance("offset(10, 0)") with { Source = "effect \"parameter\" version 3\nshort-clip compress\nvar amount = 1\nscope clip current\nsegment all flex 1\nat 0 position factor(amount, amount)\nat 1 position factor(amount, amount)\nend\nend" };
        var layer = Shape() with { Effects = [first, Instance("factor(2, 1)")] };
        var document = new ProjectDocument { Layers = [layer] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        var id = new TimelineEffectClipId(layer.Id, new(AnimationProperty.POSITION), first.Id);
        Assert.True(model.SelectEffectClip(new(id)));
        var drafts = context.Session.ViewModel.Effects.InstanceParameters!;
        drafts.Rows[0].RawText = "7e-";

        await model.CommitEffectClipReorderAsync(new(id, null, document));

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal("7e-", drafts.Rows[0].RawText);
    }

    private static ProjectLayer Shape() => new()
    {
        Kind = LayerKind.SHAPE, End = new(4), Shape = new(ShapeKind.RECTANGLE, 20, 20),
        Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5d)])]
    };

    private static EffectInstance Instance(string value) => new()
    {
        Name = "Timeline instance", End = new(4),
        Source = "effect \"timeline-order\" version 3\nshort-clip compress\nscope clip current\nsegment all flex 1\n" +
            "at 0 position " + value + "\nat 1 position " + value + "\nend\nend"
    };
}
