using System.Collections.Immutable;

namespace AegiNext.SubtitleFormats;

/// <summary>完整可写入文本及导出时的格式损失。</summary>
public sealed record SubtitleFormatWriteResult(string Text, ImmutableArray<SubtitleFormatDiagnostic> Diagnostics);
