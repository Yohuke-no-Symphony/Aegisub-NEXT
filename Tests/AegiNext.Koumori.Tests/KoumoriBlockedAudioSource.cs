namespace AegiNext.Koumori.Tests;

internal sealed class KoumoriBlockedAudioSource : IKoumoriAudioSource
{
    public long SampleCount => 16000;

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async ValueTask<int> ReadAsync(long startSample, Memory<float> destination, CancellationToken cancellationToken)
    {
        Entered.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
}
