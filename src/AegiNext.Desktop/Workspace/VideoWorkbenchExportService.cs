using AegiNext.Export;

namespace AegiNext.Desktop.Workspace;

internal sealed class VideoWorkbenchExportService(VideoExporter exporter, SubtitleMuxExporter subtitleMuxExporter) : IWorkbenchExportService
{
    /// <summary>通过独立导出 worker 压制不可变的工作台工程快照。</summary>
    public Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken)
    {
        return exporter.ExportAsync(request, progress, cancellationToken);
    }

    public Task<VideoExportResult> ExportWithCommitAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        Action beforeCommit, CancellationToken cancellationToken) => exporter.ExportAsync(request, progress, beforeCommit, cancellationToken);

    public Task<SubtitleMuxResult> SubtitleMuxWithCommitAsync(SubtitleMuxRequest request, IProgress<SubtitleMuxProgress> progress,
        Action beforeCommit, CancellationToken cancellationToken)
    {
        return subtitleMuxExporter.ExportAsync(request, progress, beforeCommit, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
