using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

/// <summary>按源参考分辨率转换的具名 ASS 样式及其来源行上的转换损失。</summary>
public sealed record AssImportedStyle(string Name, SubtitleStyle Style,
    ImmutableArray<SubtitleFormatDiagnostic> Diagnostics);
