using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

/// <summary>验证字幕参考尺寸的严格存储及旧工程、样式库的兼容迁移。</summary>
public sealed class SubtitleScalingPersistenceTests
{
    /// <summary>旧工程的完整样式以原画布作为参考，所有加载入口保留原样式与文件。</summary>
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
    public async Task LegacyProjectsUseOriginalCanvasForEveryFullStyle(int version)
    {
        using var directory = new TemporaryProjectDirectory();
        var original = Document(1280, 720);
        var root = LegacyProject(original, version);
        var bytes = Encode(root);
        var expected = ProjectStore.Serialize(original);
        using var parsed = JsonDocument.Parse(bytes);

        Assert.Equal(expected, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
        Assert.Equal(expected, ProjectStore.Serialize(ProjectStore.Deserialize(parsed.RootElement)));
        var path = Path.Combine(directory.Path, "legacy-scaling.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.Equal(expected, ProjectStore.Serialize(await ProjectStore.LoadAsync(path)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(version, root["version"]!.GetValue<int>());
        Assert.False(root["subtitles"]![0]!["style"]!.AsObject().ContainsKey("scaling"));
    }

    /// <summary>旧工程缺省或空的轨道默认样式不会生成新的样式对象。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyProjectsKeepNullOrOmittedTrackDefaults(bool omit)
    {
        foreach (var version in Enumerable.Range(3, 12))
        {
            var original = Document(1280, 720) with { Tracks = [ProjectTrack.Default] };
            var root = LegacyProject(original, version);
            if (omit)
            {
                (root["tracks"] ?? root["subtitleTracks"])![0]!.AsObject().Remove("defaultStyle");
            }

            var restored = ProjectStore.Deserialize(Encode(root));
            Assert.Null(Assert.Single(restored.Tracks).DefaultStyle);
            Assert.Equal(ProjectStore.Serialize(original), ProjectStore.Serialize(restored));
        }
    }

    /// <summary>旧样式库统一补齐 1080p 参考与居中匹配权重，并保持原预设数据。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task LegacyLibrariesAreRejected(int version)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{{\"version\":{version},\"presets\":[]}}");
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(bytes));
    }

    /// <summary>当前工程和样式库完整保留用户设定的参考尺寸与匹配权重。</summary>
    [Fact]
    public void CurrentProjectAndLibraryRoundTripCustomScaling()
    {
        var scaling = new SubtitleScalingSettings { ReferenceWidth = 2048, ReferenceHeight = 858, MatchWidthOrHeight = 0.75 };
        var original = Document(1280, 720);
        original = original with
        {
            Subtitles = [original.Subtitles[0] with { Style = original.Subtitles[0].Style with { Scaling = scaling } }],
            Tracks = [original.Tracks[0] with { DefaultStyle = original.Tracks[0].DefaultStyle! with { Scaling = scaling } }]
        };
        var bytes = ProjectStore.Serialize(original);
        Assert.Equal(bytes, ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
        Assert.Equal(ProjectDocument.CURRENT_VERSION, ProjectStore.Deserialize(bytes).Version);
        var collection = Library(original.Subtitles[0].Style);
        bytes = SubtitleStylePresetMetadataStore.Serialize(collection);
        Assert.Equal(bytes, SubtitleStylePresetMetadataStore.Serialize(SubtitleStylePresetMetadataStore.Deserialize(bytes)));
        Assert.Equal(SubtitleStylePresetCollection.CURRENT_VERSION, SubtitleStylePresetMetadataStore.Deserialize(bytes).Version);
    }

    /// <summary>当前完整样式拒绝缺失、空、未知、不完整或非法的参考设置。</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("missingWidth")]
    [InlineData("missingHeight")]
    [InlineData("missingMatch")]
    [InlineData("unknown")]
    [InlineData("zeroWidth")]
    [InlineData("negativeHeight")]
    [InlineData("largeWidth")]
    [InlineData("negativeMatch")]
    [InlineData("largeMatch")]
    [InlineData("stringWidth")]
    public void CurrentContractsRejectIncompleteUnknownOrInvalidScaling(string mutation)
    {
        foreach (var path in new[] { "line", "track" })
        {
            var root = JsonNode.Parse(ProjectStore.Serialize(Document(1920, 1080)))!.AsObject();
            MutateScaling(StyleAt(root, path), mutation);
            var bytes = Encode(root);
            using var parsed = JsonDocument.Parse(bytes);
            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(bytes));
            Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(parsed.RootElement));
        }

