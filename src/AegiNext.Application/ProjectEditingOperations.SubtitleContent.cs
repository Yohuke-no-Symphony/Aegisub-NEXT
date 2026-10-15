using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>替换字素安全的文字范围，同时迁移局部样式和局部字时间，保留字幕与图层时间。</summary>
    public static ProjectDocument ReplaceSubtitleTextRange(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length, string replacement)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        return WithSubtitleContent(document, index,
            SubtitleContentEditing.ReplaceText(document.Subtitles[index], utf16Start, utf16Length, replacement));
    }

    /// <summary>将明确设置的样式字段合并到选区；不修改其他字段、字时间或图层。</summary>
    public static ProjectDocument ApplySubtitleInlineStyle(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length, SubtitleInlineStyleOverride style)
    {
        ArgumentNullException.ThrowIfNull(style);
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        return WithSubtitleContent(document, index,
            SubtitleContentEditing.ApplyInlineStyle(document.Subtitles[index], utf16Start, utf16Length, style));
    }

    /// <summary>清除选区的局部覆盖，使文字重新继承整行样式，保留字时间。</summary>
    public static ProjectDocument ClearSubtitleInlineStyle(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        return WithSubtitleContent(document, index,
            SubtitleContentEditing.ApplyInlineStyle(document.Subtitles[index], utf16Start, utf16Length, null));
    }

    /// <summary>替换整行样式并清除局部覆盖；保留卡拉 OK 身份、时间和高亮配置。</summary>
    public static ProjectDocument ApplySubtitleStyle(ProjectDocument document, Guid subtitleId, SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        return line.Style == style && line.InlineSpans.IsEmpty ? document : Verified(
            SubtitleStyleReplacement.Apply(document, new HashSet<Guid> { subtitleId }, style));
    }

    /// <summary>精确修改逐字片段时长并顺延后续片段；允许超出字幕可见范围，保留字幕与图层起止。</summary>
    public static ProjectDocument SetKaraokeClipDuration(ProjectDocument document, Guid subtitleId, Guid clipId, MediaTime duration)
    {
        ProjectValidator.Validate(document);
        if (duration <= MediaTime.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "卡拉 OK 片段必须具有正时长。");
        }
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        var clipIndex = KaraokeClipIndex(line, clipId);
        var clip = line.Karaoke[clipIndex];
        return WithSubtitleContent(document, index, line with
        {
            Karaoke = KaraokeTimingEditing.SetRange(line.Karaoke, clipId, clip.Start, clip.Start + duration, true)
        });
    }

    private static ProjectDocument WithSubtitleContent(ProjectDocument document, int index, SubtitleLine line)
    {
        return line == document.Subtitles[index] ? document : Verified(document with
        {
            Subtitles = document.Subtitles.SetItem(index, line),
            Layers = MapTrackLayers(document.Layers, layer => layer.SubtitleId == line.Id
                ? PruneEffectInstanceTargets(SubtitleAnimationRangeEditing.PruneTargets(layer, line), line) : layer)
        });
    }
}
