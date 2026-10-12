using AegiNext.Export;

namespace AegiNext.Cli.Tests;

internal sealed class RecordingVideoExportService : IVideoExportService
{
    internal VideoExportRequest? Request { get; private set; }
    internal Exception? Failure { get; set; }

    public Task<VideoExportResult> ExportAsync(VideoExportRequest request, IProgress<VideoExportProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Request = request;
        if (Failure is not null)
        {
            return Task.FromException<VideoExportResult>(Failure);
        }
        progress.Report(new(5, new(1, 2), null, "encoding", "test-encoder"));
        return Task.FromResult(new VideoExportResult(request.OutputPath, 5, "test-encoder"));
    }
}
