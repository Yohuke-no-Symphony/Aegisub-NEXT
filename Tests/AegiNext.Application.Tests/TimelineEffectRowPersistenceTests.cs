using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class TimelineEffectRowPersistenceTests
{
    [Fact]
    public void MixedRowOrderRoundTripsWithoutMovingItIntoNonUndoableViewState()
    {
        var document = TimelineEffectRowEditingTests.Document();
        var layer = document.Layers[0];
        var order = TimelineEffectRowOrdering.GetOrder(layer, document.Subtitles[0]).Reverse().ToImmutableArray();
        document = document with { Layers = [layer with { EffectRowOrder = order }] };

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(document));

        Assert.Equal(19, loaded.Version);
        Assert.Equal(order.ToArray(), loaded.Layers[0].EffectRowOrder.ToArray());
        Assert.Equal(document.TimelineViewState.ExpandedAnimationClips.ToArray(), loaded.TimelineViewState.ExpandedAnimationClips.ToArray());
        Assert.Equal(document.TimelineViewState.CollapsedAnimationRows.ToArray(), loaded.TimelineViewState.CollapsedAnimationRows.ToArray());
        Assert.Equal(document.TimelineViewState.CollapsedTrackIds.ToArray(), loaded.TimelineViewState.CollapsedTrackIds.ToArray());
        Assert.Equal(document.Layers[0].Effects.Select(instance => instance.Id), loaded.Layers[0].Effects.Select(instance => instance.Id));
    }

    [Fact]
    public void OlderVersionNineteenWithoutTheOptionalFieldUsesTheDefaultOrderAndEmptyOrdersAreOmitted()
    {
        var document = TimelineEffectRowEditingTests.Document();
        var bytes = ProjectStore.Serialize(document);
        var root = JsonNode.Parse(bytes)!.AsObject();
        Assert.Null(root["layers"]![0]!["effectRowOrder"]);

        var loaded = ProjectStore.Deserialize(bytes);

        Assert.Empty(loaded.Layers[0].EffectRowOrder);
        Assert.Equal(TimelineEffectRowOrdering.GetOrder(document.Layers[0], document.Subtitles[0]).ToArray(),
            TimelineEffectRowOrdering.GetOrder(loaded.Layers[0], loaded.Subtitles[0]).ToArray());
    }

    [Fact]
    public void TemporarilyMissingRowsRemainValidHintsButUnknownPayloadMembersAreRejected()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 10),
            EffectRowOrder = [new(InstanceId: Guid.NewGuid()), new(new(AnimationProperty.MASK_NODE_POSITION, Guid.NewGuid()))]
        };
        var bytes = ProjectStore.Serialize(new() { Layers = [layer] });
        var loaded = ProjectStore.Deserialize(bytes);
        Assert.Equal(layer.EffectRowOrder.ToArray(), loaded.Layers[0].EffectRowOrder.ToArray());
        Assert.Empty(TimelineEffectRowOrdering.GetOrder(loaded.Layers[0], null));
        var root = JsonNode.Parse(bytes)!.AsObject();
        root["layers"]![0]!["effectRowOrder"]![0]!["unexpected"] = true;

        var error = Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        Assert.IsType<System.Text.Json.JsonException>(error.InnerException);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void InvalidUnionOrCollectionCannotEnterAProject(int mutation)
    {
        var identity = new TimelineEffectRowIdentity(new(AnimationProperty.OPACITY));
        ImmutableArray<TimelineEffectRowIdentity> order = mutation switch
        {
            0 => [new()],
            1 => [new(new(AnimationProperty.OPACITY), Guid.NewGuid())],
            2 => [new(InstanceId: Guid.Empty)],
            3 => [new(new(AnimationProperty.POSITION_X))],
            4 => default,
            5 => [identity, identity],
            _ => [null!]
        };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 10), EffectRowOrder = order };
        var document = new ProjectDocument { Layers = [layer] };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(document));
    }

    [Fact]
    public void IdentityBudgetIsBoundedAcrossAllClips()
    {
        var ids = Enumerable.Range(0, 100000).Select(_ => new TimelineEffectRowIdentity(InstanceId: Guid.NewGuid())).ToImmutableArray();
        var first = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 10), EffectRowOrder = ids };
        var document = new ProjectDocument { Layers = [first] };
        ProjectValidator.Validate(document);
        var second = first with { Id = Guid.NewGuid(), Start = first.End, End = first.End + new AegiNext.Core.Timing.MediaTime(1), EffectRowOrder = [new(InstanceId: Guid.NewGuid())] };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document with { Layers = [first, second] }));
    }
}
