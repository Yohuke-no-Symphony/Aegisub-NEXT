using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Editing;

public sealed class TimelineEffectRowOrderingTests
{
    [Fact]
    public void EmptyHintsKeepTheExistingTopToBottomOrder()
    {
        var first = new EffectInstance();
        var second = new EffectInstance();
        var layer = new ProjectLayer
        {
            Tracks = [Track(new(AnimationProperty.OPACITY)), Track(new(AnimationProperty.POSITION))],
            Effects = [first, second]
        };

        Assert.Equal(new TimelineEffectRowIdentity[]
        {
            new(InstanceId: second.Id), new(InstanceId: first.Id), new(new(AnimationProperty.OPACITY)), new(new(AnimationProperty.POSITION))
        }, TimelineEffectRowOrdering.GetOrder(layer, null));
    }

    [Fact]
    public void HintsMixIndividualRangeStatesNodesAndInstancesThenAppendNewRows()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "A", AnimationRanges = [range] };
        var node = new MaskNode();
        var first = new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id);
        var second = first with { State = SubtitleAnimationState.ACTIVE };
        var maskTarget = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id);
        var instance = new EffectInstance();
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id,
            Mask = new VectorClipMask { Contours = [new() { Nodes = [node] }] },
            Tracks = [Track(first), Track(second), Track(maskTarget), Track(new(AnimationProperty.OPACITY))],
            Effects = [instance],
            EffectRowOrder = [new(first), new(InstanceId: Guid.NewGuid()), new(maskTarget), new(InstanceId: instance.Id), new(second)]
        };

        Assert.Equal(new TimelineEffectRowIdentity[]
        {
            new(first), new(maskTarget), new(InstanceId: instance.Id), new(second), new(new(AnimationProperty.OPACITY))
        }, TimelineEffectRowOrdering.GetOrder(layer, line));
        Assert.Equal(first.TextRangeId, second.TextRangeId);
        Assert.Equal(4, layer.Tracks.Length);
    }

    [Fact]
    public void EmptyTracksAndTemporarilyMissingHintTargetsDoNotCreateRows()
    {
        var missing = new TimelineEffectRowIdentity(new(AnimationProperty.MASK_NODE_POSITION, Guid.NewGuid()));
        var layer = new ProjectLayer
        {
            Tracks = [new(AnimationProperty.OPACITY, [])], EffectRowOrder = [missing]
        };

        Assert.Empty(TimelineEffectRowOrdering.GetOrder(layer, null));
        Assert.Same(missing, layer.EffectRowOrder[0]);
    }

    private static AnimationTrack Track(AnimationTrackTarget target) => new(target, [new(new(0), AnimationPropertyMetadata.GetValueKind(target.Property) switch
    {
        AnimationValueKind.VECTOR => new ScenePoint(1, 1),
        AnimationValueKind.COLOR => new SceneColor(1, 1, 1, 1),
        _ => 1d
    })]);
}
