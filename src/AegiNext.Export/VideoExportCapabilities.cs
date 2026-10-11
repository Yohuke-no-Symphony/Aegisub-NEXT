namespace AegiNext.Export;

/// <summary>导出参数、界面与完成回执共用的编码能力合同。</summary>
public static class VideoExportCapabilities
{
    private static readonly IReadOnlyList<string> legacyContainers = Array.AsReadOnly(new[] { ".mkv", ".mp4" });
    private static readonly IReadOnlyList<string> modernContainers = Array.AsReadOnly(new[] { ".mkv", ".webm", ".mp4" });
    private static readonly IReadOnlyList<VideoCodecCapabilities> codecs = Array.AsReadOnly(new[]
    {
        new VideoCodecCapabilities(VideoCodec.Auto, 0, 51, 20, VideoRateControlMode.CRF, string.Empty,
            Array.AsReadOnly(new[] { "h264_videotoolbox", "hevc_videotoolbox", "h264_nvenc", "hevc_nvenc", "h264_qsv", "hevc_qsv", "h264_amf", "hevc_amf" }), legacyContainers),
        new VideoCodecCapabilities(VideoCodec.H264, 0, 51, 20, VideoRateControlMode.CRF, "libx264",
            Array.AsReadOnly(new[] { "h264_videotoolbox", "h264_nvenc", "h264_qsv", "h264_amf" }), legacyContainers),
        new VideoCodecCapabilities(VideoCodec.Hevc, 0, 51, 20, VideoRateControlMode.CRF, "libx265",
            Array.AsReadOnly(new[] { "hevc_videotoolbox", "hevc_nvenc", "hevc_qsv", "hevc_amf" }), legacyContainers),
        new VideoCodecCapabilities(VideoCodec.Vp8, 0, 63, 32, VideoRateControlMode.CQ, "libvpx",
            Array.Empty<string>(), Array.AsReadOnly(new[] { ".mkv", ".webm" })),
        new VideoCodecCapabilities(VideoCodec.Vp9, 0, 63, 32, VideoRateControlMode.CRF, "libvpx-vp9",
            Array.AsReadOnly(new[] { "vp9_qsv" }), modernContainers),
        new VideoCodecCapabilities(VideoCodec.Av1, 1, 63, 32, VideoRateControlMode.CRF, "libsvtav1",
            Array.AsReadOnly(new[] { "av1_nvenc", "av1_qsv", "av1_amf" }), modernContainers)
    });

    /// <summary>稳定的全部编码格式能力。</summary>
    public static IReadOnlyList<VideoCodecCapabilities> All => codecs;

    /// <summary>取得指定编码格式的合同；未知枚举值立即拒绝。</summary>
    public static VideoCodecCapabilities Get(VideoCodec codec) => codecs.SingleOrDefault(item => item.Codec == codec)
        ?? throw new ArgumentOutOfRangeException(nameof(codec));

    /// <summary>创建适用于该编码器的软件默认设置。</summary>
    public static VideoExportSettings CreateDefaultSettings(VideoCodec codec)
    {
        var capability = Get(codec);
        return new() { Codec = codec, Quality = capability.DefaultQuality, RateControlMode = capability.QualityMode };
    }
}
