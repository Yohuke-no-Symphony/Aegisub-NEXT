using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Presets;

/// <summary>在工程字体资源与可迁移样式之间转换；不修改输入快照或编辑历史。</summary>
public static class SubtitleStylePresetService
{
    /// <summary>捕获样式并内嵌其工程字体；缺失或被替换的字体明确失败。</summary>
    public static async Task<SubtitleStylePreset> CaptureAsync(string name, SubtitleStyle style, ProjectDocument project,
        string projectDirectory, Guid? id = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectValidator.Validate(project);
        ProjectValidator.ValidateSubtitleStyle(style);
        var preset = new SubtitleStylePreset(id ?? Guid.NewGuid(), name, style with { FontAssetId = null });
        SubtitleStylePresetValidator.Validate(preset);
        if (style.FontAssetId is { } fontId)
        {
            var asset = project.Assets.FirstOrDefault(item => item.Id == fontId && item.Kind == ProjectAssetKind.FONT) ??
                throw new InvalidDataException("样式引用的项目字体不存在。");
            var path = ProjectAssetLocation.Resolve(asset, projectDirectory);
            var bytes = await PresetFileReader.ReadAsync(path, SubtitleStylePresetValidator.MAXIMUM_FONT_BYTES, cancellationToken).ConfigureAwait(false);
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (asset.Sha256 is { } expected && !string.Equals(digest, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("项目字体内容与记录的 SHA-256 不一致。");
            }

            preset = preset with
            {
                Font = new(Path.GetFileName(path), digest, ImmutableCollectionsMarshal.AsImmutableArray(bytes))
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        SubtitleStylePresetValidator.Validate(preset);
        return preset;
    }

    /// <summary>将便携样式套用到选中字幕，并在需要时导入或复用目标工程字体资源。</summary>
    public static async Task<ProjectDocument> ApplyAsync(SubtitleStylePreset preset, ProjectDocument project,
        string projectDirectory, IEnumerable<Guid> subtitleIds, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        ArgumentNullException.ThrowIfNull(subtitleIds);
        cancellationToken.ThrowIfCancellationRequested();
        SubtitleStylePresetValidator.Validate(preset);
        ProjectValidator.Validate(project);
        var selection = subtitleIds.ToHashSet();
        if (project.Subtitles.Count(line => selection.Contains(line.Id)) != selection.Count)
        {
            throw new InvalidDataException("待套用样式的字幕不存在。");
        }

        if (selection.Count == 0)
        {
            return project;
        }

        var prepared = await PrepareAsync(preset, project, projectDirectory, cancellationToken).ConfigureAwait(false);
        var rebased = SubtitleStyleReplacement.Apply(prepared.Project, selection, prepared.Style);
        var result = rebased with
        {
            Subtitles = rebased.Subtitles.Select(line => selection.Contains(line.Id)
                ? line with { StyleName = preset.Name, StylePresetId = preset.Id } : line).ToImmutableArray()
        };
        ProjectValidator.Validate(result);
        return result;
    }

    /// <summary>导入或复用便携样式字体并返回可提交的样式；不要求工程已有字幕，也不修改编辑历史。</summary>
    public static async Task<PreparedSubtitleStyle> PrepareAsync(SubtitleStylePreset preset, ProjectDocument project,
        string projectDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        SubtitleStylePresetValidator.Validate(preset);
        ProjectValidator.Validate(project);
        var assets = project.Assets;
        var style = preset.Style;
        if (preset.Font is { } font)
        {
            var asset = assets.FirstOrDefault(item => item.Kind == ProjectAssetKind.FONT &&
                string.Equals(item.Sha256, font.Sha256, StringComparison.OrdinalIgnoreCase));
            if (asset is not null)
            {
                var existing = await PresetFileReader.ReadAsync(ProjectAssetLocation.Resolve(asset, projectDirectory),
                    SubtitleStylePresetValidator.MAXIMUM_FONT_BYTES, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(existing)), font.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("目标项目已有字体资源被替换，不能复用。");
                }
            }
            else
            {
                if (assets.Length >= 10000)
                {
                    throw new InvalidDataException("项目资源数量已达到上限，无法导入字体。");
                }

                asset = await ImportFontAsync(font, projectDirectory, cancellationToken).ConfigureAwait(false);
                assets = assets.Add(asset);
            }

            style = style with { FontAssetId = asset.Id };
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = assets == project.Assets ? project : project with { Assets = assets };
        ProjectValidator.Validate(result);
        ProjectValidator.ValidateSubtitleStyle(style);
        return new(result, style, preset.Name, preset.Id);
    }

    private static async Task<ProjectAsset> ImportFontAsync(EmbeddedSubtitleFont font, string projectDirectory,
        CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"aeginext-style-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var temporary = Path.Combine(temporaryDirectory, font.FileName);
            await File.WriteAllBytesAsync(temporary, font.Data.AsMemory(), cancellationToken).ConfigureAwait(false);
            return await ProjectResources.ImportAsync(temporary, ProjectAssetKind.FONT, projectDirectory, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, true);
        }
    }
}
