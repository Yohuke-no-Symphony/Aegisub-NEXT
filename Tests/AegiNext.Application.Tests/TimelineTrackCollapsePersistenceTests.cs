using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class TimelineTrackCollapsePersistenceTests
{
    [Fact]
    public async Task OverallCollapseRoundTripsEmptyAndShapeTracksAndDormantIdentitiesWithIndependentProperties()
    {
        using var directory = new TemporaryProjectDirectory();
        var sceneTrack = new ProjectTrack { Name = "Shape" };
        var scene = new ProjectLayer { TrackId = sceneTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20) };
        var dormant = Guid.NewGuid();
        var effect = new TimelineEffectClipId(scene.Id, new(AnimationProperty.OPACITY));
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, sceneTrack],
            Layers = [scene],
            TimelineViewState = new()
            {
                CollapsedTrackIds = [ProjectTrack.DEFAULT_TRACK_ID, sceneTrack.Id, dormant],
                ExpandedAnimationClips = [effect]
            }
        };
        var path = Path.Combine(directory.Path, "overall-collapse.aeginext");

        await ProjectStore.SaveAsync(document, path);
        var loaded = await ProjectStore.LoadAsync(path);

        Assert.Equal(document.TimelineViewState.CollapsedTrackIds.ToArray(), loaded.TimelineViewState.CollapsedTrackIds.ToArray());
        Assert.Equal(document.TimelineViewState.ExpandedAnimationClips.ToArray(), loaded.TimelineViewState.ExpandedAnimationClips.ToArray());
        Assert.Equal(ProjectStore.Serialize(document), ProjectStore.Serialize(loaded));
    }

    [Fact]
    public void ExistingTimelineStateWithoutOverallCollapseFieldDefaultsToExpandedTracks()
    {
        var row = new TimelineAnimationRowId(TimelineRowScope.TRACK,
            ProjectTrack.DEFAULT_TRACK_ID, AnimationProperty.OPACITY);
        var root = JsonNode.Parse(ProjectStore.Serialize(new()
        {
            TimelineViewState = new() { CollapsedAnimationRows = [row] }
        }))!.AsObject();
        LegacyProjectJsonFixture.AddAnimationRows(root, [row]);
        Assert.True(root["timelineViewState"]!.AsObject().Remove("collapsedTrackIds"));

        var loaded = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));

        Assert.False(loaded.TimelineViewState.CollapsedTrackIds.IsDefault);
        Assert.Empty(loaded.TimelineViewState.CollapsedTrackIds);
        Assert.Equal(row, Assert.Single(loaded.TimelineViewState.CollapsedAnimationRows));
        Assert.Empty(loaded.TimelineViewState.ExpandedAnimationClips);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidOverallIdentityCollectionsAreRejected(int mutation)
    {
        var id = Guid.NewGuid();
        var state = mutation switch
        {
            0 => new TimelineViewState { CollapsedTrackIds = [Guid.Empty] },
            1 => new TimelineViewState { CollapsedTrackIds = [id, id] },
            2 => new TimelineViewState { CollapsedTrackIds = default },
            3 => new TimelineViewState { CollapsedTrackIds = [.. Enumerable.Range(0, 100001).Select(_ => Guid.NewGuid())] },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        Assert.Throws<InvalidDataException>(() => state.Validate());
        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(new() { TimelineViewState = state }));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[\"00000000-0000-0000-0000-000000000000\"]")]
    [InlineData("[\"invalid-id\"]")]
    [InlineData("[\"ffffffff-ffff-ffff-ffff-ffffffffffff\",\"ffffffff-ffff-ffff-ffff-ffffffffffff\"]")]
    public void InvalidSerializedOverallIdentitiesAreRejected(string value)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(new()))!.AsObject();
        root["timelineViewState"]!["collapsedTrackIds"] = JsonNode.Parse(value);

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
}
