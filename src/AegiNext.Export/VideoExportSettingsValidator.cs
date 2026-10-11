namespace AegiNext.Export;

/// <summary>校验运行时压制参数与可复用预设，并解析兼容请求的码率控制方式。</summary>
public static class VideoExportSettingsValidator
{
    /// <summary>校验当前模式使用的参数；保留的非活动质量和码率数值不阻塞运行。</summary>
    public static void Validate(VideoExportSettings settings)
    {
        var rateControlMode = ValidateCommon(settings);
        if (rateControlMode == VideoRateControlMode.CRF)
        {
            ValidateCrf(settings);
        }
        else
        {
            ValidateVideoBitrate(settings);
        }

        if (settings.AudioMode == AudioExportMode.Aac)
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
            throw new ArgumentException("压制预设必须保存明确的 RateControlMode，不能使用 AUTOMATIC。", nameof(settings));
        }

        ValidateCrf(settings);
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
            throw new ArgumentException("不支持的 EncodingMode 或 RateControlMode。", nameof(settings));
        }

        if (settings.RateControlMode != VideoRateControlMode.AUTOMATIC)
        {
            return settings.RateControlMode;
        }

        return settings.EncodingMode == VideoEncodingMode.SOFTWARE
            ? VideoRateControlMode.CRF
            : VideoRateControlMode.VBR;
    }

    private static VideoRateControlMode ValidateCommon(VideoExportSettings settings)
    {
        var rateControlMode = ResolveRateControlMode(settings);
        if (!Enum.IsDefined(settings.Codec) || !Enum.IsDefined(settings.AudioMode))
        {
            throw new ArgumentException("不支持的 Codec 或 AudioMode。", nameof(settings));
        }

        if (settings.Preset is not ("ultrafast" or "superfast" or "veryfast" or "faster" or "fast" or
            "medium" or "slow" or "slower" or "veryslow"))
        {
            throw new ArgumentException("不支持的编码速度 Preset。", nameof(settings));
        }

        if (settings.EncodingMode == VideoEncodingMode.HARDWARE && rateControlMode == VideoRateControlMode.CRF)
        {
            throw new ArgumentException("硬件编码不支持 CRF；请选择 VBR 或 CBR。", nameof(settings));
        }

        return rateControlMode;
    }

    private static void ValidateCrf(VideoExportSettings settings)
    {
        if (settings.Crf is < 0 or > 51)
        {
            throw new ArgumentException("Crf 必须为 0 至 51。", nameof(settings));
        }
    }

    private static void ValidateVideoBitrate(VideoExportSettings settings)
    {
        if (settings.VideoBitrate is < 100000 or > 200000000)
        {
            throw new ArgumentException("VideoBitrate 必须为 100000 至 200000000 比特每秒。", nameof(settings));
        }
    }

    private static void ValidateAudioBitrate(VideoExportSettings settings)
    {
        if (settings.AudioBitrate is < 32000 or > 512000)
        {
            throw new ArgumentException("AudioBitrate 必须为 32000 至 512000 比特每秒。", nameof(settings));
        }
    }
}
