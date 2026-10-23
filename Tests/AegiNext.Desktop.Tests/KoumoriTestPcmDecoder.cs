using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests;

internal sealed class KoumoriTestPcmDecoder(params AudioSampleBlock[] blocks) : IAudioSampleSource
{
    private int index;

    public AudioSampleFormat Format { get; } = new(16000, 1);
    public MediaTime LastSeek { get; private set; }
    public bool IsDisposed { get; private set; }
    public int ReadCount { get; private set; }
    public int SeekCount { get; private set; }
    public Action<int>? OnRead { get; init; }

    public AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        OnRead?.Invoke(ReadCount);
        return index < blocks.Length ? blocks[index++] : null;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastSeek = target;
        SeekCount++;
        index = 0;
    }

    public void Cancel()
    {
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
