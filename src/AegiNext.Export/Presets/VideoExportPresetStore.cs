using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AegiNext.Export.Presets;

/// <summary>严格、版本化的 .aegiexports UTF-8 交换文件和个人库原子存储。</summary>
public static class VideoExportPresetStore
{
    public const int MAXIMUM_FILE_BYTES = 4 * 1024 * 1024;

    private static readonly UTF8Encoding strictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions options = CreateOptions();

    /// <summary>有界读取并完整验证压制预设；文件不存在时保留对应 I/O 异常。</summary>
    public static async Task<VideoExportPresetCollection> LoadAsync(string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (stream.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("压制预设文件超过 4 MiB。");
        }

        using var buffer = new MemoryStream(checked((int)stream.Length));
        var chunk = new byte[65536];
        var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
        while (read > 0)
        {
            if (buffer.Length + read > MAXIMUM_FILE_BYTES)
            {
                throw new InvalidDataException("压制预设文件超过 4 MiB。");
            }

            buffer.Write(chunk, 0, read);
            read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Deserialize(buffer.ToArray());
    }

    /// <summary>验证完整集合后原子替换目标文件；提交前失败或取消保留旧文件并清理暂存。</summary>
    public static async Task SaveAsync(VideoExportPresetCollection collection, string path,
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
            File.Move(temporary, fullPath, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>验证身份、名称和完整参数后生成容量受限的 UTF-8 交换文档。</summary>
    public static byte[] Serialize(VideoExportPresetCollection collection)
    {
        VideoExportPresetValidator.Validate(collection);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(collection, options);
        if (bytes.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("压制预设文件超过 4 MiB。");
        }

        return bytes;
    }

    /// <summary>拒绝非法 UTF-8、重复、未知、缺失字段、整数枚举和未支持的版本或参数。</summary>
    public static VideoExportPresetCollection Deserialize(ReadOnlySpan<byte> json)
    {
        if (json.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("压制预设文件超过 4 MiB。");
        }

        try
        {
            _ = strictUtf8.GetCharCount(json);
            using var document = JsonDocument.Parse(json.ToArray(), new() { MaxDepth = 8 });
            RejectDuplicateKeys(document.RootElement);
            var normalized = Migrate(document.RootElement);
            var collection = normalized.Deserialize<VideoExportPresetCollection>(options) ??
                             throw new JsonException("压制预设库不能为 null。");
            VideoExportPresetValidator.Validate(collection);
            return collection;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("压制预设必须使用有效的 UTF-8 JSON。", error);
        }
    }

    private static JsonElement Migrate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number is not (1 or 2))
        {
            return root;
        }

        if (!root.TryGetProperty("presets", out var presets) || presets.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("旧版压制预设集合无效。");
        }

        foreach (var preset in presets.EnumerateArray())
        {
            if (preset.ValueKind != JsonValueKind.Object || !preset.TryGetProperty("settings", out var settings) ||
                settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty("codec", out var codec) ||
                codec.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("旧版压制预设参数无效。");
            }

            if (number == 1 && (!settings.TryGetProperty("crf", out var crf) ||
                crf.ValueKind != JsonValueKind.Number || !crf.TryGetInt32(out _) || settings.TryGetProperty("quality", out _) ||
                !LegacyChoice(settings, "codec", "Auto", "H264", "Hevc") ||
                !LegacyChoice(settings, "audioMode", "Copy", "Aac", "None") ||
                !LegacyChoice(settings, "rateControlMode", "CRF", "VBR", "CBR")))
            {
                throw new JsonException("旧版压制预设参数不符合 v1 合同。");
            }
        }

        var migrated = JsonNode.Parse(root.GetRawText())!.AsObject();
        migrated["version"] = VideoExportPresetCollection.CURRENT_VERSION;
        foreach (var preset in migrated["presets"]!.AsArray())
        {
            var settings = preset!["settings"]!.AsObject();
            if (number == 1)
            {
                settings["quality"] = settings["crf"]!.DeepClone();
                settings.Remove("crf");
            }

            if (!settings.ContainsKey("container"))
            {
                var codec = settings["codec"]!.Deserialize<VideoCodec>(options);
                settings["container"] = VideoExportCapabilities.GetDefaultContainer(codec).ToString();
            }
        }

        return JsonSerializer.SerializeToElement(migrated);
    }

    private static bool LegacyChoice(JsonElement settings, string name, params string[] choices) =>
        settings.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        choices.Contains(value.GetString(), StringComparer.OrdinalIgnoreCase);

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Kind == JsonTypeInfoKind.Object &&
                (info.Type == typeof(VideoExportPresetCollection) || info.Type == typeof(VideoExportPreset) ||
                 info.Type == typeof(VideoExportSettings)))
            {
                foreach (var property in info.Properties)
                {
                    property.IsRequired = true;
                }
            }
        });
        var result = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 8,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver
        };
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
