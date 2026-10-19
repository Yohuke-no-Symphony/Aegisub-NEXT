using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

/// <summary>验证独立边距的严格存储和旧标量契约迁移。</summary>
public sealed class SubtitleMarginsPersistenceTests
{
    /// <summary>旧工程的字幕和轨道默认样式同时迁移，各入口保持原位置和精确内容时钟。</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task LegacyProjectsMigrateAllStylesWithoutChangingExplicitPositionOrContent(int version)
    {
        using var directory = new TemporaryProjectDirectory();
        var original = Document(new(27.25, 27.25, 27.25));
        var root = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
        root["version"] = version;
        LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        var expected = ProjectStore.Serialize(original);
        using var parsed = JsonDocument.Parse(bytes);

        Assert.Equal(expected, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
        Assert.Equal(expected, ProjectStore.Serialize(ProjectStore.Deserialize(parsed.RootElement)));
        var path = Path.Combine(directory.Path, "legacy.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.Equal(expected, ProjectStore.Serialize(await ProjectStore.LoadAsync(path)));
        Assert.Equal(version, root["version"]!.GetValue<int>());
        Assert.True(root["subtitles"]![0]!["style"]!.AsObject().ContainsKey("margin"));
    }

    /// <summary>当前工程与样式库保存每个独立值而不折叠为统一边距。</summary>
    [Fact]
    public void CurrentProjectAndStyleLibraryRoundTripIndependentMargins()
    {
        var document = Document(new(12.25, 73.5, 9.75));
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(document));
        Assert.Equal(document.Subtitles[0].Style, restored.Subtitles[0].Style);
        Assert.Equal(document.Tracks[0].DefaultStyle, restored.Tracks[0].DefaultStyle);
        var collection = new SubtitleStylePresetCollection
        {
            Presets = [new(Guid.NewGuid(), "Independent", document.Subtitles[0].Style)]
        };
        var encoded = SubtitleStylePresetMetadataStore.Serialize(collection);
        var library = SubtitleStylePresetMetadataStore.Deserialize(encoded);
        Assert.Equal(collection.Presets[0], library.Presets[0]);
        var root = JsonNode.Parse(encoded)!;
        Assert.Equal(SubtitleStylePresetCollection.CURRENT_VERSION, root["version"]!.GetValue<int>());
        Assert.False(root["presets"]![0]!["style"]!.AsObject().ContainsKey("margin"));
    }

    /// <summary>可选轨道默认样式在旧工程升级时保持缺省或 null，不创建额外样式。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyProjectsKeepNullOrOmittedTrackDefaults(bool omit)
    {
        foreach (var version in new[] { 3, 4, 5, 6, 7 })
        {
            var original = Document(new()) with { Tracks = [ProjectTrack.Default] };
            var root = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
            root["version"] = version;
            LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
            if (omit)
            {
                root["subtitleTracks"]![0]!.AsObject().Remove("defaultStyle");
            }
            var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString()));

            Assert.Null(Assert.Single(restored.Tracks).DefaultStyle);
            Assert.Equal(ProjectStore.Serialize(original), ProjectStore.Serialize(restored));
        }
    }

    /// <summary>所有旧样式库版本迁移标量边距，保留位置与关联处理器。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void LegacyStyleLibrariesAreRejected(int version)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{{\"version\":{version},\"presets\":[]}}");
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(bytes));
    }

    /// <summary>当前版本不能接受旧字段、缺失边距或不完整的独立边距对象。</summary>
    [Theory]
    [InlineData("missingMargins")]
    [InlineData("missingLeft")]
    [InlineData("missingRight")]
    [InlineData("missingVertical")]
    [InlineData("legacyMargin")]
    [InlineData("unknownEdge")]
    [InlineData("negative")]
    [InlineData("tooLarge")]
    [InlineData("nullMargins")]
    public void CurrentContractsRejectMissingUnknownLegacyOrInvalidMargins(string mutation)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(Document(new())))!.AsObject();
        MutateCurrentStyle(root["subtitles"]![0]!["style"]!.AsObject(), mutation);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        root = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(new()
        {
            Presets = [new(Guid.NewGuid(), "Strict", new())]
        }))!.AsObject();
        MutateCurrentStyle(root["presets"]![0]!["style"]!.AsObject(), mutation);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
    }

    /// <summary>旧版本不能冒认新字段，也不能通过迁移补全缺失或无效标量。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("modern")]
    [InlineData("mixed")]
    [InlineData("negative")]
    [InlineData("tooLarge")]
    [InlineData("string")]
    [InlineData("null")]
    public void LegacyContractsRejectIncompleteInvalidOrUndeclaredMarginFields(string mutation)
    {
        foreach (var version in new[] { 3, 4, 5, 6, 7 })
        {
            var root = JsonNode.Parse(ProjectStore.Serialize(Document(new())))!.AsObject();
            root["version"] = version;
            LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
            MutateLegacyStyle(root["subtitles"]![0]!["style"]!.AsObject(), mutation);
            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        }
        foreach (var version in new[] { 1, 2, 3, 4 })
        {
            var root = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(new()
            {
                Presets = [new(Guid.NewGuid(), "Strict", new())]
            }))!.AsObject();
            root["version"] = version;
            LegacySubtitleMarginsJsonFixture.DowngradeLibrary(root);
            var style = root["presets"]![0]!["style"]!.AsObject();
            if (version == 1)
            {
                style.Remove("position");
            }
            MutateLegacyStyle(style, mutation);
            Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(root.ToJsonString())));
        }
    }

    /// <summary>边距内部的转义重复键在迁移或反序列化之前拒绝。</summary>
    [Fact]
    public void EscapedDuplicateMarginKeysAreRejected()
    {
        var json = Encoding.UTF8.GetString(ProjectStore.Serialize(Document(new())));
        var duplicate = json.Replace("\"left\": 40,", "\"left\": 40, \"le\\u0066t\": 40,", StringComparison.Ordinal);
        Assert.NotEqual(json, duplicate);
        var bytes = Encoding.UTF8.GetBytes(duplicate);
        using var parsed = JsonDocument.Parse(bytes);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(bytes));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(parsed.RootElement));
        json = Encoding.UTF8.GetString(SubtitleStylePresetMetadataStore.Serialize(new()
        {
            Presets = [new(Guid.NewGuid(), "Strict", new())]
        }));
        duplicate = json.Replace("\"left\": 40,", "\"left\": 40, \"le\\u0066t\": 40,", StringComparison.Ordinal);
        Assert.NotEqual(json, duplicate);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encoding.UTF8.GetBytes(duplicate)));
    }

    private static ProjectDocument Document(SubtitleMargins margins)
    {
        var style = new SubtitleStyle
        {
            Margins = margins, Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0, 1), Offset = new(17.125, -29.25) }
        };
        var line = new SubtitleLine { Text = "ab", Start = new(1001, 30000), End = new(61001, 30000), Style = style,
            InlineSpans = [new(1, 1, new() { Italic = true })],
            Karaoke = [new(0, 1, new(0), new(1, 3), SceneColor.White)] };
        return new()
        {
            Tracks = [ProjectTrack.Default with
            {
                DefaultStyle = style, StylePresetId = Guid.NewGuid(), StylePresetName = "Track default"
            }], Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }

    private static void MutateCurrentStyle(JsonObject style, string mutation)
    {
        switch (mutation)
        {
            case "missingMargins":
                style.Remove("margins");
                break;
            case "missingLeft":
                style["margins"]!.AsObject().Remove("left");
                break;
            case "missingRight":
                style["margins"]!.AsObject().Remove("right");
                break;
            case "missingVertical":
                style["margins"]!.AsObject().Remove("vertical");
                break;
            case "legacyMargin":
                style["margin"] = 40;
                break;
            case "unknownEdge":
                style["margins"]!["top"] = 40;
                break;
            case "negative":
                style["margins"]!["left"] = -1;
                break;
            case "tooLarge":
                style["margins"]!["right"] = 32769;
                break;
            case "nullMargins":
                style["margins"] = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private static void MutateLegacyStyle(JsonObject style, string mutation)
    {
        switch (mutation)
        {
            case "missing":
                style.Remove("margin");
                break;
            case "modern":
                style.Remove("margin");
                style["margins"] = new JsonObject { ["left"] = 40, ["right"] = 40, ["vertical"] = 40 };
                break;
            case "mixed":
                style["margins"] = new JsonObject { ["left"] = 40, ["right"] = 40, ["vertical"] = 40 };
                break;
            case "negative":
                style["margin"] = -1;
                break;
            case "tooLarge":
                style["margin"] = 32769;
                break;
            case "string":
                style["margin"] = "40";
                break;
            case "null":
                style["margin"] = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }
}
