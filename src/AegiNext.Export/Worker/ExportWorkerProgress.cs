namespace AegiNext.Export.Worker;

internal sealed class ExportWorkerProgress(ExportWorkerMessageWriter writer) : IProgress<VideoExportProgress>
{
    /// <inheritdoc />
    public void Report(VideoExportProgress value)
    {
        writer.Send(new("progress", value.FrameCount, value.Position.Numerator, value.Position.Denominator, Encoder: value.Encoder));
    }
}
