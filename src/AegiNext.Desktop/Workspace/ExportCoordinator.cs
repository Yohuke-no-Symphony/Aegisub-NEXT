using System.Globalization;
using AegiNext.Desktop.I18n;
using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Export;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs,
    IWorkbenchExportService exportService) : IDisposable
{
    private AegiTaskHandle<VideoExportResult>? taskHandle;
    private bool activeOperation;
    private bool disposed;
    private long revision;
    private string? statusKey;
    private string? statusEncoder;
    private string? failureReason;
    internal bool IsChoosingOutput { get; private set; }
    internal bool IsRunning => session.ViewModel.Export.IsRunning;
    internal bool CanStart => !disposed && !activeOperation;
    internal bool CanCancel => taskHandle?.Snapshot is { CanCancel: true, State: AegiTaskState.Queued or AegiTaskState.Running };
    public Task Completion { get; private set; } = Task.CompletedTask;
    internal void Cancel() => taskHandle?.RequestCancel();

    internal void RefreshLanguage()
    {
        if (statusKey is not null)
        {
            session.ViewModel.Export.Status = failureReason is not null
                ? Localization.Format("Workbench.ExportFailureStatus", failureReason)
                : Localization.Get(statusKey) +
                    (string.IsNullOrWhiteSpace(statusEncoder) ? string.Empty : " · " + statusEncoder);
        }
    }

    internal Task EncodeAsync()
    {
        if (!CanStart || session.IsProjectBusy || session.IsClosing)
        {
            return Task.CompletedTask;
        }

        activeOperation = true;
        revision++;
        session.ViewModel.RefreshCommands();
        Completion = ChooseAndRunAsync();
        return Completion;
    }

    private async Task ChooseAndRunAsync()
    {
        try
        {
            if (!session.TryCommitDrafts())
            {
                var draftError = session.LastError ?? new InvalidDataException(session.ViewModel.Error ??
                    Localization.Get("Workbench.ExportInvalidDrafts"));
                await ShowFailureAsync(draftError, session.LastError is null);
                return;
            }
            var vm = session.ViewModel.Export;
            var request = new VideoExportRequest(session.Editor.Snapshot, session.ProjectDirectory, string.Empty)
                .WithSettings(vm.CaptureSettings());
            var duration = session.Controller.Snapshot.Duration;
            var outputDirectory = session.ProjectPath is null ? null : Path.Combine(request.ProjectDirectory, "output");
            if (outputDirectory is not null)
            {
                Directory.CreateDirectory(outputDirectory);
            }
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var name = string.Concat(session.ProjectDisplayName.Select(character =>
                Array.IndexOf(invalidCharacters, character) >= 0 ? '_' : character));
            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            IsChoosingOutput = true;
            session.ViewModel.RefreshCommands();
            var path = await dialogs.SaveFileAsync("Export", "Videos", ["*.mp4", "*.mkv"], ".mp4",
                $"{name}-{timestamp}.mp4", outputDirectory);
            if (path is null || session.IsClosing)
            {
                return;
            }

            IsChoosingOutput = false;
            vm.IsRunning = true;
            vm.ProgressVisible = true;
            vm.ProgressIndeterminate = true;
            statusKey = "Tasks.Queued";
            statusEncoder = null;
            failureReason = null;
            RefreshLanguage();
            taskHandle = session.ApplicationContext.Tasks.Submit(new VideoExportTask(session, this, request with { OutputPath = path }, duration));
            await taskHandle.Completion;
        }
        catch (OperationCanceledException)
        {
            if (!session.IsClosing)
            {
                statusKey = "Workbench.Cancelled";
                statusEncoder = null;
                failureReason = null;
                RefreshLanguage();
                session.LogInfo("Export", session.ViewModel.Export.Status);
                session.ViewModel.Export.ProgressVisible = false;
            }
        }
        catch (Exception error)
        {
            await ShowFailureAsync(error);
        }
        finally
        {
            IsChoosingOutput = false;
            activeOperation = false;
            session.ViewModel.Export.IsRunning = false;
            session.ViewModel.RefreshCommands();
        }
    }

    private async Task ShowFailureAsync(Exception error, bool recordLog = true)
    {
        if (session.IsClosing)
        {
            return;
        }
        session.ShowError(error, recordLog);
        statusKey = "Workbench.ExportFailed";
        statusEncoder = null;
        failureReason = string.IsNullOrWhiteSpace(error.Message)
            ? error.GetType().Name : error.Message;
        var vm = session.ViewModel.Export;
        vm.IsRunning = false;
        vm.ProgressVisible = false;
        vm.ProgressIndeterminate = false;
        RefreshLanguage();
        try
        {
            await dialogs.ShowErrorAsync(statusKey, failureReason, session.ProjectOperationsToken);
        }
        catch (OperationCanceledException) when (session.ProjectOperationsToken.IsCancellationRequested)
        {
        }
    }

    internal async Task<VideoExportResult> RunAsync(VideoExportRequest request, AegiTaskExecutionContext context, MediaTime? duration)
    {
        var token = context.CancellationToken;
        return await RunExportCoreAsync(request, context, duration, token);
    }

    private async Task<VideoExportResult> RunExportCoreAsync(VideoExportRequest request, AegiTaskExecutionContext context, MediaTime? duration, CancellationToken token)
    {
        var vm = session.ViewModel.Export;
        vm.IsRunning = true;
        vm.ProgressVisible = true;
        vm.ProgressIndeterminate = true;
        statusKey = null;
        statusEncoder = null;
        failureReason = null;
        session.LogInfo("Export", Localization.Get("Workbench.Export"));
        var exportRevision = revision;
        string? reportedEncoder = null;
        var progress = new Progress<VideoExportProgress>(value =>
        {
            if (session.IsClosing || token.IsCancellationRequested || !vm.IsRunning || exportRevision != revision)
            {
                return;
            }

            var fraction = value.Fraction ?? (duration is { } end && WorkbenchSession.ToSeconds(end) > 0
                ? WorkbenchSession.ToSeconds(value.Position) / WorkbenchSession.ToSeconds(end) : (double?)null);
            context.ReportProgress(new("Tasks.Encoding", fraction, 1));
            vm.ProgressIndeterminate = fraction is null;
            vm.Progress = fraction is { } known ? Math.Clamp(known, 0, 1) : 0;
            statusKey = null;
            statusEncoder = null;
            vm.Status = $"{WorkbenchSession.FormatTime(value.Position)} · {value.FrameCount}";
            if (!string.IsNullOrWhiteSpace(value.Encoder))
            {
                vm.Status += " · " + value.Encoder;
                if (reportedEncoder != value.Encoder)
                {
                    reportedEncoder = value.Encoder;
                    session.LogInfo("Export", Localization.Get("Workbench.Codec") + ": " + value.Encoder);
                }
            }
        });
        try
        {
            var result = await exportService.ExportWithCommitAsync(request, progress, () => context.EnterCommit(), token);
            if (!session.IsClosing)
            {
                vm.ProgressIndeterminate = false;
                vm.Progress = 1;
                statusKey = "Workbench.Exported";
                statusEncoder = result.Encoder;
                RefreshLanguage();
                session.LogInfo("Export", vm.Status);
            }
            return result;
        }
        finally
        {
            vm.IsRunning = false;
            session.ViewModel.RefreshCommands();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        exportService.Dispose();
    }
}
