using System.Runtime.InteropServices;
using AegiNext.Export;

namespace AegiNext.Export.Tests;

public sealed class VideoCodecCapabilitiesTests
{
    /// <summary>平台能力匹配原生后端范围，而非跨平台编码器列表。</summary>
    [Theory]
    [InlineData(VideoCodec.Auto, true, true)]
    [InlineData(VideoCodec.H264, true, true)]
    [InlineData(VideoCodec.Hevc, true, true)]
    [InlineData(VideoCodec.Vp8, false, false)]
    [InlineData(VideoCodec.Vp9, false, true)]
    [InlineData(VideoCodec.Av1, false, true)]
    public void HardwarePlatformsMatchNativeEncoderCandidates(VideoCodec codec, bool mac, bool windows)
    {
        var capability = VideoExportCapabilities.Get(codec);

        Assert.Equal(mac, capability.SupportsHardwareOn(OSPlatform.OSX));
        Assert.Equal(windows, capability.SupportsHardwareOn(OSPlatform.Windows));
        Assert.False(capability.SupportsHardwareOn(OSPlatform.Linux));
        Assert.False(capability.SupportsHardwareOn(OSPlatform.Create("AEGINEXT_UNSUPPORTED")));
        Assert.Equal(OperatingSystem.IsMacOS() && mac || OperatingSystem.IsWindows() && windows,
            capability.SupportsHardwareOnCurrentPlatform);
        Assert.Equal(windows, capability.SupportsHardware);
    }

    /// <summary>平台投影仅改变不可用的硬件模式，预设保持可交换且重复投影稳定。</summary>
    [Theory]
    [InlineData(VideoCodec.Vp9, VideoRateControlMode.VBR)]
    [InlineData(VideoCodec.Vp9, VideoRateControlMode.CBR)]
    [InlineData(VideoCodec.Av1, VideoRateControlMode.VBR)]
    [InlineData(VideoCodec.Av1, VideoRateControlMode.CBR)]
    public void PresetPlatformProjectionPreservesCrossPlatformContract(VideoCodec codec, VideoRateControlMode mode)
    {
        var original = VideoExportCapabilities.CreateDefaultSettings(codec) with
        {
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = mode,
            Preset = "slow",
            Quality = 27,
            VideoBitrate = 12345678,
            AudioMode = AudioExportMode.Opus,
            AudioBitrate = 96000
        };

        var projected = VideoExportCapabilities.ProjectPresetToCurrentPlatform(original);

        Assert.Equal(original with
        {
            EncodingMode = OperatingSystem.IsWindows() ? VideoEncodingMode.HARDWARE : VideoEncodingMode.SOFTWARE
        }, projected);
        Assert.Equal(VideoEncodingMode.HARDWARE, original.EncodingMode);
        VideoExportSettingsValidator.ValidatePreset(original);
        VideoExportSettingsValidator.ValidatePreset(projected);
        Assert.Same(projected, VideoExportCapabilities.ProjectPresetToCurrentPlatform(projected));
    }

    /// <summary>软件预设不因平台投影改变，未指定码控的运行时请求不能作为预设降级。</summary>
    [Fact]
    public void PresetPlatformProjectionKeepsSoftwareAndRejectsAutomaticRateControl()
    {
        var software = VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Av1);

