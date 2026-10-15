using System.Text.Json.Nodes;
using AegiNext.Export;
using AegiNext.Export.Presets;
using Utf8Encoding = System.Text.Encoding;

namespace AegiNext.Export.Tests.Presets;

/// <summary>验证旧压制预设升级到具备具体输出容器的 v3 合同。</summary>
public sealed class VideoExportPresetMigrationTests
{
    /// <summary>v1 和 v2 缺少格式时补充编码器默认值，身份和非活动参数保持原样。</summary>
    [Theory]
    [InlineData(1, VideoCodec.Auto, VideoContainerFormat.MP4)]
    [InlineData(1, VideoCodec.H264, VideoContainerFormat.MP4)]
    [InlineData(1, VideoCodec.Hevc, VideoContainerFormat.MP4)]
    [InlineData(2, VideoCodec.Auto, VideoContainerFormat.MP4)]
    [InlineData(2, VideoCodec.H264, VideoContainerFormat.MP4)]
    [InlineData(2, VideoCodec.Hevc, VideoContainerFormat.MP4)]
    [InlineData(2, VideoCodec.Vp8, VideoContainerFormat.MKV)]
    [InlineData(2, VideoCodec.Vp9, VideoContainerFormat.MKV)]
    [InlineData(2, VideoCodec.Av1, VideoContainerFormat.MKV)]
    public void LegacyPresetsGainCodecDefaultsWithoutLosingParameters(int version, VideoCodec codec, VideoContainerFormat expected)
    {
        var preset = new VideoExportPreset(Guid.NewGuid(), "Legacy 字幕", VideoExportCapabilities.CreateDefaultSettings(codec) with
        {
            Quality = 27,
            VideoBitrate = 12345678,
            AudioBitrate = 96000
        });
        var document = LegacyDocument(preset, version);

        var migrated = VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes(document.ToJsonString()));

        Assert.Equal(3, migrated.Version);
        Assert.Equal(preset with { Settings = preset.Settings with { Container = expected } }, Assert.Single(migrated.Presets));
        var saved = JsonNode.Parse(VideoExportPresetStore.Serialize(migrated))!.AsObject();
        Assert.Equal(expected.ToString(), saved["presets"]![0]!["settings"]!["container"]!.GetValue<string>());
        Assert.Null(saved["presets"]![0]!["settings"]!["crf"]);
    }

    /// <summary>迁移只补格式，不能掩盖旧文档的缺失、未知或非法字段。</summary>
    [Theory]
    [InlineData(1, "preset")]
    [InlineData(1, "codec")]
    [InlineData(2, "quality")]
    [InlineData(2, "audioMode")]
    public void MigrationDoesNotRestoreOtherMissingFields(int version, string field)
    {
        var document = LegacyDocument(new(Guid.NewGuid(), "Legacy", new()), version);
        document["presets"]![0]!["settings"]!.AsObject().Remove(field);

        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    /// <summary>旧文档升级仍拒绝未知字段、整数枚举和重复字段。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacyMigrationRetainsStrictFieldValidation(int version)
    {
        var document = LegacyDocument(new(Guid.NewGuid(), "Legacy", new()), version);
        var settings = document["presets"]![0]!["settings"]!.AsObject();
        settings["unknown"] = true;
        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes(document.ToJsonString())));
        settings.Remove("unknown");
        settings["codec"] = 0;
        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes(document.ToJsonString())));
        settings["codec"] = "Auto";
        var duplicate = document.ToJsonString().Replace("\"preset\":\"medium\"",
            "\"preset\":\"medium\",\"preset\":\"slow\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => VideoExportPresetStore.Deserialize(Utf8Encoding.UTF8.GetBytes(duplicate)));
    }

    /// <summary>新增容器在 v3 中完整往返，不改变自动兼容值或明确选择。</summary>
    [Theory]
    [InlineData(VideoContainerFormat.AUTO)]
    [InlineData(VideoContainerFormat.MP4)]
    [InlineData(VideoContainerFormat.MKV)]
    [InlineData(VideoContainerFormat.WEBM)]
    public void CurrentPresetsRoundTripEveryDefinedContainer(VideoContainerFormat container)
    {
        var preset = new VideoExportPreset(Guid.NewGuid(), "Portable", VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp9) with
        {
            Container = container,
            AudioMode = AudioExportMode.Opus
        });

        var serialized = VideoExportPresetStore.Serialize(new() { Presets = [preset] });

        Assert.Equal(preset, Assert.Single(VideoExportPresetStore.Deserialize(serialized).Presets));
    }

    private static JsonObject LegacyDocument(VideoExportPreset preset, int version)
    {
        var document = JsonNode.Parse(VideoExportPresetStore.Serialize(new() { Presets = [preset] }))!.AsObject();
        document["version"] = version;
        var settings = document["presets"]![0]!["settings"]!.AsObject();
        settings.Remove("container");
        if (version == 1)
        {
            settings["crf"] = settings["quality"]!.DeepClone();
            settings.Remove("quality");
        }

        return document;
    }
}
