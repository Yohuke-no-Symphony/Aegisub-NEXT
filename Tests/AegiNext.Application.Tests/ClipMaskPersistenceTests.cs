using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ClipMaskPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentVersionRoundTripPreservesMaskGeometryIdentityAndIndependentTransform(bool vector)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "mask");
        ClipMask mask = vector ? new VectorClipMask
        {
            Contours = [new()
            {
                Id = Guid.NewGuid(), Nodes =
                [
                    new() { Id = Guid.NewGuid(), Position = new(10, 20), OutHandle = new(4, -5) },
                    new() { Id = Guid.NewGuid(), Position = new(100, 20), InHandle = new(-4, -5) },
                    new() { Id = Guid.NewGuid(), Position = new(100, 80) }
                ]
            }]
        } : new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(100, 80) };
        mask = mask with
        {
            Inverted = true,
            Placement = new() { Anchor = new(0.25, 0.75), Pivot = new(0.5, 0.5), Offset = new(16, -8) },
            ReferenceFrame = new() { Origin = new(10, 20), Size = new(90, 60) },
            Transform = new() { Position = new(13, -7), Scale = new(2, 3), Rotation = 17 }
        };
        editor.SetClipMask(id, mask);
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(0), new ScenePoint(40, 50), KeyframeInterpolation.EASE_IN)
        {
            CurveStart = 0.1, CurveEnd = 0.9,
            ComponentCurves = [new(KeyframeInterpolation.EASE_OUT, 0.2, 0.8)]
        });
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(3, 2), new ScenePoint(60, 80)));
        var bytes = ProjectStore.Serialize(editor.Snapshot);
        var root = JsonNode.Parse(bytes)!;
        Assert.Equal(ProjectDocument.CURRENT_VERSION, root["version"]!.GetValue<int>());
        Assert.Equal(vector ? "VECTOR" : "RECTANGLE", root["layers"]![0]!["mask"]!["kind"]!.GetValue<string>());
        var track = root["layers"]![0]!["tracks"]![0]!.AsObject();
        Assert.False(track.ContainsKey("property"));
        Assert.Equal("POSITION", track["target"]!["property"]!.GetValue<string>());
        Assert.Null(track["target"]!["nodeId"]);
        Assert.Equal(bytes, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public void LegacyEmptyMasksUpgradeAndPresetFieldsAreRemoved(int version, bool omit)
    {
        var root = LegacyProjectJsonFixture.Create(version);
        var layer = root["layers"]![0]!.AsObject();
        var preset = root["presets"]![0]!.AsObject();
        if (omit)
        {
            layer.Remove("mask");
            preset.Remove("mask");
        }
        var input = root.ToJsonString();
        var document = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(input));
        Assert.Equal(ProjectDocument.CURRENT_VERSION, document.Version);
        Assert.Null(document.Layers[0].Mask);
        Assert.Equal(input, root.ToJsonString());
        var saved = JsonNode.Parse(ProjectStore.Serialize(document))!;
        Assert.False(saved["presets"]![0]!.AsObject().ContainsKey("mask"));
        Assert.Equal("OPACITY", saved["layers"]![0]!["tracks"]![0]!["target"]!["property"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public async Task LegacyNonemptyMaskRejectsWithObjectLocationAndLeavesFileUntouched(int version, bool preset)
    {
        var root = LegacyProjectJsonFixture.Create(version);
        var owner = preset ? root["presets"]![0]!.AsObject() : root["layers"]![0]!.AsObject();
        owner["mask"] = new JsonObject { ["path"] = new JsonObject(), ["inverted"] = false };
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "legacy.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));
        Assert.Contains(preset ? "presets[0]" : "layers[0]", error.Message, StringComparison.Ordinal);
        Assert.Contains(owner["id"]!.GetValue<string>(), error.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void VersionFiveRejectsMissingUnknownAndLegacyTrackTargetFields(int mutation)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "strict");
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.5));
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        var track = root["layers"]![0]!["tracks"]![0]!.AsObject();
        switch (mutation)
        {
            case 0:
                track.Remove("target");
                break;
            case 1:
                track["property"] = "OPACITY";
                break;
            case 2:
                track["target"]!.AsObject().Remove("property");
                break;
            case 3:
                track["target"]!.AsObject().Remove("nodeId");
                break;
            case 4:
                track["target"]!["unexpected"] = true;
                break;
            case 5:
                track["target"]!["nodeId"] = Guid.NewGuid().ToString();
                break;
            default:
                track.Remove("target");
                track["property"] = "OPACITY";
                break;
        }
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void VersionFiveMaskPayloadIsStrict(int mutation)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "strict mask");
        editor.SetClipMask(id, new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(100, 80) });
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        var mask = root["layers"]![0]!["mask"]!.AsObject();
        switch (mutation)
        {
            case 0:
                mask.Remove("transform");
                break;
            case 1:
                mask["path"] = new JsonObject();
                break;
            case 2:
                mask["kind"] = "UNKNOWN";
                break;
            default:
                mask["topLeft"]!.AsObject().Remove("x");
                break;
        }
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    public void LegacyTrackMigrationRejectsMissingMixedAndUnknownFields(int version, int mutation)
    {
        var root = LegacyProjectJsonFixture.Create(version);
        var track = root["layers"]![0]!["tracks"]![0]!.AsObject();
        switch (mutation)
        {
            case 0:
                track.Remove("property");
                break;
            case 1:
                track["target"] = new JsonObject { ["property"] = "OPACITY", ["nodeId"] = null };
                break;
            default:
                track["unexpected"] = true;
                break;
        }
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public void LegacyNestedMaskReportsFullLayerLocation()
    {
        var root = LegacyProjectJsonFixture.Create(4);
        var child = root["layers"]![0]!.DeepClone();
        var layer = root["layers"]![0]!.AsObject();
        layer["children"] = new JsonArray(child);
        child["mask"] = new JsonObject();
        var error = Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        Assert.Contains("layers[0].children[0]", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionFiveRejectsLegacyCurvesAndTransformAliases(bool transform)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "strict aliases");
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(0), new ScenePoint(40, 50)));
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        if (transform)
        {
            root["layers"]![0]!["transform"]!["x"] = 10;
        }
        else
        {
            root["layers"]![0]!["tracks"]![0]!["keyframes"]![0]!["vectorCurve"] = null;
        }
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Fact]
    public void VersionFiveDoesNotAcceptRemovedPresetMaskField()
    {
        var editor = new ProjectEditor(new()
        {
            Presets = [new(Guid.NewGuid(), "current", [new(AnimationProperty.OPACITY, [new(new(0), 0.4)])])]
        });
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!;
        root["presets"]![0]!["mask"] = null;
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }
}
