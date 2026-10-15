using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

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
                diagnostics.Add(new("Srt.Appearance", "SRT preserves only text and timing. Local styles, karaoke, and layout will be discarded.", SubtitleId: line.Id));
            }
        }
        foreach (var layer in document.Layers)
        {
            if (layer.Effects.Any(instance => instance.IsEnabled))
            {
                diagnostics.Add(new("Srt.EffectInstances", "SRT does not support effect instances. Enabled effects, script parameters, and animation will be discarded during export.",
                    SubtitleId: layer.SubtitleId, LayerId: layer.SubtitleId is null ? layer.Id : null));
            }
            if (layer.Blur > 0)
            {
                diagnostics.Add(new("Srt.Blur", "SRT does not support whole-layer blur. Blur will be discarded during export.", SubtitleId: layer.SubtitleId,
                    LayerId: layer.SubtitleId is null ? layer.Id : null));
            }
            if (layer.Mask is not null)
            {
                diagnostics.Add(new("Srt.Mask", "SRT does not support clipping masks. Mask geometry will be discarded during export.", SubtitleId: layer.SubtitleId,
                    LayerId: layer.SubtitleId is null ? layer.Id : null));
            }
            if (!layer.Tracks.IsEmpty)
            {
                diagnostics.Add(new("Srt.Animation", "SRT does not support clip or mask animation. Animation tracks will be discarded during export.", SubtitleId: layer.SubtitleId,
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
                layer.Effects.Any(instance => instance.IsEnabled) ||
                layer.Tracks.Any(track => !supportsMasks || !AnimationPropertyMetadata.IsMaskProperty(track.Property)) || layer.MotionPath is not null || !supportsMasks && layer.Mask is not null ||
                layer.Transform != new LayerTransform() || !layer.Opacity.Equals(1d) || layer.Blend != BlendMode.NORMAL)
            {
                diagnostics.Add(new("Subtitle.Composition", "Subtitle formats cannot preserve project compositing, animation, or shape layers.", SubtitleId: layer.SubtitleId,
                    LayerId: layer.SubtitleId is null ? layer.Id : null));
            }
        }
    }

    internal static void AddAssKaraokeLoss(SubtitleLine line, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (!line.InactiveKaraoke.IsEmpty)
        {
            diagnostics.Add(new("Ass.InactiveKaraoke", "ASS cannot store cached timing, modes, and default highlight colors for disabled karaoke groups. This text was exported as ordinary text.", SubtitleId: line.Id));
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
                diagnostics.Add(new("Ass.DormantKaraokeStyle", "Stored ACTIVE/INACTIVE appearance for untimed text cannot be written to ASS. The text was exported as ordinary text; highlight style ranges in the project were preserved.", SubtitleId: line.Id));
                return;
            }
        }
    }

}