        Assert.Same(software, VideoExportCapabilities.ProjectPresetToCurrentPlatform(software));
        Assert.Throws<ArgumentException>(() => VideoExportCapabilities.ProjectPresetToCurrentPlatform(software with
        {
            EncodingMode = VideoEncodingMode.HARDWARE,
            RateControlMode = VideoRateControlMode.AUTOMATIC
        }));
    }

    [Theory]
    [InlineData(VideoCodec.Vp8, 0, 63, VideoRateControlMode.CQ, "libvpx")]
    [InlineData(VideoCodec.Vp9, 0, 63, VideoRateControlMode.CRF, "libvpx-vp9")]
    [InlineData(VideoCodec.Av1, 1, 63, VideoRateControlMode.CRF, "libsvtav1")]
    public void NewCodecDefaultsUseTheBackendQualityContract(VideoCodec codec, int minimum, int maximum,
        VideoRateControlMode qualityMode, string encoder)
    {
        var capability = VideoExportCapabilities.Get(codec);
        var settings = VideoExportCapabilities.CreateDefaultSettings(codec);
        Assert.Equal(minimum, capability.QualityMinimum);
        Assert.Equal(maximum, capability.QualityMaximum);
        Assert.Equal(encoder, capability.SoftwareEncoder);
        Assert.Equal(qualityMode, settings.RateControlMode);
        Assert.Equal(32, settings.Quality);
        Assert.Equal(AudioExportMode.Copy, settings.AudioMode);
        VideoExportSettingsValidator.ValidatePreset(settings);
    }

    [Theory]
    [InlineData(VideoCodec.Vp8, VideoRateControlMode.CRF)]
    [InlineData(VideoCodec.Vp9, VideoRateControlMode.CQ)]
    [InlineData(VideoCodec.Av1, VideoRateControlMode.CQ)]
    public void QualityModesCannotCrossBackendSemantics(VideoCodec codec, VideoRateControlMode mode)
    {
        var settings = VideoExportCapabilities.CreateDefaultSettings(codec) with { RateControlMode = mode };
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings));
    }

    [Fact]
    public void Vp8ConstrainedQualityRequiresQualityAndTargetBitrate()
    {
        var settings = VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp8);
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings with { VideoBitrate = 0 }));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings with { Quality = 64 }));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings with { EncodingMode = VideoEncodingMode.HARDWARE }));
        Assert.Equal(VideoRateControlMode.CQ, VideoExportSettingsValidator.ResolveRateControlMode(settings with { RateControlMode = VideoRateControlMode.AUTOMATIC }));
    }

    [Theory]
    [InlineData(VideoCodec.Vp8, ".webm", true)]
    [InlineData(VideoCodec.Vp8, ".mp4", false)]
    [InlineData(VideoCodec.Vp9, ".mp4", true)]
    [InlineData(VideoCodec.Av1, ".webm", true)]
    [InlineData(VideoCodec.Auto, ".webm", false)]
    public void ContainerCompatibilityIsExplicit(VideoCodec codec, string extension, bool compatible)
    {
        Assert.Equal(compatible, VideoExportCapabilities.Get(codec).Extensions.Contains(extension));
    }

    [Theory]
    [InlineData(VideoRateControlVerification.ConfiguredAndInitialized, 8000000, true)]
    [InlineData(VideoRateControlVerification.ConfiguredAndInitialized, 8000500, false)]
    [InlineData(VideoRateControlVerification.RuntimeQueried, 8000500, true)]
    [InlineData(VideoRateControlVerification.RuntimeQueried, 8001000, false)]
    [InlineData((VideoRateControlVerification)0, 8000000, false)]
    public void OnlyDriverReadbackMayApplyTheReportedBitratePrecision(VideoRateControlVerification verification, int actual, bool accepted)
    {
        var request = CreateRequest() with { RateControlMode = VideoRateControlMode.VBR };
        var receipt = new VideoRateControlInfo(VideoRateControlMode.VBR, actual, 0, verification);
        if (accepted)
        {
            VideoExporter.ValidateCompletedRateControl(request, receipt);
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedRateControl(request, receipt));
        }
    }

    [Theory]
    [InlineData(0U, VideoCodec.Vp9, 8, "yuv420p", 1)]
    [InlineData(1U, VideoCodec.Vp9, 8, "yuv420p", 1)]
    [InlineData(2U, VideoCodec.Vp9, 8, "yuv420p", 1)]
    [InlineData(3U, VideoCodec.Av1, 8, "yuv420p", 1)]
    [InlineData(3U, VideoCodec.Vp9, 10, "yuv420p", 1)]
    [InlineData(3U, VideoCodec.Vp9, 8, "yuv420p", 16)]
    public void IncompleteOrInconsistentOutputEvidenceCannotCommit(uint verified, VideoCodec actualCodec, int depth, string pixelFormat, int transfer)
    {
        var request = CreateRequest();
        var color = new VideoExportColor(1, 1, 1, transfer, 1, 0, 0, actualCodec, depth, pixelFormat, OutputVerification: verified);
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedOutput(request, color));
    }

    [Theory]
    [InlineData(".webm", AudioExportMode.Copy, "aac", false)]
    [InlineData(".webm", AudioExportMode.Copy, "opus", true)]
    [InlineData(".webm", AudioExportMode.Copy, "vorbis", true)]
    [InlineData(".webm", AudioExportMode.Opus, "aac", true)]
    [InlineData(".webm", AudioExportMode.Aac, null, false)]
    [InlineData(".mkv", AudioExportMode.Copy, "aac", true)]
    public void WebmNeverSilentlyChangesIncompatibleCopyAudio(string extension, AudioExportMode mode, string? codec, bool accepted)
    {
        if (accepted)
        {
            ExportMuxer.ValidateAudioCompatibility(extension, mode, codec);
        }
        else
        {
            Assert.Throws<NotSupportedException>(() => ExportMuxer.ValidateAudioCompatibility(extension, mode, codec));
        }
    }

    private static VideoExportRequest CreateRequest() => new(new(), Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "output.mkv"))
    {
        Codec = VideoCodec.Vp9, RateControlMode = VideoRateControlMode.CRF, Quality = 32
    };
}
