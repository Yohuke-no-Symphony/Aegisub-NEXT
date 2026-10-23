using System.Runtime.InteropServices;
using AegiNext.Core.Timing;
using AegiNext.Koumori;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Koumori;

internal sealed class KoumoriMediaSource : IKoumoriAudioSource, IDisposable, IAsyncDisposable
{
    internal const string PCM_CONTRACT = "pcm-f32le-media-zero-v1-sequential-async1";
    private readonly IAudioSampleSource decoder;
    private readonly MediaTime origin;
    private readonly FileStream cache;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly byte[] silence = new byte[4096 * sizeof(float)];
    private AudioSampleBlock? pending;
    private bool ended;
    private long cachedSampleCount;
    private int disposed;

    public KoumoriMediaSource(IAudioSampleSource decoder, long sampleCount, MediaTime origin, string cachePath)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleCount);
        if (decoder.Format.SampleRate != KoumoriAnalyzer.SAMPLE_RATE || decoder.Format.Channels != 1)
        {
            throw new ArgumentException("Koumori requires 16 kHz mono floating-point PCM.", nameof(decoder));
        }
        this.decoder = decoder;
        this.origin = origin;
        SampleCount = sampleCount;
        var path = Path.GetFullPath(cachePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        cache = new(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1,
            FileOptions.Asynchronous | FileOptions.RandomAccess | FileOptions.DeleteOnClose);
    }

    public long SampleCount { get; }
    public long CachedSampleCount => Interlocked.Read(ref cachedSampleCount);
    public bool IsCanceled => Volatile.Read(ref disposed) != 0 || lifetime.IsCancellationRequested;

    public async ValueTask<int> ReadAsync(long startSample, Memory<float> destination, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startSample);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        await gate.WaitAsync(request.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            request.Token.ThrowIfCancellationRequested();
            var length = (int)Math.Min(destination.Length, Math.Max(0, SampleCount - startSample));
            destination.Span.Clear();
            if (length == 0)
            {
                return 0;
            }
            await Task.Run(() => EnsurePrefix(startSample + length, request.Token), CancellationToken.None).ConfigureAwait(false);
            request.Token.ThrowIfCancellationRequested();
            var bytes = MemoryMarshal.AsBytes(destination.Span[..length]);
            var read = 0;
            while (read < bytes.Length)
            {
                request.Token.ThrowIfCancellationRequested();
                var count = RandomAccess.Read(cache.SafeFileHandle, bytes[read..], checked(startSample * sizeof(float) + read));
                if (count == 0)
                {
                    throw new EndOfStreamException("The Koumori PCM prefix is incomplete.");
                }
                read += count;
            }
            return length;
        }
        finally
        {
            gate.Release();
        }
    }

    private void EnsurePrefix(long endSample, CancellationToken request)
    {
        while (cachedSampleCount < endSample)
        {
            request.ThrowIfCancellationRequested();
            if (!ended && pending is null)
            {
                pending = decoder.Read(lifetime.Token);
                ended = pending is null;
            }
            if (pending is null)
            {
                AppendSilence(endSample, request);
                continue;
            }
            var blockStart = (pending.Start - origin).ToTimestamp(new(1, KoumoriAnalyzer.SAMPLE_RATE), MediaTimeRounding.TO_EVEN).Value;
            if (blockStart > cachedSampleCount)
            {
                AppendSilence(Math.Min(blockStart, endSample), request);
                if (cachedSampleCount >= endSample)
                {
                    continue;
                }
            }
            var last = Math.Min(SampleCount, blockStart + pending.FrameCount);
            if (last > cachedSampleCount)
            {
                var offset = checked((int)(cachedSampleCount - blockStart));
                var length = checked((int)(last - cachedSampleCount));
                RandomAccess.Write(cache.SafeFileHandle, MemoryMarshal.AsBytes(pending.Samples.Span.Slice(offset, length)),
                    checked(cachedSampleCount * sizeof(float)));
                Interlocked.Exchange(ref cachedSampleCount, last);
            }
            pending = null;
        }
    }

    private void AppendSilence(long endSample, CancellationToken request)
    {
        while (cachedSampleCount < endSample)
        {
            request.ThrowIfCancellationRequested();
            var frames = (int)Math.Min(silence.Length / sizeof(float), endSample - cachedSampleCount);
            RandomAccess.Write(cache.SafeFileHandle, silence.AsSpan(0, frames * sizeof(float)), checked(cachedSampleCount * sizeof(float)));
            Interlocked.Add(ref cachedSampleCount, frames);
        }
    }

    public void Cancel()
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            try
            {
                lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            _ = CompleteDisposalAsync();
        }
        return new(disposal.Task);
    }

    private async Task CompleteDisposalAsync()
    {
        try
        {
            lifetime.Cancel();
            await gate.WaitAsync().ConfigureAwait(false);
            ReleaseResources();
            disposal.TrySetResult();
        }
        catch (Exception error)
        {
            disposal.TrySetException(error);
        }
    }

    private void ReleaseResources()
    {
        try
        {
            decoder.Dispose();
        }
        finally
        {
            cache.Dispose();
            lifetime.Dispose();
            gate.Dispose();
        }
    }
}
