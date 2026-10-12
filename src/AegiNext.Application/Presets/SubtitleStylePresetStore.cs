using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Presets;

/// <summary>严格、版本化、自包含的 .aegistyles UTF-8 存储。</summary>
public static class SubtitleStylePresetStore
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目常量采用 ALL_UPPER。")]
    public const int MAXIMUM_FILE_BYTES = 96 * 1024 * 1024;
    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions options = CreateOptions();

    /// <summary>读取并验证交换文件；文件不存在时抛出对应 I/O 异常。</summary>
    public static async Task<SubtitleStylePresetCollection> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var bytes = await PresetFileReader.ReadAsync(path, MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
        return Deserialize(bytes);
    }

    /// <summary>验证完整集合后原子替换目标文件；提交前失败或取消保留旧文件。</summary>
    public static Task SaveAsync(SubtitleStylePresetCollection collection, string path, CancellationToken cancellationToken = default)
    {
        return SaveAsync(collection, path, null, cancellationToken);
    }

    /// <summary>Prepares an atomic replacement, invoking the commit boundary only after the temporary file is flushed.</summary>
    public static async Task SaveAsync(SubtitleStylePresetCollection collection, string path, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Serialize(collection);
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

    /// <summary>产生包含字体 base64 的 UTF-8 交换文件。</summary>
    public static byte[] Serialize(SubtitleStylePresetCollection collection)
    {
        SubtitleStylePresetValidator.Validate(collection);
        var result = JsonSerializer.SerializeToUtf8Bytes(collection, options);
        if (result.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("预设文件超过 96 MiB。");
        }

        return result;
    }

    /// <summary>拒绝无效 UTF-8、重复／未知／缺失字段和任何不完整字体载荷。</summary>
    public static SubtitleStylePresetCollection Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("预设文件超过 96 MiB。");
        }

        try
        {
            _ = strictUtf8.GetCharCount(json);
            using var parsed = JsonDocument.Parse(json.ToArray(), new() { MaxDepth = 16 });
            RejectDuplicateKeys(parsed.RootElement);
            var upgraded = SubtitlePositionJsonMigration.UpgradeVersionOne(parsed.RootElement, "presets");
            var content = upgraded ?? JsonNode.Parse(parsed.RootElement.GetRawText())!.AsObject();
            if (!parsed.RootElement.TryGetProperty("version", out var version) || !version.TryGetInt32(out var number) ||
                number is not (1 or 2 or 3 or 4 or 5 or 6 or SubtitleStylePresetCollection.CURRENT_VERSION))
            {
                throw new JsonException("样式库版本无效。");
            }
            SubtitleContentJsonMigration.UpgradeStyleLibrary(content, number);
            SubtitleMarginsJsonMigration.UpgradeStyleLibrary(content, number);
            SubtitleAppearanceJsonMigration.UpgradeStyleLibrary(content, number);
            SubtitleScalingJsonMigration.UpgradeStyleLibrary(content, number);
            var collection = content.Deserialize<SubtitleStylePresetCollection>(options) ??
                throw new JsonException("样式库不能为 null。");
            SubtitleStylePresetValidator.Validate(collection);
            return collection;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("字幕样式 JSON 格式无效。", error);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind == JsonTypeInfoKind.Object &&
                (info.Type.Namespace == typeof(SubtitleStylePreset).Namespace || info.Type.Namespace == typeof(SubtitleStyle).Namespace ||
                 info.Type == typeof(TimingPostProcessorOptions)))
            {
                foreach (var property in info.Properties)
                {
                    property.IsRequired = !(info.Type == typeof(SubtitleStylePreset) && property.Name == "timingPostProcessor") &&
                        !(info.Type == typeof(SubtitleStyle) && property.Name is "fontVariant" or "textAlign") &&
                        !(info.Type == typeof(SubtitleFontVariant) && property.Name == "postScriptName") &&
                        !(info.Type == typeof(SubtitleInlineStyleOverride) && property.Name is "fontVariant" or "clearFontVariant");
                }
            }
        });
        var result = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 16,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver
        };
        result.Converters.Add(new EmbeddedFontDataJsonConverter());
        result.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return result;
    }

    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException($"重复的字段：{property.Name}");
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
