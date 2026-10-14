using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AegiNext.Application.ColorTags;
using AegiNext.Application.Presets;
using AegiNext.Desktop.Layouts;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Settings.Transfer;

internal static class UserSettingsBundleStore
{
    internal const int CURRENT_VERSION = 3;
    internal const int MAXIMUM_FILE_BYTES = 128 * 1024 * 1024;
    private const string MANIFEST_NAME = "manifest.json";
    private const int MAXIMUM_MANIFEST_BYTES = 1024;
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    private static ImmutableArray<UserSettingsBundleFile> VersionOneFiles { get; } =
    [
        new("preferences.json", WorkbenchPreferencesStore.MAXIMUM_FILE_BYTES),
        new("subtitle-styles.aegistyles", SubtitleStylePresetStore.MAXIMUM_FILE_BYTES),
        new("effect-scripts.json", EffectScriptPresetStore.MAXIMUM_FILE_BYTES),
        new("export-presets.aegiexports", VideoExportPresetStore.MAXIMUM_FILE_BYTES),
        new("layouts.json", WorkspaceLayoutStore.MAXIMUM_FILE_BYTES)
    ];
    private static ImmutableArray<UserSettingsBundleFile> VersionTwoFiles { get; } =
        [.. VersionOneFiles, new("subtitle-color-tags.json", SubtitleColorTagStore.MAXIMUM_FILE_BYTES)];
    internal static ImmutableArray<UserSettingsBundleFile> Files { get; } =
    [
        new("preferences.json", WorkbenchPreferencesStore.MAXIMUM_FILE_BYTES),
        new("export-presets.aegiexports", VideoExportPresetStore.MAXIMUM_FILE_BYTES),
        new("layouts.json", WorkspaceLayoutStore.MAXIMUM_FILE_BYTES),
        new("subtitle-color-tags.json", SubtitleColorTagStore.MAXIMUM_FILE_BYTES)
    ];

    internal static ImmutableArray<UserSettingsBundleFile> GetFiles(int version)
    {
        return version switch
        {
            1 => VersionOneFiles,
            2 => VersionTwoFiles,
            CURRENT_VERSION => Files,
            _ => throw new InvalidDataException("用户设置包版本不受支持。")
        };
    }

