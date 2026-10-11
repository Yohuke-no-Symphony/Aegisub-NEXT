using AegiNext.Export;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class CancelExportProbeProgress(CancellationTokenSource cancellation) : IProgress<VideoExportProgress>
{
    /// <inheritdoc />
    public void Report(VideoExportProgress value)
    {
        if (value.Stage == "encoding" && value.FrameCount > 0)
        {
            cancellation.Cancel();
        }
    }
}
