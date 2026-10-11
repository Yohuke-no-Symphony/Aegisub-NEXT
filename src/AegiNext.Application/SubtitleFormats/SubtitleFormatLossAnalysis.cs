using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application.SubtitleFormats;

/// <summary>导出前的显式能力损失分析。</summary>
public static class SubtitleFormatLossAnalysis
{
    /// <summary>SRT 只保留纯文本及时间；列出被舍弃的排版、富文本、卡拉 OK 和合成效果。</summary>
    public static ImmutableArray<SubtitleFormatDiagnostic> ForSrt(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        foreach (var line in document.Subtitles)
        {
            if (!line.InlineSpans.IsEmpty || !line.Karaoke.IsEmpty || line.Style != new SubtitleStyle())
            {
                diagnostics.Add(new("Srt.Appearance", "SRT 仅保留文字和时间，局部样式、卡拉 OK 与排版将被舍弃。", SubtitleId: line.Id));
            }
        }
        foreach (var layer in document.Layers)
        {
            if (layer.Blur > 0)
            {
                diagnostics.Add(new("Srt.Blur", "SRT 不支持整层模糊，导出时模糊将被舍弃。", SubtitleId: layer.SubtitleId,
                    LayerId: layer.SubtitleId is null ? layer.Id : null));
            }
            if (layer.Mask is not null)
            {
                diagnostics.Add(new("Srt.Mask", "SRT 不支持裁切蒙版，导出时蒙版几何将被舍弃。", SubtitleId: layer.SubtitleId,
                    LayerId: layer.SubtitleId is null ? layer.Id : null));
            }
            if (!layer.Tracks.IsEmpty)
            {
                diagnostics.Add(new("Srt.Animation", "SRT 不支持片段及蒙版动画，导出时动画轨道将被舍弃。", SubtitleId: layer.SubtitleId,
                    LayerId: layer.SubtitleId is null ? layer.Id : null));
            }
        }
        AddCompositionLoss(document, diagnostics);
        return diagnostics.ToImmutable();
    }

    internal static void AddCompositionLoss(ProjectDocument document, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, bool supportsMasks = false)
    {
        foreach (var layer in document.Layers)
        {
            if (layer.Kind != LayerKind.SUBTITLE ||
                layer.Tracks.Any(track => !supportsMasks || !AnimationPropertyMetadata.IsMaskProperty(track.Property)) || layer.MotionPath is not null || !supportsMasks && layer.Mask is not null ||
                layer.Transform != new LayerTransform() || !layer.Opacity.Equals(1d) || layer.Blend != BlendMode.NORMAL)
            {
                diagnostics.Add(new("Subtitle.Composition", "字幕格式不能保留项目合成、动画或图形图层。", SubtitleId: layer.SubtitleId,
                    LayerId: layer.SubtitleId is null ? layer.Id : null));
            }
        }
    }

    internal static void AddAssKaraokeLoss(SubtitleLine line, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (!line.InactiveKaraoke.IsEmpty)
        {
            diagnostics.Add(new("Ass.InactiveKaraoke", "ASS 无法保存停用演唱组的缓存时间、模式和默认高亮颜色；这些文字按普通正文导出。", SubtitleId: line.Id));
        }
        if (line.KaraokeStyleSpans.IsEmpty && line.KaraokeStyle is null)
        {
            return;
        }
        var boundaries = new SortedSet<int> { 0, line.Text.Length };
        foreach (var span in line.InlineSpans)
        {
            boundaries.Add(span.Utf16Start);
            boundaries.Add(span.Utf16Start + span.Utf16Length);
        }
        foreach (var span in line.KaraokeStyleSpans)
        {
            boundaries.Add(span.Utf16Start);
            boundaries.Add(span.Utf16Start + span.Utf16Length);
        }
        foreach (var group in line.Karaoke)
        {
            boundaries.Add(group.Utf16Start);
            boundaries.Add(group.Utf16Start + group.Utf16Length);
        }
        var offsets = boundaries.ToArray();
        var clipIndex = 0;
        var inlineIndex = 0;
        for (var index = 0; index < offsets.Length - 1; index++)
        {
            var offset = offsets[index];
            while (clipIndex < line.Karaoke.Length && line.Karaoke[clipIndex].Utf16Start + line.Karaoke[clipIndex].Utf16Length <= offset)
            {
                clipIndex++;
            }
            if (clipIndex < line.Karaoke.Length && line.Karaoke[clipIndex].Utf16Start <= offset)
            {
                continue;
            }
            while (inlineIndex < line.InlineSpans.Length && line.InlineSpans[inlineIndex].Utf16Start + line.InlineSpans[inlineIndex].Utf16Length <= offset)
            {
                inlineIndex++;
            }
            var ordinary = inlineIndex < line.InlineSpans.Length && line.InlineSpans[inlineIndex].Utf16Start <= offset
                ? line.InlineSpans[inlineIndex].Style.ApplyTo(line.Style) : line.Style;
            var defaultActive = KaraokeVisualStyleResolver.ResolveActive(ordinary, null, null);
            var active = KaraokeVisualStyleResolver.ResolveActive(ordinary, line.KaraokeStyle, null,
                KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.ACTIVE));
            var inactive = KaraokeVisualStyleResolver.ResolveInactive(ordinary, null,
                KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.INACTIVE));
            if (active != defaultActive || inactive != ordinary)
            {
                diagnostics.Add(new("Ass.DormantKaraokeStyle", "未计时文字保存的 ACTIVE/INACTIVE 外观无法写入 ASS；已按普通正文导出，保留项目中的高亮样式范围。", SubtitleId: line.Id));
                return;
            }
        }
    }

}
