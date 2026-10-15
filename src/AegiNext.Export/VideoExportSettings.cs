namespace AegiNext.Export;

/// <summary>可复用的不可变压制参数，不包含工程、资源路径或运行时后端信息。</summary>
public sealed record VideoExportSettings
{
    /// <summary>输出视频编码格式。</summary>
    public VideoCodec Codec { get; init; } = VideoCodec.Auto;

    /// <summary>输出容器；未指定时沿用编码器的默认格式。</summary>
    public VideoContainerFormat Container { get; init; } = VideoContainerFormat.AUTO;

    /// <summary>软件或必须成功启用的硬件视频编码。</summary>
    public VideoEncodingMode EncodingMode { get; init; } = VideoEncodingMode.SOFTWARE;

    /// <summary>视频质量或码率控制方式。</summary>
    public VideoRateControlMode RateControlMode { get; init; } = VideoRateControlMode.CRF;

    /// <summary>视频编码速度档位。</summary>
    public string Preset { get; init; } = "medium";

    /// <summary>CRF 或 CQ 模式的质量值，取值范围由编码器能力合同规定。</summary>
    public int Quality { get; init; } = 20;

    /// <summary>CQ、VBR 或 CBR 模式的视频目标码率，单位为比特每秒。</summary>
    public int VideoBitrate { get; init; } = 8000000;

    /// <summary>输出音频的处理方式。</summary>
    public AudioExportMode AudioMode { get; init; } = AudioExportMode.Copy;

    /// <summary>AAC 或 Opus 编码的音频目标码率，单位为比特每秒。</summary>
    public int AudioBitrate { get; init; } = 192000;
}
