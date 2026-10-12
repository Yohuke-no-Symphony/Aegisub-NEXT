using AegiNext.Export;

namespace AegiNext.Cli;

internal interface IVideoExportService
{
    Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken);
}
