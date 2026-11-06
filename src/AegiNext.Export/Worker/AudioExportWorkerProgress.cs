namespace AegiNext.Export.Worker;

internal sealed class AudioExportWorkerProgress(ExportWorkerMessageWriter writer) : IProgress<AudioExportProgress>
{
    /// <inheritdoc />
    public void Report(AudioExportProgress value)
    {
        writer.Send(new("progress", PositionNumerator: value.Position.Numerator,
            PositionDenominator: value.Position.Denominator, Fraction: value.Fraction, Stage: value.Stage));
    }
}
