namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriTestAudioSource(float[] samples, int maximumRead = int.MaxValue, long? declaredSampleCount = null) : IKoumoriAudioSource
{
    public long SampleCount => declaredSampleCount ?? samples.Length;

    public int ReadCount { get; private set; }

    public int MaximumRequestedSamples { get; private set; }

    public ValueTask<int> ReadAsync(long startSample, Memory<float> destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        MaximumRequestedSamples = Math.Max(MaximumRequestedSamples, destination.Length);
        var count = Math.Min(Math.Min(destination.Length, maximumRead), Math.Max(0, samples.Length - checked((int)startSample)));
        if (count > 0)
        {
            samples.AsMemory(checked((int)startSample), count).CopyTo(destination);
        }
        return ValueTask.FromResult(count);
    }
}
