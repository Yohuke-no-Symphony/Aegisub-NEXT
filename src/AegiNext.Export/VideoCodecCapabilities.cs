namespace AegiNext.Export;

/// <summary>编码器的质量语义、容器与已实现后端，不表示当前机器具有对应硬件。</summary>
public sealed record VideoCodecCapabilities(VideoCodec Codec, int QualityMinimum, int QualityMaximum,
    int DefaultQuality, VideoRateControlMode QualityMode, string SoftwareEncoder,
    IReadOnlyList<string> HardwareEncoders, IReadOnlyList<string> Extensions)
{
    /// <summary>是否实现了该编码格式的硬件路径。</summary>
    public bool SupportsHardware => HardwareEncoders.Count > 0;
}
