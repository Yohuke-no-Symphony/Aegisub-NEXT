namespace AegiNext.SubtitleFormats;

/// <summary>ASS 来源范围到显示文本 UTF-16 范围的对应关系；标签的显示长度为零。</summary>
public sealed record AssSourceMapEntry(int SourceStart, int SourceLength, int Utf16Start, int Utf16Length);
