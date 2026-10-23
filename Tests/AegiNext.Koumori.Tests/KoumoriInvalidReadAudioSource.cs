namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriInvalidReadAudioSource(int count) : IKoumoriAudioSource
{
    public long SampleCount => 16000;

    public ValueTask<int> ReadAsync(long startSample, Memory<float> destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(count);
    }
}
