using AegiNext.Desktop.Panels.Export;

namespace AegiNext.Desktop.Tests.Workspace;

/// <summary>验证独立封装方式不会提交或丢失烧录参数草稿。</summary>
[Collection("Workspace session")]
public sealed class SubtitleMuxSettingsTests
{
    /// <summary>方式往返保留全部烧录参数与原始草稿，不产生工程修改。</summary>
    [Fact]
    public async Task MethodRoundTripPreservesEncodingChoicesRawDraftsAndProjectIdentity()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        var snapshot = context.Editor.Snapshot;
        model.QualityMode = 1;
        model.BitrateMode = 1;
        model.AudioMode = 1;
        model.Speed = 0;
        model.QualityText = "unfinished quality";
        model.VideoBitrateText = "unfinished video bitrate";
        model.AudioBitrateText = "unfinished audio bitrate";
        Assert.Equal(ExportMethod.BURN_IN, model.SelectedMethod);

        model.SelectedMethod = ExportMethod.SUBTITLE_MUX;

        Assert.True(model.IsSubtitleMux);
        Assert.False(model.IsBurnIn);
        Assert.True(context.Session.TryCommitDrafts(false));
        model.SelectedMethod = ExportMethod.BURN_IN;

        Assert.True(model.IsBurnIn);
        Assert.False(model.IsSubtitleMux);
        Assert.Equal(1, model.QualityMode);
        Assert.Equal(1, model.BitrateMode);
        Assert.Equal(1, model.AudioMode);
        Assert.Equal(0, model.Speed);
        Assert.Equal("unfinished quality", model.QualityText);
        Assert.Equal("unfinished video bitrate", model.VideoBitrateText);
        Assert.Equal("unfinished audio bitrate", model.AudioBitrateText);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>封装跳过隐藏参数校验，回到烧录后同一非法活动字段继续阻止提交。</summary>
    [Theory]
    [InlineData(0, 0, "invalid quality", "8", "192", "QualityInput")]
    [InlineData(1, 0, "20", "invalid video bitrate", "192", "VideoBitrateInput")]
    [InlineData(0, 1, "20", "8", "invalid audio bitrate", "AudioBitrateInput")]
    public async Task HiddenInvalidEncodingDraftDoesNotBlockMuxAndStillFailsAfterReturningToBurnIn(
        int qualityMode, int audioMode, string quality, string bitrate, string audio, string field)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        var snapshot = context.Editor.Snapshot;
        model.QualityMode = qualityMode;
        model.AudioMode = audioMode;
        model.QualityText = quality;
        model.VideoBitrateText = bitrate;
        model.AudioBitrateText = audio;
        model.SelectedMethod = ExportMethod.SUBTITLE_MUX;

        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(quality, model.QualityText);
        Assert.Equal(bitrate, model.VideoBitrateText);
        Assert.Equal(audio, model.AudioBitrateText);
        model.SelectedMethod = ExportMethod.BURN_IN;

        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Equal(field, context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal(quality, model.QualityText);
        Assert.Equal(bitrate, model.VideoBitrateText);
        Assert.Equal(audio, model.AudioBitrateText);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }
}
