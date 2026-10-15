using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Export;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证压制失败反馈、默认输出及会话关闭边界。</summary>
public sealed class ExportWorkflowFeedbackUiTests
{
    /// <summary>编码失败只提示和记录一次，结束进度并允许重试。</summary>
    [AvaloniaFact]
    public async Task EncodingFailureShowsOneDialogAndLogAndRestoresCommands()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Dialogs.ResolveOutput(context.OutputPath);
        var session = context.Session;
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var error = new InvalidOperationException("Encoder unavailable 中文");

        context.ExportService.Fail(error);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        context.Flush();

        Assert.Equal(error.Message, Assert.Single(context.Dialogs.ErrorMessages));
        var entry = Assert.Single(session.Journal.Entries, value => value.Summary.Contains(error.Message, StringComparison.Ordinal));
        Assert.Contains(error.ToString(), entry.Details, StringComparison.Ordinal);
        Assert.Same(error, session.LastError);
        Assert.False(session.ViewModel.Export.IsRunning);
        Assert.False(session.ViewModel.Export.ProgressVisible);
        Assert.Contains(error.Message, session.ViewModel.Export.Status, StringComparison.Ordinal);
        Assert.True(session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        Localization.SetLanguage("zh-CN");
        Assert.StartsWith("压制失败：", session.ViewModel.Export.Status, StringComparison.Ordinal);
        Assert.Contains(error.Message, session.ViewModel.Export.Status, StringComparison.Ordinal);
        var failedStatus = session.ViewModel.Export.Status;
        context.ExportService.Report(new(99, new(19), 0.95, "encoding", "late encoder"));
        context.Flush();
        Assert.Equal(failedStatus, session.ViewModel.Export.Status);
    }

    /// <summary>输入校验失败也提示原因；修正输入后可成功压制。</summary>
    [AvaloniaFact]
    public async Task InvalidInputShowsErrorAndCorrectedInputCanExport()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var session = context.Session;
        session.ViewModel.Export.QualityText = "invalid";

