using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Diagnostics.CodeAnalysis;
using AegiNext.Core.Effects;

namespace AegiNext.Application.Presets;

/// <summary>个人脚本 JSON 库与 .aegifx 文本交换文件的严格、原子存储。</summary>
public static class EffectScriptPresetStore
{
    /// <summary>个人脚本库 UTF-8 文档的最大容量。</summary>
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目交换文件预算常量采用 ALL_UPPER。")]
    public const int MAXIMUM_FILE_BYTES = 8 * 1024 * 1024;
    private static readonly UTF8Encoding utf8 = new(false, true);
    private static readonly JsonSerializerOptions options = CreateOptions();

    /// <summary>完整读取、验证个人库，不修改磁盘或调用方快照。</summary>
    public static async Task<EffectScriptPresetDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await PresetFileReader.ReadAsync(path, MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
        return Deserialize(bytes);
    }

    /// <summary>完整验证个人脚本库后生成有界 UTF-8 JSON。</summary>
    public static byte[] Serialize(EffectScriptPresetDocument collection)
    {
        EffectScriptPresetService.Validate(collection);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(collection, options);
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("特效脚本库超过 8 MiB。");
        }

        return bytes;
    }

    /// <summary>拒绝非法 UTF-8、重复、未知或缺失字段，并验证全部个人脚本。</summary>
    public static EffectScriptPresetDocument Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("特效脚本库超过 8 MiB。");
        }

        try
        {
            _ = utf8.GetCharCount(json);
            using var document = JsonDocument.Parse(json.ToArray(), new() { MaxDepth = 8 });
            ValidateKeys(document.RootElement);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out _) || !root.TryGetProperty("presets", out _))
            {
                throw new JsonException("脚本库缺少版本或模板集合。");
            }

            var collection = root.Deserialize<EffectScriptPresetDocument>(options) ?? throw new JsonException("脚本库不能为 null。");
            EffectScriptPresetService.Validate(collection);
            return collection;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("特效脚本库不是有效的 UTF-8 JSON。", error);
        }
    }

    /// <summary>验证后原子保存，取消、失败或非法输入保留原文件。</summary>
    public static Task SaveAsync(EffectScriptPresetDocument collection, string path, CancellationToken cancellationToken = default)
    {
        return SaveAsync(collection, path, null, cancellationToken);
    }

    /// <summary>Prepares the library file and invokes the commit boundary immediately before atomic replacement.</summary>
    public static Task SaveAsync(EffectScriptPresetDocument collection, string path, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WriteAtomicAsync(path, Serialize(collection), beforeCommit, cancellationToken);
    }

    /// <summary>读取严格单项的 .aegieffect 文件，保留既有身份、展示名称及脚本原文。</summary>
    public static async Task<EffectScriptPreset> ReadPresetAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await PresetFileReader.ReadAsync(path, MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
        return DeserializePreset(bytes);
    }

    /// <summary>将一个个人特效模板编码为沿用版本化脚本文档的单项交换文件。</summary>
    public static byte[] SerializePreset(EffectScriptPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return Serialize(new() { Presets = [preset] });
    }

    /// <summary>拒绝空集合、多项集合以及任何不符合个人模板约束的完整特效文件。</summary>
    public static EffectScriptPreset DeserializePreset(ReadOnlySpan<byte> bytes)
    {
        var document = Deserialize(bytes);
        if (document.Presets.Length != 1)
        {
            throw new InvalidDataException("完整特效文件必须包含一个模板。");
        }
        return document.Presets[0];
    }

    /// <summary>验证完整个人模板后原子写入 .aegieffect；失败或取消保留既有文件。</summary>
    public static Task WritePresetAsync(EffectScriptPreset preset, string path, CancellationToken cancellationToken = default)
    {
        return WritePresetAsync(preset, path, null, cancellationToken);
    }

    /// <summary>完整模板准备完毕后，在原子替换前调用宿主提交边界。</summary>
    public static Task WritePresetAsync(EffectScriptPreset preset, string path, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WriteAtomicAsync(path, SerializePreset(preset), beforeCommit, cancellationToken);
    }

    /// <summary>读取单个可交换脚本，采用严格 UTF-8 并返回解析后的同一份源。</summary>
    public static async Task<EffectScriptTemplate> ReadScriptAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await PresetFileReader.ReadAsync(path, EffectScriptParser.MAXIMUM_SOURCE_CHARACTERS * 4, cancellationToken).ConfigureAwait(false);
        string source;
        try
        {
            source = utf8.GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException error)
        {
            throw new InvalidDataException("特效脚本必须使用 UTF-8 编码。", error);
        }

        return new(source, EffectScriptParser.Parse(source));
    }

    /// <summary>导出内置或个人脚本的原文，格式为独立 UTF-8 .aegifx 文件。</summary>
    public static Task WriteScriptAsync(string source, string path, CancellationToken cancellationToken = default)
    {
        return WriteScriptAsync(source, path, null, cancellationToken);
    }

    /// <summary>Prepares an exchanged script and invokes the commit boundary immediately before atomic replacement.</summary>
    public static Task WriteScriptAsync(string source, string path, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = EffectScriptParser.Parse(source);
        return WriteAtomicAsync(path, utf8.GetBytes(source), beforeCommit, cancellationToken);
    }

    private static async Task WriteAtomicAsync(string path, byte[] bytes, Action? beforeCommit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("特效脚本文件超过 8 MiB。");
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
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

    private static void ValidateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException($"重复的字段：{property.Name}。");
                }

                ValidateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ValidateKeys(item);
            }
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind == JsonTypeInfoKind.Object &&
                (info.Type == typeof(EffectScriptPresetDocument) || info.Type == typeof(EffectScriptPreset)))
            {
                foreach (var property in info.Properties)
                {
                    property.IsRequired = true;
                }
            }
        });
        return new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver,
            MaxDepth = 8
        };
    }
}
