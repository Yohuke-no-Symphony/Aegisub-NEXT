using AegiNext.Desktop.Editing;
using AegiNext.Export;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class VideoExportCodecSettingsTests
{
    /// <summary>编码器切换采用共用能力的默认质量、范围与显式模式。</summary>
    [Theory]
    [InlineData(VideoCodec.H264, 20, 0, 51, VideoRateControlMode.CRF)]
    [InlineData(VideoCodec.Hevc, 20, 0, 51, VideoRateControlMode.CRF)]
    [InlineData(VideoCodec.Vp8, 32, 0, 63, VideoRateControlMode.CQ)]
    [InlineData(VideoCodec.Vp9, 32, 0, 63, VideoRateControlMode.CRF)]
    [InlineData(VideoCodec.Av1, 32, 1, 63, VideoRateControlMode.CRF)]
    public void CodecDefaultsFollowCapabilities(VideoCodec codec, int quality, int minimum, int maximum,
        VideoRateControlMode mode)
    {
        var model = new VideoExportSettingsViewModel { SelectedCodec = codec };

        var settings = model.CaptureSettings(true);

        Assert.Equal(codec, settings.Codec);
        Assert.Equal(quality, settings.Quality);
        Assert.Equal(minimum, model.QualityMinimum);
        Assert.Equal(maximum, model.QualityMaximum);
        Assert.Equal(mode, settings.RateControlMode);
    }

    /// <summary>有效自定义质量跨编码器保留，未完成输入保持可编辑。</summary>
    [Theory]
    [InlineData("27")]
    [InlineData("7e-")]
    public void CodecSwitchAndLanguageRefreshPreserveDraftAndStableIdentity(string draft)
    {
        var model = new VideoExportSettingsViewModel { QualityText = draft };
        var choice = Assert.Single(model.Codecs, value => value.Codec == VideoCodec.Av1);

        model.SelectedCodecChoice = choice;
        model.RefreshLanguage();

        Assert.Same(choice, model.SelectedCodecChoice);
        Assert.Equal(VideoCodec.Av1, model.SelectedCodec);
        Assert.Equal(draft, model.QualityText);
    }

    /// <summary>切换到较小质量范围时使用目标编码器默认值。</summary>
    [Fact]
    public void CodecSwitchReplacesPreviouslyValidOutOfRangeQuality()
    {
        var model = new VideoExportSettingsViewModel { SelectedCodec = VideoCodec.Vp9, QualityText = "63" };

        model.SelectedCodec = VideoCodec.H264;

        Assert.Equal("20", model.QualityText);
        Assert.Equal(20, model.CaptureSettings(true).Quality);
    }

    /// <summary>VP8 无硬件后端，回到软件并恢复先前质量模式。</summary>
    [Fact]
    public void Vp8DisablesHardwareAndRestoresSoftwareQualityMode()
    {
        var model = new VideoExportSettingsViewModel { UseHardwareEncoder = true };

        model.SelectedCodec = VideoCodec.Vp8;
        model.UseHardwareEncoder = true;

        Assert.False(model.CanUseHardwareEncoder);
        Assert.False(model.UseHardwareEncoder);
        Assert.Equal(VideoRateControlMode.CQ, model.CaptureSettings(true).RateControlMode);
    }

    /// <summary>CQ 的质量与目标码率均为活动字段。</summary>
    [Theory]
    [InlineData("invalid", "8", "QualityInput")]
    [InlineData("32", "invalid", "VideoBitrateInput")]
    public void ConstrainedQualityValidatesBothInputs(string quality, string bitrate, string field)
    {
        var model = new VideoExportSettingsViewModel
        {
            SelectedCodec = VideoCodec.Vp8,
            QualityText = quality,
            VideoBitrateText = bitrate
        };

        var error = Assert.Throws<ExportSettingsValidationException>(() => model.CaptureSettings());

        Assert.Equal(field, error.FieldKey);
        Assert.Equal(quality, model.QualityText);
        Assert.Equal(bitrate, model.VideoBitrateText);
    }

    /// <summary>Opus 码率参与活动校验，完整预设投影保持编码身份与 CQ 两参数。</summary>
    [Fact]
    public void OpusAndConstrainedQualityRoundTripAndValidateAudioDraft()
    {
        var model = new VideoExportSettingsViewModel();
        var settings = VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp8) with
        {
            Quality = 42,
            VideoBitrate = 3500000,
            AudioMode = AudioExportMode.Opus,
            AudioBitrate = 96000
        };

        model.ApplySettings(settings);

        Assert.Equal(settings, model.CaptureSettings(true));
        Assert.True(model.IsQualityMode);
        Assert.True(model.IsVideoBitrateActive);
        Assert.True(model.IsEncodedAudio);
        model.AudioBitrateText = "pending";
        Assert.Equal("AudioBitrateInput", Assert.Throws<ExportSettingsValidationException>(() => model.CaptureSettings()).FieldKey);
    }
}
