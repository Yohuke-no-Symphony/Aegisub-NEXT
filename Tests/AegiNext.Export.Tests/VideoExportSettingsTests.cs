using AegiNext.Export;

namespace AegiNext.Export.Tests;

/// <summary>验证可复用压制参数的模式组合、边界与预设保存约束。</summary>
public sealed class VideoExportSettingsTests
{
    /// <summary>默认参数保持日常软件 CRF 压制与既有音频设置。</summary>
    [Fact]
    public void DefaultsPreserveSoftwareCrfAndExistingEncodingParameters()
    {
        var settings = new VideoExportSettings();

        Assert.Equal(VideoCodec.Auto, settings.Codec);
        Assert.Equal(VideoEncodingMode.SOFTWARE, settings.EncodingMode);
        Assert.Equal(VideoRateControlMode.CRF, settings.RateControlMode);
        Assert.Equal("medium", settings.Preset);
        Assert.Equal(20, settings.Crf);
        Assert.Equal(8000000, settings.VideoBitrate);
        Assert.Equal(AudioExportMode.Copy, settings.AudioMode);
        Assert.Equal(192000, settings.AudioBitrate);
        VideoExportSettingsValidator.Validate(settings);
        VideoExportSettingsValidator.ValidatePreset(settings);
    }

    /// <summary>修改副本不会改变已保存的不可变参数。</summary>
    [Fact]
    public void SettingsCanBeCopiedWithoutMutatingTheOriginal()
    {
        var original = new VideoExportSettings();
        var copy = original with { RateControlMode = VideoRateControlMode.CBR, VideoBitrate = 12000000 };

        Assert.Equal(VideoRateControlMode.CRF, original.RateControlMode);
        Assert.Equal(8000000, original.VideoBitrate);
        Assert.Equal(VideoRateControlMode.CBR, copy.RateControlMode);
        Assert.Equal(12000000, copy.VideoBitrate);
        Assert.Equal(original, new VideoExportSettings());
        Assert.NotEqual(original, copy);
    }

