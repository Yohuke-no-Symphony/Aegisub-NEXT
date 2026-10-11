using AegiNext.Export;

namespace AegiNext.Desktop.Workspace;

internal interface IWorkbenchExportService : IDisposable
{
    Task<VideoExportResult> ExportWithCommitAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        Action beforeCommit, CancellationToken cancellationToken) => ExportAsync(request, progress, cancellationToken);

    Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken);
}
