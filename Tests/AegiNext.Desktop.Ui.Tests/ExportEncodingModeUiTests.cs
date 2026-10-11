using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Export;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ExportEncodingModeUiTests
{
    [AvaloniaFact]
    public async Task HardwareToggleShowsBitratePreservesOptionsAndExportsImmutableModeSnapshot()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var vm = context.Session.ViewModel.Export;
        var toggle = view.FindControl<CheckBox>("HardwareEncoderToggle")!;
        var crf = view.FindControl<NumericDraftInput>("QualityInput")!;
        var bitrate = view.FindControl<NumericDraftInput>("VideoBitrateInput")!;
        Assert.False(vm.UseHardwareEncoder);
        Assert.True(crf.IsEffectivelyVisible);
        Assert.False(bitrate.IsEffectivelyVisible);
        UiTestCapture.CaptureExportPanel(context.Owner, "cpu-crf");
        toggle.IsChecked = true;
        bitrate.RawText = "12.5";
        crf.RawText = "pending CPU quality";
        context.Flush();
        Assert.True(vm.UseHardwareEncoder);
        Assert.False(crf.IsEffectivelyVisible);
        Assert.True(bitrate.IsEffectivelyVisible);
        UiTestCapture.CaptureExportPanel(context.Owner, "hardware-vbr");
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.ENCODE));
        context.Flush();
        Assert.True(vm.UseHardwareEncoder);
        Assert.Equal("12.5", vm.VideoBitrateText);
        Assert.Equal("pending CPU quality", vm.QualityText);
        context.Dialogs.ResolveOutput(context.OutputPath);
        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(VideoEncodingMode.HARDWARE, request.EncodingMode);
        Assert.Equal(VideoRateControlMode.VBR, request.RateControlMode);
        Assert.Equal(12500000, request.VideoBitrate);
        Assert.Equal(20, request.Quality);
        context.ExportService.Report(new(3, new(1), 0.5, "encoding", "h264_videotoolbox"));
        context.Flush();
        Assert.Contains("h264_videotoolbox", vm.Status, StringComparison.Ordinal);
        Assert.Contains(context.Session.Journal.Entries, entry => entry.Summary.Contains("h264_videotoolbox", StringComparison.Ordinal));
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(vm.IsRunning);
    }

    [AvaloniaFact]
    public async Task InvalidActiveBitrateBlocksExportButHiddenBitrateDoesNotBlockCpu()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var vm = context.Session.ViewModel.Export;
        vm.UseHardwareEncoder = true;
        vm.VideoBitrateText = "invalid";
        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal("VideoBitrateInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal("invalid", vm.VideoBitrateText);
        vm.UseHardwareEncoder = false;
        context.Dialogs.ResolveOutput(context.OutputPath);
        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(VideoEncodingMode.SOFTWARE, request.EncodingMode);
        Assert.Equal(VideoRateControlMode.CRF, request.RateControlMode);
        Assert.Equal(8000000, request.VideoBitrate);
        Assert.Equal("invalid", vm.VideoBitrateText);
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>软件编码面板可显式选择 VBR 或 CBR，并捕获稳定请求。</summary>
    [AvaloniaTheory]
    [InlineData(0, VideoRateControlMode.VBR)]
    [InlineData(1, VideoRateControlMode.CBR)]
    public async Task SoftwareBitrateControlsExportExplicitModeAndImmutableParameters(int mode,
        VideoRateControlMode expected)
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var quality = view.FindControl<ComboBox>("QualityModeCombo")!;
        var rateControl = view.FindControl<ComboBox>("BitrateModeCombo")!;
        var bitrate = view.FindControl<NumericDraftInput>("VideoBitrateInput")!;
        quality.SelectedIndex = 1;
        rateControl.SelectedIndex = mode;
        bitrate.RawText = "12.5";
        context.Flush();
        Assert.True(quality.IsEnabled);
        Assert.True(bitrate.IsEffectivelyVisible);
        Assert.True(rateControl.IsEffectivelyVisible);
        Assert.False(view.FindControl<NumericDraftInput>("QualityInput")!.IsEffectivelyVisible);
        UiTestCapture.CaptureExportPanel(context.Owner, "cpu-" + expected.ToString().ToLowerInvariant());
        context.Dialogs.ResolveOutput(context.OutputPath);

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bitrate.RawText = "24";
        rateControl.SelectedIndex = 1 - mode;

        Assert.Equal(VideoEncodingMode.SOFTWARE, request.EncodingMode);
        Assert.Equal(expected, request.RateControlMode);
        Assert.Equal(12500000, request.VideoBitrate);
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>GPU 切换固定 Mbps，恢复 CPU 时恢复原来的质量模式和草稿。</summary>
    [AvaloniaFact]
    public async Task HardwareToggleLocksQualitySelectorAndRestoresCpuSelectionWithoutDiscardingDrafts()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var quality = view.FindControl<ComboBox>("QualityModeCombo")!;
        var toggle = view.FindControl<CheckBox>("HardwareEncoderToggle")!;
        var crf = view.FindControl<NumericDraftInput>("QualityInput")!;
        var bitrate = view.FindControl<NumericDraftInput>("VideoBitrateInput")!;
        crf.RawText = "unfinished quality";
        bitrate.RawText = "unfinished bitrate";

        toggle.IsChecked = true;
        context.Flush();
        Assert.Equal(1, quality.SelectedIndex);
        Assert.False(quality.IsEnabled);
        Assert.True(bitrate.IsEffectivelyVisible);
        toggle.IsChecked = false;
        context.Flush();

        Assert.Equal(0, quality.SelectedIndex);
        Assert.True(quality.IsEnabled);
        Assert.True(crf.IsEffectivelyVisible);
        Assert.Equal("unfinished quality", crf.RawText);
        Assert.Equal("unfinished bitrate", bitrate.RawText);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>音频码率只在 AAC 时显示和阻塞导出，Copy 保留未完成草稿。</summary>
    [AvaloniaFact]
    public async Task OnlyActiveAacBitrateBlocksExportAndHiddenInvalidAudioIsPreserved()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var audio = view.FindControl<ComboBox>("AudioModeCombo")!;
        var bitrate = view.FindControl<NumericDraftInput>("AudioBitrateInput")!;
        Assert.False(bitrate.IsEffectivelyVisible);
        audio.SelectedIndex = 1;
        bitrate.RawText = "unfinished";
        context.Flush();
        Assert.True(bitrate.IsEffectivelyVisible);

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Equal("AudioBitrateInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal("unfinished", bitrate.RawText);
        audio.SelectedIndex = 0;
        context.Flush();
        Assert.False(bitrate.IsEffectivelyVisible);
        context.Dialogs.ResolveOutput(context.OutputPath);
        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AudioExportMode.Copy, request.AudioMode);
        Assert.Equal("unfinished", bitrate.RawText);
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>语言刷新保留速度、码控选择和全部原始输入。</summary>
    [AvaloniaFact]
    public async Task LanguageSwitchKeepsRateControlSpeedAndInvalidRawDrafts()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var vm = context.Session.ViewModel.Export;
        view.FindControl<ComboBox>("QualityModeCombo")!.SelectedIndex = 1;
        view.FindControl<ComboBox>("BitrateModeCombo")!.SelectedIndex = 1;
        view.FindControl<ComboBox>("SpeedCombo")!.SelectedIndex = 0;
        view.FindControl<NumericDraftInput>("QualityInput")!.RawText = "unfinished quality";
        view.FindControl<NumericDraftInput>("VideoBitrateInput")!.RawText = "unfinished bitrate";
        view.FindControl<NumericDraftInput>("AudioBitrateInput")!.RawText = "unfinished audio";

        Localization.SetLanguage("zh-CN");
        context.Flush();

        Assert.Equal(1, vm.QualityMode);
        Assert.Equal(1, vm.BitrateMode);
        Assert.Equal(0, vm.Speed);
        Assert.Equal("veryslow", vm.EncodingPreset);
        Assert.Equal("unfinished quality", vm.QualityText);
        Assert.Equal("unfinished bitrate", vm.VideoBitrateText);
        Assert.Equal("unfinished audio", vm.AudioBitrateText);
        Assert.Equal(1, view.FindControl<ComboBox>("QualityModeCombo")!.SelectedIndex);
        Assert.Equal(1, view.FindControl<ComboBox>("BitrateModeCombo")!.SelectedIndex);
        Assert.Equal(0, view.FindControl<ComboBox>("SpeedCombo")!.SelectedIndex);
        Assert.False(context.Editor.CanUndo);
    }
}
