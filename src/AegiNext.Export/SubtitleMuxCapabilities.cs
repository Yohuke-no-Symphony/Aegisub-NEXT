namespace AegiNext.Export;

/// <summary>仅封装导出的容器与文本字幕编码合同。</summary>
public static class SubtitleMuxCapabilities
{
    public static IReadOnlyList<string> Extensions { get; } = Array.AsReadOnly(new[] { ".mkv", ".mp4" });

    /// <summary>取得目标容器的字幕编码；不支持的容器立即拒绝。</summary>
    public static string GetSubtitleCodec(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        return extension.ToLowerInvariant() switch
        {
            ".mkv" => "subrip",
            ".mp4" => "mov_text",
            _ => throw new ArgumentException("内挂文本字幕仅支持 MKV 或 MP4 容器。", nameof(extension))
        };
    }
}
