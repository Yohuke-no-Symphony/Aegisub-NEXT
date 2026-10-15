using AegiNext.Export;

namespace AegiNext.Export.Tests;

/// <summary>验证输出容器的兼容默认值、编码器能力和路径权威。</summary>
public sealed class VideoContainerFormatTests
{
    /// <summary>未指定容器的旧参数仍按编码器采用既有输出格式。</summary>
    [Theory]
    [InlineData(VideoCodec.Auto, VideoContainerFormat.MP4)]
    [InlineData(VideoCodec.H264, VideoContainerFormat.MP4)]
    [InlineData(VideoCodec.Hevc, VideoContainerFormat.MP4)]
    [InlineData(VideoCodec.Vp8, VideoContainerFormat.MKV)]
    [InlineData(VideoCodec.Vp9, VideoContainerFormat.MKV)]
    [InlineData(VideoCodec.Av1, VideoContainerFormat.MKV)]
    public void AutomaticContainerPreservesCodecDefaults(VideoCodec codec, VideoContainerFormat expected)
    {
        var settings = VideoExportCapabilities.CreateDefaultSettings(codec) with { Container = VideoContainerFormat.AUTO };

        VideoExportSettingsValidator.Validate(settings);
        VideoExportSettingsValidator.ValidatePreset(settings);

        Assert.Equal(VideoContainerFormat.AUTO, new VideoExportSettings { Codec = codec }.Container);
        Assert.Equal(expected, VideoExportCapabilities.GetDefaultContainer(codec));
        Assert.Equal(expected, VideoExportCapabilities.CreateDefaultSettings(codec).Container);
        Assert.Equal(expected, VideoExportCapabilities.ResolveContainer(settings));
        var projected = VideoExportCapabilities.ProjectPresetToCurrentPlatform(settings);
        Assert.Equal(settings with { Container = expected }, projected);
        Assert.Same(projected, VideoExportCapabilities.ProjectPresetToCurrentPlatform(projected));
        Assert.Equal(VideoExportCapabilities.Get(codec).Extensions,
            VideoExportCapabilities.GetContainers(codec).Select(VideoExportCapabilities.GetExtension));
    }

    /// <summary>扩展名解析忽略大小写，并保持稳定的具体容器映射。</summary>
    [Theory]
    [InlineData(VideoContainerFormat.MP4, ".mp4")]
    [InlineData(VideoContainerFormat.MKV, ".mkv")]
    [InlineData(VideoContainerFormat.WEBM, ".webm")]
    public void ExplicitContainersRoundTripThroughExtensions(VideoContainerFormat container, string extension)
    {
        Assert.Equal(extension, VideoExportCapabilities.GetExtension(container));
        Assert.Equal(container, VideoExportCapabilities.GetContainer(extension));
        Assert.Equal(container, VideoExportCapabilities.GetContainer(extension.ToUpperInvariant()));
    }

    /// <summary>不兼容容器、未知枚举及 WebM AAC 在执行和预设保存前被拒绝。</summary>
    [Theory]
    [InlineData(VideoCodec.H264, VideoContainerFormat.WEBM, AudioExportMode.Copy)]
    [InlineData(VideoCodec.Vp8, VideoContainerFormat.MP4, AudioExportMode.None)]
    [InlineData(VideoCodec.Vp9, VideoContainerFormat.WEBM, AudioExportMode.Aac)]
    [InlineData(VideoCodec.H264, (VideoContainerFormat)99, AudioExportMode.Copy)]
    public void InvalidContainerCombinationsAreRejected(VideoCodec codec, VideoContainerFormat container, AudioExportMode audio)
    {
        var settings = VideoExportCapabilities.CreateDefaultSettings(codec) with { Container = container, AudioMode = audio };

        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.Validate(settings));
        Assert.Throws<ArgumentException>(() => VideoExportSettingsValidator.ValidatePreset(settings));
    }

    /// <summary>没有来源信息的 WebM 拷贝保留意图，Opus 和无音频可以直接保存。</summary>
    [Theory]
    [InlineData(AudioExportMode.Copy)]
    [InlineData(AudioExportMode.Opus)]
    [InlineData(AudioExportMode.None)]
    public void WebmAcceptsAudioModesThatDoNotRequireSourceInspection(AudioExportMode audio)
    {
        var settings = VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp9) with
        {
            Container = VideoContainerFormat.WEBM,
            AudioMode = audio
        };

        VideoExportSettingsValidator.Validate(settings);
        VideoExportSettingsValidator.ValidatePreset(settings);
    }

    /// <summary>自动格式不能直接生成后缀，未知扩展名和枚举不降级为默认值。</summary>
    [Fact]
    public void InvalidContainerConversionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoExportCapabilities.GetExtension(VideoContainerFormat.AUTO));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoExportCapabilities.GetExtension((VideoContainerFormat)99));
        Assert.Throws<ArgumentException>(() => VideoExportCapabilities.GetContainer(".avi"));
        Assert.Throws<ArgumentException>(() => VideoExportCapabilities.GetContainer(string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoExportCapabilities.GetDefaultContainer((VideoCodec)99));
        Assert.Throws<ArgumentNullException>(() => VideoExportCapabilities.ResolveContainer(null!));
    }

    /// <summary>应用配置保留实际输出路径，反向捕获时以路径后缀为准。</summary>
    [Theory]
    [InlineData(".mp4", VideoContainerFormat.MP4)]
    [InlineData(".MKV", VideoContainerFormat.MKV)]
    [InlineData(".webm", VideoContainerFormat.WEBM)]
    public void RequestOutputPathRemainsTheActualContainerAuthority(string extension, VideoContainerFormat expected)
    {
        var request = new VideoExportRequest(new(), Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "output" + extension));
        var settings = VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp9) with { Container = VideoContainerFormat.MP4 };

        var changed = request.WithSettings(settings);

        Assert.Equal(request.OutputPath, changed.OutputPath);
        Assert.Equal(settings with { Container = expected }, changed.ToSettings());
        Assert.Equal(VideoContainerFormat.MP4, settings.Container);
    }
}
