using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Presets;

/// <summary>捕获样式参数及其引用的工程字体族，并准备不持有项目字体引用的样式应用。</summary>
public static class SubtitleStylePresetService
{
    /// <summary>保存样式参数快照；仅引用工程字体时捕获完整字体族，工程字体缺失或摘要不符时明确失败。</summary>
    public static async Task<CapturedSubtitleStyle> CaptureAsync(string name, SubtitleStyle style, ProjectDocument project,
        string projectDirectory, IFontFamilySource fontSource, Guid? id = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fontSource);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectValidator.Validate(project);
        ProjectValidator.ValidateSubtitleStyle(style);
        var preset = new SubtitleStylePreset(id ?? Guid.NewGuid(), name, style with { FontAssetId = null });
        SubtitleStylePresetValidator.Validate(preset);
        if (style.FontAssetId is not { } fontId)
        {
            return new(preset, []);
        }
        var asset = project.Assets.FirstOrDefault(item => item.Id == fontId && item.Kind == ProjectAssetKind.FONT) ??
            throw new InvalidDataException("样式引用的项目字体不存在。");
        var path = ProjectAssetLocation.Resolve(asset, projectDirectory);
        var bytes = await PresetFileReader.ReadAsync(path, FontFamilyBundleValidator.MAXIMUM_FONT_BYTES, cancellationToken).ConfigureAwait(false);
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (asset.Sha256 is { } expected && !string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("项目字体内容与记录的 SHA-256 不一致。");
        }
        var primary = new EmbeddedSubtitleFont(Path.GetFileName(path), digest, ImmutableCollectionsMarshal.AsImmutableArray(bytes));
        var family = await fontSource.CaptureAsync(style.FontFamily, primary, cancellationToken).ConfigureAwait(false);
        FontFamilyBundleValidator.Validate(family);
        preset = preset with { Style = preset.Style with { FontFamily = family.FamilyName } };
        SubtitleStylePresetValidator.Validate(preset);
        return new(preset, [family]);
    }

    /// <summary>将已安装便携样式应用到选中字幕，不再复制字体到工程资源。</summary>
    public static async Task<ProjectDocument> ApplyAsync(SubtitleStylePreset preset, ProjectDocument project,
        string projectDirectory, IEnumerable<Guid> subtitleIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subtitleIds);
        var selection = subtitleIds.ToHashSet();
        ProjectValidator.Validate(project);
        if (project.Subtitles.Count(line => selection.Contains(line.Id)) != selection.Count)
        {
            throw new InvalidDataException("待套用样式的字幕不存在。");
        }
        var prepared = await PrepareAsync(preset, project, projectDirectory, cancellationToken).ConfigureAwait(false);
        if (selection.Count == 0)
        {
            return project;
        }
        var rebased = SubtitleStyleReplacement.Apply(prepared.Project, selection, prepared.Style);
        var result = rebased with
        {
            Subtitles = rebased.Subtitles.Select(line => selection.Contains(line.Id)
                ? line with { StyleName = preset.Name, StylePresetId = preset.Id } : line).ToImmutableArray()
        };
        ProjectValidator.Validate(result);
        return result;
    }

    /// <summary>准备使用应用私有字体库解析的样式，保留工程和编辑历史。</summary>
    public static Task<PreparedSubtitleStyle> PrepareAsync(SubtitleStylePreset preset, ProjectDocument project,
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        SubtitleStylePresetValidator.Validate(preset);
        ProjectValidator.Validate(project);
        return Task.FromResult(new PreparedSubtitleStyle(project, preset.Style, preset.Name, preset.Id));
    }
}
