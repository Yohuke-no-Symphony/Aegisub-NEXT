using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegiNext.Application.Fonts;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

/// <summary>带严格清单、原始字体文件和 SHA-256 的当前版本效果库 ZIP 交换包。</summary>
public static class EffectLibraryPackageStore
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目预算常量采用 ALL_UPPER。")]
    public const int MAXIMUM_FILE_BYTES = 1024 * 1024 * 1024;
    private const int MAXIMUM_MANIFEST_BYTES = 4 * 1024 * 1024;
    private const int MAXIMUM_ENTRY_COUNT = 16384;
    private const string MANIFEST_NAME = "manifest.json";
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    /// <summary>流式读取并完整验证效果库包，不向文件系统解压。</summary>
    public static Task<EffectLibraryPackage> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        return LoadCoreAsync(path, false, cancellationToken);
    }

    /// <summary>完整准备后原子替换效果库包。</summary>
    public static Task SaveAsync(EffectLibraryPackage package, string path, CancellationToken cancellationToken = default)
    {
        return SaveAsync(package, path, null, cancellationToken);
    }

    /// <summary>以流式 ZIP 暂存输出，只在全部写入后进入宿主提交阶段。</summary>
    public static Task SaveAsync(EffectLibraryPackage package, string path, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        return SaveCoreAsync(package, path, false, beforeCommit, cancellationToken);
    }

    /// <summary>编码当前效果库交换包，字体按 SHA-256 全局去重。</summary>
    public static byte[] Serialize(EffectLibraryPackage package, CancellationToken cancellationToken = default)
    {
        return SerializeCore(package, false, cancellationToken);
    }

    /// <summary>拒绝旧版、未知、重复、不完整、摘要不符或超出预算的包。</summary>
    public static EffectLibraryPackage Deserialize(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken = default)
    {
        return DeserializeCore(bytes, false, cancellationToken);
    }

    internal static async Task<EffectLibraryPackage> LoadCoreAsync(string path, bool stylesOnly, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("交换包超过 1 GiB。");
        }
        return ReadArchive(stream, stylesOnly, cancellationToken);
    }

    internal static async Task SaveCoreAsync(EffectLibraryPackage package, string path, bool stylesOnly,
        Action? beforeCommit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var files = Prepare(package, stylesOnly, cancellationToken);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                WriteArchive(stream, files, cancellationToken);
                if (stream.Length > MAXIMUM_FILE_BYTES)
                {
                    throw new InvalidDataException("交换包超过 1 GiB。");
                }
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static byte[] SerializeCore(EffectLibraryPackage package, bool stylesOnly, CancellationToken cancellationToken)
    {
        var files = Prepare(package, stylesOnly, cancellationToken);
        using var buffer = new MemoryStream();
        WriteArchive(buffer, files, cancellationToken);
        if (buffer.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("交换包超过 1 GiB。");
        }
        return buffer.ToArray();
    }

    internal static EffectLibraryPackage DeserializeCore(ReadOnlySpan<byte> bytes, bool stylesOnly, CancellationToken cancellationToken)
    {
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("交换包超过 1 GiB。");
        }
        using var stream = new MemoryStream(bytes.ToArray(), false);
        return ReadArchive(stream, stylesOnly, cancellationToken);
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> Prepare(EffectLibraryPackage package, bool stylesOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();
        _ = SubtitleStylePresetMetadataStore.Serialize(package.Styles);
        _ = EffectScriptPresetStore.Serialize(package.Effects);
        PrivateFontFiles.ValidateCoverage(package.Styles, package.FontFamilies, package.IncludesFonts);
        if (stylesOnly && !package.Effects.Presets.IsEmpty)
        {
            throw new InvalidDataException("样式包不能包含特效。");
        }
        var files = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var styles = ImmutableArray.CreateBuilder<EffectLibraryPackageFile>();
        foreach (var preset in package.Styles.Presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = $"styles/{preset.Id:N}.json";
            var bytes = SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [preset] });
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
        foreach (var font in package.FontFamilies.SelectMany(family => family.Files).OrderBy(file => file.Sha256, StringComparer.Ordinal))
        {
            files.TryAdd("fonts/" + PrivateFontFiles.Name(font.Sha256), font.Data.AsMemory());
        }
        var manifest = new EffectLibraryPackageManifest
        {
            Version = stylesOnly ? 1 : 2,
            Format = stylesOnly ? "aeginext.styles" : "aeginext.library",
            Styles = styles.ToImmutable(), Effects = effects.ToImmutable(),
            IncludesFonts = package.IncludesFonts,
            FontFamilies = package.IncludesFonts ? PrivateFontFiles.References(package.FontFamilies) : []
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, options);
        if (manifestBytes.Length > MAXIMUM_MANIFEST_BYTES || files.Count >= MAXIMUM_ENTRY_COUNT ||
            files.Values.Sum(bytes => (long)bytes.Length) + manifestBytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("交换包条目数量、清单或去重后内容超过容量预算。");
        }
        files.Add(MANIFEST_NAME, manifestBytes);
        return files;
    }

    private static EffectLibraryPackage ReadArchive(Stream stream, bool stylesOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Span<byte> signature = stackalloc byte[4];
            if (stream.Read(signature) != 4 || !signature.SequenceEqual("PK\u0003\u0004"u8))
            {
                throw new InvalidDataException("不支持旧版样式或效果库交换文件，请使用当前版本重新导出。");
            }
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            if (archive.Entries.Count is < 1 or > MAXIMUM_ENTRY_COUNT)
            {
                throw new InvalidDataException("交换包条目数量无效。");
            }
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Length > EntryMaximum(entry.FullName) || !entries.TryAdd(entry.FullName, entry))
                {
                    throw new InvalidDataException("交换包包含重复或超预算条目。");
                }
                expanded = checked(expanded + entry.Length);
                if (expanded > MAXIMUM_FILE_BYTES)
                {
                    throw new InvalidDataException("交换包解压内容超过 1 GiB。");
                }
            }
            if (!entries.TryGetValue(MANIFEST_NAME, out var manifestEntry))
            {
                throw new InvalidDataException("交换包缺少清单。");
            }
            var manifestBytes = ReadEntry(manifestEntry, MAXIMUM_MANIFEST_BYTES, cancellationToken);
            _ = strictUtf8.GetCharCount(manifestBytes);
            using var document = JsonDocument.Parse(manifestBytes, new() { MaxDepth = 8 });
            RejectDuplicateKeys(document.RootElement);
            var manifest = document.RootElement.Deserialize<EffectLibraryPackageManifest>(options) ??
                throw new InvalidDataException("交换包清单不能为 null。");
            if (manifest.Version != (stylesOnly ? 1 : 2) || manifest.Format != (stylesOnly ? "aeginext.styles" : "aeginext.library") ||
                manifest.Styles.IsDefault || manifest.Effects.IsDefault || manifest.FontFamilies.IsDefault ||
                manifest.Styles.Length > 256 || manifest.Effects.Length > 512 || manifest.FontFamilies.Length > 256 ||
                stylesOnly && !manifest.Effects.IsEmpty)
            {
                throw new InvalidDataException("不支持的交换包版本或无效的清单集合。");
            }
            if (!manifest.IncludesFonts && !manifest.FontFamilies.IsEmpty)
            {
                throw new InvalidDataException("不携带字体的交换包不能声明字体文件。");
            }
            var referenced = new HashSet<string>(StringComparer.Ordinal) { MANIFEST_NAME };
            var styles = ImmutableArray.CreateBuilder<SubtitleStylePreset>();
            foreach (var file in manifest.Styles)
            {
                var content = ReadReferenced(entries, referenced, file, "styles/", ".json", cancellationToken);
                var collection = SubtitleStylePresetMetadataStore.Deserialize(content);
                if (collection.Presets.Length != 1 || collection.Presets[0].Id != PathIdentity(file.Path, "styles/", ".json"))
                {
                    throw new InvalidDataException("包内样式身份与文件路径不一致。");
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
                    throw new InvalidDataException("包内特效身份与文件路径不一致。");
                }
                effects.Add(preset);
            }
            var fontData = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal);
            var families = ImmutableArray.CreateBuilder<FontFamilyBundle>();
            foreach (var family in manifest.FontFamilies)
            {
                if (family is null || family.Files.IsDefaultOrEmpty)
                {
                    throw new InvalidDataException("字体族清单无效。");
                }
                var files = ImmutableArray.CreateBuilder<EmbeddedSubtitleFont>();
                foreach (var file in family.Files)
                {
                    if (file is null)
                    {
                        throw new InvalidDataException("字体文件引用不能为 null。");
                    }
                    var path = "fonts/" + PrivateFontFiles.Name(file.Sha256);
                    if (!fontData.TryGetValue(file.Sha256, out var data))
                    {
                        if (!entries.TryGetValue(path, out var entry))
                        {
                            throw new InvalidDataException("交换包缺少字体文件。");
                        }
                        var bytes = ReadEntry(entry, FontFamilyBundleValidator.MAXIMUM_FONT_BYTES, cancellationToken);
                        if (Digest(bytes) != file.Sha256)
                        {
                            throw new InvalidDataException("字体文件与清单 SHA-256 不一致。");
                        }
                        data = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(bytes);
                        fontData.Add(file.Sha256, data);
                        referenced.Add(path);
                    }
                    files.Add(new(file.FileName, file.Sha256, data));
                }
                families.Add(new(family.FamilyName, files.ToImmutable()));
            }
            if (entries.Count != referenced.Count)
            {
                throw new InvalidDataException("交换包存在未列入清单的条目。");
            }
            var result = new EffectLibraryPackage
            {
                Styles = new() { Presets = styles.ToImmutable() },
                Effects = new() { Presets = effects.ToImmutable() },
                IncludesFonts = manifest.IncludesFonts,
                FontFamilies = families.ToImmutable()
            };
            _ = SubtitleStylePresetMetadataStore.Serialize(result.Styles);
            _ = EffectScriptPresetStore.Serialize(result.Effects);
            PrivateFontFiles.ValidateCoverage(result.Styles, result.FontFamilies, result.IncludesFonts);
            return result;
        }
        catch (Exception error) when (error is ArgumentException or JsonException or OverflowException or NotSupportedException)
        {
            throw new InvalidDataException("交换包格式无效。", error);
        }
    }

    private static byte[] ReadReferenced(Dictionary<string, ZipArchiveEntry> entries, HashSet<string> referenced,
        EffectLibraryPackageFile file, string prefix, string extension, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            throw new InvalidDataException("交换包清单条目无效。");
        }
        _ = PrivateFontFiles.Name(file.Sha256);
        _ = PathIdentity(file.Path, prefix, extension);
        if (!referenced.Add(file.Path) || !entries.TryGetValue(file.Path, out var entry))
        {
            throw new InvalidDataException("交换包清单包含重复路径或缺少文件。");
        }
        var bytes = ReadEntry(entry, EntryMaximum(file.Path), cancellationToken);
        if (Digest(bytes) != file.Sha256)
        {
            throw new InvalidDataException("交换包条目与清单 SHA-256 不一致。");
        }
        return bytes;
    }

    private static int EntryMaximum(string name)
    {
        if (name == MANIFEST_NAME)
        {
            return MAXIMUM_MANIFEST_BYTES;
        }
        if (name.StartsWith("styles/", StringComparison.Ordinal))
        {
            _ = PathIdentity(name, "styles/", ".json");
            return SubtitleStylePresetMetadataStore.MAXIMUM_FILE_BYTES;
        }
        if (name.StartsWith("fonts/", StringComparison.Ordinal) && name.Length == 75 && name.EndsWith(".font", StringComparison.Ordinal))
        {
            _ = PrivateFontFiles.Name(name[6..70]);
            return FontFamilyBundleValidator.MAXIMUM_FONT_BYTES;
        }
        _ = PathIdentity(name, "effects/", ".aegieffect");
        return EffectScriptPresetStore.MAXIMUM_FILE_BYTES;
    }

    private static Guid PathIdentity(string? path, string prefix, string extension)
    {
        if (path is null || path.Length != prefix.Length + 32 + extension.Length ||
            !path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(extension, StringComparison.Ordinal) ||
            !Guid.TryParseExact(path.AsSpan(prefix.Length, 32), "N", out var id) || id == Guid.Empty ||
            path.AsSpan(prefix.Length, 32).ContainsAnyExcept("0123456789abcdef"))
        {
            throw new InvalidDataException("交换包包含未知或非法路径。");
        }
        return id;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, int maximum, CancellationToken cancellationToken)
    {
        if (entry.Length > maximum)
        {
            throw new InvalidDataException("交换包条目超过容量预算。");
        }
        using var stream = entry.Open();
        var bytes = new byte[checked((int)entry.Length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(bytes.AsSpan(offset, Math.Min(65536, bytes.Length - offset)));
            if (read == 0)
            {
                throw new InvalidDataException("交换包条目长度不一致。");
            }
            offset += read;
        }
        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("交换包条目长度不一致。");
        }
        return bytes;
    }

    private static void WriteArchive(Stream destination, Dictionary<string, ReadOnlyMemory<byte>> files, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, true);
        foreach (var file in files.OrderBy(file => file.Key == MANIFEST_NAME ? 0 : 1).ThenBy(file => file.Key, StringComparer.Ordinal))
        {
            using var stream = archive.CreateEntry(file.Key, CompressionLevel.Optimal).Open();
            var data = file.Value.Span;
            while (!data.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(data.Length, 65536);
                stream.Write(data[..count]);
                data = data[count..];
            }
        }
    }

    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("交换包清单字段重复。");
                }
                RejectDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateKeys(item);
            }
        }
    }
}
