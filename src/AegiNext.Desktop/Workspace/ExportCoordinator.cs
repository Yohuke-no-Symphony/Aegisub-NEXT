using System.Globalization;
using AegiNext.Desktop.I18n;
using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Export;
using AegiNext.SubtitleFormats;

namespace AegiNext.Desktop.Workspace;

internal sealed class ExportCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs,
    IWorkbenchExportService exportService) : IDisposable
{
    private AegiTaskHandle? taskHandle;
    private bool activeOperation;
    private bool disposed;
    private long revision;
    private string? statusKey;
    private string? statusEncoder;
    private string? failureReason;
    private MediaTime? statusPosition;
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
                    (string.IsNullOrWhiteSpace(statusEncoder) ? string.Empty : " · " + statusEncoder) +
                    (statusPosition is { } position ? " · " + WorkbenchSession.FormatTime(position) : string.Empty);
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
        session.ViewModel.Export.RefreshTaskState();
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
            var isSubtitleMux = vm.IsSubtitleMux;
            var document = session.Editor.Snapshot;
            var projectDirectory = session.ProjectDirectory;
            var settings = isSubtitleMux ? null : vm.CaptureSettings();
            if (isSubtitleMux)
            {
                var diagnostics = await Task.Run(() => SubtitleFormatLossAnalysis.ForSrt(document), session.ProjectOperationsToken);
                if (!diagnostics.IsEmpty && !await dialogs.ConfirmSubtitleConversionAsync(
                        new(diagnostics, document.Subtitles, document.Layers), session.ProjectOperationsToken))
                {
                    throw new OperationCanceledException("Subtitle conversion declined.");
                }
                session.ProjectOperationsToken.ThrowIfCancellationRequested();
                if (session.IsClosing)
                {
                    return;
                }
            }
            var duration = session.Controller.Snapshot.Duration;
            var outputDirectory = session.ProjectPath is null ? null : Path.Combine(projectDirectory, "output");
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
            var extensions = isSubtitleMux ? SubtitleMuxCapabilities.Extensions : VideoExportCapabilities.Get(settings!.Codec).Extensions;
            var extension = isSubtitleMux || settings!.Codec is VideoCodec.Vp8 or VideoCodec.Vp9 or VideoCodec.Av1 ? ".mkv" : ".mp4";
            var patterns = extensions.Select(value => "*" + value).ToArray();
            var path = await dialogs.SaveFileAsync("Export", "Videos", patterns, extension,
                $"{name}-{timestamp}{extension}", outputDirectory);
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
            statusPosition = null;
            RefreshLanguage();
            if (isSubtitleMux)
            {
                var request = new SubtitleMuxRequest(document, projectDirectory, path);
                var submitted = session.ApplicationContext.Tasks.Submit(new SubtitleMuxTask(session, this, request));
                taskHandle = submitted;
                await submitted.Completion;
            }
            else
            {
                var request = new VideoExportRequest(document, projectDirectory, path).WithSettings(settings!);
                var submitted = session.ApplicationContext.Tasks.Submit(new VideoExportTask(session, this, request, duration));
                taskHandle = submitted;
                await submitted.Completion;
            }
        }
        catch (OperationCanceledException)
        {
            if (!session.IsClosing)
            {
                statusKey = "Workbench.Cancelled";
                statusEncoder = null;
                failureReason = null;
                statusPosition = null;
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
            session.ViewModel.Export.RefreshTaskState();
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
        statusPosition = null;
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
        statusPosition = null;
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

    internal async Task<SubtitleMuxResult> RunSubtitleMuxAsync(SubtitleMuxRequest request, AegiTaskExecutionContext context)
    {
        var token = context.CancellationToken;
        var vm = session.ViewModel.Export;
        vm.IsRunning = true;
        vm.ProgressVisible = true;
        vm.ProgressIndeterminate = true;
        statusKey = null;
        statusEncoder = null;
        failureReason = null;
        statusPosition = null;
        session.LogInfo("Export", Localization.Get("Workbench.ExportSubtitleMux"));
        var exportRevision = revision;
        var progress = new Progress<SubtitleMuxProgress>(value =>
        {
            if (session.IsClosing || token.IsCancellationRequested || !vm.IsRunning || exportRevision != revision)
            {
                return;
            }

            statusKey = value.Stage switch
            {
                "preflight" => "Tasks.MuxPreparing",
                "verifying" => "Tasks.MuxVerifying",
                _ => "Tasks.Muxing"
            };
            context.ReportProgress(new(statusKey, value.Fraction, 1));
            vm.ProgressIndeterminate = value.Fraction is null;
            vm.Progress = value.Fraction is { } known ? Math.Clamp(known, 0, 1) : 0;
            statusPosition = value.Position;
            RefreshLanguage();
        });
        try
        {
            var result = await exportService.SubtitleMuxWithCommitAsync(request, progress, () => context.EnterCommit(), token);
            if (!session.IsClosing)
            {
                vm.ProgressIndeterminate = false;
                vm.Progress = 1;
                statusKey = "Workbench.Exported";
                statusEncoder = null;
                statusPosition = null;
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
