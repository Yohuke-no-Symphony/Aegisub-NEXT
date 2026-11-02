using System.Numerics;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Preview;

/// <summary>
/// Synthetic frame source for audio-only media: emits placeholder frames on a fixed grid from the media start
/// so the playback session, audio clock, and subtitle overlay work without a video stream.
/// </summary>
public sealed class AudioOnlyFrameSource : IVideoFrameSource
{
    /// <summary>Placeholder frames per second; only drives overlay refresh, never media timing.</summary>
    public const int FRAMES_PER_SECOND = 25;

    private const long UNBOUNDED_FRAMES = FRAMES_PER_SECOND * 60L * 60 * 24;
    private readonly int width;
    private readonly int height;
    private readonly MediaTime start;
    private readonly long frameCount;
    private readonly int timeBaseDenominator;
    private long next;
    private int cancelled;
    private int disposed;

    public AudioOnlyFrameSource(int width, int height, MediaTime start, MediaTime? duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        this.width = width;
        this.height = height;
        this.start = start;
        frameCount = duration is { } value && value > MediaTime.Zero
            ? Math.Max(1, (long)CeilingDivide(new BigInteger(value.Numerator) * FRAMES_PER_SECOND, value.Denominator))
            : UNBOUNDED_FRAMES;
        var lcm = BigInteger.Abs(start.Denominator * (BigInteger)FRAMES_PER_SECOND) /
            BigInteger.GreatestCommonDivisor(start.Denominator, FRAMES_PER_SECOND);
        if (lcm > int.MaxValue)
        {
            throw new NotSupportedException("The audio start time cannot be represented by the placeholder time base.");
        }

        timeBaseDenominator = (int)lcm;
    }

    public PositionedVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        return next < frameCount ? Create(next++, false) : null;
    }

    public PositionedVideoFrame? SeekFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        var offset = target - start;
        var index = offset < MediaTime.Zero ? 0
            : (long)BigInteger.Min(frameCount - 1, new BigInteger(offset.Numerator) * FRAMES_PER_SECOND / offset.Denominator);
        next = index + 1;
        return Create(index, target < start);
    }

    public void Cancel()
    {
        Interlocked.Exchange(ref cancelled, 1);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref disposed, 1);
    }

    internal MediaTime FrameTime(long index)
    {
        return start + new MediaTime(index, FRAMES_PER_SECOND);
    }

    private PositionedVideoFrame Create(long index, bool beforeFirst)
    {
        var time = FrameTime(index);
        var last = index + 1 >= frameCount;
        var timestamp = checked(time.Numerator * (timeBaseDenominator / time.Denominator));
        return new(new AudioOnlyVideoFrame(width, height, timestamp, timeBaseDenominator), time,
            last ? null : FrameTime(index + 1), beforeFirst, last);
    }

    private void CheckState(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref cancelled) != 0)
        {
            throw new OperationCanceledException("The audio-only frame source was cancelled.");
        }
    }

    private static BigInteger CeilingDivide(BigInteger value, BigInteger divisor)
    {
        return (value + divisor - 1) / divisor;
    }
}
