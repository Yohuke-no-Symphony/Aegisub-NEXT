using System.Collections.Immutable;

namespace AegiNext.SubtitleFormats;

/// <summary>按源文件顺序提取的全部 ASS 样式和独立于具体样式的脚本级诊断。</summary>
public sealed record AssStyleImportResult(ImmutableArray<AssImportedStyle> Styles,
    ImmutableArray<SubtitleFormatDiagnostic> Diagnostics);
