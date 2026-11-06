using AegiNext.Desktop.Workspace;
using AegiNext.Export;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class ControlledWorkbenchExportService : IWorkbenchExportService
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IProgress<VideoExportProgress>? videoProgress;
    private IProgress<SubtitleMuxProgress>? muxProgress;
    private IProgress<AudioExportProgress>? audioProgress;
    internal TaskCompletionSource<VideoExportRequest> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<SubtitleMuxRequest> MuxEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<AudioExportRequest> AudioEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int RequestCount { get; private set; }
    internal int MuxRequestCount { get; private set; }
    internal int AudioRequestCount { get; private set; }
    internal int BeforeCommitCount { get; private set; }
    internal int CancellationCount { get; private set; }
    internal int DisposeCount { get; private set; }
    internal Action? CommitObserver { get; set; }

    internal void Release() => release.TrySetResult();
    internal void Fail(Exception error) => release.TrySetException(error);
    internal void Report(VideoExportProgress value) => videoProgress!.Report(value);
    internal void ReportMux(SubtitleMuxProgress value)
    {
        muxProgress!.Report(value);
    }

    internal void ReportAudio(AudioExportProgress value)
    {
        audioProgress!.Report(value);
    }

    /// <summary>保留不可变请求并模拟必须等待资源排空的可取消导出边界。</summary>
    public async Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        videoProgress = progress;
        using var registration = cancellationToken.Register(() =>
        {
            CancellationCount++;
            CancellationObserved.TrySetResult();
        });
        Entered.TrySetResult(request);
        await release.Task;
        cancellationToken.ThrowIfCancellationRequested();
        return new(request.OutputPath, 30);
    }

    /// <summary>模拟独立封装请求、取消排空与应用提交边界。</summary>
    public async Task<SubtitleMuxResult> SubtitleMuxWithCommitAsync(SubtitleMuxRequest request,
        IProgress<SubtitleMuxProgress> progress, Action beforeCommit, CancellationToken cancellationToken)
    {
        MuxRequestCount++;
        muxProgress = progress;
        using var registration = cancellationToken.Register(() =>
        {
            CancellationCount++;
            CancellationObserved.TrySetResult();
        });
        MuxEntered.TrySetResult(request);
        await release.Task;
        cancellationToken.ThrowIfCancellationRequested();
        beforeCommit();
        BeforeCommitCount++;
        CommitObserver?.Invoke();
        return new(request.OutputPath, 1, 0, 0, 1,
            SubtitleMuxCapabilities.GetSubtitleCodec(Path.GetExtension(request.OutputPath)));
    }

    /// <summary>模拟音轨导出的冻结请求、取消排空与提交边界。</summary>
    public async Task<AudioExportResult> AudioExportWithCommitAsync(AudioExportRequest request,
        IProgress<AudioExportProgress> progress, Action beforeCommit, CancellationToken cancellationToken)
    {
        AudioRequestCount++;
        audioProgress = progress;
        using var registration = cancellationToken.Register(() =>
        {
            CancellationCount++;
            CancellationObserved.TrySetResult();
        });
        AudioEntered.TrySetResult(request);
        await release.Task;
        cancellationToken.ThrowIfCancellationRequested();
        beforeCommit();
        BeforeCommitCount++;
        CommitObserver?.Invoke();
        return new(request.OutputPath, request.Project.Media!.AudioStreamIndex ?? 1, "controlled-audio", 48000, 1);
    }

    /// <inheritdoc />
    public void Dispose() => DisposeCount++;
}
