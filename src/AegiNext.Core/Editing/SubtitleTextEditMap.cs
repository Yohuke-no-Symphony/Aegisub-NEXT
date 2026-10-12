using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>以完整字素边界映射文字替换前后的偏移，供编辑和格式交换共享。</summary>
public sealed class SubtitleTextEditMap
{
    /// <summary>为指定 UTF-16 区间的替换创建映射；拒绝不完整字素范围及无效替换文字。</summary>
    public SubtitleTextEditMap(string text, int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        OldBoundaries = Boundaries(text);
        ValidateRange(text, OldBoundaries, start, length);
        try
        {
            ProjectValidator.ValidateText(replacement);
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException("替换文字包含无效 Unicode 或空字符。", nameof(replacement), exception);
        }
        Text = string.Concat(text.AsSpan(0, start), replacement, text.AsSpan(start + length));
        NewBoundaries = Boundaries(Text);
        Delta = replacement.Length - length;
        OriginalStart = start;
        OriginalEnd = start + length;
        OriginalNewEnd = start + replacement.Length;
        var oldStart = start;
        var oldEnd = start + length;
        while (NewBoundaries.IndexOf(oldStart) < 0)
        {
            oldStart = OldBoundaries[OldBoundaries.IndexOf(oldStart) - 1];
        }
        while (NewBoundaries.IndexOf(oldEnd + Delta) < 0)
        {
            oldEnd = OldBoundaries[OldBoundaries.IndexOf(oldEnd) + 1];
        }
        OldStart = oldStart;
        OldEnd = oldEnd;
        NewStart = oldStart;
        NewEnd = oldEnd + Delta;
        OldStartIndex = OldBoundaries.IndexOf(OldStart);
        NewStartIndex = NewBoundaries.IndexOf(NewStart);
        OldCount = OldBoundaries.IndexOf(OldEnd) - OldStartIndex;
        NewCount = NewBoundaries.IndexOf(NewEnd) - NewStartIndex;
    }

    public string Text { get; }
    public SubtitleTextBoundaries OldBoundaries { get; }
    public SubtitleTextBoundaries NewBoundaries { get; }
    public int Delta { get; }
    public int OldStart { get; }
    public int OldEnd { get; }
    public int NewStart { get; }
    public int NewEnd { get; }
    public int OldCount { get; }
    public int NewCount { get; }
    public int OriginalStart { get; }
    public int OriginalEnd { get; }
    public int OriginalNewEnd { get; }
    private int OldStartIndex { get; }
    private int NewStartIndex { get; }

    /// <summary>映射旧字素边界；受影响范围内部只有字素数量相等时才能保留索引。</summary>
    public int MapBoundary(int offset)
    {
        if (offset <= OldStart)
        {
            return offset;
        }
        if (offset >= OldEnd)
        {
            return offset + Delta;
        }
        if (OldCount != NewCount)
        {
            throw new InvalidOperationException("受影响范围内部不能按原索引映射。");
        }
        return NewBoundaries[NewStartIndex + OldBoundaries.IndexOf(offset) - OldStartIndex];
    }

    /// <summary>查找新字素继承样式的旧文字偏移，末尾插入继承最后一个字素。</summary>
    public int StyleSourceOffset(int offset)
    {
        if (offset < NewStart)
        {
            return offset;
        }
        if (offset >= NewEnd)
        {
            return offset - Delta;
        }
        if (OldCount == NewCount)
        {
            return OldBoundaries[OldStartIndex + NewBoundaries.IndexOf(offset) - NewStartIndex];
        }
        if (offset < OriginalStart)
        {
            return offset;
        }
        if (offset >= OriginalNewEnd)
        {
            return offset - Delta;
        }
        return OriginalStart == OldBoundaries[^1] && OriginalStart > 0 ? OldBoundaries[^2] : OriginalStart;
    }

    /// <summary>构建包含末尾偏移的完整字素边界。</summary>
    public static SubtitleTextBoundaries Boundaries(string text)
    {
        return new(text);
    }

    /// <summary>保留共同字素前后缀，为两份文字之间的变化创建单一区间映射。</summary>
    public static SubtitleTextEditMap Between(string original, string edited)
    {
        var previous = Boundaries(original);
        var next = Boundaries(edited);
        var prefix = 0;
        while (prefix < previous.Length - 1 && prefix < next.Length - 1 &&
            original.AsSpan(previous[prefix], previous[prefix + 1] - previous[prefix])
                .SequenceEqual(edited.AsSpan(next[prefix], next[prefix + 1] - next[prefix])))
        {
            prefix++;
        }

        var previousEnd = previous.Length - 1;
        var nextEnd = next.Length - 1;
        while (previousEnd > prefix && nextEnd > prefix &&
            original.AsSpan(previous[previousEnd - 1], previous[previousEnd] - previous[previousEnd - 1])
                .SequenceEqual(edited.AsSpan(next[nextEnd - 1], next[nextEnd] - next[nextEnd - 1])))
        {
            previousEnd--;
            nextEnd--;
        }

        return new(original, previous[prefix], previous[previousEnd] - previous[prefix], edited[next[prefix]..next[nextEnd]]);
    }

    /// <summary>验证 UTF-16 范围位于文字内，并以完整字素边界开始和结束。</summary>
    public static void ValidateRange(string text, SubtitleTextBoundaries boundaries, int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > text.Length ||
            !boundaries.Contains(start) || !boundaries.Contains(start + length))
        {
            throw new ArgumentOutOfRangeException(nameof(start), "编辑范围必须位于完整字素边界。");
        }
    }
}
