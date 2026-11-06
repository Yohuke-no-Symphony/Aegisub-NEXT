using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Export;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Export;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证独立音轨面板的草稿隔离、冻结请求和可取消任务生命周期。</summary>
public sealed class AudioExportUiTests
{
    /// <summary>切换方式和语言、重建布局均保留音频选项身份与视频原始草稿。</summary>
    [AvaloniaFact]
    public async Task AudioControlsPreserveIndependentDraftsAcrossLanguageAndLayoutChanges()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var model = context.Session.ViewModel.Export;
        var methodSelector = view.FindControl<ComboBox>("ExportMethodCombo")!;
        var encoding = view.FindControl<Control>("BurnInSettings")!;
        var audioSettings = view.FindControl<Control>("AudioExportSettings")!;
        var formatSelector = view.FindControl<ComboBox>("AudioExportFormatCombo")!;
        var snapshot = context.Editor.Snapshot;
        view.FindControl<NumericDraftInput>("QualityInput")!.RawText = "unfinished quality";
        view.FindControl<NumericDraftInput>("VideoBitrateInput")!.RawText = "unfinished video bitrate";
        view.FindControl<NumericDraftInput>("AudioBitrateInput")!.RawText = "unfinished video audio";
        Assert.True(encoding.IsEffectivelyVisible);
        Assert.False(audioSettings.IsEffectivelyVisible);

        var method = model.Methods.Single(value => value.Method == ExportMethod.AUDIO);
        methodSelector.SelectedItem = method;
        model.AudioExport.Format = AudioFileFormat.Mp3;
        view.FindControl<NumericDraftInput>("AudioExportBitrateInput")!.RawText = "unfinished audio bitrate";
        model.AudioExport.Format = AudioFileFormat.Copy;
        context.Flush();
        var format = model.AudioExport.SelectedFormatChoice;
        var englishLabel = method.Label;

        Assert.False(encoding.IsEffectivelyVisible);
        Assert.True(audioSettings.IsEffectivelyVisible);
        Assert.False(view.FindControl<Control>("SubtitleMuxHint")!.IsEffectivelyVisible);
        Localization.SetLanguage("zh-CN");
        context.Flush();