    /// <summary>所有明确受支持的设备与码控组合均可用于运行和预设。</summary>
    [Theory]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CRF)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CBR)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.CBR)]
    public void ExplicitSupportedCombinationsAreAccepted(VideoEncodingMode encodingMode,
        VideoRateControlMode rateControlMode)
    {
        var settings = new VideoExportSettings { EncodingMode = encodingMode, RateControlMode = rateControlMode };

        VideoExportSettingsValidator.Validate(settings);
        VideoExportSettingsValidator.ValidatePreset(settings);
        Assert.Equal(rateControlMode, VideoExportSettingsValidator.ResolveRateControlMode(settings));
    }

    /// <summary>自动请求按现有设备语义解析，但预设必须保存明确的模式。</summary>
    [Theory]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CRF)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.VBR)]
    public void AutomaticModePreservesLegacyRequestsAndCannotBeSaved(VideoEncodingMode encodingMode,
        VideoRateControlMode expected)
    {
        var settings = new VideoExportSettings
            { EncodingMode = encodingMode, RateControlMode = VideoRateControlMode.AUTOMATIC };

        Assert.Equal(expected, VideoExportSettingsValidator.ResolveRateControlMode(settings));
        VideoExportSettingsValidator.Validate(settings);
        var error = Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
        Assert.Contains(nameof(VideoExportSettings.RateControlMode), error.Message, StringComparison.Ordinal);
        Assert.Equal(VideoRateControlMode.AUTOMATIC, settings.RateControlMode);
    }

    /// <summary>硬件 CRF 不会被静默替换为目标码率模式。</summary>
    [Fact]
    public void HardwareCrfIsRejectedForRequestsAndPresets()
    {
        var settings = new VideoExportSettings { EncodingMode = VideoEncodingMode.HARDWARE };

        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
    }

    /// <summary>CRF 的两个有效边界均可运行并保存。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void CrfBoundsAreInclusive(int crf)
    {
        var settings = new VideoExportSettings { Crf = crf };

        VideoExportSettingsValidator.Validate(settings);
        VideoExportSettingsValidator.ValidatePreset(settings);
    }

    /// <summary>CRF 活动值越界时在运行前被拒绝。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(52)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void InvalidActiveCrfIsRejected(int crf)
    {
        var settings = new VideoExportSettings { Crf = crf };

        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
    }

    /// <summary>CPU 和 GPU 的 VBR、CBR 均使用相同的码率单位与边界。</summary>
    [Theory]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.VBR, 100000)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.VBR, 200000000)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CBR, 100000)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CBR, 200000000)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.VBR, 100000)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.VBR, 200000000)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.CBR, 100000)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.CBR, 200000000)]
    public void VideoBitrateBoundsAreInclusive(VideoEncodingMode encodingMode, VideoRateControlMode rateControlMode,
        int videoBitrate)
    {
        var settings = new VideoExportSettings
        {
            EncodingMode = encodingMode,
            RateControlMode = rateControlMode,
            VideoBitrate = videoBitrate
        };

        VideoExportSettingsValidator.Validate(settings);
        VideoExportSettingsValidator.ValidatePreset(settings);
    }

    /// <summary>活动目标码率不能超出现有压制范围。</summary>
    [Theory]
    [InlineData(VideoRateControlMode.VBR, 99999)]
    [InlineData(VideoRateControlMode.VBR, 200000001)]
    [InlineData(VideoRateControlMode.CBR, 99999)]
    [InlineData(VideoRateControlMode.CBR, 200000001)]
    public void InvalidActiveVideoBitrateIsRejected(VideoRateControlMode rateControlMode, int videoBitrate)
    {
        var settings = new VideoExportSettings { RateControlMode = rateControlMode, VideoBitrate = videoBitrate };

        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
    }

    /// <summary>AAC 码率接受两个已有范围边界。</summary>
    [Theory]
    [InlineData(32000)]
    [InlineData(512000)]
    public void AacBitrateBoundsAreInclusive(int audioBitrate)
    {
        var settings = new VideoExportSettings { AudioMode = AudioExportMode.Aac, AudioBitrate = audioBitrate };

        VideoExportSettingsValidator.Validate(settings);
        VideoExportSettingsValidator.ValidatePreset(settings);
    }

    /// <summary>AAC 活动码率越界时在运行前被拒绝。</summary>
    [Theory]
    [InlineData(31999)]
    [InlineData(512001)]
    public void InvalidActiveAacBitrateIsRejected(int audioBitrate)
    {
        var settings = new VideoExportSettings { AudioMode = AudioExportMode.Aac, AudioBitrate = audioBitrate };

        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
    }

    /// <summary>非活动数值原样保留，运行只校验当前选中的参数。</summary>
    [Fact]
    public void InactiveNumericFieldsRemainStoredWithoutBlockingRuntimeValidation()
    {
        var quality = new VideoExportSettings { VideoBitrate = -1, AudioBitrate = -1 };
        var bitrate = new VideoExportSettings
        {
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = VideoRateControlMode.VBR,
            Crf = -1,
            AudioMode = AudioExportMode.None,
            AudioBitrate = -1
        };

        VideoExportSettingsValidator.Validate(quality);
        VideoExportSettingsValidator.Validate(bitrate);
        Assert.Equal(-1, quality.VideoBitrate);
        Assert.Equal(-1, quality.AudioBitrate);
        Assert.Equal(-1, bitrate.Crf);
        Assert.Equal(-1, bitrate.AudioBitrate);
    }

    /// <summary>预设连未活动的质量、视频码率和音频码率也必须可用。</summary>
    [Theory]
    [InlineData(nameof(VideoExportSettings.Crf), -1)]
    [InlineData(nameof(VideoExportSettings.Crf), 52)]
    [InlineData(nameof(VideoExportSettings.VideoBitrate), 99999)]
    [InlineData(nameof(VideoExportSettings.VideoBitrate), 200000001)]
    [InlineData(nameof(VideoExportSettings.AudioBitrate), 31999)]
    [InlineData(nameof(VideoExportSettings.AudioBitrate), 512001)]
    public void PresetsRequireEveryStoredNumericFieldToBeValid(string field, int value)
    {
        var settings = field switch
        {
            nameof(VideoExportSettings.Crf) => new VideoExportSettings
                { RateControlMode = VideoRateControlMode.VBR, Crf = value },
            nameof(VideoExportSettings.VideoBitrate) => new VideoExportSettings { VideoBitrate = value },
            _ => new VideoExportSettings { AudioBitrate = value }
        };

        VideoExportSettingsValidator.Validate(settings);
        var error = Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
        Assert.Contains(field, error.Message, StringComparison.Ordinal);
    }

    /// <summary>自动模式仍按解析后的活动字段校验兼容请求。</summary>
    [Fact]
    public void AutomaticModeValidatesTheResolvedActiveField()
    {
        var software = new VideoExportSettings
        {
            RateControlMode = VideoRateControlMode.AUTOMATIC,
            Crf = -1,
            VideoBitrate = 8000000
        };
        var hardware = new VideoExportSettings
        {
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = VideoRateControlMode.AUTOMATIC,
            Crf = 20,
            VideoBitrate = -1
        };

        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(software));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(hardware));
        VideoExportSettingsValidator.Validate(software with { Crf = 20, VideoBitrate = -1 });
        VideoExportSettingsValidator.Validate(hardware with { Crf = -1, VideoBitrate = 8000000 });
    }

    /// <summary>已有九种编码速度均可用于可复用参数。</summary>
    [Theory]
    [InlineData("ultrafast")]
    [InlineData("superfast")]
    [InlineData("veryfast")]
    [InlineData("faster")]
    [InlineData("fast")]
    [InlineData("medium")]
    [InlineData("slow")]
    [InlineData("slower")]
    [InlineData("veryslow")]
    public void ExistingEncodingSpeedPresetsAreAccepted(string preset)
    {
        var settings = new VideoExportSettings { Preset = preset };

        VideoExportSettingsValidator.Validate(settings);
        VideoExportSettingsValidator.ValidatePreset(settings);
    }

    /// <summary>未知或空编码速度不进入运行与预设文件。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Medium")]
    [InlineData("custom")]
    public void UnknownEncodingSpeedPresetsAreRejected(string? preset)
    {
        var settings = new VideoExportSettings { Preset = preset! };

        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
    }

    /// <summary>所有配置枚举都拒绝未定义值。</summary>
    [Fact]
    public void UnknownEnumValuesAreRejected()
    {
        VideoExportSettings[] invalidSettings =
        [
            new() { Codec = (VideoCodec)99 },
            new() { EncodingMode = (VideoEncodingMode)99 },
            new() { RateControlMode = (VideoRateControlMode)99 },
            new() { AudioMode = (AudioExportMode)99 }
        ];

        foreach (var settings in invalidSettings)
        {
            Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings));
            Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
        }

        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ResolveRateControlMode(invalidSettings[1]));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ResolveRateControlMode(invalidSettings[2]));
    }

    /// <summary>所有公共校验入口明确拒绝空参数。</summary>
    [Fact]
    public void NullSettingsAreRejectedAtEveryPublicEntryPoint()
    {
        Assert.Throws<ArgumentNullException>(() => VideoExportSettingsValidator.Validate(null!));
        Assert.Throws<ArgumentNullException>(() => VideoExportSettingsValidator.ValidatePreset(null!));
        Assert.Throws<ArgumentNullException>(() => VideoExportSettingsValidator.ResolveRateControlMode(null!));
        Assert.Throws<ArgumentNullException>(() => VideoExportSettingsValidator.Normalize(null!));
    }

    /// <summary>执行设置使用共同的 1 kbps 精度，规范化幂等且不改变用户保存的原始值。</summary>
    [Theory]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.SOFTWARE, VideoRateControlMode.CBR)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.VBR)]
    [InlineData(VideoEncodingMode.HARDWARE, VideoRateControlMode.CBR)]
    public void NormalizeUsesWholeKilobitsWithoutChangingPortableSettings(VideoEncodingMode encodingMode,
        VideoRateControlMode rateControlMode)
    {
        var original = new VideoExportSettings { EncodingMode = encodingMode, RateControlMode = rateControlMode, VideoBitrate = 8000999 };

        var normalized = VideoExportSettingsValidator.Normalize(original);

        Assert.Equal(8000000, normalized.VideoBitrate);
        Assert.Equal(8000999, original.VideoBitrate);
        Assert.Equal(original.Crf, normalized.Crf);
        Assert.Equal(original.AudioBitrate, normalized.AudioBitrate);
        Assert.Equal(normalized, VideoExportSettingsValidator.Normalize(normalized));
    }

    /// <summary>规范化只处理有效码率，CRF 及隐藏字段的原始值保留；自动模式变为明确模式。</summary>
    [Fact]
    public void NormalizeResolvesAutomaticModeAndKeepsInactiveValues()
    {
        var quality = new VideoExportSettings { RateControlMode = VideoRateControlMode.AUTOMATIC, VideoBitrate = -1, AudioBitrate = -1 };
        var bitrate = new VideoExportSettings
        {
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = VideoRateControlMode.AUTOMATIC,
            Crf = -1,
            VideoBitrate = 8000999,
            AudioBitrate = -1
        };

        var normalizedQuality = VideoExportSettingsValidator.Normalize(quality);
        var normalizedBitrate = VideoExportSettingsValidator.Normalize(bitrate);

        Assert.Equal(VideoRateControlMode.CRF, normalizedQuality.RateControlMode);
        Assert.Equal(-1, normalizedQuality.VideoBitrate);
        Assert.Equal(-1, normalizedQuality.AudioBitrate);
        Assert.Equal(VideoRateControlMode.VBR, normalizedBitrate.RateControlMode);
        Assert.Equal(8000000, normalizedBitrate.VideoBitrate);
        Assert.Equal(-1, normalizedBitrate.Crf);
        Assert.Equal(-1, normalizedBitrate.AudioBitrate);
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Normalize(quality with { Crf = -1 }));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Normalize(bitrate with { VideoBitrate = -1 }));
    }
}
