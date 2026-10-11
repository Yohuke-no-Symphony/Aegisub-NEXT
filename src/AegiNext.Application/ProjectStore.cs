using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>版本化 JSON 工程存储；同目录临时文件落盘成功后才原子替换目标。</summary>
public static class ProjectStore
{
    private static readonly JsonSerializerOptions options = CreateOptions();
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        MaxDepth = 128,
        AllowDuplicateProperties = false
    };

    /// <summary>从文件加载工程；重复键、未知字段、缺少必需字段与非法版本全部拒绝。</summary>
    public static async Task<ProjectDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        try
        {
            using var parsed = await JsonDocument.ParseAsync(stream, documentOptions, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var document = DeserializeCore(parsed.RootElement);
            cancellationToken.ThrowIfCancellationRequested();
            return document;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("项目 JSON 格式无效。", error);
        }
    }

    /// <summary>先验证快照并完整写入临时文件；提交前取消或失败不修改已有工程。</summary>
    public static Task SaveAsync(ProjectDocument document, string path, CancellationToken cancellationToken = default)
    {
        return WriteAsync(document, path, true, null, cancellationToken);
    }

    /// <summary>写入完整临时文件后，在原子替换前进入调用方的提交阶段。</summary>
    public static Task SaveAsync(ProjectDocument document, string path, Action beforeCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeCommit);
        return WriteAsync(document, path, true, beforeCommit, cancellationToken);
    }

    /// <summary>原子提交新工程文件；已有文件或目录永不覆盖。</summary>
    public static Task CreateAsync(ProjectDocument document, string path, CancellationToken cancellationToken = default)
    {
        return WriteAsync(document, path, false, null, cancellationToken);
    }

    /// <summary>在新文件原子提交前进入调用方的提交阶段，保留不覆盖语义。</summary>
    public static Task CreateAsync(ProjectDocument document, string path, Action beforeCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeCommit);
        return WriteAsync(document, path, false, beforeCommit, cancellationToken);
    }

    private static async Task WriteAsync(ProjectDocument document, string path, bool overwrite, Action? beforeCommit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectValidator.Validate(document);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            File.Move(temporary, fullPath, overwrite);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>输出可保存的 UTF-8 JSON 字节，时间保持有理表示。</summary>
    public static byte[] Serialize(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        return JsonSerializer.SerializeToUtf8Bytes(document, options);
    }

    /// <summary>验证工程可按存储配置完整序列化，不保留完整 JSON 字节数组。</summary>
    public static void ValidateSerialization(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        JsonSerializer.Serialize(Stream.Null, document, options);
    }

    /// <summary>计算存储 JSON 的 SHA-256 小写指纹，不保留完整 JSON 字节数组。</summary>
    public static string ComputeFingerprint(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        using var hash = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        JsonSerializer.Serialize(stream, document, options);
        stream.FlushFinalBlock();
        return Convert.ToHexStringLower(hash.Hash!);
    }

    /// <summary>解析 UTF-8 JSON，并统一报告工程格式错误。</summary>
    public static ProjectDocument Deserialize(ReadOnlySpan<byte> json)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json.ToArray(), documentOptions);
            return DeserializeCore(parsed.RootElement);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("项目 JSON 格式无效。", error);
        }
    }

    /// <summary>解析外部 JSON 元素；迁移前拒绝所有层级的重复字段并保持严格工程校验。</summary>
    public static ProjectDocument Deserialize(JsonElement json)
    {
        try
        {
            RejectDuplicateKeys(json);
            return DeserializeCore(json);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException("项目 JSON 格式无效。", error);
        }
    }

    private static ProjectDocument DeserializeCore(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number) || number is not (3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or ProjectDocument.CURRENT_VERSION))
        {
            throw new InvalidDataException($"只支持项目版本 3、4、5、6、7、8、9、10、11、12、13 和 {ProjectDocument.CURRENT_VERSION}，更旧项目需要使用对应版本打开。");
        }

        if (number >= 5)
        {
            ClipMaskJsonMigration.RejectCurrentLegacyFields(root);
        }

        if (number == ProjectDocument.CURRENT_VERSION)
        {
            var current = root.Deserialize<ProjectDocument>(options) ?? throw new JsonException("项目不能为空。");
            ProjectValidator.Validate(current);
            return current;
        }

        var content = JsonNode.Parse(root.GetRawText(), documentOptions: documentOptions)!.AsObject();
        GraphicAssetJsonMigration.Upgrade(content);
        if (number < 13)
        {
            SubtitleAnimationJsonMigration.RejectScopedTransformFields(content);
        }
        if (number < 12)
        {
            SubtitleAnimationJsonMigration.RejectUndeclaredFields(content);
        }
        SubtitleAppearanceJsonMigration.UpgradeProject(content, number);
        SubtitleMarginsJsonMigration.UpgradeProject(content, number);
        if (number is 3 or 4)
        {
            SubtitleContentJsonMigration.UpgradeProject(content, number);
            ClipMaskJsonMigration.UpgradeLegacy(content);
            using var normalized = JsonDocument.Parse(content.ToJsonString(new()
            {
                MaxDepth = 128
            }), documentOptions);
            content = VectorAnimationJsonMigration.Upgrade(normalized.RootElement, options);
        }

        PlaybackOriginJsonMigration.Upgrade(content, number);
        if (number < 9)
        {
            FlatClipJsonMigration.Upgrade(content, options);
        }
        if (number < 11)
        {
            SubtitleKaraokeStyleJsonMigration.Upgrade(content, options);
        }
        content["version"] = ProjectDocument.CURRENT_VERSION;
        var document = content.Deserialize<ProjectDocument>(options) ?? throw new JsonException("项目不能为空。");
        if (number < 11)
        {
            return LegacySubtitleKaraokeMigration.Upgrade(document);
        }
        ProjectValidator.Validate(document);
        return document;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type.Namespace == typeof(ProjectDocument).Namespace && info.Kind == JsonTypeInfoKind.Object)
            {
                foreach (var property in info.Properties)
                {
                    property.IsRequired = (property.Get is not null || property.Set is not null) &&
                        !(info.Type == typeof(ProjectDocument) && property.Name == "timelineViewState") &&
                        !(info.Type == typeof(ProjectDocument) && property.Name == "colorTags") &&
                        !(info.Type == typeof(TimelineViewState) && property.Name is "collapsedTrackIds" or "collapsedAnimationRows" or "expandedAnimationClips") &&
                        !(info.Type == typeof(SubtitleStyle) && property.Name is "fontVariant" or "textAlign") &&
                        !(info.Type == typeof(SubtitleFontVariant) && property.Name == "postScriptName") &&
                        !(info.Type == typeof(SubtitleInlineStyleOverride) && property.Name is "fontVariant" or "clearFontVariant") &&
                        !(info.Type == typeof(Keyframe) && property.Name is "componentCurves" or "exponent" or "reverse") &&
                        !(info.Type == typeof(AnimationCurve) && property.Name is "exponent" or "reverse") &&
                        !(info.Type == typeof(AnimationTrack) && property.Name is "initialValue" or "transforms" or "colorSpace") &&
                        !(info.Type == typeof(AnimationTrackTarget) && property.Name is "textRangeId" or "state") &&
                        !(info.Type == typeof(TimelineAnimationRowId) && property.Name is "textRangeId" or "state") &&
                        !(info.Type == typeof(AnimationTransformOperation) && property.Name is "componentMask" or "mode") &&
                        !(info.Type == typeof(SubtitleAnimationRange) && property.Name is "rotation" or "pivot" or "offset" or "generatedOrigin") &&
                        !(info.Type == typeof(SubtitleLine) && property.Name is "karaokeStyle" or "karaokeStyleSpans" or "inactiveKaraoke" or "styleName" or "stylePresetId" or "colorTagId" or "animationRanges") &&
                        !(info.Type == typeof(SubtitleKaraokeStyleSpan) && property.Name is "activeStyle" or "inactiveStyle") &&
                        !(info.Type == typeof(ProjectTrack) && property.Name is "defaultStyle" or "stylePresetId" or "stylePresetName" or "autoApplyStyle");
                    if (info.Type == typeof(SubtitleLine) && property.Name == "inactiveKaraoke")
                    {
                        property.ShouldSerialize = static (instance, _) => !((SubtitleLine)instance).InactiveKaraoke.IsEmpty;
                    }
                    if (info.Type == typeof(TimelineViewState) && property.Name == "collapsedAnimationRows")
                    {
                        property.ShouldSerialize = static (_, _) => false;
                    }
                    if (info.Type == typeof(SubtitleLine) && property.Name == "animationRanges")
                    {
                        property.ShouldSerialize = static (instance, _) => !((SubtitleLine)instance).AnimationRanges.IsEmpty;
                    }
                    if (info.Type == typeof(SubtitleLine) && property.Name == "karaokeStyleSpans")
                    {
                        property.ShouldSerialize = static (instance, _) => !((SubtitleLine)instance).KaraokeStyleSpans.IsEmpty;
                    }
                    if (info.Type == typeof(ProjectDocument) && property.Name == "colorTags")
                    {
                        property.ShouldSerialize = static (instance, _) => !((ProjectDocument)instance).ColorTags.IsEmpty;
                    }
                    if (info.Type == typeof(SubtitleLine) && property.Name == "colorTagId")
                    {
                        property.ShouldSerialize = static (instance, _) => ((SubtitleLine)instance).ColorTagId.HasValue;
                    }
                }
            }
        });
        var result = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            MaxDepth = 128,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            TypeInfoResolver = resolver
        };
        result.Converters.Add(new MediaTimeJsonConverter());
        result.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return result;
    }

    private static void RejectDuplicateKeys(JsonElement element, int depth = 0)
    {
        if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array && depth >= options.MaxDepth)
        {
            throw new JsonException("项目 JSON 嵌套过深。");
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException($"重复的字段：{property.Name}");
                }

                RejectDuplicateKeys(property.Value, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateKeys(item, depth + 1);
            }
        }
    }
}
