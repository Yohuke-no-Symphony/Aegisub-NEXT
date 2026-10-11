using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectStoreStrictParsingTests
{
    [Theory]
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
    public async Task EscapedDuplicateKeysInsideNestedObjectsAreRejectedByAllEntriesBeforeMigration(int version)
    {
        using var directory = new TemporaryProjectDirectory();
        var node = JsonNode.Parse(ProjectStore.Serialize(CreateAnimatedDocument()))!.AsObject();
        node["version"] = version;
        if (version < ProjectDocument.CURRENT_VERSION)
        {
            LegacySubtitleMarginsJsonFixture.DowngradeProject(node);
        }
        var json = node.ToJsonString();
        var modified = json.Replace("\"fontSize\":64", "\"fontSize\":64,\"font\\u0053ize\":64", StringComparison.Ordinal);
        Assert.NotEqual(json, modified);
        var bytes = Encoding.UTF8.GetBytes(modified);
        using var parsed = JsonDocument.Parse(bytes);

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(bytes));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(parsed.RootElement));
        var path = Path.Combine(directory.Path, "duplicate.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("scaleX")]
    [InlineData("scaleY")]
    [InlineData("anchorX")]
    [InlineData("anchorY")]
    [InlineData("property")]
    [InlineData("vectorCurve")]
    public void CurrentVersionsRejectIgnoredLegacyAliasesInFlatClips(string field)
    {
        foreach (var version in new[] { 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 })
        {
            var root = JsonNode.Parse(ProjectStore.Serialize(CreateAnimatedDocument()))!.AsObject();
            root["version"] = version;
            if (version < ProjectDocument.CURRENT_VERSION)
            {
                LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
            }
            var layer = root["layers"]![0]!.AsObject();
            if (field == "property")
            {
                layer["tracks"]![0]![field] = "OPACITY";
            }
            else if (field == "vectorCurve")
            {
                layer["tracks"]![0]!["keyframes"]![0]![field] = null;
            }
            else
            {
                layer["transform"]![field] = 0;
            }
            var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
            using var parsed = JsonDocument.Parse(bytes);

            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(bytes));
            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(parsed.RootElement));
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public async Task FileAndExternalElementEntriesPreserveLegacyMigration(int version)
    {
        using var directory = new TemporaryProjectDirectory();
        var node = LegacyProjectJsonFixture.Create(version);
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        var expected = ProjectStore.Serialize(ProjectStore.Deserialize(bytes));
        using var parsed = JsonDocument.Parse(bytes);

        Assert.Equal(expected, ProjectStore.Serialize(ProjectStore.Deserialize(parsed.RootElement)));
        var path = Path.Combine(directory.Path, "legacy.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.Equal(expected, ProjectStore.Serialize(await ProjectStore.LoadAsync(path)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void CurrentVersionsRejectIgnoredLegacyAliasesInPresets(int version)
    {
        var document = CreateAnimatedDocument() with
        {
            Presets = [new(Guid.NewGuid(), "Preset", [new(AnimationProperty.OPACITY, [new(new(0), 0.4)])])]
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = version;
        if (version < ProjectDocument.CURRENT_VERSION)
        {
            LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        }
        root["presets"]![0]!["tracks"]![0]!["property"] = "OPACITY";

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public async Task PlaybackOriginMigrationAndRequiredFieldsRemainStrictForFileAndElementEntries(int version)
    {
        using var directory = new TemporaryProjectDirectory();
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: "/media/source.mkv");
        var document = CreateAnimatedDocument() with
        {
            Assets = [asset],
            Media = new(asset.Id, 0, null, new(1001, 30000))
            {
                PlaybackOrigin = new(-1001, 30000)
            }
        };
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        root["version"] = version;
        if (version < ProjectDocument.CURRENT_VERSION)
        {
            LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        }
        if (version == 5)
        {
            root["media"]!.AsObject().Remove("playbackOrigin");
            document = document with
            {
                Media = document.Media! with
                {
                    PlaybackOrigin = null
                }
            };
        }
        var expected = ProjectStore.Serialize(document);
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        using var parsed = JsonDocument.Parse(bytes);
        Assert.Equal(expected, ProjectStore.Serialize(ProjectStore.Deserialize(parsed.RootElement)));
        var path = Path.Combine(directory.Path, "project.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.Equal(expected, ProjectStore.Serialize(await ProjectStore.LoadAsync(path)));

        if (version >= 6)
        {
            root["media"]!.AsObject().Remove("playbackOrigin");
            var missing = Encoding.UTF8.GetBytes(root.ToJsonString());
            using var invalid = JsonDocument.Parse(missing);
            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(invalid.RootElement));
            await File.WriteAllBytesAsync(path, missing);
            await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));
        }
    }

    private static ProjectDocument CreateAnimatedDocument()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "subtitle");
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.25));
        return editor.Snapshot;
    }
}
