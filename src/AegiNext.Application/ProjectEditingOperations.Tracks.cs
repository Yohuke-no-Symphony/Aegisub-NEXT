using System.Collections.Immutable;
using System.Numerics;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>更新指定轨道的默认样式，可选择同步现有字幕；效果层、时间和逐字高亮保持原样。</summary>
    public static ProjectDocument SetSubtitleTrackStyle(ProjectDocument document, Guid trackId,
        Guid presetId, string presetName, SubtitleStyle style, bool updateExisting = false)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        var subtitleIds = new ProjectClipIndex(document).GetTrackSubtitles(trackId).Select(line => line.Id).ToHashSet();
        var track = document.Tracks[index] with
        {
            DefaultStyle = style,
            StylePresetId = presetId,
            StylePresetName = presetName
        };
        if (track == document.Tracks[index] &&
            (!updateExisting || document.Subtitles.Where(line => subtitleIds.Contains(line.Id)).All(line =>
                line.Style == style && line.StyleName == presetName && line.StylePresetId == presetId && line.InlineSpans.IsEmpty)))
        {
            return document;
        }

        var prepared = updateExisting ? SubtitleStyleReplacement.Apply(document, subtitleIds, style) : document;
        return Verified(prepared with
        {
            Tracks = document.Tracks.SetItem(index, track),
            Subtitles = updateExisting ? prepared.Subtitles.Select(line => subtitleIds.Contains(line.Id)
                ? line with { StyleName = presetName, StylePresetId = presetId } : line).ToImmutableArray() : document.Subtitles
        });
    }

    /// <summary>切换轨道新字幕的自动样式应用；不修改现有字幕或保存的默认样式。</summary>
    public static ProjectDocument SetSubtitleTrackAutoApplyStyle(ProjectDocument document, Guid trackId, bool enabled)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        var track = document.Tracks[index];
        return track.AutoApplyStyle == enabled ? document : Verified(document with
        {
            Tracks = document.Tracks.SetItem(index, track with { AutoApplyStyle = enabled })
        });
    }

    /// <summary>在一个已准备资源的快照中创建字幕片段，继承启用的轨道默认值或提供的备用样式。</summary>
    public static ProjectDocument CreateSubtitleClips(ProjectDocument document, IEnumerable<SubtitleLine> lines,
        Guid trackId, SubtitleStyle? fallbackStyle = null)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(lines);
        var track = document.Tracks[TrackIndex(document, trackId)];
        var style = track.AutoApplyStyle && track.DefaultStyle is { } defaultStyle
            ? defaultStyle : fallbackStyle ?? new SubtitleStyle();
        var imported = lines.Select(line => SubtitleStyleScaling.ConvertUnits(line, style.Scaling, document.Width, document.Height) with
        {
            Style = style,
            StyleName = track.AutoApplyStyle && track.DefaultStyle is not null ? track.StylePresetName! : line.StyleName,
            StylePresetId = track.AutoApplyStyle && track.DefaultStyle is not null ? track.StylePresetId : line.StylePresetId
        }).ToImmutableArray();
        if (imported.IsEmpty)
        {
            return document;
        }

        return Verified(document with
        {
            Subtitles = document.Subtitles.AddRange(imported),
            Layers = document.Layers.AddRange(imported.Select(line => new ProjectLayer
            {
                Id = line.Id, Name = "Subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, TrackId = trackId,
                Start = line.Start, End = line.End
            }))
        });
    }

    /// <summary>新增轨道并验证完整快照，不重新创建任何片段。</summary>
    public static ProjectDocument AddTrack(ProjectDocument document, ProjectTrack track)
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(track);
        return Verified(document with { Tracks = document.Tracks.Add(track) });
    }

    /// <summary>重命名存在的轨道，无变化时保留原快照。</summary>
    public static ProjectDocument RenameTrack(ProjectDocument document, Guid trackId, string name)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        var track = document.Tracks[index];
        return track.Name == name ? document : Verified(document with
        {
            Tracks = document.Tracks.SetItem(index, track with { Name = name })
        });
    }

    /// <summary>删除轨道及其全部类型片段；允许删除最后一条轨道，保留其他轨道和共享资源。</summary>
    public static ProjectDocument RemoveTrack(ProjectDocument document, Guid trackId)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        var layerIds = document.Layers.Where(layer => layer.TrackId == trackId)
            .Select(layer => layer.Id).ToImmutableArray();
        var result = RemoveClips(document, layerIds);
        return Verified(result with { Tracks = result.Tracks.RemoveAt(index) });
    }

    /// <summary>调整轨道显示和叠覆顺序；最上方轨道最后绘制，所有片段身份保持原样。</summary>
    public static ProjectDocument MoveTrack(ProjectDocument document, Guid trackId, int newIndex)
    {
        ProjectValidator.Validate(document);
        var index = TrackIndex(document, trackId);
        ArgumentOutOfRangeException.ThrowIfNegative(newIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newIndex, document.Tracks.Length);
        return index == newIndex ? document : document with
        {
            Tracks = document.Tracks.RemoveAt(index).Insert(newIndex, document.Tracks[index])
        };
    }

    /// <summary>转移字幕所属轨道，保留时间与片段效果；任一类型的同轨重叠会整体拒绝。</summary>
    public static ProjectDocument MoveSubtitleToTrack(ProjectDocument document, Guid subtitleId, Guid trackId)
    {
        ProjectValidator.Validate(document);
        var clip = new ProjectClipIndex(document).GetSubtitleClip(subtitleId);
        return MoveClip(document, clip.Id, trackId, clip.Start, clip.End, TimelineEditMode.CROP, true);
    }

    /// <summary>原子调整字幕片段的轨道和区间，保留内容相位并按裁剪或拉伸处理动画。</summary>
    public static ProjectDocument MoveSubtitleClip(ProjectDocument document, Guid subtitleId, Guid trackId,
        MediaTime start, MediaTime end, TimelineEditMode mode, bool move)
    {
        ProjectValidator.Validate(document);
        var clip = new ProjectClipIndex(document).GetSubtitleClip(subtitleId);
        return MoveClip(document, clip.Id, trackId, start, end, mode, move);
    }

    /// <summary>一次性修改任意类型片段的轨道与时间；移动保持时长，裁剪和拉伸保留各自内容时钟。</summary>
    public static ProjectDocument MoveClip(ProjectDocument document, Guid clipId, Guid trackId,
        MediaTime start, MediaTime end, TimelineEditMode mode, bool move)
    {
        ProjectValidator.Validate(document);
        TrackIndex(document, trackId);
        var clipIndex = -1;
        for (var index = 0; index < document.Layers.Length; index++)
        {
            if (document.Layers[index].Id == clipId)
            {
                clipIndex = index;
                break;
            }
        }
        if (clipIndex < 0)
        {
            throw new KeyNotFoundException("片段不存在。");
        }

        var clip = document.Layers[clipIndex];
        if (start >= end || !Enum.IsDefined(mode) || move && end - start != clip.End - clip.Start)
        {
            throw new ArgumentException("片段区间或模式无效，整体移动必须保持时长。", nameof(end));
        }
        if (clip.TrackId == trackId && clip.Start == start && clip.End == end)
        {
            return document;
        }

        var changed = (move ? clip with { Start = start, End = end }
            : LayerAnimationTiming.Retime(clip, start, end, mode)) with { TrackId = trackId };
        var subtitles = document.Subtitles;
        if (clip.SubtitleId is { } subtitleId)
        {
            var subtitleIndex = SubtitleIndex(document, subtitleId);
            var line = subtitles[subtitleIndex];
            var karaoke = line.Karaoke;
            var inactiveKaraoke = line.InactiveKaraoke;
            if (!move && mode == TimelineEditMode.STRETCH)
            {
                karaoke = ScaleTrackKaraoke(karaoke, end - start, line.End - line.Start);
                inactiveKaraoke = ScaleTrackKaraoke(inactiveKaraoke, end - start, line.End - line.Start);
            }
            subtitles = subtitles.SetItem(subtitleIndex, line with
            {
                Start = start, End = end, Karaoke = karaoke, InactiveKaraoke = inactiveKaraoke
            });
        }

        return Verified(document with { Subtitles = subtitles, Layers = document.Layers.SetItem(clipIndex, changed) });
    }

    private static int TrackIndex(ProjectDocument document, Guid id)
    {
        for (var index = 0; index < document.Tracks.Length; index++)
        {
            if (document.Tracks[index].Id == id)
            {
                return index;
            }
        }

        throw new KeyNotFoundException("轨道不存在。");
    }

    private static ImmutableArray<ProjectLayer> MapTrackLayers(ImmutableArray<ProjectLayer> layers, Func<ProjectLayer, ProjectLayer> edit)
    {
        ImmutableArray<ProjectLayer>.Builder? changed = null;
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var next = edit(layer);
            if (next != layer)
            {
                changed ??= layers.ToBuilder();
                changed[index] = next;
            }
        }

        return changed?.ToImmutable() ?? layers;
    }

    private static MediaTime ScaleTrackTime(MediaTime time, MediaTime newDuration, MediaTime oldDuration)
    {
        var numerator = (BigInteger)time.Numerator * newDuration.Numerator * oldDuration.Denominator;
        var denominator = (BigInteger)time.Denominator * newDuration.Denominator * oldDuration.Numerator;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }

    private static ImmutableArray<KaraokeSegment> ScaleTrackKaraoke(ImmutableArray<KaraokeSegment> segments,
        MediaTime newDuration, MediaTime oldDuration)
    {
        return segments.Select(segment => segment with
        {
            Start = ScaleTrackTime(segment.Start, newDuration, oldDuration),
            End = ScaleTrackTime(segment.End, newDuration, oldDuration)
        }).ToImmutableArray();
    }
}
