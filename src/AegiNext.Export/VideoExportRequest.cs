using AegiNext.Core.Projects;
using AegiNext.Media.Decoding;

namespace AegiNext.Export;

/// <summary>当前工程的独立压制快照；资源目录必须显式指定，最终输出不会覆盖现有文件。</summary>
public sealed record VideoExportRequest(ProjectDocument Project, string ProjectDirectory, string OutputPath)
{
    public VideoCodec Codec { get; init; } = VideoCodec.Auto;
    public VideoEncodingMode EncodingMode { get; init; } = VideoEncodingMode.SOFTWARE;
    public VideoRateControlMode RateControlMode { get; init; } = VideoRateControlMode.AUTOMATIC;
    public VideoDecodeMode DecodeMode { get; init; } = VideoDecodeMode.Auto;
    public int VideoBitrate { get; init; } = 8000000;
    public string Preset { get; init; } = "medium";
    public int Quality { get; init; } = 20;
    public AudioExportMode AudioMode { get; init; } = AudioExportMode.Copy;
    public int AudioBitrate { get; init; } = 192000;
    public string? FfmpegPath { get; init; }
    public string? WorkerPath { get; init; }

    /// <summary>提取独立于工程和运行路径的压制配置，保留兼容请求的自动码控意图。</summary>
    public VideoExportSettings ToSettings()
    {
        return new()
        {
            Codec = Codec,
            EncodingMode = EncodingMode,
            RateControlMode = RateControlMode,
            Preset = Preset,
            Quality = Quality,
            VideoBitrate = VideoBitrate,
            AudioMode = AudioMode,
            AudioBitrate = AudioBitrate
        };
    }

    /// <summary>使用指定压制配置创建新请求，保留工程快照、解码模式和全部运行路径。</summary>
    public VideoExportRequest WithSettings(VideoExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return this with
        {
            Codec = settings.Codec,
            EncodingMode = settings.EncodingMode,
            RateControlMode = settings.RateControlMode,
            Preset = settings.Preset,
            Quality = settings.Quality,
            VideoBitrate = settings.VideoBitrate,
            AudioMode = settings.AudioMode,
            AudioBitrate = settings.AudioBitrate
        };
    }
}
