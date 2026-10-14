using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

/// <summary>严格、版本化的 .aegilibrary ZIP 交换包，逐项携带内容摘要且不包含应用设置。</summary>
public static class EffectLibraryPackageStore
{
    /// <summary>包文件与全部解压内容各自允许的最大容量。</summary>
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目预算常量采用 ALL_UPPER。")]
    public const int MAXIMUM_FILE_BYTES = 128 * 1024 * 1024;
    private const int MAXIMUM_MANIFEST_BYTES = 256 * 1024;
    private const string MANIFEST_NAME = "manifest.json";
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    private static readonly SearchValues<char> hexadecimal = SearchValues.Create("0123456789abcdef");
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 4,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    /// <summary>读取有界包文件并验证所有条目，不解压到调用方目录。</summary>
    public static async Task<EffectLibraryPackage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await PresetFileReader.ReadAsync(path, MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
        return Deserialize(bytes, cancellationToken);
    }

    /// <summary>完整校验后原子保存选择项或全库共享包。</summary>
    public static Task SaveAsync(EffectLibraryPackage package, string path, CancellationToken cancellationToken = default)
    {
        return SaveAsync(package, path, null, cancellationToken);
    }

    /// <summary>包全部准备完成后，在原子替换前调用宿主提交边界。</summary>
    public static Task SaveAsync(EffectLibraryPackage package, string path, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        return EffectLibraryTransferFiles.WriteAtomicAsync(path, Serialize(package, cancellationToken), beforeCommit, cancellationToken);
    }

