using AegiNext.Core.Projects;

namespace AegiNext.Export;

/// <summary>导出冻结工程绑定的完整音轨，不依赖工程字幕或视频设置。</summary>
public sealed record AudioExportRequest(ProjectDocument Project, string ProjectDirectory, string OutputPath)
{
    public AudioFileFormat Format { get; init; } = AudioFileFormat.Copy;
    public int Bitrate { get; init; } = 192000;
    public string? FfmpegPath { get; init; }
    public string? WorkerPath { get; init; }
}
