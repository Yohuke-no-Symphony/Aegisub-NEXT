namespace AegiNext.Core.Timing;

/// <summary>
/// 非空半开时间区间 [Start, End)，允许负媒体时间及字幕重叠。
/// </summary>
public sealed record MediaTimeRange
{
    /// <summary>
    /// 创建结束时间严格晚于开始时间的区间。
    /// </summary>
    public MediaTimeRange(MediaTime start, MediaTime end)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), end, "The end time must be later than the start time.");
        }

        Start = start;
        End = end;
    }

    public MediaTime Start { get; }

    public MediaTime End { get; }

    public MediaTime Duration => End - Start;

    /// <summary>
    /// 判断时间是否包含在半开区间内。
    /// </summary>
    public bool Contains(MediaTime time)
    {
        return time >= Start && time < End;
    }

    /// <summary>
    /// 判断两个区间是否具有正长度的交集。
    /// </summary>
    public bool Overlaps(MediaTimeRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start < other.End && other.Start < End;
    }
}
