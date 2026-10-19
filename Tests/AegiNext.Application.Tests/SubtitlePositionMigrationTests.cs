using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class SubtitlePositionMigrationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OldProjectVersionsAreRejectedWithoutPositionOrAnimationMigration(int version)
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(MediaTime.Zero, new(2), "legacy");
        var legacy = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        legacy["version"] = version;
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(legacy.ToJsonString())));
        Assert.Equal(ProjectDocument.CURRENT_VERSION, editor.Snapshot.Version);
    }

    [Fact]
    public void VersionOneMigrationStillRejectsMissingOldFieldsAndInvalidTrackCollections()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(MediaTime.Zero, new(2), "legacy");
        var legacy = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        MakeLegacy(legacy);
        legacy["subtitles"]![0]!["style"]!.AsObject().Remove("fontSize");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(legacy.ToJsonString())));

        legacy = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        MakeLegacy(legacy);
        legacy["layers"]![0]!["tracks"] = new JsonArray(new JsonObject
        {
            ["property"] = "OPACITY", ["keyframes"] = new JsonArray()
        });
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(legacy.ToJsonString())));
    }

    [Fact]
    public void CurrentProjectRejectsMissingPositionInsteadOfSilentlyInferringIt()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(MediaTime.Zero, new(2), "strict");
        var current = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        current["subtitles"]![0]!["style"]!.AsObject().Remove("position");

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(current.ToJsonString())));
    }

    [Fact]
    public void CurrentProjectRejectsMissingCurveFieldsAndAnimationOutsideTheClip()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(MediaTime.Zero, new(2), "strict");
        editor.Apply("Track", document => document with
        {
            Layers = [document.Layers[0] with
            {
                Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0), new(new(2), 1)])]
            }]
        });
        var current = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        var frame = current["layers"]![0]!["tracks"]![0]!["keyframes"]![1]!.AsObject();
        frame.Remove("curveStart");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(current.ToJsonString())));
        frame["curveStart"] = 0;
        frame["time"]!["numerator"] = 4;
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(current.ToJsonString())));
    }

    [Fact]
    public void VersionOneStyleLibraryAndVersionTwoAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize("{\"version\":1,\"presets\":[]}"u8));
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize("{\"version\":2,\"presets\":[]}"u8));
    }

    [Fact]
    public void VersionTwoLibraryRejectsIncompleteOrOutOfRangePosition()
    {
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "position", new() { Position = new() });
        var current = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [preset] }))!.AsObject();
        var position = current["presets"]![0]!["style"]!["position"]!.AsObject();
        position.Remove("pivot");
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(current.ToJsonString())));
        position["pivot"] = new JsonObject { ["x"] = 0.5, ["y"] = 1 };
        position["anchor"]!["x"] = 2;
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(current.ToJsonString())));
    }

    private static void MakeLegacy(JsonObject root)
    {
        root["version"] = 1;
        if (root["presets"] is JsonArray)
        {
            LegacySubtitleMarginsJsonFixture.DowngradeLibrary(root);
        }
        else
        {
            LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        }
        RemoveNewFields(root);
    }

    private static void RemoveNewFields(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            value.Remove("position");
            value.Remove("curveStart");
            value.Remove("curveEnd");
            foreach (var property in value)
            {
                RemoveNewFields(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RemoveNewFields(item);
            }
        }
    }
}
