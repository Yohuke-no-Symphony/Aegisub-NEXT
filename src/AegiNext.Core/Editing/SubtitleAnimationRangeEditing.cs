using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>文字动画范围的字素重映射、身份复制与来源清理，不提交编辑事务。</summary>
public static class SubtitleAnimationRangeEditing
{
    /// <summary>按文字变化重映射范围，保留覆盖顺序和身份，并清理失去父范围的生成来源。</summary>
    public static ImmutableArray<SubtitleAnimationRange> Remap(ImmutableArray<SubtitleAnimationRange> ranges, SubtitleTextEditMap map)
    {
        if (ranges.IsEmpty)
        {
            return ranges;
        }

        var sources = new int[map.NewBoundaries.Length - 1];
        for (var index = 0; index < sources.Length; index++)
        {
            sources[index] = map.StyleSourceOffset(map.NewBoundaries[index]);
        }

        var result = ImmutableArray.CreateBuilder<SubtitleAnimationRange>(ranges.Length);
        foreach (var range in ranges)
        {
            var first = LowerBound(sources, range.Utf16Start);
            var end = LowerBound(sources, checked(range.Utf16Start + range.Utf16Length));
            if (end > first)
            {
                result.Add(range with
                {
                    Utf16Start = map.NewBoundaries[first],
                    Utf16Length = map.NewBoundaries[end] - map.NewBoundaries[first]
                });
            }
        }

        var remapped = PruneOrphanedOrigins(result.ToImmutable());
        return remapped.SequenceEqual(ranges) ? ranges : remapped;
    }

    /// <summary>文字变化且调用方未改写动画范围时，按原字素样式归属更新范围。</summary>
    public static SubtitleLine RemapTextChange(SubtitleLine original, SubtitleLine edited)
    {
        if (original.Text == edited.Text || original.AnimationRanges.IsEmpty ||
            !original.AnimationRanges.SequenceEqual(edited.AnimationRanges))
        {
            return edited;
        }

        return edited with { AnimationRanges = Remap(original.AnimationRanges, SubtitleTextEditMap.Between(original.Text, edited.Text)) };
    }

    /// <summary>截取分割点一侧的范围；右侧通过 rangeIds 映射范围与生成父来源的身份。</summary>
    public static ImmutableArray<SubtitleAnimationRange> Split(ImmutableArray<SubtitleAnimationRange> ranges, int offset,
        bool right, IReadOnlyDictionary<Guid, Guid>? rangeIds = null)
    {
        var result = ImmutableArray.CreateBuilder<SubtitleAnimationRange>();
        foreach (var range in ranges)
        {
            var first = right ? Math.Max(offset, range.Utf16Start) : range.Utf16Start;
            var end = right ? checked(range.Utf16Start + range.Utf16Length) : Math.Min(offset, checked(range.Utf16Start + range.Utf16Length));
            if (end > first)
            {
                result.Add(range with
                {
                    Id = right ? rangeIds![range.Id] : range.Id,
                    GeneratedOrigin = right ? RemapOrigin(range.GeneratedOrigin, rangeIds!) : range.GeneratedOrigin,
                    Utf16Start = right ? first - offset : first,
                    Utf16Length = end - first
                });
            }
        }

        return PruneOrphanedOrigins(result.ToImmutable());
    }

    /// <summary>按指定身份映射复制范围及其父来源，并平移文字偏移。</summary>
    public static ImmutableArray<SubtitleAnimationRange> Clone(ImmutableArray<SubtitleAnimationRange> ranges,
        IReadOnlyDictionary<Guid, Guid> rangeIds, int textOffset = 0)
    {
        return ranges.Select(range => range with
        {
            Id = rangeIds[range.Id],
            Utf16Start = checked(range.Utf16Start + textOffset),
            GeneratedOrigin = RemapOrigin(range.GeneratedOrigin, rangeIds)
        }).ToImmutableArray();
    }

    /// <summary>递归移除缺少父范围的生成范围，保留其余范围的顺序。</summary>
    public static ImmutableArray<SubtitleAnimationRange> PruneOrphanedOrigins(ImmutableArray<SubtitleAnimationRange> ranges)
    {
        var current = ranges;
        while (!current.IsEmpty)
        {
            var knownIds = current.Select(range => range.Id).ToHashSet();
            var retained = current.Where(range => range.GeneratedOrigin?.ParentRangeId is not { } parentId ||
                knownIds.Contains(parentId)).ToImmutableArray();
            if (retained.Length == current.Length)
            {
                return current;
            }
            current = retained;
        }
        return current;
    }

    private static SubtitleAnimationRangeOrigin? RemapOrigin(SubtitleAnimationRangeOrigin? origin,
        IReadOnlyDictionary<Guid, Guid> rangeIds)
    {
        return origin?.ParentRangeId is { } parentId && rangeIds.TryGetValue(parentId, out var replacement)
            ? origin with { ParentRangeId = replacement } : origin;
    }

    /// <summary>清除引用已删除文字范围的动画轨道，保留整句轨道和有效范围的轨道。</summary>
    public static ProjectLayer PruneTargets(ProjectLayer layer, SubtitleLine line)
    {
        if (!layer.Tracks.Any(track => track.Target.TextRangeId.HasValue))
        {
            return layer;
        }

        var rangeIds = line.AnimationRanges.Select(range => range.Id).ToHashSet();
        var tracks = layer.Tracks.Where(track => track.Target.TextRangeId is not { } id || rangeIds.Contains(id)).ToImmutableArray();
        return tracks.SequenceEqual(layer.Tracks) ? layer : layer with { Tracks = tracks };
    }

    private static int LowerBound(int[] sources, int offset)
    {
        var first = 0;
        var end = sources.Length;
        while (first < end)
        {
            var middle = first + (end - first) / 2;
            if (sources[middle] < offset)
            {
                first = middle + 1;
            }
            else
            {
                end = middle;
            }
        }

        return first;
    }
}