        var library = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(Library(new())))!.AsObject();
        MutateScaling(library["presets"]![0]!["style"]!.AsObject(), mutation);
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encode(library)));
    }

    /// <summary>任何旧版本都不能携带未声明的参考设置，即使字段值为空。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyContractsRejectUndeclaredScaling(bool nullValue)
    {
        foreach (var version in Enumerable.Range(3, 12))
        {
            foreach (var path in new[] { "line", "track" })
            {
                var root = LegacyProject(Document(1280, 720), version);
                StyleAt(root, path)["scaling"] = nullValue ? null : ScalingNode();
                Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(root)));
            }
        }

        foreach (var version in Enumerable.Range(1, 6))
        {
            var root = JsonNode.Parse(SubtitleStylePresetMetadataStore.Serialize(Library(new())))!.AsObject();
            root["version"] = version;
            LegacySubtitleMarginsJsonFixture.DowngradeLibrary(root);
            if (version == 1)
            {
                root["presets"]![0]!["style"]!.AsObject().Remove("position");
            }
            root["presets"]![0]!["style"]!["scaling"] = nullValue ? null : ScalingNode();
            Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(Encode(root)));
        }
    }

    /// <summary>参考设置的转义重复键在迁移或反序列化前被各加载入口拒绝。</summary>
    [Fact]
    public async Task EscapedDuplicateScalingKeysAreRejected()
    {
        using var directory = new TemporaryProjectDirectory();
        var json = Encoding.UTF8.GetString(ProjectStore.Serialize(Document(1920, 1080)));
        var duplicate = DuplicateWidth(json);
        var bytes = Encoding.UTF8.GetBytes(duplicate);
        using var parsed = JsonDocument.Parse(bytes);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(bytes));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(parsed.RootElement));
        var path = Path.Combine(directory.Path, "duplicate.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));

        json = Encoding.UTF8.GetString(SubtitleStylePresetMetadataStore.Serialize(Library(new())));
        bytes = Encoding.UTF8.GetBytes(DuplicateWidth(json));
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetMetadataStore.Deserialize(bytes));
        path = Path.Combine(directory.Path, "duplicate.aegistyles");
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => SubtitleStylePresetMetadataStore.LoadAsync(path));
    }

    private static ProjectDocument Document(int width, int height)
    {
        var style = new SubtitleStyle
        {
            Scaling = new() { ReferenceWidth = width, ReferenceHeight = height },
            Position = new() { Anchor = new(0.25, 0.75), Pivot = new(0, 1), Offset = new(17.125, -29.25) }
        };
        var line = new SubtitleLine
        {
            Text = "ab", Start = new(1001, 30000), End = new(61001, 30000), Style = style,
            InlineSpans = [new(1, 1, new() { FontSize = 27.5, Italic = true })],
            Karaoke = [new(0, 1, new(0), new(1, 3), SceneColor.White)]
        };
        return new()
        {
            Width = width, Height = height,
            Tracks = [ProjectTrack.Default with
            {
                DefaultStyle = style, StylePresetId = Guid.NewGuid(), StylePresetName = "Scaling"
            }], Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }

    private static SubtitleStylePresetCollection Library(SubtitleStyle style)
    {
        return new() { Presets = [new(Guid.NewGuid(), "Scaling", style)] };
    }

    private static JsonObject LegacyProject(ProjectDocument document, int version)
    {
        var root = JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        if (version < 5)
        {
            LegacyProjectJsonFixture.Downgrade(root, version);
        }
        else
        {
            root["version"] = version;
            LegacySubtitleMarginsJsonFixture.DowngradeProject(root);
        }
        return root;
    }

    private static JsonObject StyleAt(JsonObject root, string path)
    {
        return path == "line" ? root["subtitles"]![0]!["style"]!.AsObject()
            : (root["tracks"] ?? root["subtitleTracks"])![0]!["defaultStyle"]!.AsObject();
    }

    private static JsonObject ScalingNode()
    {
        return new() { ["referenceWidth"] = 1920, ["referenceHeight"] = 1080, ["matchWidthOrHeight"] = 0.5 };
    }

    private static byte[] Encode(JsonObject root)
    {
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static string DuplicateWidth(string json)
    {
        var duplicate = json.Replace("\"referenceWidth\": 1920,", "\"referenceWidth\": 1920, \"reference\\u0057idth\": 1920,", StringComparison.Ordinal);
        Assert.NotEqual(json, duplicate);
        return duplicate;
    }

    private static void MutateScaling(JsonObject style, string mutation)
    {
        switch (mutation)
        {
            case "missing":
                style.Remove("scaling");
                break;
            case "null":
                style["scaling"] = null;
                break;
            case "missingWidth":
                style["scaling"]!.AsObject().Remove("referenceWidth");
                break;
            case "missingHeight":
                style["scaling"]!.AsObject().Remove("referenceHeight");
                break;
            case "missingMatch":
                style["scaling"]!.AsObject().Remove("matchWidthOrHeight");
                break;
            case "unknown":
                style["scaling"]!["enabled"] = true;
                break;
            case "zeroWidth":
                style["scaling"]!["referenceWidth"] = 0;
                break;
            case "negativeHeight":
                style["scaling"]!["referenceHeight"] = -1;
                break;
            case "largeWidth":
                style["scaling"]!["referenceWidth"] = 32769;
                break;
            case "negativeMatch":
                style["scaling"]!["matchWidthOrHeight"] = -0.01;
                break;
            case "largeMatch":
                style["scaling"]!["matchWidthOrHeight"] = 1.01;
                break;
            case "stringWidth":
                style["scaling"]!["referenceWidth"] = "1920";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }
}
