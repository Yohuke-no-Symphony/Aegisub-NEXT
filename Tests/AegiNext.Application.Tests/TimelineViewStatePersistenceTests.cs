using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class TimelineViewStatePersistenceTests
{
    [Fact]
    public async Task FileRoundTripPreservesIndependentEffectsForMixedClipsSharingATrack()
    {
        using var directory = new TemporaryProjectDirectory();
        var scene = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(2), End = new(4),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])]
        };
        var editor = new ProjectEditor();
        var cue = editor.AddSubtitle(new(0), new(2), "First");
        editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(0), 0.5));
        editor.SetKeyframe(cue, AnimationProperty.POSITION, new(new(0), new ScenePoint(10, 20)));
        var state = new TimelineViewState
        {
            ExpandedAnimationClips =
            [
                new(cue, new(AnimationProperty.OPACITY)),
                new(scene.Id, new(AnimationProperty.OPACITY))
            ]
        };
        var document = editor.Snapshot with
        {
            Layers = [.. editor.Snapshot.Layers, scene],
            TimelineViewState = state
        };
        var path = Path.Combine(directory.Path, "timeline.aeginext");

        await ProjectStore.SaveAsync(document, path);
        var loaded = await ProjectStore.LoadAsync(path);

        Assert.Equal(state.ExpandedAnimationClips.ToArray(), loaded.TimelineViewState.ExpandedAnimationClips.ToArray());
        Assert.DoesNotContain(new TimelineEffectClipId(cue, new(AnimationProperty.POSITION)), loaded.TimelineViewState.ExpandedAnimationClips);
        Assert.Empty(loaded.TimelineViewState.CollapsedAnimationRows);
        Assert.Equal(ProjectStore.Serialize(document), ProjectStore.Serialize(loaded));
        Assert.Single(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void CompleteTargetIdentityDistinguishesNodesRangesAndVisualStatesWithoutRequiringLiveEntities()
    {
        var layerId = Guid.NewGuid();
        var rangeId = Guid.NewGuid();
        var state = new TimelineViewState
        {
            ExpandedAnimationClips =
            [
                new(layerId, new(AnimationProperty.FILL)),
                new(layerId, new(AnimationProperty.FILL, TextRangeId: rangeId)),
                new(layerId, new(AnimationProperty.FILL, TextRangeId: rangeId, State: SubtitleAnimationState.ACTIVE)),
                new(layerId, new(AnimationProperty.FILL, TextRangeId: rangeId, State: SubtitleAnimationState.INACTIVE)),
                new(layerId, new(AnimationProperty.FILL, TextRangeId: Guid.NewGuid())),
                new(layerId, new(AnimationProperty.MASK_NODE_POSITION, Guid.NewGuid())),
                new(layerId, new(AnimationProperty.MASK_NODE_POSITION, Guid.NewGuid())),
                new(Guid.NewGuid(), new(AnimationProperty.FILL, TextRangeId: rangeId))
            ]
        };

        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(new() { TimelineViewState = state }));

        Assert.Empty(loaded.Layers);
        Assert.Empty(loaded.Subtitles);
        Assert.Equal(state.ExpandedAnimationClips.ToArray(), loaded.TimelineViewState.ExpandedAnimationClips.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingViewStateOrExpansionFieldDefaultsToCollapsedEffects(bool removeState)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(new()))!.AsObject();
        Assert.True(removeState ? root.Remove("timelineViewState") : root["timelineViewState"]!.AsObject().Remove("expandedAnimationClips"));

        var loaded = Read(root);

        Assert.Equal(ProjectDocument.CURRENT_VERSION, loaded.Version);
        Assert.NotNull(loaded.TimelineViewState);
        Assert.False(loaded.TimelineViewState.ExpandedAnimationClips.IsDefault);
        Assert.Empty(loaded.TimelineViewState.ExpandedAnimationClips);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void LegacyFixturesWithoutTimelineViewStateMigrateToCollapsedEffects(int version)
    {
        var root = LegacyProjectJsonFixture.Create(version);
        Assert.True(root.Remove("timelineViewState"));

        var loaded = Read(root);

        Assert.Equal(ProjectDocument.CURRENT_VERSION, loaded.Version);
        Assert.False(loaded.TimelineViewState.ExpandedAnimationClips.IsDefault);
        Assert.Empty(loaded.TimelineViewState.ExpandedAnimationClips);
        Assert.Equal("legacy", Assert.Single(loaded.Subtitles).Text);
        Assert.Equal(AnimationProperty.OPACITY, Assert.Single(Assert.Single(loaded.Layers).Tracks).Property);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void InvalidEffectIdentitiesAreRejectedByValidatorAndSerializer(int mutation)
    {
        var clip = new TimelineEffectClipId(Guid.NewGuid(), new(AnimationProperty.OPACITY));
        var state = new TimelineViewState { ExpandedAnimationClips = [clip] };
        state = mutation switch
        {
            0 => state with { ExpandedAnimationClips = [clip, clip with { }] },
            1 => state with { ExpandedAnimationClips = [clip with { LayerId = Guid.Empty }] },
            2 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.POSITION_X) }] },
            3 => state with { ExpandedAnimationClips = [clip with { Target = new((AnimationProperty)int.MaxValue) }] },
            4 => state with { ExpandedAnimationClips = [null!] },
            5 => state with { ExpandedAnimationClips = default },
            6 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.MASK_NODE_POSITION) }] },
            7 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.MASK_NODE_POSITION, Guid.Empty) }] },
            8 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.OPACITY, Guid.NewGuid()) }] },
            9 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.FILL, TextRangeId: Guid.Empty) }] },
            10 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.OPACITY, TextRangeId: Guid.NewGuid()) }] },
            11 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.MASK_NODE_POSITION, Guid.NewGuid(), Guid.NewGuid()) }] },
            12 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.POSITION, State: SubtitleAnimationState.ACTIVE) }] },
            13 => state with { ExpandedAnimationClips = [clip with { Target = new(AnimationProperty.FILL, State: (SubtitleAnimationState)int.MaxValue) }] },
            14 => null!,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var document = new ProjectDocument { TimelineViewState = state };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Serialize(document));
    }

    [Fact]
    public void ExpansionIdentityBudgetAllowsTheLimitAndRejectsTheNextEntry()
    {
        var identities = Enumerable.Range(0, 100000)
            .Select(_ => new TimelineEffectClipId(Guid.NewGuid(), new(AnimationProperty.OPACITY))).ToImmutableArray();
        var state = new TimelineViewState { ExpandedAnimationClips = identities };

        state.Validate();

        var excessive = state with { ExpandedAnimationClips = identities.Add(new(Guid.NewGuid(), new(AnimationProperty.OPACITY))) };
        Assert.Throws<InvalidDataException>(() => excessive.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void InvalidSerializedExpansionCannotBypassDocumentValidation(int mutation)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(new()
        {
            TimelineViewState = new()
            {
                ExpandedAnimationClips = [new(Guid.NewGuid(), new(AnimationProperty.OPACITY))]
            }
        }))!.AsObject();
        var state = root["timelineViewState"]!.AsObject();
        var clips = state["expandedAnimationClips"]!.AsArray();
        var clip = clips[0]!.AsObject();
        switch (mutation)
        {
            case 0:
                clips.Add(clip.DeepClone());
                break;
            case 1:
                clip["layerId"] = Guid.Empty;
                break;
            case 2:
                clip["target"]!["property"] = "POSITION_X";
                break;
            case 3:
                clip["target"]!["property"] = "UNKNOWN_PROPERTY";
                break;
            case 4:
                clips[0] = null;
                break;
            case 5:
                state["expandedAnimationClips"] = null;
                break;
            case 6:
                root["timelineViewState"] = null;
                break;
            case 7:
                Assert.True(clip.Remove("target"));
                break;
            case 8:
                Assert.True(clip.Remove("layerId"));
                break;
            case 9:
                clip["target"] = null;
                break;
            case 10:
                clip["unknown"] = 0;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.Throws<InvalidDataException>(() => Read(root));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LegacyAnimationRowsRemainStrictWhenPresent(int mutation)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(new()))!.AsObject();
        LegacyProjectJsonFixture.AddAnimationRows(root,
            [new(TimelineRowScope.TRACK, ProjectTrack.DEFAULT_TRACK_ID, AnimationProperty.OPACITY)]);
        var state = root["timelineViewState"]!.AsObject();
        var rows = state["collapsedAnimationRows"]!.AsArray();
        var row = rows[0]!.AsObject();
        switch (mutation)
        {
            case 0:
                rows.Add(row.DeepClone());
                break;
            case 1:
                row["ownerId"] = Guid.Empty;
                break;
            case 2:
                row["scope"] = "UNKNOWN_SCOPE";
                break;
            case 3:
                row["property"] = "POSITION_X";
                break;
            case 4:
                rows[0] = null;
                break;
            case 5:
                state["collapsedAnimationRows"] = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        Assert.Throws<InvalidDataException>(() => Read(root));
    }

    private static ProjectDocument Read(JsonObject root)
    {
        return ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