        await session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.ExportService.RequestCount);
        Assert.Equal("QualityInput", session.ViewModel.InvalidFieldKey);
        Assert.False(session.ViewModel.Export.ProgressVisible);
        session.ViewModel.Export.QualityText = "20";
        context.Dialogs.ResolveOutput(context.OutputPath);
        var operation = session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.Equal(Localization.Get("Workbench.Exported"), session.ViewModel.Export.Status);
        Assert.Null(session.LastError);
    }

    /// <summary>选择输出失败也提示一次，并恢复开始压制的可用状态。</summary>
    [AvaloniaFact]
    public async Task PickerFailureShowsErrorAndClearsChoosingState()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var error = new IOException("Cannot choose output");
        context.Dialogs.PickerError = error;

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Equal(error.Message, Assert.Single(context.Dialogs.ErrorMessages));
        Assert.False(context.Session.Export.IsChoosingOutput);
        Assert.False(context.Session.ViewModel.Export.ProgressVisible);
        Assert.True(context.Session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        Assert.Equal(0, context.ExportService.RequestCount);
    }

    /// <summary>任务资源准备失败仍通过统一错误出口提示且不启动编码。</summary>
    [AvaloniaFact]
    public async Task TaskSubmissionFailureShowsErrorWithoutStartingEncoding()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Dialogs.ResolveOutput("\0");

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.IsType<ArgumentException>(context.Session.LastError);
        Assert.False(context.Session.ViewModel.Export.IsRunning);
        Assert.False(context.Session.ViewModel.Export.ProgressVisible);
        Assert.True(context.Session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO).CanExecute(null));
        Assert.Equal(0, context.ExportService.RequestCount);
    }

    /// <summary>保存项目的压制默认创建输出目录并建议带时间戳的文件名。</summary>
    [AvaloniaFact]
    public async Task SavedProjectSuggestsOutputDirectoryAndTimestampedProjectName()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var directory = Path.GetDirectoryName(context.OutputPath)!;
        context.Session.SetProjectLocation(Path.Combine(directory, "项目.aeginext"), directory);
        context.Dialogs.ResolveOutput(null);

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Matches(@"^项目-\d{8}-\d{6}\.mp4$", context.Dialogs.SuggestedFileName);
        Assert.True(Directory.Exists(Path.Combine(directory, "output")));
        Assert.Equal(Path.Combine(directory, "output"), context.Dialogs.SuggestedDirectory);
        Assert.Empty(context.Dialogs.ErrorMessages);
        Assert.Equal(0, context.ExportService.RequestCount);
    }

    /// <summary>无法创建默认输出目录时提示失败，不打开选择器或启动编码。</summary>
    [AvaloniaFact]
    public async Task OutputDirectoryCreationFailureIsReportedBeforeOpeningPicker()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var directory = Path.GetDirectoryName(context.OutputPath)!;
        context.Session.SetProjectLocation(Path.Combine(directory, "项目.aeginext"), directory);
        var blockingPath = Path.Combine(directory, "output");
        await File.WriteAllTextAsync(blockingPath, "Keep this file");

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Single(context.Dialogs.ErrorMessages);
        Assert.IsAssignableFrom<IOException>(context.Session.LastError);
        Assert.Equal("Keep this file", await File.ReadAllTextAsync(blockingPath));
        Assert.Equal(0, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.ExportService.RequestCount);
    }

    /// <summary>未保存工程不将成片建议到关闭时会被删除的临时工程目录。</summary>
    [AvaloniaFact]
    public async Task UnsavedProjectDoesNotSuggestScratchOutputDirectory()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Dialogs.ResolveOutput(null);

        await context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);

        Assert.Null(context.Dialogs.SuggestedDirectory);
        Assert.False(Directory.Exists(Path.Combine(context.Session.ScratchDirectory, "output")));
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    /// <summary>面板选择 MKV 后保存到用户确认的目录和路径。</summary>
    [AvaloniaFact]
    public async Task ExplicitMkvOutputOverridesDefaultSuggestion()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        var directory = Path.GetDirectoryName(context.OutputPath)!;
        context.Session.SetProjectLocation(Path.Combine(directory, "项目.aeginext"), directory);
        context.Session.ViewModel.Export.SelectedContainer = VideoContainerFormat.MKV;
        var selected = Path.ChangeExtension(context.OutputPath, ".mkv");
        context.Dialogs.ResolveOutput(selected);

        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        var request = await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(selected, request.OutputPath);
        Assert.Equal(Path.Combine(directory, "output"), context.Dialogs.SuggestedDirectory);
        context.ExportService.Release();
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(context.Dialogs.ErrorMessages);
    }

    /// <summary>取消选择和主动取消编码均不显示失败弹窗。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDoesNotShowError(bool afterStarting)
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Dialogs.ResolveOutput(afterStarting ? context.OutputPath : null);
        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        if (afterStarting)
        {
            await context.ExportService.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await context.Session.CancelExportAsync();
            await context.ExportService.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.ExportService.Release();
        }
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(context.Dialogs.ErrorMessages);
        Assert.Null(context.Session.LastError);
    }

    /// <summary>关闭工程会取消尚待确认的错误窗口，避免阻塞资源释放。</summary>
    [AvaloniaFact]
    public async Task ClosingCancelsPendingErrorDialogAndDrainsExport()
    {
        await using var context = new ExportWorkspaceTestContext();
        await context.InitializeAsync();
        context.Dialogs.PendingErrorAcknowledgement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Dialogs.PickerError = new IOException("Picker failed");
        var operation = context.Session.ViewModel.ExecuteCommandAsync(WorkbenchCommand.EXPORT_VIDEO);
        await context.Dialogs.ErrorShown.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(operation.IsCompleted);
        Assert.True(await context.Session.RequestCloseAsync(context.Layouts.FlushAsync).WaitAsync(TimeSpan.FromSeconds(5)));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, context.ExportService.DisposeCount);
    }
}
