namespace AegiNext.Export;

/// <summary>校验运行时压制参数与可复用预设，并解析兼容请求的码率控制方式。</summary>
public static class VideoExportSettingsValidator
{
    /// <summary>校验当前模式使用的参数；保留的非活动质量和码率数值不阻塞运行。</summary>
    public static void Validate(VideoExportSettings settings)
    {
        var rateControlMode = ValidateCommon(settings);
        if (rateControlMode is VideoRateControlMode.CRF or VideoRateControlMode.CQ)
        {
            ValidateQuality(settings);
        }

        if (rateControlMode != VideoRateControlMode.CRF)
        {
            ValidateVideoBitrate(settings);
        }

        if (settings.AudioMode is AudioExportMode.Aac or AudioExportMode.Opus)
        {
            ValidateAudioBitrate(settings);
        }
    }

    /// <summary>校验预设的明确模式与全部保存数值，确保切换模式时仍可使用保存的参数。</summary>
    public static void ValidatePreset(VideoExportSettings settings)
    {
        ValidateCommon(settings);
        if (settings.RateControlMode == VideoRateControlMode.AUTOMATIC)
        {
            throw new ArgumentException("Video export presets must store an explicit RateControlMode; AUTOMATIC is not allowed.", nameof(settings));
        }

        ValidateQuality(settings);
        ValidateVideoBitrate(settings);
        ValidateAudioBitrate(settings);
    }

    /// <summary>验证活动参数并解析显式码控方式；码率按所有编码器共同支持的 1 kbps 精度向下规范化，保留非活动参数。</summary>
    public static VideoExportSettings Normalize(VideoExportSettings settings)
    {
        Validate(settings);
        var mode = ResolveRateControlMode(settings);
        return settings with
        {
            RateControlMode = mode,
            VideoBitrate = mode == VideoRateControlMode.CRF ? settings.VideoBitrate : settings.VideoBitrate / 1000 * 1000
        };
    }

    /// <summary>解析有效码控方式；自动模式保持软件 CRF、硬件 VBR 的已有请求语义。</summary>
    public static VideoRateControlMode ResolveRateControlMode(VideoExportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Enum.IsDefined(settings.EncodingMode) || !Enum.IsDefined(settings.RateControlMode))
        {
            throw new ArgumentException("Unsupported EncodingMode or RateControlMode.", nameof(settings));
        }

        if (settings.RateControlMode != VideoRateControlMode.AUTOMATIC)
        {
            return settings.RateControlMode;
        }

        return settings.EncodingMode == VideoEncodingMode.SOFTWARE
            ? VideoExportCapabilities.Get(settings.Codec).QualityMode
            : VideoRateControlMode.VBR;
    }

    private static VideoRateControlMode ValidateCommon(VideoExportSettings settings)
    {
        var rateControlMode = ResolveRateControlMode(settings);
        if (!Enum.IsDefined(settings.Codec) || !Enum.IsDefined(settings.AudioMode))
        {
            throw new ArgumentException("Unsupported Codec or AudioMode.", nameof(settings));
        }

        if (settings.Preset is not ("ultrafast" or "superfast" or "veryfast" or "faster" or "fast" or
            "medium" or "slow" or "slower" or "veryslow"))
        {
            throw new ArgumentException("Unsupported encoding speed Preset.", nameof(settings));
        }

        var capability = VideoExportCapabilities.Get(settings.Codec);
        if (settings.EncodingMode == VideoEncodingMode.HARDWARE &&
            (!capability.SupportsHardware || rateControlMode is VideoRateControlMode.CRF or VideoRateControlMode.CQ))
        {
            throw new ArgumentException("This codec or quality mode does not support hardware encoding; select VBR or CBR with an available backend.", nameof(settings));
        }

        if (rateControlMode is VideoRateControlMode.CRF or VideoRateControlMode.CQ && rateControlMode != capability.QualityMode)
        {
            throw new ArgumentException("The quality mode does not match the codec.", nameof(settings));
        }

        return rateControlMode;
    }

    private static void ValidateQuality(VideoExportSettings settings)
    {
        var capability = VideoExportCapabilities.Get(settings.Codec);
        if (settings.Quality < capability.QualityMinimum || settings.Quality > capability.QualityMaximum)
        {
            throw new ArgumentException($"Quality must be between {capability.QualityMinimum} and {capability.QualityMaximum}.", nameof(settings));
        }
    }

    private static void ValidateVideoBitrate(VideoExportSettings settings)
    {
        if (settings.VideoBitrate is < 100000 or > 200000000)
        {
            throw new ArgumentException("VideoBitrate must be between 100000 and 200000000 bits per second.", nameof(settings));
        }
    }

    private static void ValidateAudioBitrate(VideoExportSettings settings)
    {
        if (settings.AudioBitrate is < 32000 or > 512000)
        {
            throw new ArgumentException("AudioBitrate must be between 32000 and 512000 bits per second.", nameof(settings));
        }
    }
}
