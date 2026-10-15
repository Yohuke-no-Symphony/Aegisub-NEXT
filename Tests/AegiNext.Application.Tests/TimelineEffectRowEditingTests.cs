using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class TimelineEffectRowEditingTests
{
    [Fact]
    public void ARangeStateLeafCanMoveAcrossNativeNodesAndInstancesWithoutRewritingItsContent()
    {
        var document = Document();
        var layer = document.Layers[0];
        var line = document.Subtitles[0];
        var target = layer.Tracks[2].Target;
        var identity = new TimelineEffectRowIdentity(target);
        var before = new TimelineEffectRowIdentity(InstanceId: layer.Effects[^1].Id);
        var editor = new ProjectEditor(document);

        editor.MoveTimelineEffectRow(layer.Id, identity, before);

        var result = editor.Snapshot;
        Assert.Equal(identity, TimelineEffectRowOrdering.GetOrder(result.Layers[0], line)[0]);
        Assert.Equal(layer.Tracks, result.Layers[0].Tracks);
        Assert.Equal(layer.Effects, result.Layers[0].Effects);
        Assert.Equal(document.Subtitles, result.Subtitles);
        Assert.Same(line.AnimationRanges[0], result.Subtitles[0].AnimationRanges[0]);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(result, editor.Snapshot);
    }

    [Fact]
    public void CrossingInstanceRowsChangesCompositionAndLayoutInTheSameUndo()
    {
        var document = Document();
        var layer = document.Layers[0];
        var editor = new ProjectEditor(document);
        Assert.Equal(22, SceneEvaluator.EvaluateLayer(layer, document.Subtitles[0], new(1)).Transform.Position.X);

        editor.MoveTimelineEffectRow(layer.Id, new(InstanceId: layer.Effects[0].Id), new(InstanceId: layer.Effects[1].Id));

        var result = editor.Snapshot.Layers[0];
        Assert.Equal([layer.Effects[1].Id, layer.Effects[0].Id], result.Effects.Select(instance => instance.Id));
        Assert.Equal(12, SceneEvaluator.EvaluateLayer(result, document.Subtitles[0], new(1)).Transform.Position.X);
        Assert.All(layer.Effects, instance => Assert.Same(instance, result.Effects.Single(value => value.Id == instance.Id)));
        Assert.Equal(layer.Tracks, result.Tracks);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ANativePropertyCanMoveToTheVisualEndWithoutRewritingTracksOrViewState()
    {
        var document = Document();
        var layer = document.Layers[0];
        var id = new TimelineEffectRowIdentity(layer.Tracks[^1].Target);
        var editor = new ProjectEditor(document);

        editor.MoveTimelineEffectRow(layer.Id, id, null);

        Assert.NotSame(document, editor.Snapshot);
        Assert.Equal(id, TimelineEffectRowOrdering.GetOrder(editor.Snapshot.Layers[0], document.Subtitles[0])[^1]);
        Assert.Same(document.TimelineViewState, editor.Snapshot.TimelineViewState);
        Assert.Equal(layer.Effects, editor.Snapshot.Layers[0].Effects);
        Assert.Equal(layer.Tracks, editor.Snapshot.Layers[0].Tracks);
    }

    [Fact]
    public void NoOpMovesPreserveSnapshotAndRedo()
    {
        var document = Document();
        var layer = document.Layers[0];
        var rows = TimelineEffectRowOrdering.GetOrder(layer, document.Subtitles[0]);
        var editor = new ProjectEditor(document);
        editor.MoveTimelineEffectRow(layer.Id, rows[0], null);
        Assert.True(editor.Undo());

        editor.MoveTimelineEffectRow(layer.Id, rows[0], rows[0]);
        editor.MoveTimelineEffectRow(layer.Id, rows[0], rows[1]);
        editor.MoveTimelineEffectRow(layer.Id, rows[^1], null);

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownSourceOrAnchorRejectsTheWholeEdit(bool source)
    {
        var document = Document();
        var layer = document.Layers[0];
        var missing = new TimelineEffectRowIdentity(InstanceId: Guid.NewGuid());
        var existing = new TimelineEffectRowIdentity(InstanceId: layer.Effects[0].Id);
        var editor = new ProjectEditor(document);

        Assert.Throws<KeyNotFoundException>(() => editor.MoveTimelineEffectRow(layer.Id, source ? missing : existing, source ? existing : missing));

        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    internal static ProjectDocument Document()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 2, 2);
        var line = new SubtitleLine { Text = "ABCD", End = new(4), AnimationRanges = [first, second] };
        var node = new MaskNode { Position = new(10, 20) };
        return new()
        {
            Subtitles = [line], Layers = [new()
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End,
                Transform = new() { Position = new(1, 0) },
                Mask = new VectorClipMask { Contours = [new() { Nodes = [node] }] },
                Tracks =
                [
                    new(AnimationProperty.OPACITY, [new(new(0), 0.5d)]),
                    new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id), [new(new(0), new SceneColor(1, 0, 0, 1))]),
                    new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id, State: SubtitleAnimationState.ACTIVE), [new(new(0), new SceneColor(0, 1, 0, 1))]),
                    new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id), [new(new(0), node.Position)])
                ],
                Effects = [Instance("Add", "offset(10, 0)"), Instance("Multiply", "factor(2, 1)")]
            }]
        };
    }

    private static EffectInstance Instance(string name, string value) => new()
    {
        Name = name, End = new(4),
        Source = "effect \"row-order\" version 3\nshort-clip compress\nscope whole current\nsegment all flex 1\n" +
            "at 0 position " + value + "\nat 1 position " + value + "\nend\nend"
    };
}
