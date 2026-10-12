using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Rendering.Images;

/// <summary>解码得到的尺寸和完整帧时序；重复次数不含首次播放，-1 表示无限循环。</summary>
public sealed record ImageMetadata
{
    private readonly ImmutableArray<MediaTime> frameEnds;

    /// <summary>建立有序帧边界，供播放时以二分查找定位。</summary>
    public ImageMetadata(int width, int height, ImmutableArray<MediaTime> frameDurations, int repetitionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (frameDurations.IsDefaultOrEmpty || frameDurations.Any(value => value < MediaTime.Zero) ||
            (frameDurations.Length > 1 && frameDurations.Any(value => value == MediaTime.Zero)))
        {
            throw new ArgumentException("The image frame duration is invalid.", nameof(frameDurations));
        }
        Width = width;
        Height = height;
        FrameDurations = frameDurations;
        RepetitionCount = repetitionCount;
        var ends = ImmutableArray.CreateBuilder<MediaTime>(frameDurations.Length);
        var end = MediaTime.Zero;
        foreach (var duration in frameDurations)
        {
            end += duration;
            ends.Add(end);
        }
        frameEnds = ends.MoveToImmutable();
    }

    public int Width { get; }
    public int Height { get; }
    public ImmutableArray<MediaTime> FrameDurations { get; }
    public int RepetitionCount { get; init; }
    public int FrameCount => FrameDurations.Length;
    public bool IsAnimated => FrameCount > 1;
    public MediaTime Duration => frameEnds[^1];

    /// <summary>按半开帧区间选帧；负源时间保持首帧，有限播放结束保持末帧。</summary>
    public int GetFrameIndex(MediaTime sourceTime, ImagePlaybackMode mode)
    {
        if (!IsAnimated || sourceTime <= MediaTime.Zero)
        {
            return 0;
        }

        var duration = Duration;
        var repeats = mode switch
        {
            ImagePlaybackMode.LOOP => -1,
            ImagePlaybackMode.FILE => RepetitionCount,
            ImagePlaybackMode.ONCE => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        if (repeats >= 0 && sourceTime >= duration * ((long)repeats + 1))
        {
            return FrameCount - 1;
        }

        var numerator = (System.Numerics.BigInteger)sourceTime.Numerator * duration.Denominator;
        var denominator = (System.Numerics.BigInteger)sourceTime.Denominator * duration.Numerator;
        var remainder = numerator % denominator;
        var commonDenominator = (System.Numerics.BigInteger)sourceTime.Denominator * duration.Denominator;
        var divisor = System.Numerics.BigInteger.GreatestCommonDivisor(remainder, commonDenominator);
        var localTime = new MediaTime(checked((long)(remainder / divisor)), checked((long)(commonDenominator / divisor)));
        var lower = 0;
        var upper = FrameCount - 1;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (localTime < frameEnds[middle])
            {
                upper = middle;
            }
            else
            {
                lower = middle + 1;
            }
        }
        return lower;
    }
}
