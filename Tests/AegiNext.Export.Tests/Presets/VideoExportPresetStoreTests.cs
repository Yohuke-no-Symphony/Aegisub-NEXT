using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AegiNext.Export;
using AegiNext.Export.Presets;
using Utf8Encoding = System.Text.Encoding;

namespace AegiNext.Export.Tests.Presets;

/// <summary>压制预设交换文档的完整性、版本及原子写入验证。</summary>
public sealed class VideoExportPresetStoreTests
{
    private static readonly string[] rootFields = ["version", "presets"];

    [Fact]
    public void LegacyV1MigratesQualityWithoutChangingIdentityOrInactiveValues()
    {
        var preset = new VideoExportPreset(Guid.NewGuid(), "Legacy", new() { Quality = 27, VideoBitrate = 12345000 });
        var document = JsonNode.Parse(VideoExportPresetStore.Serialize(new() { Presets = [preset] }))!.AsObject();
        document["version"] = 1;
        var legacySettings = document["presets"]![0]!["settings"]!.AsObject();
        legacySettings.Remove("container");
        legacySettings["crf"] = legacySettings["quality"]!.DeepClone();
        legacySettings.Remove("quality");
        var migrated = VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes(document.ToJsonString()));
        Assert.Equal(3, migrated.Version);
        Assert.Equal(preset with { Settings = preset.Settings with { Container = VideoContainerFormat.MP4 } }, Assert.Single(migrated.Presets));
        var saved = Utf8Encoding.UTF8.GetString(VideoExportPresetStore.Serialize(migrated));
        Assert.Contains("\"quality\": 27", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("\"crf\"", saved, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VideoCodec.Vp8)]
    [InlineData(VideoCodec.Vp9)]
    [InlineData(VideoCodec.Av1)]
    public void NewCodecAndOpusRoundTripWithExplicitBackendSettings(VideoCodec codec)
    {
        var preset = new VideoExportPreset(Guid.NewGuid(), "Modern", VideoExportCapabilities.CreateDefaultSettings(codec) with { AudioMode = AudioExportMode.Opus });
        var serialized = VideoExportPresetStore.Serialize(new() { Presets = [preset] });
        Assert.Equal(preset, Assert.Single(VideoExportPresetStore.Deserialize(serialized).Presets));
    }

