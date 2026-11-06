using AegiNext.Export;

namespace AegiNext.Desktop.Workspace;

internal sealed class VideoWorkbenchExportService(VideoExporter exporter, SubtitleMuxExporter subtitleMuxExporter,
    AudioExporter audioExporter) : IWorkbenchExportService
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

    /// <summary>通过独立 worker 导出冻结工程绑定的完整音轨。</summary>
    public Task<AudioExportResult> AudioExportWithCommitAsync(AudioExportRequest request, IProgress<AudioExportProgress> progress,
        Action beforeCommit, CancellationToken cancellationToken)
    {
        return audioExporter.ExportAsync(request, progress, beforeCommit, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
