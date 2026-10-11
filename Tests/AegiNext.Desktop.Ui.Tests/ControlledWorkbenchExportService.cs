using AegiNext.Desktop.Workspace;
using AegiNext.Export;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class ControlledWorkbenchExportService : IWorkbenchExportService
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IProgress<VideoExportProgress>? progress;
    internal TaskCompletionSource<VideoExportRequest> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int RequestCount { get; private set; }
    internal int CancellationCount { get; private set; }
    internal int DisposeCount { get; private set; }

    internal void Release() => release.TrySetResult();
    internal void Fail(Exception error) => release.TrySetException(error);
    internal void Report(VideoExportProgress value) => progress!.Report(value);

    /// <summary>保留不可变请求并模拟必须等待资源排空的可取消导出边界。</summary>
    public async Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        this.progress = progress;
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

    /// <inheritdoc />
    public void Dispose() => DisposeCount++;
}
