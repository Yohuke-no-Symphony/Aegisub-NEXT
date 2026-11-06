using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Panels.Export;
using AegiNext.Export;

namespace AegiNext.Desktop.Tests.Workspace;

/// <summary>验证独立音轨格式草稿与视频编码草稿互不干扰。</summary>
[Collection("Workspace session")]
public sealed class AudioExportSettingsTests
{
    /// <summary>音轨方式跳过隐藏视频草稿，返回烧录后原始错误仍可定位。</summary>
    [Fact]
    public async Task AudioMethodPreservesVideoDraftsAndProjectIdentity()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        var snapshot = context.Editor.Snapshot;
        model.QualityText = "unfinished quality";
        model.VideoBitrateText = "unfinished video bitrate";
        model.AudioMode = 1;
        model.AudioBitrateText = "unfinished video audio bitrate";

        model.SelectedMethod = ExportMethod.AUDIO;

        Assert.True(model.IsAudioExport);
        Assert.False(model.IsBurnIn);
        Assert.False(model.IsSubtitleMux);
        Assert.True(context.Session.TryCommitDrafts(false));
        model.AudioExport.Format = AudioFileFormat.Mp3;
        model.AudioExport.BitrateText = "256";
        Assert.True(context.Session.TryCommitDrafts(false));
        model.SelectedMethod = ExportMethod.SUBTITLE_MUX;
        model.SelectedMethod = ExportMethod.AUDIO;
        Assert.Equal(AudioFileFormat.Mp3, model.AudioExport.Format);
        Assert.Equal("256", model.AudioExport.BitrateText);
        model.SelectedMethod = ExportMethod.BURN_IN;

        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Equal("QualityInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal("unfinished quality", model.QualityText);
        Assert.Equal("unfinished video bitrate", model.VideoBitrateText);
        Assert.Equal("unfinished video audio bitrate", model.AudioBitrateText);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>无损格式和复制模式不校验隐藏码率，切回有损编码时仍要求修正。</summary>
    [Theory]
    [InlineData(AudioFileFormat.Copy)]
    [InlineData(AudioFileFormat.Wav)]
    [InlineData(AudioFileFormat.Flac)]
    public async Task InactiveInvalidBitrateSurvivesFormatRoundTrip(AudioFileFormat format)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        model.SelectedMethod = ExportMethod.AUDIO;
        model.AudioExport.Format = AudioFileFormat.Mp3;
        model.AudioExport.BitrateText = "unfinished bitrate";

        model.AudioExport.Format = format;
        var settings = model.AudioExport.CaptureSettings();

        Assert.Equal(format, settings.Format);
        Assert.False(model.AudioExport.IsBitrateActive);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal("unfinished bitrate", model.AudioExport.BitrateText);
        model.AudioExport.Format = AudioFileFormat.Mp3;

        Assert.True(model.AudioExport.IsBitrateActive);
        var error = Assert.Throws<ExportSettingsValidationException>(() => model.AudioExport.CaptureSettings());
        Assert.Equal("AudioExportBitrateInput", error.FieldKey);
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Equal("AudioExportBitrateInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal("unfinished bitrate", model.AudioExport.BitrateText);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>各有损格式按自己的码率上限捕获不可变设置，不静默夹紧非法输入。</summary>
    [Theory]
    [InlineData(AudioFileFormat.Mp3, "320", 320000, "321", 320)]
    [InlineData(AudioFileFormat.Aac, "512", 512000, "513", 512)]
    [InlineData(AudioFileFormat.Opus, "512", 512000, "513", 512)]
    public async Task ActiveBitrateLimitsMatchTheSelectedFormat(AudioFileFormat format,
        string valid, int expected, string invalid, int maximum)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export.AudioExport;
        model.Format = format;
        model.BitrateText = valid;

        var settings = model.CaptureSettings();
        model.BitrateText = invalid;

        Assert.True(model.IsBitrateActive);
        Assert.Equal(maximum, model.BitrateMaximum);
        Assert.Equal(format, settings.Format);
        Assert.Equal(expected, settings.Bitrate);
        Assert.Throws<ExportSettingsValidationException>(() => model.CaptureSettings());
        Assert.Equal(invalid, model.BitrateText);
    }

    /// <summary>活动码率拒绝低于最小值和不能表达为整数的输入。</summary>
    [Theory]
    [InlineData("31")]
    [InlineData("192.5")]
    [InlineData("invalid")]
    public async Task InvalidActiveBitratesRemainUncommitted(string raw)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        model.SelectedMethod = ExportMethod.AUDIO;
        model.AudioExport.Format = AudioFileFormat.Aac;
        model.AudioExport.BitrateText = raw;

        Assert.False(context.Session.TryCommitDrafts(false));

        Assert.Equal("AudioExportBitrateInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal(raw, model.AudioExport.BitrateText);
        Assert.False(context.Editor.CanUndo);
    }
}
