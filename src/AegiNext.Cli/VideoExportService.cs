using AegiNext.Export;

namespace AegiNext.Cli;

internal sealed class VideoExportService : IVideoExportService
{
    /// <inheritdoc />
    public Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken)
    {
        return new VideoExporter().ExportAsync(request, progress, cancellationToken);
    }
}
