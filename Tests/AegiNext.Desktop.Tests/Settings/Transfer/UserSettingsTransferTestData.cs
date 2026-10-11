using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using AegiNext.Application.ColorTags;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Tests.Settings.Transfer;

internal static class UserSettingsTransferTestData
{
    internal static UserSettingsBundle CreateBundle()
    {
        var layout = WorkspaceLayoutPresets.BuiltIn.Single(value => value.Id == WorkspaceLayoutPresets.EFFECTS).Layout;
        var fontBytes = ImmutableArray.Create<byte>(1, 2, 3, 4);
        var font = new EmbeddedSubtitleFont("字幕字体.ttf", Convert.ToHexStringLower(SHA256.HashData(fontBytes.AsSpan())), fontBytes);
        return new()
        {
            Preferences = new()
            {
                Language = "zh-CN", Theme = WorkbenchTheme.DARK, AccentColor = "#123456", Volume = 0.375f,
                WindowMenuOnMac = true, PreviewQuality = PreviewQuality.HIGH, PreviewDecodeMode = VideoDecodeMode.Software,
                SubtitleAuditionMilliseconds = 250, TimelineClassicTimingEnabled = true, TimelineSnapEnabled = false,
                TimelineStepEnabled = true, TimelineSpectrumVisible = false, TimelineWaveformVisible = false,
                AudioCalibrations = [new("transfer-device", "transfer-backend", 48000, 2, 34)],
                Projects = new()
                {
                    WorkspaceRoot = Path.Combine(Path.GetTempPath(), "transfer-workspace"), AutoSaveEnabled = false,
                    AutoSaveIntervalMinutes = 7, BackupEnabled = true, BackupIntervalMinutes = 11, MaximumBackupCount = 17
                }
            },
            Styles = new() { Presets = [new(Guid.NewGuid(), "个人样式", new() { FontSize = 62 }, font,
                new() { LeadInMilliseconds = 120, BiasPercent = 40 })] },
            Effects = new()
            {
                Presets = [new(Guid.NewGuid(), "个人脚本", "effect \"transfer-user\" version 1\nshort-clip compress\nsegment stay flex 1\n    at 0 opacity base\n    at 1 opacity base\nend\n")]
            },
            ExportPresets = new()
            {
                Presets = [new(Guid.NewGuid(), "字幕压制", new()
                {
                    Codec = VideoCodec.Hevc, EncodingMode = VideoEncodingMode.HARDWARE, RateControlMode = VideoRateControlMode.CBR,
                    Preset = "veryslow", Crf = 17, VideoBitrate = 12345678, AudioMode = AudioExportMode.Aac, AudioBitrate = 256000
                })]
            },
            ColorTags = new SubtitleColorTagLibraryDocument
            {
                Tags = [new SubtitleColorTag { Name = "复核", ColorHex = "#123456" }]
            },
            Layouts = new()
            {
                Current = layout, CurrentPresetId = "user-transfer",
                Presets = [new("user-transfer", "个人工作区", false, layout)]
            }
        };
    }

    internal static Dictionary<string, byte[]> ReadArchive(byte[] bytes)
    {
        using var buffer = new MemoryStream(bytes, false);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var stream = entry.Open();
            using var content = new MemoryStream();
            stream.CopyTo(content);
            return content.ToArray();
        }, StringComparer.Ordinal);
    }

    internal static byte[] WriteArchive(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(bytes);
            }
        }
        return buffer.ToArray();
    }

    internal static void AssertBundleEqual(UserSettingsBundle expected, UserSettingsBundle actual)
    {
        var before = UserSettingsBundleStore.SerializeFiles(expected);
        var after = UserSettingsBundleStore.SerializeFiles(actual);
        Assert.Equal(expected.ColorTags is null, actual.ColorTags is null);
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var name in before.Keys)
        {
            Assert.Equal(before[name], after[name]);
        }
    }
}
