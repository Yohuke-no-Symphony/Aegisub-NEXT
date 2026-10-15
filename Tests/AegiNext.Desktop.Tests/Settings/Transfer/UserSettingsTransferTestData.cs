using System.IO.Compression;
using AegiNext.Application.ColorTags;
using AegiNext.Application.Presets;
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
            ExportPresets = new()
            {
                Presets = [new(Guid.NewGuid(), "字幕压制", new()
                {
                    Codec = VideoCodec.Hevc, EncodingMode = VideoEncodingMode.HARDWARE, RateControlMode = VideoRateControlMode.CBR,
                    Preset = "veryslow", Quality = 17, VideoBitrate = 12345678, AudioMode = AudioExportMode.Aac, AudioBitrate = 256000
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

    internal static byte[] CreateLegacyArchive(int version, WorkbenchPreferences? preferences = null)
    {
        var bundle = CreateBundle();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["manifest.json"] = System.Text.Encoding.UTF8.GetBytes($"{{\"version\":{version}}}"),
            ["preferences.json"] = WorkbenchPreferencesStore.Serialize(preferences ?? bundle.Preferences, allowForeignWorkspace: true),
            ["subtitle-styles.aegistyles"] = SubtitleStylePresetStore.Serialize(new()
            {
                Presets = [new(Guid.NewGuid(), "Legacy style", new() { FontSize = 62 })]
            }),
            ["effect-scripts.json"] = EffectScriptPresetStore.Serialize(new()
            {
                Presets = [new(Guid.NewGuid(), "Legacy effect", "effect \"legacy-transfer\" version 3\nshort-clip compress\nscope clip current\nsegment stay flex 1\n    at 0 opacity base\n    at 1 opacity base\nend\nend\n")]
            }),
            ["export-presets.aegiexports"] = VideoExportPresetStore.Serialize(bundle.ExportPresets),
            ["layouts.json"] = WorkspaceLayoutStore.Serialize(bundle.Layouts)
        };
        if (version == 2)
        {
            files.Add("subtitle-color-tags.json", SubtitleColorTagStore.Serialize(bundle.ColorTags!));
        }
        return WriteArchive(files);
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
