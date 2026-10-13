using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

/// <summary>已解析的字幕及可供确认的格式损失。</summary>
public sealed record AssImportResult(ImmutableArray<SubtitleLine> Lines,
    ImmutableArray<SubtitleFormatDiagnostic> Diagnostics)
{
    public ImmutableArray<SubtitleClipImport> Clips { get; init; } = [];

    /// <summary>格式导入产生的完整原生图层，包含字幕及可编辑图形。</summary>
    public ImmutableArray<ProjectLayer> Layers { get; init; } = [];
}
