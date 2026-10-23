using System.Diagnostics.CodeAnalysis;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Audio;

/// <summary>
/// 固定 FFmpeg 后端的同步流式 PCM 解码器；输出块独立，取消永久终止当前实例。
/// </summary>
public sealed class FfmpegAudioDecoder : IAudioSampleSource
{
    private readonly AudioDecoderHandle handle;
    private readonly Lock gate = new();

    private FfmpegAudioDecoder(AudioDecoderHandle handle, AudioSampleFormat format)
    {
        this.handle = handle;
        Format = format;
    }

    public AudioSampleFormat Format { get; }

    /// <summary>
    /// 打开本地文件绝对流索引，默认转换到 48kHz 立体声；分析可请求 16kHz 单声道。
    /// </summary>
    public static unsafe FfmpegAudioDecoder Open(string path, int streamIndex,
        AudioSampleFormat? format = null, CancellationToken cancellationToken = default,
        AudioTimestampPolicy timestampPolicy = AudioTimestampPolicy.PRESERVE_DISCONTINUITIES)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        if (!Enum.IsDefined(timestampPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(timestampPolicy));
        }
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("音频文件不存在。", path);
        }

        if (NativeAudioMethods.AbiVersion() != 2)
        {
            throw new InvalidOperationException("原生音频 ABI 版本不匹配。");
        }

        format ??= new();
        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* text = error)
        {
            NativeAudioError.Check(NativeAudioMethods.CreateDecoder(out var pointer, text, (uint)error.Length), error, cancellationToken);
            var handle = new AudioDecoderHandle(pointer);
            try
            {
                using var registration = cancellationToken.UnsafeRegister(static state => NativeAudioMethods.Cancel((AudioDecoderHandle)state!), handle);
                if (timestampPolicy != AudioTimestampPolicy.PRESERVE_DISCONTINUITIES)
                {
                    NativeAudioError.Check(NativeAudioMethods.SetTimestampPolicy(handle, (int)timestampPolicy, text, (uint)error.Length), error, cancellationToken);
                }
                NativeAudioError.Check(NativeAudioMethods.OpenDecoder(handle, path, streamIndex, format.SampleRate, format.Channels, text, (uint)error.Length), error, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new(handle, format);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public unsafe AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var samples = new float[4096 * Format.Channels];
        var frames = ReadInto(samples, out var start, cancellationToken);
        return frames == 0 ? null : new(Format, start, samples.AsSpan(0, checked(frames * Format.Channels)));
    }

    internal unsafe int ReadInto(Span<float> samples, out MediaTime start, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (samples.Length == 0 || samples.Length % Format.Channels != 0 || samples.Length / Format.Channels > 262144)
        {
            throw new ArgumentException("PCM 缓冲必须包含完整声道帧且不超过原生容量。", nameof(samples));
        }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
            error.Clear();
            using var registration = cancellationToken.UnsafeRegister(static state => NativeAudioMethods.Cancel((AudioDecoderHandle)state!), handle);
            fixed (float* data = samples)
            fixed (byte* text = error)
            {
                var status = NativeAudioMethods.Read(handle, data, samples.Length / Format.Channels, out var frames,
                    out var firstSample, text, (uint)error.Length);
                cancellationToken.ThrowIfCancellationRequested();
                if (status == 1)
                {
                    start = default;
                    return 0;
                }

                NativeAudioError.Check(status, error, cancellationToken);
                start = new(firstSample, Format.SampleRate);
                return frames;
            }
        }
    }

    /// <inheritdoc />
    public unsafe void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            var sample = target.ToTimestamp(new(1, Format.SampleRate), MediaTimeRounding.CEILING).Value;
            Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
            error.Clear();
            using var registration = cancellationToken.UnsafeRegister(static state => NativeAudioMethods.Cancel((AudioDecoderHandle)state!), handle);
            fixed (byte* text = error)
            {
                NativeAudioError.Check(NativeAudioMethods.Seek(handle, sample, text, (uint)error.Length), error, cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField", Justification = "Native cancellation only sets an atomic flag; SafeHandle pins its lifetime while a read holds the gate.")]
    public void Cancel()
    {
        NativeAudioMethods.Cancel(handle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (!handle.IsClosed)
            {
                NativeAudioMethods.Cancel(handle);
            }
        }
        catch (ObjectDisposedException)
        {
        }

        lock (gate)
        {
            handle.Dispose();
        }
    }
}
