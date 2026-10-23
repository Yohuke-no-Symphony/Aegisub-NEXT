using AegiNext.Koumori;

namespace AegiNext.Desktop.Tests;

internal sealed class KoumoriTestArrayAudioSource(float[] samples) : IKoumoriAudioSource
{
    public long SampleCount => samples.Length;

    public ValueTask<int> ReadAsync(long startSample, Memory<float> destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = Math.Min(destination.Length, samples.Length - checked((int)startSample));
        samples.AsMemory(checked((int)startSample), count).CopyTo(destination);
        return new(count);
    }
}
