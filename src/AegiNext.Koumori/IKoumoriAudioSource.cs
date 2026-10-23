namespace AegiNext.Koumori;

/// <summary>
/// Provides random access to 16 kHz mono floating point PCM on the media sample grid.
/// The caller owns the source and must keep it available until analysis completes.
/// </summary>
public interface IKoumoriAudioSource
{
    long SampleCount { get; }

    /// <summary>
    /// Reads up to the destination length from an absolute sample index; returns zero at EOF.
    /// </summary>
    ValueTask<int> ReadAsync(long startSample, Memory<float> destination, CancellationToken cancellationToken);
}
