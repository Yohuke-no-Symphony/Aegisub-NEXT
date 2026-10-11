using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class LegacyTrackOrderMigrationTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void NonOverlappingInterleavedClipsKeepOriginalTracksIncludingEmptyTrack(int version)
    {
        var ranges = Enumerable.Range(0, 8).SelectMany(index => Enumerable.Range(0, 3)
            .Select(track => (track, new MediaTime(index * 6 + track * 2), new MediaTime(index * 6 + track * 2 + 1)))).ToArray();
        var (root, original) = CreateLegacy(version, 4, ranges);
        var input = root.ToJsonString();

        var migrated = Read(root);

        Assert.Equal(original.Tracks.ToArray(), migrated.Tracks.ToArray());
        Assert.Equal(original.Layers.Select(clip => clip.TrackId), migrated.Layers.Select(clip => clip.TrackId));
        Assert.Equal(original.TimelineViewState.CollapsedTrackIds.ToArray(), migrated.TimelineViewState.CollapsedTrackIds.ToArray());
        Assert.Equal(original.TimelineViewState.CollapsedAnimationRows.ToArray(), migrated.TimelineViewState.CollapsedAnimationRows.ToArray());
        Assert.Equal(input, root.ToJsonString());
        AssertActiveOrder(original, migrated);
        AssertStable(root, migrated);
    }

    [Fact]
    public void ThreeThousandInterleavedClipsStillUseTheOriginalThreeTracks()
    {
        var ranges = Enumerable.Range(0, 1000).SelectMany(index => Enumerable.Range(0, 3)
            .Select(track => (track, new MediaTime(index * 6 + track * 2), new MediaTime(index * 6 + track * 2 + 1)))).ToArray();
        var (root, original) = CreateLegacy(8, 3, ranges);

        var migrated = Read(root);

        Assert.Equal(original.Tracks.ToArray(), migrated.Tracks.ToArray());
        Assert.Equal(3000, migrated.Layers.Length);
        Assert.Equal(original.Layers.Select(clip => clip.TrackId), migrated.Layers.Select(clip => clip.TrackId));
        AssertStable(root, migrated);
    }

    [Fact]
    public void CompatibleOverlayConstraintsReorderExistingTracksWithoutSplittingThem()
    {
        var (root, original) = CreateLegacy(8, 3,
            (0, new(0), new(5)), (1, new(0), new(5)), (1, new(5), new(10)), (2, new(5), new(10)));

        var migrated = Read(root);

        Assert.Equal(original.Tracks.Reverse(), migrated.Tracks);
        Assert.Equal(original.Layers.Select(clip => clip.TrackId), migrated.Layers.Select(clip => clip.TrackId));
        AssertActiveOrder(original, migrated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingOverlayOrderSplitsOnlyOneOwnerAndPreservesItsMetadata(bool threeTracks)
    {
        var ranges = threeTracks
            ? new (int, MediaTime, MediaTime)[]
            {
                (0, new(0), new(1)), (1, new(0), new(1)),
                (1, new(2), new(3)), (2, new(2), new(3)),
                (2, new(4), new(5)), (0, new(4), new(5))
            }
            : [(0, new(0), new(1)), (1, new(0), new(1)), (1, new(2), new(3)), (0, new(2), new(3))];
        var (root, original) = CreateLegacy(8, threeTracks ? 3 : 2, ranges);

        var migrated = Read(root);

        Assert.Equal(original.Tracks.Length + 1, migrated.Tracks.Length);
        Assert.All(original.Tracks, track => Assert.Contains(track, migrated.Tracks));
        var added = Assert.Single(migrated.Tracks, track => original.Tracks.All(old => old.Id != track.Id));
        Assert.Equal(original.Tracks[0] with { Id = added.Id }, added);
        Assert.Contains(added.Id, migrated.TimelineViewState.CollapsedTrackIds);
        Assert.Contains(new(TimelineRowScope.TRACK, added.Id, AnimationProperty.OPACITY),
            migrated.TimelineViewState.CollapsedAnimationRows);
        Assert.Equal(original.Tracks.Length + 1, migrated.TimelineViewState.CollapsedTrackIds.Length);
        AssertActiveOrder(original, migrated);
        AssertStable(root, migrated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RationalTouchingBoundariesReuseOneTrackAndActualOverlapRequiresTwo(bool overlap)
    {
        var boundary = new MediaTime(1001, 30000);
        var (root, original) = CreateLegacy(8, 1, (0, MediaTime.Zero, boundary), (0, boundary, boundary * 2));
        if (overlap)
        {
            var start = new JsonObject { ["numerator"] = 1001, ["denominator"] = 60000 };
            root["subtitles"]![1]!["start"] = start.DeepClone();
            root["layers"]![1]!["start"] = start;
            original = original with
            {
                Subtitles = original.Subtitles.SetItem(1, original.Subtitles[1] with { Start = boundary / 2 }),
                Layers = original.Layers.SetItem(1, original.Layers[1] with { Start = boundary / 2 })
            };
        }

        var migrated = Read(root);

        Assert.Equal(overlap ? 2 : 1, migrated.Tracks.Length);
        AssertActiveOrder(original, migrated);
        AssertStable(root, migrated);
    }

    [Fact]
    public void RepeatedReversalReusesDerivedTrackAndReturnsUnconstrainedClipsToOriginalOwner()
    {
        var (root, original) = CreateLegacy(8, 2,
            (0, new(0), new(1)), (1, new(0), new(1)),
            (1, new(2), new(3)), (0, new(2), new(3)),
            (0, new(4), new(5)), (1, new(6), new(7)), (0, new(6), new(7)));

        var migrated = Read(root);

        Assert.Equal(3, migrated.Tracks.Length);
        Assert.Equal(original.Tracks[0].Id, migrated.Layers[4].TrackId);
        Assert.Equal(migrated.Layers[3].TrackId, migrated.Layers[6].TrackId);
        AssertActiveOrder(original, migrated);
        AssertStable(root, migrated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShuffledLegacyClipsPreserveActiveOrderAndNeverSplitConsistentTrackOrders(bool consistent)
    {
        for (var seed = 0; seed < 32; seed++)
        {
            var random = new Random(seed);
            var ranges = Enumerable.Range(0, 5).SelectMany(time => Enumerable.Range(0, 4)
                .Select(track => (Track: track, Start: new MediaTime(time * 2), End: new MediaTime(time * 2 + 1)))).ToArray();
            if (consistent)
            {
                var tracks = Enumerable.Range(0, 4).OrderBy(_ => random.Next()).Select((track, index) => (track, index))
                    .ToDictionary(pair => pair.track, pair => pair.index);
                var times = Enumerable.Range(0, 5).OrderBy(_ => random.Next()).Select((time, index) => (time, index))
                    .ToDictionary(pair => pair.time, pair => pair.index);
                ranges = ranges.OrderBy(range => times[(int)(range.Start.Numerator / 2)])
                    .ThenBy(range => tracks[range.Track]).ToArray();
            }
            else
            {
                ranges = ranges.OrderBy(_ => random.Next()).ToArray();
            }
            var (root, original) = CreateLegacy(8, 4, ranges);

            var migrated = Read(root);

            if (consistent)
            {
                Assert.Equal(4, migrated.Tracks.Length);
            }
            Assert.All(original.Tracks, track => Assert.Contains(track, migrated.Tracks));
            AssertActiveOrder(original, migrated);
            AssertStable(root, migrated);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllocationConsidersBothDrawingDirectionsBeforeKeepingAnExtraTrack(bool reverseDrawingOrder)
    {
        var ranges = new (int, MediaTime, MediaTime)[]
        {
            (0, new(0), new(1)), (1, new(0), new(1)), (1, new(3), new(5)),
            (0, new(2), new(5)), (1, new(2), new(3))
        };
        if (reverseDrawingOrder)
        {
            Array.Reverse(ranges);
        }
        var (root, original) = CreateLegacy(8, 2, ranges);

        var migrated = Read(root);

        Assert.Equal(3, migrated.Tracks.Length);
        Assert.All(original.Layers.Where(clip => clip.TrackId == original.Tracks[0].Id), clip =>
            Assert.Equal(clip.TrackId, migrated.Layers.Single(candidate => candidate.Id == clip.Id).TrackId));
        AssertActiveOrder(original, migrated);
        AssertStable(root, migrated);
    }

    private static (JsonObject Root, ProjectDocument Document) CreateLegacy(int version, int trackCount,
        params (int Track, MediaTime Start, MediaTime End)[] ranges)
    {
        var tracks = Enumerable.Range(0, trackCount).Select(index => new ProjectTrack
        {
            Name = $"Track {index}", DefaultStyle = new() { FontSize = 37 + index },
            StylePresetId = Guid.NewGuid(), StylePresetName = $"Style {index}", AutoApplyStyle = index % 2 == 0
        }).ToImmutableArray();
        var lines = ranges.Select(range => new SubtitleLine
        {
            Start = range.Start, End = range.End, Text = "migration"
        }).ToImmutableArray();
        var document = new ProjectDocument
        {
            Tracks = tracks, Subtitles = lines,
            Layers = ranges.Select((range, index) => new ProjectLayer
            {
                Id = lines[index].Id, Name = "Subtitle", Kind = LayerKind.SUBTITLE,
                SubtitleId = lines[index].Id, TrackId = tracks[range.Track].Id, Start = range.Start, End = range.End
            }).ToImmutableArray(),
            TimelineViewState = new()
            {
                CollapsedTrackIds = tracks.Select(track => track.Id).ToImmutableArray(),
                CollapsedAnimationRows = tracks.Select(track => new TimelineAnimationRowId(
                    TimelineRowScope.TRACK, track.Id, AnimationProperty.OPACITY)).ToImmutableArray()
            }
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        LegacyProjectJsonFixture.AddAnimationRows(root, document.TimelineViewState.CollapsedAnimationRows);
        LegacyProjectJsonFixture.Downgrade(root, version);
        return (root, document);
    }

    private static void AssertActiveOrder(ProjectDocument original, ProjectDocument migrated)
    {
        var boundaries = original.Layers.SelectMany(clip => new[] { clip.Start, clip.End }).Distinct().Order().ToArray();
        var samples = boundaries.Concat(boundaries.Zip(boundaries.Skip(1), (start, end) => (start + end) / 2));
        var scene = new PreparedProjectScene(migrated);
        foreach (var time in samples)
        {
            Assert.Equal(original.Layers.Where(clip => clip.Start <= time && time < clip.End).Select(clip => clip.Id),
                SceneEvaluator.Evaluate(scene, time).Select(clip => clip.Source.Id));
        }
    }

    private static void AssertStable(JsonObject root, ProjectDocument migrated)
    {
        var bytes = ProjectStore.Serialize(migrated);
        Assert.Equal(bytes, ProjectStore.Serialize(Read(root)));
        Assert.Equal(bytes, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
    }

    private static ProjectDocument Read(JsonObject root)
    {
        return ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
