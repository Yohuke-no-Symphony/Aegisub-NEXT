using AegiNext.Core.Projects;

namespace AegiNext.Export;

/// <summary>仅封装的独立工程快照；复制源音视频并保留原字幕，追加工程生成的纯文本字幕轨。</summary>
public sealed record SubtitleMuxRequest(ProjectDocument Project, string ProjectDirectory, string OutputPath)
{
    public string? FfmpegPath { get; init; }
    public string? WorkerPath { get; init; }
}
