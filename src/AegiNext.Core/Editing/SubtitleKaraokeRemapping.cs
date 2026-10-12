using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>按字素归属重映射启用及停用计时组，保留身份、时间和外观。</summary>
public static class SubtitleKaraokeRemapping
{
    /// <summary>重映射文字变化后的计时范围；拒绝将不同计时归属合并到同一字素。</summary>
    public static (ImmutableArray<KaraokeSegment> Karaoke, ImmutableArray<KaraokeSegment> InactiveKaraoke) Remap(
        SubtitleLine line, SubtitleTextEditMap map)
    {
        if (line.InactiveKaraoke.IsEmpty)
        {
            return (Remap(line.Karaoke, map), line.InactiveKaraoke);
        }
        if (line.Karaoke.IsEmpty)
        {
            return (line.Karaoke, Remap(line.InactiveKaraoke, map));
        }
        var inactiveIds = line.InactiveKaraoke.Select(clip => clip.Id).ToHashSet();
        var clips = line.Karaoke.AddRange(line.InactiveKaraoke).OrderBy(clip => clip.Utf16Start).ToImmutableArray();
        var remapped = Remap(clips, map);
        return (
            Reuse(line.Karaoke, remapped.Where(clip => !inactiveIds.Contains(clip.Id)).ToImmutableArray()),
            Reuse(line.InactiveKaraoke, remapped.Where(clip => inactiveIds.Contains(clip.Id)).ToImmutableArray()));
    }

    private static ImmutableArray<KaraokeSegment> Remap(ImmutableArray<KaraokeSegment> clips,
        SubtitleTextEditMap map)
    {
        if (clips.IsEmpty || map.Text.Length == 0)
        {
            return [];
        }
        var replacementOwner = -1;
        for (var index = 0; index < clips.Length; index++)
        {
            var clip = clips[index];
            var end = clip.Utf16Start + clip.Utf16Length;
            var contains = map.OriginalStart == map.OriginalEnd
                ? clip.Utf16Start < map.OriginalStart && map.OriginalStart < end
                : clip.Utf16Start <= map.OriginalStart && map.OriginalEnd <= end;
            if (contains)
            {
                replacementOwner = index;
                break;
            }
        }
        var result = ImmutableArray.CreateBuilder<KaraokeSegment>();
        var completed = new HashSet<Guid>();
        for (var index = 0; index < map.NewBoundaries.Length - 1; index++)
        {
            var start = map.NewBoundaries[index];
            var end = map.NewBoundaries[index + 1];
            int? owner = null;
            if (start < map.OriginalStart)
            {
                CollectOwners(clips, start, Math.Min(end, map.OriginalStart), ref owner);
            }
            if (start < map.OriginalNewEnd && end > map.OriginalStart)
            {
                var mappedOwner = replacementOwner;
                if (mappedOwner < 0 && map.OriginalStart != map.OriginalEnd && map.OldCount == map.NewCount)
                {
                    mappedOwner = OwnerAt(clips, map.StyleSourceOffset(start));
                }
                IncludeOwner(mappedOwner, ref owner);
            }
            if (end > map.OriginalNewEnd)
            {
                CollectOwners(clips, Math.Max(start, map.OriginalNewEnd) - map.Delta,
                    end - map.Delta, ref owner);
            }
            if (owner is not >= 0)
            {
                continue;
            }
            var template = clips[owner.Value];
            if (result.Count > 0 && result[^1].Id == template.Id &&
                result[^1].Utf16Start + result[^1].Utf16Length == start)
            {
                var groupStart = result[^1].Utf16Start;
                result[^1] = template.Utf16Start == groupStart && template.Utf16Length == end - groupStart
                    ? template : result[^1] with { Utf16Length = end - groupStart };
            }
            else
            {
                if (!completed.Add(template.Id))
                {
                    throw new InvalidOperationException("编辑会拆开同一计时组，请先显式拆分计时组。");
                }
                result.Add(template.Utf16Start == start && template.Utf16Length == end - start
                    ? template : template with { Utf16Start = start, Utf16Length = end - start });
            }
        }
        return Reuse(clips, result.ToImmutable());
    }

    private static void CollectOwners(ImmutableArray<KaraokeSegment> clips, int start, int end, ref int? owner)
    {
        var cursor = start;
        while (cursor < end)
        {
            var index = OwnerAt(clips, cursor);
            IncludeOwner(index, ref owner);
            if (index >= 0)
            {
                cursor = Math.Min(end, clips[index].Utf16Start + clips[index].Utf16Length);
            }
            else
            {
                cursor = Math.Min(end, NextClipStart(clips, cursor, end));
            }
        }
    }

    private static void IncludeOwner(int candidate, ref int? owner)
    {
        if (owner.HasValue && owner.Value != candidate)
        {
            throw new InvalidOperationException("编辑会合并不同计时归属的字素，请先调整文字或分组。");
        }
        owner = candidate;
    }

    private static int OwnerAt(ImmutableArray<KaraokeSegment> clips, int offset)
    {
        var low = 0;
        var high = clips.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var clip = clips[middle];
            if (offset < clip.Utf16Start)
            {
                high = middle - 1;
            }
            else if (offset >= clip.Utf16Start + clip.Utf16Length)
            {
                low = middle + 1;
            }
            else
            {
                return middle;
            }
        }
        return -1;
    }

    private static int NextClipStart(ImmutableArray<KaraokeSegment> clips, int offset, int fallback)
    {
        var low = 0;
        var high = clips.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (clips[middle].Utf16Start <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low < clips.Length ? clips[low].Utf16Start : fallback;
    }

    private static ImmutableArray<T> Reuse<T>(ImmutableArray<T> original, ImmutableArray<T> next)
    {
        return original.SequenceEqual(next) ? original : next;
    }
}