    /// <summary>将完整个人内容编码为带清单和 SHA-256 的有界 ZIP；内置模板不可写入完整包。</summary>
    public static byte[] Serialize(EffectLibraryPackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        _ = SubtitleStylePresetStore.Serialize(package.Styles);
        _ = EffectScriptPresetStore.Serialize(package.Effects);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var styles = ImmutableArray.CreateBuilder<EffectLibraryPackageFile>();
        foreach (var preset in package.Styles.Presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = $"styles/{preset.Id:N}.aegistyles";
            var bytes = SubtitleStylePresetStore.Serialize(new() { Presets = [preset] });
            files.Add(path, bytes);
            styles.Add(new() { Path = path, Sha256 = Digest(bytes) });
        }
        var effects = ImmutableArray.CreateBuilder<EffectLibraryPackageFile>();
        foreach (var preset in package.Effects.Presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = $"effects/{preset.Id:N}.aegieffect";
            var bytes = EffectScriptPresetStore.SerializePreset(preset);
            files.Add(path, bytes);
            effects.Add(new() { Path = path, Sha256 = Digest(bytes) });
        }
        var manifest = new EffectLibraryPackageManifest { Styles = styles.ToImmutable(), Effects = effects.ToImmutable() };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, options);
        if (manifestBytes.Length > MAXIMUM_MANIFEST_BYTES || files.Values.Sum(bytes => (long)bytes.Length) + manifestBytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("效果库包解压内容超过 128 MiB。");
        }
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            WriteEntry(archive, MANIFEST_NAME, manifestBytes, cancellationToken);
            foreach (var file in files)
            {
                WriteEntry(archive, file.Key, file.Value, cancellationToken);
            }
        }
        if (buffer.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("效果库包超过 128 MiB。");
        }
        return buffer.ToArray();
    }

    /// <summary>拒绝未知、重复、带非法路径、摘要不符、缺失或超预算内容，再校验两类完整个人集合。</summary>
    public static EffectLibraryPackage Deserialize(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("效果库包超过 128 MiB。");
        }
        try
        {
            using var buffer = new MemoryStream(bytes.ToArray(), false);
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);
            if (archive.Entries.Count is < 1 or > 769)
            {
                throw new InvalidDataException("效果库包条目数量无效。");
            }
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var maximum = EntryMaximum(entry.FullName);
                if (entry.Length > maximum || !entries.TryAdd(entry.FullName, entry))
                {
                    throw new InvalidDataException("效果库包包含重复或超出预算的条目。");
                }
                expanded = checked(expanded + entry.Length);
                if (expanded > MAXIMUM_FILE_BYTES)
                {
                    throw new InvalidDataException("效果库包解压内容超过 128 MiB。");
                }
            }
            if (!entries.TryGetValue(MANIFEST_NAME, out var manifestEntry))
            {
                throw new InvalidDataException("效果库包缺少清单。");
            }
            var manifest = ReadManifest(ReadEntry(manifestEntry, MAXIMUM_MANIFEST_BYTES, cancellationToken));
            if (entries.Count != manifest.Styles.Length + manifest.Effects.Length + 1)
            {
                throw new InvalidDataException("效果库包条目与清单不一致。");
            }
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            var styles = ImmutableArray.CreateBuilder<SubtitleStylePreset>();
            foreach (var file in manifest.Styles)
            {
                var content = ReadReferenced(entries, referenced, file, "styles/", ".aegistyles", cancellationToken);
                var collection = SubtitleStylePresetStore.Deserialize(content);
                if (collection.Presets.Length != 1 || collection.Presets[0].Id != PathIdentity(file.Path, "styles/", ".aegistyles"))
                {
                    throw new InvalidDataException("包内样式文件必须包含与清单路径对应的单个样式。");
                }
                styles.Add(collection.Presets[0]);
            }
            var effects = ImmutableArray.CreateBuilder<EffectScriptPreset>();
            foreach (var file in manifest.Effects)
            {
                var content = ReadReferenced(entries, referenced, file, "effects/", ".aegieffect", cancellationToken);
                var preset = EffectScriptPresetStore.DeserializePreset(content);
                if (preset.Id != PathIdentity(file.Path, "effects/", ".aegieffect"))
                {
                    throw new InvalidDataException("包内特效身份与清单路径不一致。");
                }
                effects.Add(preset);
            }
            var result = new EffectLibraryPackage
            {
                Styles = new() { Presets = styles.ToImmutable() },
                Effects = new() { Presets = effects.ToImmutable() }
            };
            _ = SubtitleStylePresetStore.Serialize(result.Styles);
            _ = EffectScriptPresetStore.Serialize(result.Effects);
            return result;
        }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException or NotSupportedException)
        {
            throw new InvalidDataException("效果库包格式无效。", error);
        }
    }

    private static byte[] ReadReferenced(Dictionary<string, ZipArchiveEntry> entries, HashSet<string> referenced,
        EffectLibraryPackageFile file, string prefix, string extension, CancellationToken cancellationToken)
    {
        if (file is null || !ValidDigest(file.Sha256))
        {
            throw new InvalidDataException("效果库包清单条目或摘要无效。");
        }
        _ = PathIdentity(file.Path, prefix, extension);
        if (!referenced.Add(file.Path) || !entries.TryGetValue(file.Path, out var entry))
        {
            throw new InvalidDataException("效果库包清单包含重复路径或缺失文件。");
        }
        var bytes = ReadEntry(entry, EntryMaximum(file.Path), cancellationToken);
        if (!string.Equals(Digest(bytes), file.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException("效果库包条目与清单 SHA-256 不一致。");
        }
        return bytes;
    }

    private static EffectLibraryPackageManifest ReadManifest(byte[] bytes)
    {
        _ = strictUtf8.GetCharCount(bytes);
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = 4 });
        RejectDuplicateKeys(document.RootElement);
        var manifest = document.RootElement.Deserialize<EffectLibraryPackageManifest>(options) ??
            throw new InvalidDataException("效果库包清单不能为 null。");
        if (manifest.Version != 1 || manifest.Styles.IsDefault || manifest.Effects.IsDefault ||
            manifest.Styles.Length > 256 || manifest.Effects.Length > 512)
        {
            throw new InvalidDataException("效果库包版本或集合数量无效。");
        }
        return manifest;
    }

    private static int EntryMaximum(string name)
    {
        if (name == MANIFEST_NAME)
        {
            return MAXIMUM_MANIFEST_BYTES;
        }
        if (name.StartsWith("styles/", StringComparison.Ordinal))
        {
            _ = PathIdentity(name, "styles/", ".aegistyles");
            return SubtitleStylePresetStore.MAXIMUM_FILE_BYTES;
        }
        _ = PathIdentity(name, "effects/", ".aegieffect");
        return EffectScriptPresetStore.MAXIMUM_FILE_BYTES;
    }

    private static Guid PathIdentity(string? path, string prefix, string extension)
    {
        if (path is null || path.Length != prefix.Length + 32 + extension.Length ||
            !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(extension, StringComparison.Ordinal) ||
            path.AsSpan(prefix.Length, 32).ContainsAnyExcept(hexadecimal) ||
            !Guid.TryParseExact(path.AsSpan(prefix.Length, 32), "N", out var id) || id == Guid.Empty)
        {
            throw new InvalidDataException("效果库包包含未知或非法路径。");
        }
        return id;
    }

    private static bool ValidDigest(string? digest)
    {
        return digest is { Length: 64 } && !digest.AsSpan().ContainsAnyExcept(hexadecimal);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximum, CancellationToken cancellationToken)
    {
        using var stream = entry.Open();
        var bytes = EffectLibraryTransferFiles.Read(stream, maximum, cancellationToken);
        if (bytes.LongLength != entry.Length)
        {
            throw new InvalidDataException("效果库包条目长度与记录不一致。");
        }
        return bytes;
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes, CancellationToken cancellationToken)
    {
        using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        const int CHUNK_SIZE = 65536;
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(CHUNK_SIZE, bytes.Length - offset);
            stream.Write(bytes.AsSpan(offset, length));
            offset += length;
        }
    }

    private static string Digest(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name))
                {
                    throw new InvalidDataException("效果库包清单包含重复字段。");
                }
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                RejectDuplicateKeys(child);
            }
        }
    }
}
