using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Media;

/// <summary>
/// 保存显示顺序的精确帧起点和关键帧候选；时间已经减去媒体时间原点。
/// </summary>
public sealed class VideoTimingIndex
{
    private const int MAX_FRAME_COUNT = 2_000_000;

    public static int MaximumFrameCount => MAX_FRAME_COUNT;

    /// <summary>
    /// 创建严格递增的帧时间索引；不补帧、不排序修复、不使用平均帧率。
    /// </summary>
    public VideoTimingIndex(ImmutableArray<MediaTime> frameTimes, ImmutableArray<int> keyframes)
    {
        if (frameTimes.IsDefaultOrEmpty)
        {
            throw new ArgumentException("The video frame timing index cannot be empty.", nameof(frameTimes));
        }

        if (frameTimes.Length > MAX_FRAME_COUNT)
        {
            throw new ArgumentOutOfRangeException(nameof(frameTimes), $"The video frame index supports at most {MAX_FRAME_COUNT} frames.");
        }

        if (keyframes.IsDefault)
        {
            throw new ArgumentException("The keyframe index must be initialized.", nameof(keyframes));
        }

        for (var index = 1; index < frameTimes.Length; index++)
        {
            if (frameTimes[index] <= frameTimes[index - 1])
            {
                throw new ArgumentException($"The PTS of video frame {index} is not strictly increasing.", nameof(frameTimes));
            }
        }

        for (var index = 0; index < keyframes.Length; index++)
        {
            var frame = keyframes[index];
            if (frame < 0 || frame >= frameTimes.Length || (index > 0 && frame <= keyframes[index - 1]))
            {
                throw new ArgumentException("Keyframe indices must be strictly increasing and within the video frame range.", nameof(keyframes));
            }
        }

        FrameTimes = frameTimes;
        Keyframes = keyframes;
    }

    public ImmutableArray<MediaTime> FrameTimes { get; }

    public ImmutableArray<int> Keyframes { get; }

    /// <summary>
    /// 定位实际 PTS 所属帧；结束时间恰等帧起点时取前帧，范围外时间夹到首尾帧。
    /// </summary>
    public int FrameAtTime(MediaTime time, bool end = false)
    {
        var low = 0;
        var high = FrameTimes.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (FrameTimes[middle] < time || (!end && FrameTimes[middle] == time))
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return Math.Clamp(low - 1, 0, FrameTimes.Length - 1);
    }

    /// <summary>
    /// 以帧编号距离选择最近关键帧候选，等距时取之前的候选。
    /// </summary>
    public int NearestKeyframe(int frame)
    {
        ValidateFrame(frame);
        if (Keyframes.IsEmpty)
        {
            throw new InvalidOperationException("The video has no candidate keyframes.");
        }

        var low = 0;
        var high = Keyframes.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (Keyframes[middle] <= frame)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low == 0)
        {
            return Keyframes[0];
        }

        if (low == Keyframes.Length)
        {
            return Keyframes[^1];
        }

        var previous = Keyframes[low - 1];
        var next = Keyframes[low];
        return next - frame < frame - previous ? next : previous;
    }

    /// <summary>
    /// 返回帧的精确起点，供半开区间的开始和结束共同使用。
    /// </summary>
    public MediaTime KeyframeBoundary(int frame)
    {
        ValidateFrame(frame);
        return FrameTimes[frame];
    }

    private void ValidateFrame(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, FrameTimes.Length);
    }
}
