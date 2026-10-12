using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

/// <summary>在外部播放时间与既有工程时间之间平移字幕绝对区间，不改动内容相对时钟。</summary>
public static class SubtitleTimelineExchange
{
    /// <summary>未绑定媒体使用恒等映射；已绑定但未确认播放零点时返回未知。</summary>
    public static MediaTimelineMapping? GetMapping(ProjectMediaBinding? binding)
    {
        if (binding is null)
        {
            return default(MediaTimelineMapping);
        }
        return binding.PlaybackOrigin is { } origin ? new(binding.MediaOrigin - origin) : null;
    }

    /// <summary>平移外部字幕的绝对起止时间，保留标识、样式和卡拉 OK 相对时间。</summary>
    public static ImmutableArray<SubtitleLine> ToProjectTime(IEnumerable<SubtitleLine> lines, MediaTimelineMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(line => line with
        {
            Start = mapping.ToProjectTime(line.Start), End = mapping.ToProjectTime(line.End)
        }).ToImmutableArray();
    }

    /// <summary>同时平移 ASS 行及片段的对应行，不修改蒙版、轨道及内容偏移。</summary>
    public static AssImportResult ToProjectTime(AssImportResult imported, MediaTimelineMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(imported);
        if (!imported.Clips.IsEmpty && (imported.Clips.Length != imported.Lines.Length ||
            !imported.Clips.Select(clip => clip.Line).SequenceEqual(imported.Lines)))
        {
            throw new InvalidDataException("ASS 导入片段与字幕身份不一致。");
        }
        var lines = ToProjectTime(imported.Lines, mapping);
        return imported with
        {
            Lines = lines,
            Clips = imported.Clips.Select((clip, index) => clip with { Line = lines[index] }).ToImmutableArray()
        };
    }
}
