using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Export;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证独立内挂字幕方式、草稿保留和工作台任务生命周期。</summary>
public sealed class SubtitleMuxUiTests
{
    /// <summary>方式选择隐藏烧录参数，语言和布局刷新保留选择身份及原始草稿。</summary>
    [AvaloniaFact]
    public async Task MethodSelectionHidesEncodingControlsAndPreservesDraftsAcrossLanguageAndLayout()
    {
        await using var context = new ExportWorkspaceTestContext(new() { Text = "字幕" });
        await context.InitializeAsync();
        context.Layouts.Activate(WorkbenchPanelIds.EXPORT);
        context.Flush();
        var view = context.Panels[WorkbenchPanelIds.EXPORT];
        var model = context.Session.ViewModel.Export;
        var selector = view.FindControl<ComboBox>("ExportMethodCombo")!;
        var encoding = view.FindControl<Control>("BurnInSettings")!;
        var hint = view.FindControl<TextBlock>("SubtitleMuxHint")!;
        var choice = model.Methods.Single(value => value.Method == ExportMethod.SUBTITLE_MUX);
        var snapshot = context.Editor.Snapshot;
        view.FindControl<NumericDraftInput>("QualityInput")!.RawText = "unfinished quality";
        view.FindControl<NumericDraftInput>("VideoBitrateInput")!.RawText = "unfinished bitrate";
        view.FindControl<NumericDraftInput>("AudioBitrateInput")!.RawText = "unfinished audio";
        Assert.True(encoding.IsEffectivelyVisible);
        Assert.False(hint.IsEffectivelyVisible);

        selector.SelectedItem = choice;
        context.Flush();

        Assert.Equal(ExportMethod.SUBTITLE_MUX, model.SelectedMethod);
        Assert.False(encoding.IsEffectivelyVisible);
        Assert.True(hint.IsEffectivelyVisible);
        var englishLabel = choice.Label;
        Localization.SetLanguage("zh-CN");
        context.Flush();

        Assert.Same(choice, selector.SelectedItem);
        Assert.Same(choice, model.SelectedMethodChoice);
        Assert.NotEqual(englishLabel, choice.Label);
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
        Assert.True(await context.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.ENCODE));
        context.Flush();
        Assert.Equal(ExportMethod.SUBTITLE_MUX, model.SelectedMethod);
        selector.SelectedItem = model.Methods.Single(value => value.Method == ExportMethod.BURN_IN);
        context.Flush();