    internal static byte[] Serialize(UserSettingsBundle bundle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var files = SerializeFiles(bundle, cancellationToken);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            WriteEntry(archive, MANIFEST_NAME, "{\"version\":3}"u8, cancellationToken);
            foreach (var file in Files.Where(file => files.ContainsKey(file.Name)))
            {
                WriteEntry(archive, file.Name, files[file.Name], cancellationToken);
            }
        }
        if (buffer.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("用户设置包超过 128 MiB。");
        }
        return buffer.ToArray();
    }

    internal static UserSettingsBundle Deserialize(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        var content = ReadArchive(bytes, out var version, cancellationToken);
        return DeserializeSettings(content, version, cancellationToken);
    }

    internal static UserSettingsRestorePayload DeserializeForRestore(ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        var content = ReadArchive(bytes, out var version, cancellationToken);
        var settings = DeserializeSettings(content, version, cancellationToken);
        var restored = SerializeFiles(settings, cancellationToken);
        if (version < CURRENT_VERSION)
        {
            cancellationToken.ThrowIfCancellationRequested();
            restored.Add("subtitle-styles.aegistyles",
                SubtitleStylePresetStore.Serialize(SubtitleStylePresetStore.Deserialize(content["subtitle-styles.aegistyles"])));
            cancellationToken.ThrowIfCancellationRequested();
            restored.Add("effect-scripts.json",
                EffectScriptPresetStore.Serialize(EffectScriptPresetStore.Deserialize(content["effect-scripts.json"])));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(settings, GetFiles(version).Where(file => restored.ContainsKey(file.Name)).ToImmutableArray(), restored);
    }

    private static UserSettingsBundle DeserializeSettings(Dictionary<string, byte[]> content, int version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var preferences = WorkbenchPreferencesStore.Deserialize(content["preferences.json"], allowForeignWorkspace: true);
        cancellationToken.ThrowIfCancellationRequested();
        var exports = VideoExportPresetStore.Deserialize(content["export-presets.aegiexports"]);
        cancellationToken.ThrowIfCancellationRequested();
        var tags = content.TryGetValue("subtitle-color-tags.json", out var bytes) ? SubtitleColorTagStore.Deserialize(bytes) : null;
        cancellationToken.ThrowIfCancellationRequested();
        var layouts = WorkspaceLayoutStore.Deserialize(content["layouts.json"]);
        cancellationToken.ThrowIfCancellationRequested();
        return new()
        {
            SourceVersion = version,
            Preferences = preferences,
            ExportPresets = exports,
            ColorTags = tags,
            Layouts = layouts
        };
    }

    private static Dictionary<string, byte[]> ReadArchive(ReadOnlySpan<byte> bytes, out int version,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("用户设置包超过 128 MiB。");
        }
        try
        {
            using var buffer = new MemoryStream(bytes.ToArray(), false);
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
            if (archive.Entries.Count < Files.Length || archive.Entries.Count > VersionTwoFiles.Length + 1)
            {
                throw new InvalidDataException("用户设置包必须包含清单和该版本的完整设置文件。");
            }
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            long expandedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var maximum = entry.FullName == MANIFEST_NAME ? MAXIMUM_MANIFEST_BYTES :
                    VersionTwoFiles.FirstOrDefault(file => file.Name == entry.FullName)?.MaximumBytes;
                if (maximum is null || !entries.TryAdd(entry.FullName, entry) || entry.Length > maximum.Value)
                {
                    throw new InvalidDataException("用户设置包包含未知、重复、带路径或超出预算的文件。");
                }
                expandedBytes = checked(expandedBytes + entry.Length);
                if (expandedBytes > MAXIMUM_FILE_BYTES)
                {
                    throw new InvalidDataException("用户设置包解压内容超过 128 MiB。");
                }
            }
            version = ReadManifestVersion(ReadEntry(entries[MANIFEST_NAME], MAXIMUM_MANIFEST_BYTES, cancellationToken));
            var expectedFiles = GetFiles(version);
            if (version == CURRENT_VERSION && !entries.ContainsKey("subtitle-color-tags.json"))
            {
                expectedFiles = expectedFiles.Where(file => file.Name != "subtitle-color-tags.json").ToImmutableArray();
            }
            if (entries.Count != expectedFiles.Length + 1 || expectedFiles.Any(file => !entries.ContainsKey(file.Name)))
            {
                throw new InvalidDataException("用户设置包文件与版本清单不一致。");
            }
            return expectedFiles.ToDictionary(file => file.Name,
                file => ReadEntry(entries[file.Name], file.MaximumBytes, cancellationToken), StringComparer.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException or
                                     KeyNotFoundException or InvalidOperationException or NotSupportedException)
        {
            throw new InvalidDataException("用户设置包格式无效。", error);
        }
    }

    internal static async Task<UserSettingsBundle> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await UserSettingsTransferFiles.ReadAsync(path, MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Deserialize(bytes, cancellationToken);
    }

    internal static Task SaveAsync(UserSettingsBundle bundle, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        return UserSettingsTransferFiles.WriteAtomicAsync(Path.GetFullPath(path), Serialize(bundle, cancellationToken), cancellationToken);
    }

    internal static Dictionary<string, byte[]> SerializeFiles(UserSettingsBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();
        files.Add("preferences.json", WorkbenchPreferencesStore.Serialize(bundle.Preferences, allowForeignWorkspace: true));
        cancellationToken.ThrowIfCancellationRequested();
        files.Add("export-presets.aegiexports", VideoExportPresetStore.Serialize(bundle.ExportPresets));
        cancellationToken.ThrowIfCancellationRequested();
        files.Add("layouts.json", WorkspaceLayoutStore.Serialize(bundle.Layouts));
        cancellationToken.ThrowIfCancellationRequested();
        if (bundle.ColorTags is { } tags)
        {
            files.Add("subtitle-color-tags.json", SubtitleColorTagStore.Serialize(tags));
            cancellationToken.ThrowIfCancellationRequested();
        }
        return files;
    }

    private static void WriteEntry(ZipArchive archive, string name, ReadOnlySpan<byte> bytes,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        const int CHUNK_SIZE = 65536;
        while (!bytes.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(bytes.Length, CHUNK_SIZE);
            stream.Write(bytes[..length]);
            bytes = bytes[length..];
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximumBytes, CancellationToken cancellationToken)
    {
        using var stream = entry.Open();
        var bytes = UserSettingsTransferFiles.Read(stream, maximumBytes, cancellationToken);
        if (bytes.LongLength != entry.Length)
        {
            throw new InvalidDataException("用户设置包文件长度与清单不一致。");
        }
        return bytes;
    }

    private static int ReadManifestVersion(ReadOnlySpan<byte> bytes)
    {
        _ = strictUtf8.GetCharCount(bytes);
        using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 2 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("用户设置包清单无效。");
        }
        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name != "version" ||
            properties[0].Value.ValueKind != JsonValueKind.Number ||
            !properties[0].Value.TryGetInt32(out var version) || version is not (1 or 2 or CURRENT_VERSION))
        {
            throw new InvalidDataException("用户设置包版本不受支持或清单包含未知、重复字段。");
        }
        return version;
    }
}
