namespace AegiNext.Export.Worker;

internal sealed class SubtitleMuxWorkerProgress(ExportWorkerMessageWriter writer) : IProgress<SubtitleMuxProgress>
{
    /// <inheritdoc />
    public void Report(SubtitleMuxProgress value)
    {
        writer.Send(new("progress", PositionNumerator: value.Position.Numerator,
            PositionDenominator: value.Position.Denominator, Fraction: value.Fraction, Stage: value.Stage));
    }
}