        Assert.True(encoding.IsEffectivelyVisible);
        Assert.False(hint.IsEffectivelyVisible);
        Assert.Equal("unfinished quality", model.QualityText);
        Assert.Equal("unfinished bitrate", model.VideoBitrateText);
        Assert.Equal("unfinished audio", model.AudioBitrateText);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>隐藏非法烧录草稿不阻止封装，返回烧录后原输入仍需修正。</summary>
    [AvaloniaFact]
    public async Task HiddenInvalidEncodingDraftsPermitMuxButStillBlockBurnIn()
    {
        await using var context = new ExportWorkspaceTestContext(new() { Text = "字幕" });
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Export;
        var snapshot = context.Editor.Snapshot;
        model.QualityText = "invalid quality";
        model.VideoBitrateText = "invalid bitrate";
        model.AudioMode = 1;
        model.AudioBitrateText = "invalid audio";
        model.SelectedMethod = ExportMethod.SUBTITLE_MUX;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".mkv"));

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.MuxEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(snapshot, request.Project);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Equal(1, context.ExportService.MuxRequestCount);
        Assert.Empty(context.Dialogs.ErrorMessages);
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        model.SelectedMethod = ExportMethod.BURN_IN;

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Equal("QualityInput", context.Session.ViewModel.InvalidFieldKey);
        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.Equal(1, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Equal("invalid quality", model.QualityText);
        Assert.Equal("invalid bitrate", model.VideoBitrateText);
        Assert.Equal("invalid audio", model.AudioBitrateText);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>仅提供两个封装容器且默认 MKV，文件选择期间的方式变化不替换冻结请求。</summary>
    [AvaloniaTheory]
    [InlineData(".mkv")]
    [InlineData(".mp4")]
    public async Task MuxPickerUsesSupportedContainersAndFreezesMethodBeforeSelection(string extension)
    {
        await using var context = new ExportWorkspaceTestContext(new() { Text = "字幕" });
        await context.InitializeAsync();
        var session = context.Session;
        session.ViewModel.Export.SelectedMethod = ExportMethod.SUBTITLE_MUX;
        var snapshot = context.Editor.Snapshot;

        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.Dialogs.SaveShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(session.Export.IsChoosingOutput);
        Assert.False(session.ViewModel.Export.CanSelectMethod);
        Assert.Equal<string>(["*.mkv", "*.mp4"], context.Dialogs.Patterns);
        Assert.Equal(".mkv", context.Dialogs.DefaultExtension);
        Assert.EndsWith(".mkv", context.Dialogs.SuggestedFileName, StringComparison.Ordinal);
        session.ViewModel.Export.SelectedMethod = ExportMethod.BURN_IN;
        Assert.Equal(ExportMethod.SUBTITLE_MUX, session.ViewModel.Export.SelectedMethod);
        var output = Path.ChangeExtension(context.OutputPath, extension);
        context.Dialogs.ResolveOutput(output);
        var request = await context.ExportService.MuxEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(output, request.OutputPath);
        Assert.Same(snapshot, request.Project);
        Assert.Equal(session.ProjectDirectory, request.ProjectDirectory);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Equal(1, context.ExportService.MuxRequestCount);
        Assert.Equal(0, context.Dialogs.ConversionRequests);
        context.ExportService.CommitObserver = () =>
        {
            var task = Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(),
                value => value.Name == "Tasks.SubtitleMuxExport");
            Assert.Equal(AegiTaskState.Committing, task.State);
            Assert.False(task.CanCancel);
        };
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        context.Flush();

        Assert.Equal(1, context.ExportService.BeforeCommitCount);
        Assert.False(session.ViewModel.Export.IsRunning);
        Assert.True(session.ViewModel.Export.CanSelectMethod);
        Assert.Equal(1, session.ViewModel.Export.Progress);
        Assert.Equal(Localization.Get("Workbench.Exported"), session.ViewModel.Export.Status);
        Assert.Equal(AegiTaskState.Succeeded, Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(),
            value => value.Name == "Tasks.SubtitleMuxExport").State);
        Assert.Empty(context.Dialogs.ErrorMessages);
        Assert.False(context.Editor.CanUndo);
    }

    /// <summary>封装进度支持未知比例，取消后拒绝晚进度并等待服务排空。</summary>
    [AvaloniaFact]
    public async Task MuxProgressAndCancellationDrainWithoutCommittingOrReportingFailure()
    {
        await using var context = new ExportWorkspaceTestContext(new() { Text = "字幕" });
        await context.InitializeAsync();
        var session = context.Session;
        var model = session.ViewModel.Export;
        model.SelectedMethod = ExportMethod.SUBTITLE_MUX;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".mkv"));
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.MuxEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(model.IsRunning);
        Assert.True(model.CanCancel);
        Assert.False(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        context.ExportService.ReportMux(new(new(5), null, "muxing"));
        context.Flush();
        Assert.True(model.ProgressVisible);
        Assert.True(model.ProgressIndeterminate);
        Assert.Equal(0, model.Progress);
        context.ExportService.ReportMux(new(new(8), 0.4, "muxing"));
        context.Flush();
        Assert.False(model.ProgressIndeterminate);
        Assert.Equal(0.4, model.Progress);

        await session.CancelExportAsync();
        await context.ExportService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.ExportService.ReportMux(new(new(18), 0.9, "muxing"));
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
            value => value.Name == "Tasks.SubtitleMuxExport").State);
        Assert.Empty(context.Dialogs.ErrorMessages);
        Assert.Null(session.LastError);
    }

    /// <summary>关闭时取消并排空封装任务之后才释放预览和服务。</summary>
    [AvaloniaFact]
    public async Task ClosingCancelsMuxAndWaitsBeforeDisposingResources()
    {
        await using var context = new ExportWorkspaceTestContext(new() { Text = "字幕" });
        await context.InitializeAsync();
        var session = context.Session;
        session.ViewModel.Export.SelectedMethod = ExportMethod.SUBTITLE_MUX;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".mkv"));
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.MuxEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var close = session.RequestCloseAsync(context.Layouts.FlushAsync);
        await context.ExportService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(close.IsCompleted);
        Assert.False(operation.IsCompleted);
        Assert.Equal(0, context.SourceDisposeCount);
        Assert.Equal(0, context.ExportService.DisposeCount);
        context.ExportService.Release();
        Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5)));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync();

        Assert.Equal(1, context.SourceDisposeCount);
        Assert.Equal(1, context.ExportService.DisposeCount);
        Assert.Equal(0, context.ExportService.BeforeCommitCount);
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    /// <summary>关闭取消尚未回答的转换确认，并在启动选择器或封装服务之前完成释放。</summary>
    [AvaloniaFact]
    public async Task ClosingCancelsPendingConversionReviewWithoutStartingMux()
    {
        await using var context = new ExportWorkspaceTestContext(new() { Text = "Styled 字幕", Style = new() { Bold = true } });
        await context.InitializeAsync();
        var session = context.Session;
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Dialogs.PendingConversion = confirmation;
        session.ViewModel.Export.SelectedMethod = ExportMethod.SUBTITLE_MUX;
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        try
        {
            await context.Dialogs.ConversionShown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(operation.IsCompleted);
            Assert.False(session.ViewModel.Export.CanSelectMethod);

            var close = session.RequestCloseAsync(context.Layouts.FlushAsync);
            await context.Dialogs.ConversionCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5)));
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(confirmation.Task.IsCompleted);
            Assert.Equal(1, context.Dialogs.ConversionRequests);
            Assert.Equal(0, context.Dialogs.SaveRequests);
            Assert.Equal(0, context.ExportService.MuxRequestCount);
            Assert.Equal(0, context.ExportService.RequestCount);
            Assert.Equal(0, context.ExportService.BeforeCommitCount);
            Assert.Equal(1, context.SourceDisposeCount);
            Assert.Equal(1, context.ExportService.DisposeCount);
            Assert.Empty(context.Dialogs.ErrorMessages);
            Assert.Null(session.LastError);
        }
        finally
        {
            confirmation.TrySetResult(false);
        }
    }

    /// <summary>纯文本外观损失明确接受后才进入封装，取消不修改工程或目标。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppearanceLossReviewMustBeAcceptedBeforeMuxStarts(bool accept)
    {
        var subtitle = new SubtitleLine { Text = "Styled 字幕", Style = new() { Bold = true } };
        await using var context = new ExportWorkspaceTestContext(subtitle);
        await context.InitializeAsync();
        var snapshot = context.Editor.Snapshot;
        var output = Path.ChangeExtension(context.OutputPath, ".mkv");
        context.Session.ViewModel.Export.SelectedMethod = ExportMethod.SUBTITLE_MUX;
        context.Dialogs.ConversionChoice = accept;
        context.Dialogs.ResolveOutput(output);
        if (!accept)
        {
            await File.WriteAllTextAsync(output, "Existing destination");
        }

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        if (accept)
        {
            await context.ExportService.MuxEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.ExportService.Release();
        }
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, context.Dialogs.ConversionRequests);
        var review = Assert.IsType<SubtitleConversionReview>(context.Dialogs.ConversionReview);
        Assert.Contains(review.Diagnostics, value => value.Code == "Srt.Appearance" && value.SubtitleId == subtitle.Id);
        Assert.Equal(subtitle.Id, Assert.Single(review.Subtitles).Id);
        Assert.Equal(accept ? 1 : 0, context.ExportService.MuxRequestCount);
        Assert.Equal(0, context.ExportService.RequestCount);
        if (!accept)
        {
            Assert.Equal("Existing destination", await File.ReadAllTextAsync(output));
        }
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    /// <summary>封装失败只提示一次并恢复命令，晚到进度不会覆盖失败状态。</summary>
    [AvaloniaFact]
    public async Task MuxFailureRestoresCommandsAndRejectsLateProgress()
    {
        await using var context = new ExportWorkspaceTestContext(new() { Text = "字幕" });
        await context.InitializeAsync();
        var session = context.Session;
        session.ViewModel.Export.SelectedMethod = ExportMethod.SUBTITLE_MUX;
        context.Dialogs.ResolveOutput(Path.ChangeExtension(context.OutputPath, ".mp4"));
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.MuxEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var error = new InvalidOperationException("Source subtitle codec cannot be copied 中文");

        context.ExportService.Fail(error);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        context.Flush();

        Assert.Equal(error.Message, Assert.Single(context.Dialogs.ErrorMessages));
        Assert.Same(error, session.LastError);
        Assert.False(session.ViewModel.Export.IsRunning);
        Assert.False(session.ViewModel.Export.ProgressVisible);
        Assert.True(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        Assert.Equal(0, context.ExportService.BeforeCommitCount);
        var failedStatus = session.ViewModel.Export.Status;
        context.ExportService.ReportMux(new(new(19), 0.95, "muxing"));
        context.Flush();
        Assert.Equal(failedStatus, session.ViewModel.Export.Status);
        Assert.Contains(error.Message, failedStatus, StringComparison.Ordinal);
        Assert.Equal(AegiTaskState.Failed, Assert.Single(session.ApplicationContext.Tasks.GetSnapshots(),
            value => value.Name == "Tasks.SubtitleMuxExport").State);
    }
}