        Assert.Same(method, methodSelector.SelectedItem);
        Assert.Same(format, formatSelector.SelectedItem);
        Assert.NotEqual(englishLabel, method.Label);
        Assert.Equal("unfinished audio bitrate", model.AudioExport.BitrateText);
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.ENCODE));
        context.Flush();
        Assert.Equal(ExportMethod.AUDIO, model.SelectedMethod);
        Assert.Equal(AudioFileFormat.Copy, model.AudioExport.Format);
        Assert.Equal("unfinished audio bitrate", model.AudioExport.BitrateText);
        methodSelector.SelectedItem = model.Methods.Single(value => value.Method == ExportMethod.BURN_IN);
        context.Flush();

        Assert.True(encoding.IsEffectivelyVisible);
        Assert.False(audioSettings.IsEffectivelyVisible);
        Assert.Equal("unfinished quality", model.QualityText);
        Assert.Equal("unfinished video bitrate", model.VideoBitrateText);
        Assert.Equal("unfinished video audio", model.AudioBitrateText);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>视频和纯音频工程均无需字幕，且文件选择器之前冻结完整音轨请求。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptySubtitleProjectsFreezeAudioRequestBeforeChoosingOutput(bool hasVideo)
    {
        await using var context = new ExportWorkspaceTestContext(media:
            new(hasVideo ? 0 : null, MediaTime.Zero, new(20), 1,
                VideoWidth: hasVideo ? 1 : null, VideoHeight: hasVideo ? 1 : null));
        await context.InitializeAsync();
        var session = context.Session;
        var model = session.ViewModel.Export;
        model.SelectedMethod = ExportMethod.AUDIO;
        model.AudioExport.Format = AudioFileFormat.Mp3;
        model.AudioExport.BitrateText = "256";
        var snapshot = context.Editor.Snapshot;
        Assert.Empty(snapshot.Subtitles);
        Assert.Equal(hasVideo ? 0 : null, snapshot.Media!.VideoStreamIndex);
        Assert.True(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));

        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.Dialogs.SaveShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(session.Export.IsChoosingOutput);
        Assert.False(model.CanSelectMethod);
        Assert.Equal<string>(["*.mp3"], context.Dialogs.Patterns);
        Assert.Equal(".mp3", context.Dialogs.DefaultExtension);
        Assert.EndsWith(".mp3", context.Dialogs.SuggestedFileName, StringComparison.Ordinal);
        model.SelectedMethod = ExportMethod.BURN_IN;
        Assert.Equal(ExportMethod.AUDIO, model.SelectedMethod);
        model.AudioExport.Format = AudioFileFormat.Flac;
        model.AudioExport.BitrateText = "512";
        var output = Path.ChangeExtension(context.OutputPath, ".mp3");
        context.Dialogs.ResolveOutput(output);
        var request = await context.ExportService.AudioEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(snapshot, request.Project);
        Assert.Equal(session.ProjectDirectory, request.ProjectDirectory);
        Assert.Equal(output, request.OutputPath);
        Assert.Equal(AudioFileFormat.Mp3, request.Format);
        Assert.Equal(256000, request.Bitrate);
        Assert.Equal(1, request.Project.Media!.AudioStreamIndex);
        Assert.Equal(1, context.ExportService.AudioRequestCount);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Equal(0, context.ExportService.MuxRequestCount);
        Assert.Equal(0, context.Dialogs.ConversionRequests);
        context.ExportService.CommitObserver = () =>
        {
            var task = Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(), value => value.Name == "Tasks.AudioExport");
            Assert.Equal(AegiTaskState.Committing, task.State);
            Assert.False(task.CanCancel);
        };
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        context.Flush();

        Assert.Equal(1, context.ExportService.BeforeCommitCount);
        Assert.False(model.IsRunning);
        Assert.Equal(1, model.Progress);
        Assert.Equal(Localization.Get("Workbench.Exported"), model.Status);
        Assert.Equal(AegiTaskState.Succeeded, Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(),
            value => value.Name == "Tasks.AudioExport").State);
        Assert.Empty(context.Dialogs.ErrorMessages);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>隐藏的视频草稿不阻止音轨导出，返回烧录后同一错误仍需修正。</summary>
    [AvaloniaFact]
    public async Task HiddenInvalidVideoDraftsPermitAudioExportAndStillBlockBurnIn()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        var snapshot = context.Editor.Snapshot;
        model.QualityText = "invalid quality";
        model.VideoBitrateText = "invalid video bitrate";
        model.AudioMode = 1;
        model.AudioBitrateText = "invalid video audio";
        model.SelectedMethod = ExportMethod.AUDIO;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".mka"));

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.AudioEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        model.SelectedMethod = ExportMethod.BURN_IN;

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Equal("QualityInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.Equal(1, context.Dialogs.SaveRequests);
        Assert.Equal(1, context.ExportService.AudioRequestCount);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Equal("invalid quality", model.QualityText);
        Assert.Equal("invalid video bitrate", model.VideoBitrateText);
        Assert.Equal("invalid video audio", model.AudioBitrateText);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>活动音频码率通过真实输入草稿定位错误，选择输出前完成校验。</summary>
    [AvaloniaFact]
    public async Task InvalidAudioBitrateBlocksThePickerAndPreservesRawText()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        var model = context.Session.ViewModel.Export;
        model.SelectedMethod = ExportMethod.AUDIO;
        model.AudioExport.Format = AudioFileFormat.Mp3;
        context.Flush();
        var input = context.Panels[WorkbenchPanelIds.EXPORT].FindControl<NumericDraftInput>("AudioExportBitrateInput")!;
        input.RawText = "321";

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Equal("AudioExportBitrateInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Equal("321", input.RawText);
        Assert.Equal("321", model.AudioExport.BitrateText);
        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.ExportService.AudioRequestCount);
        Assert.False(model.IsRunning);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>音频进度支持未知比例，取消后拒绝晚到更新并等待服务排空。</summary>
    [AvaloniaFact]
    public async Task AudioCancellationRejectsLateProgressAndWaitsForDrain()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        var model = session.ViewModel.Export;
        model.SelectedMethod = ExportMethod.AUDIO;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".mka"));
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.AudioEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.CanCancel);
        context.ExportService.ReportAudio(new(new(5), null, "encoding"));
        context.Flush();
        Assert.True(model.ProgressVisible);
        Assert.True(model.ProgressIndeterminate);
        context.ExportService.ReportAudio(new(new(8), 0.4, "encoding"));
        context.Flush();
        Assert.False(model.ProgressIndeterminate);
        Assert.Equal(0.4, model.Progress);

        await session.CancelExportAsync();
        await context.ExportService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.ExportService.ReportAudio(new(new(18), 0.9, "encoding"));
        context.Flush();

        Assert.False(operation.IsCompleted);
        Assert.True(model.IsRunning);
        Assert.False(model.CanCancel);
        Assert.Equal(0.4, model.Progress);
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        context.Flush();

        Assert.Equal(1, context.ExportService.CancellationCount);
        Assert.Equal(0, context.ExportService.BeforeCommitCount);
        Assert.False(model.IsRunning);
        Assert.False(model.ProgressVisible);
        Assert.True(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        Assert.Equal(AegiTaskState.Cancelled, Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(),
            value => value.Name == "Tasks.AudioExport").State);
        Assert.Empty(context.Dialogs.ErrorMessages);
        Assert.Null(session.LastError);
    }

    /// <summary>关闭工作台先取消并排空音轨任务，再释放预览及导出服务。</summary>
    [AvaloniaFact]
    public async Task ClosingWaitsForAudioExportBeforeReleasingResources()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        session.ViewModel.Export.SelectedMethod = ExportMethod.AUDIO;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".mka"));
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.AudioEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var close = session.RequestCloseAsync(context.Layouts.FlushAsync);
        await context.ExportService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(close.IsCompleted);
        Assert.False(operation.IsCompleted);
        Assert.Equal(0, context.SourceDisposeCount);
        Assert.Equal(0, context.ExportService.DisposeCount);
        context.ExportService.Release();
        Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5)));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, context.SourceDisposeCount);
        Assert.Equal(1, context.ExportService.DisposeCount);
        Assert.Equal(0, context.ExportService.BeforeCommitCount);
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    /// <summary>失败仅提示一次且恢复导出命令，晚到进度不能覆盖失败状态。</summary>
    [AvaloniaFact]
    public async Task AudioFailureRestoresCommandsAndKeepsItsFinalStatus()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        var model = session.ViewModel.Export;
        model.SelectedMethod = ExportMethod.AUDIO;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".mka"));
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.AudioEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var error = new InvalidOperationException("Cannot preserve the selected audio 中文");

        context.ExportService.Fail(error);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        context.Flush();

        Assert.Equal(error.Message, Assert.Single(context.Dialogs.ErrorMessages));
        Assert.Same(error, session.LastError);
        Assert.False(model.IsRunning);
        Assert.False(model.ProgressVisible);
        Assert.True(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        Assert.Equal(0, context.ExportService.BeforeCommitCount);
        var status = model.Status;
        context.ExportService.ReportAudio(new(new(19), 0.95, "encoding"));
        context.Flush();
        Assert.Equal(status, model.Status);
        Assert.Contains(error.Message, status, StringComparison.Ordinal);
        Assert.Equal(AegiTaskState.Failed, Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(),
            value => value.Name == "Tasks.AudioExport").State);
    }
}
