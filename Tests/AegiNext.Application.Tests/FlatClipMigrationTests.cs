using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class FlatClipMigrationTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void NeutralGroupsPreserveActiveDrawingOrderAndSplitConflictingOwnersDeterministically(int version)
    {
        var root = LegacyDocument(version);
        var input = root.ToJsonString();
        var expectedIds = LeafIds(root["layers"]!.AsArray()).ToArray();

        var migrated = Read(root);
        var repeated = Read(root);

        Assert.Equal(ProjectStore.Serialize(migrated), ProjectStore.Serialize(repeated));
        Assert.Equal(input, root.ToJsonString());
        Assert.Equal(ProjectDocument.CURRENT_VERSION, migrated.Version);
        Assert.Equal(2, migrated.Tracks.Count(track => track.DefaultStyle?.FontSize == 37));
        var subtitleTracks = migrated.Layers.Where(clip => clip.Kind == LayerKind.SUBTITLE).Select(clip => clip.TrackId).ToArray();
        Assert.Equal(2, subtitleTracks.Distinct().Count());
        Assert.All(subtitleTracks, id => Assert.Contains(id, migrated.TimelineViewState.CollapsedTrackIds));
        Assert.All(subtitleTracks, id => Assert.Contains(new(TimelineRowScope.TRACK, id, AnimationProperty.OPACITY),
            migrated.TimelineViewState.CollapsedAnimationRows));
        Assert.Equal(expectedIds.Take(3), SceneEvaluator.Evaluate(migrated, new(2)).Select(clip => clip.Source.Id));
        Assert.Equal(new[] { expectedIds[0], expectedIds[2], expectedIds[3] },
            SceneEvaluator.Evaluate(migrated, new(6)).Select(clip => clip.Source.Id));
        Assert.Equal(ProjectStore.Serialize(migrated), ProjectStore.Serialize(ProjectStore.Deserialize(ProjectStore.Serialize(migrated))));
    }

    [Theory]
    [InlineData("opacity")]
    [InlineData("transform")]
    [InlineData("blend")]
    [InlineData("blur")]
    [InlineData("tracks")]
    [InlineData("time")]
    [InlineData("childBlend")]
    public async Task ComplexGroupRejectsWithItsIdentityAndNeverRewritesTheFile(string effect)
    {
        var root = LegacyDocument(8);
        var group = root["layers"]![1]!.AsObject();
        switch (effect)
        {
            case "opacity":
                group["opacity"] = 0.5;
                break;
            case "transform":
                group["transform"]!["position"]!["x"] = 10;
                break;
            case "blend":
                group["blend"] = "SCREEN";
                break;
            case "blur":
                group["blur"] = 2;
                break;
            case "tracks":
                var editor = new ProjectEditor();
                var id = editor.AddSubtitle(new(0), new(2), "animation");
                editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.5));
                group["tracks"] = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!["layers"]![0]!["tracks"]!.DeepClone();
                break;
            case "time":
                group["end"] = JsonNode.Parse("{\"numerator\":1,\"denominator\":1}");
                break;
            case "childBlend":
                group["children"]![0]!["blend"] = "MULTIPLY";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect));
        }
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "complex.aeginext");
        await File.WriteAllBytesAsync(path, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));

        Assert.Contains("Legacy group", error.Message, StringComparison.Ordinal);
        Assert.Contains(group["id"]!.GetValue<Guid>().ToString(), error.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \n\t ")]
    [InlineData("image\nlabel")]
    public void SceneTrackGetsAValidLabelWithoutChangingTheOriginalClipName(string name)
    {
        var root = LegacyDocument(8);
        root["layers"]![0]!["name"] = name;

        var migrated = Read(root);

        Assert.Equal(name, migrated.Layers[0].Name);
        var track = migrated.Tracks.Single(track => track.Id == migrated.Layers[0].TrackId);
        Assert.False(string.IsNullOrWhiteSpace(track.Name));
        Assert.DoesNotContain(track.Name, char.IsControl);
    }

    [Fact]
    public void CurrentContractRejectsBothLegacyTreeAndDuplicateSubtitleOwnership()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(2), "strict");
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        root["layers"]![0]!["children"] = new JsonArray();
        Assert.Throws<InvalidDataException>(() => Read(root));
        root["layers"]![0]!.AsObject().Remove("children");
        root["subtitles"]![0]!["trackId"] = ProjectTrack.DEFAULT_TRACK_ID;
        Assert.Throws<InvalidDataException>(() => Read(root));
    }

    [Fact]
    public void EqualLegacySceneAndSubtitleTrackIdsKeepIndependentCollapsedOwners()
    {
        var root = LegacyDocument(8);
        root["layers"]![0]!["id"] = ProjectTrack.DEFAULT_TRACK_ID;
        root["timelineViewState"]!["collapsedAnimationRows"]!.AsArray().Add(new JsonObject
        {
            ["scope"] = "SCENE_LAYER", ["ownerId"] = ProjectTrack.DEFAULT_TRACK_ID, ["property"] = "OPACITY"
        });

        var migrated = Read(root);

        Assert.Equal(3, migrated.TimelineViewState.CollapsedTrackIds.Length);
        Assert.Equal(3, migrated.TimelineViewState.CollapsedAnimationRows.Length);
        Assert.Contains(migrated.Layers[0].TrackId, migrated.TimelineViewState.CollapsedTrackIds);
        Assert.Contains(new(TimelineRowScope.TRACK, migrated.Layers[0].TrackId, AnimationProperty.OPACITY),
            migrated.TimelineViewState.CollapsedAnimationRows);
        Assert.All(migrated.Layers.Where(clip => clip.Kind == LayerKind.SUBTITLE), clip =>
            Assert.Contains(clip.TrackId, migrated.TimelineViewState.CollapsedTrackIds));
    }

    private static JsonObject LegacyDocument(int version)
    {
        var shapeTrack = new ProjectTrack { Name = "Shape" };
        var imageTrack = new ProjectTrack { Name = "Image" };
        var imageAsset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/image.png");
        var editor = new ProjectEditor(new()
        {
            Tracks = [ProjectTrack.Default with { DefaultStyle = new() { FontSize = 37 }, StylePresetId = Guid.NewGuid(), StylePresetName = "Legacy style", AutoApplyStyle = false }, shapeTrack, imageTrack],
            Assets = [imageAsset]
        });
        editor.AddSubtitle(new(0), new(4), "first");
        editor.AddSubtitle(new(4), new(8), "second");
        var shape = new ProjectLayer { TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 10, 10) };
        var image = new ProjectLayer { TrackId = imageTrack.Id, Kind = LayerKind.IMAGE, Image = new(imageAsset.Id, 10, 10) };
        var document = editor.Snapshot with
        {
            Layers = [shape, editor.Snapshot.Layers[0], image, editor.Snapshot.Layers[1]],
            TimelineViewState = new()
            {
                CollapsedTrackIds = [ProjectTrack.DEFAULT_TRACK_ID],
                CollapsedAnimationRows = [new(TimelineRowScope.TRACK, ProjectTrack.DEFAULT_TRACK_ID, AnimationProperty.OPACITY)]
            }
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        LegacyProjectJsonFixture.AddAnimationRows(root, document.TimelineViewState.CollapsedAnimationRows);
        LegacyProjectJsonFixture.Downgrade(root, version);
        var layers = root["layers"]!.AsArray();
        var group = layers[0]!.DeepClone().AsObject();
        group["id"] = Guid.NewGuid();
        group["name"] = "Legacy group";
        group["kind"] = "GROUP";
        group["shape"] = null;
        group["children"] = new JsonArray(layers[1]!.DeepClone(), layers[2]!.DeepClone());
        root["layers"] = new JsonArray(layers[0]!.DeepClone(), group, layers[3]!.DeepClone());
        return root;
    }

    private static IEnumerable<Guid> LeafIds(JsonArray layers)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            if (layer["kind"]!.GetValue<string>() == "GROUP")
            {
                foreach (var id in LeafIds(layer["children"]!.AsArray()))
                {
                    yield return id;
                }
            }
            else
            {
                yield return layer["id"]!.GetValue<Guid>();
            }
        }
    }

    private static ProjectDocument Read(JsonObject root)
    {
        return ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
