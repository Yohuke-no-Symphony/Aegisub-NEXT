using System.Runtime.InteropServices;

namespace AegiNext.Export;

/// <summary>导出参数、界面与完成回执共用的编码能力合同。</summary>
public static class VideoExportCapabilities
{
    private static readonly IReadOnlyList<string> legacyContainers = Array.AsReadOnly(new[] { ".mkv", ".mp4" });
    private static readonly IReadOnlyList<string> modernContainers = Array.AsReadOnly(new[] { ".mkv", ".webm", ".mp4" });
    private static readonly IReadOnlyList<OSPlatform> macAndWindows = Array.AsReadOnly(new[] { OSPlatform.OSX, OSPlatform.Windows });
    private static readonly IReadOnlyList<OSPlatform> windows = Array.AsReadOnly(new[] { OSPlatform.Windows });
    private static readonly IReadOnlyList<VideoCodecCapabilities> codecs = Array.AsReadOnly(new[]
    {
        new VideoCodecCapabilities(VideoCodec.Auto, 0, 51, 20, VideoRateControlMode.CRF, string.Empty,
            Array.AsReadOnly(new[] { "h264_videotoolbox", "hevc_videotoolbox", "h264_nvenc", "hevc_nvenc", "h264_qsv", "hevc_qsv", "h264_amf", "hevc_amf" }), legacyContainers)
        {
            HardwarePlatforms = macAndWindows
        },
        new VideoCodecCapabilities(VideoCodec.H264, 0, 51, 20, VideoRateControlMode.CRF, "libx264",
            Array.AsReadOnly(new[] { "h264_videotoolbox", "h264_nvenc", "h264_qsv", "h264_amf" }), legacyContainers)
        {
            HardwarePlatforms = macAndWindows
        },
        new VideoCodecCapabilities(VideoCodec.Hevc, 0, 51, 20, VideoRateControlMode.CRF, "libx265",
            Array.AsReadOnly(new[] { "hevc_videotoolbox", "hevc_nvenc", "hevc_qsv", "hevc_amf" }), legacyContainers)
        {
            HardwarePlatforms = macAndWindows
        },
        new VideoCodecCapabilities(VideoCodec.Vp8, 0, 63, 32, VideoRateControlMode.CQ, "libvpx",
            Array.Empty<string>(), Array.AsReadOnly(new[] { ".mkv", ".webm" })),
        new VideoCodecCapabilities(VideoCodec.Vp9, 0, 63, 32, VideoRateControlMode.CRF, "libvpx-vp9",
            Array.AsReadOnly(new[] { "vp9_qsv" }), modernContainers)
        {
            HardwarePlatforms = windows
        },
        new VideoCodecCapabilities(VideoCodec.Av1, 1, 63, 32, VideoRateControlMode.CRF, "libsvtav1",
            Array.AsReadOnly(new[] { "av1_nvenc", "av1_qsv", "av1_amf" }), modernContainers)
        {
            HardwarePlatforms = windows
        }
    });

    /// <summary>稳定的全部编码格式能力。</summary>
    public static IReadOnlyList<VideoCodecCapabilities> All => codecs;

    /// <summary>取得指定编码格式的合同；未知枚举值立即拒绝。</summary>
    public static VideoCodecCapabilities Get(VideoCodec codec) => codecs.SingleOrDefault(item => item.Codec == codec)
        ?? throw new ArgumentOutOfRangeException(nameof(codec));

    /// <summary>取得编码器的默认输出容器，保持既有 MP4 与 MKV 默认选择。</summary>
    public static VideoContainerFormat GetDefaultContainer(VideoCodec codec)
    {
        var capability = Get(codec);
        return capability.Codec is VideoCodec.Auto or VideoCodec.H264 or VideoCodec.Hevc
            ? VideoContainerFormat.MP4 : VideoContainerFormat.MKV;
    }

    /// <summary>取得具体容器的小写文件扩展名；自动格式和未知枚举立即拒绝。</summary>
    public static string GetExtension(VideoContainerFormat container)
    {
        return container switch
        {
            VideoContainerFormat.MP4 => ".mp4",
            VideoContainerFormat.MKV => ".mkv",
            VideoContainerFormat.WEBM => ".webm",
            _ => throw new ArgumentOutOfRangeException(nameof(container))
        };
    }

    /// <summary>从已支持的文件扩展名解析具体容器，忽略大小写。</summary>
    public static VideoContainerFormat GetContainer(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        return extension.ToLowerInvariant() switch
        {
            ".mp4" => VideoContainerFormat.MP4,
            ".mkv" => VideoContainerFormat.MKV,
            ".webm" => VideoContainerFormat.WEBM,
            _ => throw new ArgumentException("The output container must be MP4, MKV, or WebM.", nameof(extension))
        };
    }

    /// <summary>取得编码器已支持的具体容器，顺序与共享扩展名合同一致。</summary>
    public static IReadOnlyList<VideoContainerFormat> GetContainers(VideoCodec codec)
    {
        return Array.AsReadOnly(Get(codec).Extensions.Select(GetContainer).ToArray());
    }

    /// <summary>解析未指定格式的兼容参数，不修改原设置；未知格式立即拒绝。</summary>
    public static VideoContainerFormat ResolveContainer(VideoExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.Container))
        {
            throw new ArgumentException("Unsupported Container.", nameof(settings));
        }

        return settings.Container == VideoContainerFormat.AUTO ? GetDefaultContainer(settings.Codec) : settings.Container;
    }

    /// <summary>将有效预设投影到当前平台，解析默认容器并取消无后端的硬件模式，保留明确码控和全部参数。</summary>
    public static VideoExportSettings ProjectPresetToCurrentPlatform(VideoExportSettings settings)
    {
        VideoExportSettingsValidator.ValidatePreset(settings);
        var container = ResolveContainer(settings);
        var encodingMode = settings.EncodingMode == VideoEncodingMode.HARDWARE && !Get(settings.Codec).SupportsHardwareOnCurrentPlatform
            ? VideoEncodingMode.SOFTWARE : settings.EncodingMode;
        if (container != settings.Container || encodingMode != settings.EncodingMode)
        {
            return settings with { Container = container, EncodingMode = encodingMode };
        }

        return settings;
    }

    /// <summary>创建适用于该编码器的软件默认设置。</summary>
    public static VideoExportSettings CreateDefaultSettings(VideoCodec codec)
    {
        var capability = Get(codec);
        return new()
        {
            Codec = codec,
            Container = GetDefaultContainer(codec),
            Quality = capability.DefaultQuality,
            RateControlMode = capability.QualityMode
        };
    }
}
