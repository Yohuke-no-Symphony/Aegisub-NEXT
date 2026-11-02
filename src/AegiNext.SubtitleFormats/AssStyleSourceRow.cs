namespace AegiNext.SubtitleFormats;

internal sealed record AssStyleSourceRow(Dictionary<string, string> Fields, int SourceStart, int SourceLength,
    int LineNumber);
