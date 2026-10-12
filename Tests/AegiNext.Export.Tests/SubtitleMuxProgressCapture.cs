namespace AegiNext.Export.Tests;

internal sealed class SubtitleMuxProgressCapture(Action<SubtitleMuxProgress>? report = null) : IProgress<SubtitleMuxProgress>
{
    internal List<SubtitleMuxProgress> Values { get; } = [];

    /// <inheritdoc />
    public void Report(SubtitleMuxProgress value)
    {
        Values.Add(value);
        report?.Invoke(value);
    }
}
