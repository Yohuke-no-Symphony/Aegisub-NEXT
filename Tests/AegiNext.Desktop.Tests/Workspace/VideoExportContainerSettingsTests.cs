using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings.Export;
using AegiNext.Export;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class VideoExportContainerSettingsTests
{
    [Theory]
    [InlineData(VideoCodec.H264, VideoContainerFormat.MKV, VideoCodec.Hevc, VideoContainerFormat.MKV)]
    [InlineData(VideoCodec.H264, VideoContainerFormat.MP4, VideoCodec.Vp9, VideoContainerFormat.MP4)]
    [InlineData(VideoCodec.Vp9, VideoContainerFormat.WEBM, VideoCodec.H264, VideoContainerFormat.MP4)]
    [InlineData(VideoCodec.H264, VideoContainerFormat.MP4, VideoCodec.Vp8, VideoContainerFormat.MKV)]
    public void CodecChangesPreserveCompatibleContainerAndOtherwiseUseDefault(VideoCodec initial,
        VideoContainerFormat container, VideoCodec next, VideoContainerFormat expected)
    {
        var model = new VideoExportSettingsViewModel { SelectedCodec = initial, SelectedContainer = container };

        model.SelectedCodec = next;

        Assert.Equal(expected, model.SelectedContainer);
        Assert.Equal(expected, model.CaptureSettings(true).Container);
        Assert.DoesNotContain(model.Containers, choice => choice.Container == VideoContainerFormat.AUTO);
    }

    [Fact]
    public void WebMConvertsAacToOpusWithoutChangingBitrateOrOtherDrafts()
    {
        var model = new VideoExportSettingsViewModel
        {
            SelectedCodec = VideoCodec.Vp9,
            AudioMode = (int)AudioExportMode.Aac,
            AudioBitrateText = "256",
            QualityText = "7e-"
        };

        model.SelectedContainer = VideoContainerFormat.WEBM;

        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        Assert.True(model.HasAudioCompatibilityHint);
        Assert.Contains("Opus", model.AudioCompatibilityHint, StringComparison.Ordinal);
        Assert.Equal("256", model.AudioBitrateText);
        Assert.Equal("7e-", model.QualityText);
        model.SelectedContainer = VideoContainerFormat.MKV;
        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        Assert.False(model.HasAudioCompatibilityHint);
    }

    [Theory]
    [InlineData("aac", AudioExportMode.Opus)]
    [InlineData("pcm_s16le", AudioExportMode.Opus)]
    [InlineData("opus", AudioExportMode.Copy)]
    [InlineData("vorbis", AudioExportMode.Copy)]
    [InlineData(null, AudioExportMode.Copy)]
    public void WebMCopyUsesKnownSourceCodecAndPreservesUnknownIntent(string? codec, AudioExportMode expected)
    {
        var model = new VideoExportSettingsViewModel { SelectedCodec = VideoCodec.Vp9 };
        model.UpdateSourceAudioCodec(codec);

        model.SelectedContainer = VideoContainerFormat.WEBM;

        Assert.Equal((int)expected, model.AudioMode);
        Assert.Equal(expected, model.CaptureSettings(true).AudioMode);
    }

    [Fact]
    public void NewlyKnownSourceAndManualAacSelectionBothConvertToOpus()
    {
        var model = new VideoExportSettingsViewModel
        {
            SelectedCodec = VideoCodec.Vp9,
            SelectedContainer = VideoContainerFormat.WEBM
        };
        Assert.Equal((int)AudioExportMode.Copy, model.AudioMode);

        model.UpdateSourceAudioCodec("aac");

        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        model.AudioMode = (int)AudioExportMode.Aac;
        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        model.AudioMode = (int)AudioExportMode.None;
        model.UpdateSourceAudioCodec("pcm_s16le");
        Assert.Equal((int)AudioExportMode.None, model.AudioMode);
    }

    [Fact]
    public void AutomaticAudioConversionMakesTheRetainedBitrateDraftActive()
    {
        var model = new VideoExportSettingsViewModel
        {
            SelectedCodec = VideoCodec.Vp9,
            AudioBitrateText = "unfinished audio bitrate"
        };
        model.UpdateSourceAudioCodec("aac");

        model.SelectedContainer = VideoContainerFormat.WEBM;

        var error = Assert.Throws<ExportSettingsValidationException>(() => model.CaptureSettings());
        Assert.Equal("AudioBitrateInput", error.FieldKey);
        Assert.Equal("unfinished audio bitrate", model.AudioBitrateText);
    }

    [Fact]
    public void ApplyingWebMCopyPresetUsesWorkspaceSourceAndLanguageRefreshPreservesIdentity()
    {
        var model = new VideoExportSettingsViewModel();
        model.UpdateSourceAudioCodec("aac");
        var settings = VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp9) with
        {
            Container = VideoContainerFormat.WEBM
        };

        model.ApplySettings(settings);
        var selection = model.SelectedContainerChoice;
        model.RefreshLanguage();

        Assert.Same(selection, model.SelectedContainerChoice);
        Assert.Equal(VideoContainerFormat.WEBM, model.SelectedContainer);
        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        Assert.Equal(settings with { AudioMode = AudioExportMode.Opus }, model.CaptureSettings(true));
    }

    [Fact]
    public void UnsupportedContainerCannotReplaceTheCurrentSelection()
    {
        var model = new VideoExportSettingsViewModel();

        Assert.Throws<ArgumentException>(() => model.SelectedContainer = VideoContainerFormat.WEBM);

        Assert.Equal(VideoContainerFormat.MP4, model.SelectedContainer);
    }

    [Theory]
    [InlineData(VideoCodec.Vp9, VideoContainerFormat.WEBM, VideoCodec.H264, VideoContainerFormat.MP4)]
    [InlineData(VideoCodec.Auto, VideoContainerFormat.MP4, VideoCodec.Vp8, VideoContainerFormat.MKV)]
    public void SettingsObserversOnlySeeCompatibleCodecAndContainer(VideoCodec initial, VideoContainerFormat container,
        VideoCodec next, VideoContainerFormat expected)
    {
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([new(Guid.NewGuid(), "Atomic settings", VideoExportCapabilities.CreateDefaultSettings(initial) with
        {
            Container = container
        })]);
        model.PropertyChanged += (_, _) =>
        {
            _ = model.IsDirty;
        };

        model.SelectedCodec = next;

        Assert.Equal(expected, model.SelectedContainer);
        Assert.True(model.IsDirty);
        Assert.Equal(expected, model.CaptureSettings(true).Container);
    }

    [Fact]
    public void SettingsObserversNeverReceiveTheRejectedWebMAacCombination()
    {
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([new(Guid.NewGuid(), "Atomic audio", VideoExportCapabilities.CreateDefaultSettings(VideoCodec.Vp9) with
        {
            Container = VideoContainerFormat.WEBM,
            AudioMode = AudioExportMode.Opus
        })]);
        model.PropertyChanged += (_, _) =>
        {
            _ = model.IsDirty;
        };

        model.AudioMode = (int)AudioExportMode.Aac;

        Assert.Equal((int)AudioExportMode.Opus, model.AudioMode);
        Assert.False(model.IsDirty);
    }
}
