using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Export;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ExportCodecUiTests
{
    /// <summary>VP8 显示 CQ 两参数并禁用硬件；Opus 码率与稳定选择跨语言保留。</summary>
    [AvaloniaFact]
    public async Task Vp8ShowsQualityAndBitrateWithOpusAndStableCodecIdentity()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var vm = context.Session.ViewModel.Export;
        var codec = view.FindControl<ComboBox>("CodecCombo")!;
        var selected = Assert.Single(vm.Codecs, value => value.Codec == VideoCodec.Vp8);
        codec.SelectedItem = selected;
        view.FindControl<ComboBox>("AudioModeCombo")!.SelectedIndex = (int)AudioExportMode.Opus;
        context.Flush();

        var quality = view.FindControl<NumericDraftInput>("QualityInput")!;
        var bitrate = view.FindControl<NumericDraftInput>("VideoBitrateInput")!;
        Assert.True(quality.IsEffectivelyVisible);
        Assert.True(bitrate.IsEffectivelyVisible);
        Assert.False(view.FindControl<ComboBox>("BitrateModeCombo")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<CheckBox>("HardwareEncoderToggle")!.IsEnabled);
        Assert.True(view.FindControl<NumericDraftInput>("AudioBitrateInput")!.IsEffectivelyVisible);
        Assert.Equal(63m, quality.Maximum);
        Assert.Equal("32", quality.RawText);
        quality.RawText = "unfinished quality";
        bitrate.RawText = "unfinished bitrate";

        Localization.SetLanguage("ja-JP");
        context.Flush();

        Assert.Same(selected, codec.SelectedItem);
        Assert.Equal(VideoCodec.Vp8, vm.SelectedCodec);
        Assert.Equal("unfinished quality", quality.RawText);
        Assert.Equal("unfinished bitrate", bitrate.RawText);
        Assert.Equal(0, view.FindControl<ComboBox>("QualityModeCombo")!.SelectedIndex);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>质量范围改变后保留未完成草稿，数值默认值不会被旧控件范围截断。</summary>
    [AvaloniaFact]
    public async Task ChangingCodecUpdatesBoundsAndPreservesUnfinishedQuality()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var vm = context.Session.ViewModel.Export;
        var quality = context.Panels[WorkbenchPanelIds.EXPORT].FindControl<NumericDraftInput>("QualityInput")!;
        quality.RawText = "7e-";

        vm.SelectedCodec = VideoCodec.Av1;
        context.Flush();

        Assert.Equal(1m, quality.Minimum);
        Assert.Equal(63m, quality.Maximum);
        Assert.Equal("7e-", quality.RawText);
        quality.RawText = "63";
        vm.SelectedCodec = VideoCodec.H264;
        context.Flush();
        Assert.Equal(0m, quality.Minimum);
        Assert.Equal(51m, quality.Maximum);
        Assert.Equal("20", quality.RawText);
    }

    /// <summary>保存对话框使用所选编码器的容器集合与兼容默认扩展名。</summary>
    [AvaloniaTheory]
    [InlineData(VideoCodec.Auto, ".mp4", false, true)]
    [InlineData(VideoCodec.H264, ".mp4", false, true)]
    [InlineData(VideoCodec.Hevc, ".mp4", false, true)]
    [InlineData(VideoCodec.Vp8, ".mkv", true, false)]
    [InlineData(VideoCodec.Vp9, ".mkv", true, true)]
    [InlineData(VideoCodec.Av1, ".mkv", true, true)]
    public async Task OutputPickerUsesCodecCapabilities(VideoCodec codec, string extension, bool webm, bool mp4)
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Session.ViewModel.Export.SelectedCodec = codec;
        context.Dialogs.ResolveOutput(null);

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Equal(1, context.Dialogs.SaveRequests);
        Assert.Equal(extension, context.Dialogs.DefaultExtension);
        Assert.EndsWith(extension, context.Dialogs.SuggestedFileName);
        Assert.Contains("*.mkv", context.Dialogs.Patterns);
        Assert.Equal(webm, context.Dialogs.Patterns.Contains("*.webm"));
        Assert.Equal(mp4, context.Dialogs.Patterns.Contains("*.mp4"));
        Assert.DoesNotContain("*.mov", context.Dialogs.Patterns);
        Assert.Empty(context.Dialogs.ErrorMessages);
    }
}