    /// <summary>便携文档完整保留显式模式及非活动参数，不包含工程或机器路径。</summary>
    [Theory]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CRF)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CBR)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.CBR)]
    public async Task ExplicitSettingsRoundTripWithoutRuntimePaths(VideoEncodingMode encodingMode,
        VideoRateControlMode rateControlMode)
    {
        using var directory = new ExportPresetTestDirectory();
        var preset = new VideoExportPreset(Guid.NewGuid(), "日常字幕", new()
        {
            Codec = VideoCodec.Hevc,
            EncodingMode = encodingMode,
            RateControlMode = rateControlMode,
            Preset = "slow",
            Quality = 25,
            VideoBitrate = 12500000,
            AudioMode = AudioExportMode.Aac,
            AudioBitrate = 256000
        });
        var collection = new VideoExportPresetCollection { Presets = [preset] };
        var bytes = VideoExportPresetStore.Serialize(collection);
        var document = JsonNode.Parse(bytes)!.AsObject();
        Assert.Equal(rootFields, document.Select(property => property.Key));
        var settings = document["presets"]![0]!["settings"]!.AsObject();
        Assert.Equal(9, settings.Count);
        Assert.Equal(rateControlMode.ToString(), settings["rateControlMode"]!.GetValue<string>());
        Assert.Equal(preset, Assert.Single(VideoExportPresetStore.Deserialize(bytes).Presets));
        var path = Path.Combine(directory.Path, "字幕.aegiexports");

        await VideoExportPresetStore.SaveAsync(collection, path);

        Assert.Equal(preset, Assert.Single((await VideoExportPresetStore.LoadAsync(path)).Presets));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>版本和集合等全部字段必需，重复、未知、整数枚举及非法参数整体拒绝。</summary>
    [Theory]
    [InlineData("\"version\": 3", "\"version\": 99")]
    [InlineData("\"version\": 3,", "")]
    [InlineData("\"version\": 3,", "\"version\": 3, \"version\": 3,")]
    [InlineData("\"version\": 3,", "\"version\": 3, \"unknown\": true,")]
    [InlineData("\"presets\": [", "\"items\": [")]
    [InlineData("\"name\": \"Valid\",", "")]
    [InlineData("\"settings\": {", "\"settings\": { \"unknown\": true,")]
    [InlineData("\"codec\": \"Auto\",", "")]
    [InlineData("\"container\": \"AUTO\",", "")]
    [InlineData("\"container\": \"AUTO\"", "\"container\": \"Unknown\"")]
    [InlineData("\"container\": \"AUTO\"", "\"container\": 1")]
    [InlineData("\"container\": \"AUTO\"", "\"container\": null")]
    [InlineData("\"container\": \"AUTO\"", "\"container\": \"WEBM\"")]
    [InlineData("\"container\": \"AUTO\",", "\"container\": \"AUTO\", \"container\": \"MP4\",")]
    [InlineData("\"quality\": 20,", "")]
    [InlineData("\"quality\": 20,", "\"quality\": 20, \"quality\": 30,")]
    [InlineData("\"quality\": 20,", "\"quality\": 52,")]
    [InlineData("\"rateControlMode\": \"CRF\"", "\"rateControlMode\": 1")]
    [InlineData("\"rateControlMode\": \"CRF\"", "\"rateControlMode\": \"Unknown\"")]
    [InlineData("\"rateControlMode\": \"CRF\"", "\"rateControlMode\": \"AUTOMATIC\"")]
    [InlineData("\"encodingMode\": \"SOFTWARE\"", "\"encodingMode\": \"HARDWARE\"")]
    [InlineData("\"codec\": \"Auto\"", "\"codec\": 0")]
    [InlineData("\"audioMode\": \"Copy\"", "\"audioMode\": 0")]
    [InlineData("\"videoBitrate\": 8000000", "\"videoBitrate\": 0")]
    [InlineData("\"audioBitrate\": 192000", "\"audioBitrate\": 0")]
    [InlineData("\"preset\": \"medium\"", "\"preset\": \"unsupported\"")]
    public void UnsupportedDuplicateUnknownMissingAndInvalidFieldsAreRejected(string original, string replacement)
    {
        var json = Utf8Encoding.UTF8.GetString(VideoExportPresetStore.Serialize(new()
        {
            Presets = [new(Guid.NewGuid(), "Valid", new())]
        }));
        Assert.Contains(original, json, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Deserialize(
            Utf8Encoding.UTF8.GetBytes(json.Replace(original, replacement, StringComparison.Ordinal))));
    }

    /// <summary>合法 JSON 内部的非法 UTF-8 以及无效根类型不能降级到默认文档。</summary>
    [Fact]
    public void InvalidUtf8AndInvalidRootsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Deserialize([0x7b, 0xff, 0x7d]));
        Assert.Throws<InvalidDataException>(() =>
            VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes("null")));
        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes("[]")));
        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes("{}")));
        Assert.Throws<InvalidDataException>(() =>
            VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes("{\"version\":1,\"presets\":[null]}")));
    }

    /// <summary>身份、名称和总项目数无效的集合不得被序列化。</summary>
    [Fact]
    public void InvalidIdentityNamesAndCollectionsAreRejectedBeforeSerialization()
    {
        var preset = new VideoExportPreset(Guid.NewGuid(), "Valid", new());
        Assert.Throws<InvalidDataException>(() =>
            VideoExportPresetStore.Serialize(new() { Presets = [preset with { Id = Guid.Empty }] }));
        foreach (var name in new[]
                     { string.Empty, " leading", "trailing ", "line\nfeed", "e\u0301", "\ud800", new string('a', 129) })
        {
            Assert.Throws<InvalidDataException>(() =>
                VideoExportPresetStore.Serialize(new() { Presets = [preset with { Name = name }] }));
        }

        Assert.Throws<InvalidDataException>(() =>
            VideoExportPresetStore.Serialize(new() { Presets = [preset, preset with { Name = "Different" }] }));
        Assert.Throws<InvalidDataException>(() =>
            VideoExportPresetStore.Serialize(new()
                { Presets = [preset, preset with { Id = Guid.NewGuid(), Name = "VALID" }] }));
        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Serialize(new() { Presets = default }));
        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Serialize(new()
        {
            Presets = Enumerable.Range(0, 257)
                .Select(index => new VideoExportPreset(Guid.NewGuid(), $"Preset {index}", new())).ToImmutableArray()
        }));
    }

    /// <summary>超容量输入在读取和解析前拒绝，不分配无界文件内容。</summary>
    [Fact]
    public async Task OversizedInputIsRejectedBeforeReadingOrDeserializing()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "oversized.aegiexports");
        await using (var stream = File.Create(path))
        {
            stream.SetLength(VideoExportPresetStore.MAXIMUM_FILE_BYTES + 1L);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => VideoExportPresetStore.LoadAsync(path));
        Assert.Throws<InvalidDataException>(() =>
            VideoExportPresetStore.Deserialize(new byte[VideoExportPresetStore.MAXIMUM_FILE_BYTES + 1]));
    }

    /// <summary>取消、验证失败或目标提交失败都保留旧目标并清理暂存文件。</summary>
    [Fact]
    public async Task CancellationValidationAndCommitFailurePreserveTargetsAndCleanTemporaryFiles()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "retained.aegiexports");
        var retained = new VideoExportPresetCollection { Presets = [new(Guid.NewGuid(), "Retained", new())] };
        await VideoExportPresetStore.SaveAsync(retained, path);
        var bytes = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            VideoExportPresetStore.SaveAsync(new(), path, cancellation.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            VideoExportPresetStore.SaveAsync(new() { Version = 99 }, path));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        var blocked = Path.Combine(directory.Path, "blocked.aegiexports");
        Directory.CreateDirectory(blocked);
        await Assert.ThrowsAsync<IOException>(() => VideoExportPresetStore.SaveAsync(retained, blocked));
        Assert.True(Directory.Exists(blocked));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }
}
