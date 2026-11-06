namespace AegiNext.Export.Tests;

internal sealed class AudioExportProgressCapture(Action<AudioExportProgress>? onReport = null) : IProgress<AudioExportProgress>
{
    internal List<AudioExportProgress> Values { get; } = [];

    /// <inheritdoc />
    public void Report(AudioExportProgress value)
    {
        Values.Add(value);
        onReport?.Invoke(value);
    }
}
