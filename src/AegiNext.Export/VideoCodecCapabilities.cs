using System.Runtime.InteropServices;

namespace AegiNext.Export;

/// <summary>编码器的质量语义、容器与已实现后端，不表示当前机器具有对应硬件。</summary>
public sealed record VideoCodecCapabilities(VideoCodec Codec, int QualityMinimum, int QualityMaximum,
    int DefaultQuality, VideoRateControlMode QualityMode, string SoftwareEncoder,
    IReadOnlyList<string> HardwareEncoders, IReadOnlyList<string> Extensions)
{
    /// <summary>已实现硬件编码后端的平台，不表示具体 GPU 或驱动已经可用。</summary>
    public IReadOnlyList<OSPlatform> HardwarePlatforms { get; init; } = [];

    /// <summary>是否实现了该编码格式的硬件路径。</summary>
    public bool SupportsHardware => HardwareEncoders.Count > 0;

    /// <summary>当前平台是否具有已实现的硬件编码路径，实际设备仍需由原生后端初始化确认。</summary>
    public bool SupportsHardwareOnCurrentPlatform => SupportsHardware && HardwarePlatforms.Any(RuntimeInformation.IsOSPlatform);

    /// <summary>指定平台是否具有已实现的硬件编码路径。</summary>
    public bool SupportsHardwareOn(OSPlatform platform)
    {
        return SupportsHardware && HardwarePlatforms.Contains(platform);
    }
}
