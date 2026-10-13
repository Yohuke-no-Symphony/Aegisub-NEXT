using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>完整动画目标的静态基础值编辑，保留无关样式及范围身份，不提交历史事务。</summary>
public static class SubtitleAnimationEditing
{
    /// <summary>修改完整目标的静态基础值；范围样式拆分并归一已有局部覆盖，局部变换保存在范围自身。</summary>
    public static ProjectDocument SetBaseValue(ProjectDocument document, Guid layerId, AnimationTrackTarget target,
        AnimationValue value)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layer = document.Layers.Single(layer => layer.Id == layerId);
        var subtitle = layer.SubtitleId is { } subtitleId ? document.Subtitles.Single(line => line.Id == subtitleId) : null;
        SubtitleAnimationTargetValidation.Validate(target, subtitle, layer.Mask);
        ValidateValue(target.Property, value);
        if (AnimationPropertyMetadata.IsMaskProperty(target.Property))
        {
            return ReplaceLayer(document, layer, layer with { Mask = ClipMaskAnimation.SetBaseValue(layer.Mask!, target, value) });
        }
        if (target.TextRangeId is { } rangeId)
        {
            var range = subtitle!.AnimationRanges.Single(range => range.Id == rangeId);
            if (target.Property is AnimationProperty.POSITION or AnimationProperty.SCALE or AnimationProperty.ROTATION or
                AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y or AnimationProperty.PERSPECTIVE_DISTANCE)
            {
                var changed = target.Property switch
                {
                    AnimationProperty.POSITION => range with { Offset = value.Vector },
                    AnimationProperty.SCALE => range with { Scale = value.Vector },
                    AnimationProperty.ROTATION => range with { Rotation = value.Scalar },
                    AnimationProperty.ROTATION_X => range with { RotationX = value.Scalar },
                    AnimationProperty.ROTATION_Y => range with { RotationY = value.Scalar },
                    _ => range with { PerspectiveDistance = value.Scalar }
                };
                return ReplaceSubtitle(document, subtitle, subtitle with
                {
                    AnimationRanges = subtitle.AnimationRanges.SetItem(subtitle.AnimationRanges.IndexOf(range), changed)
                });
            }
            return ReplaceSubtitle(document, subtitle, SetStyleRange(subtitle, range.Utf16Start, range.Utf16Length, target, value));
        }
        if (subtitle is not null && (AnimationPropertyMetadata.IsSubtitleOnlyProperty(target.Property) ||
            AnimationPropertyMetadata.IsSubtitleVisualProperty(target.Property)))
        {
            var changed = target.State == SubtitleAnimationState.NORMAL
                ? subtitle with { Style = SubtitleAnimationEvaluation.WithStyleValue(subtitle.Style, target.Property, value) }
                : SetStyleRange(subtitle, 0, subtitle.Text.Length, target, value);
            return ReplaceSubtitle(document, subtitle, changed);
        }
        var updated = target.Property switch
        {
            AnimationProperty.POSITION => layer with { Transform = layer.Transform with { Position = value.Vector } },
            AnimationProperty.SCALE => layer with { Transform = layer.Transform with { Scale = value.Vector } },
            AnimationProperty.ROTATION => layer with { Transform = layer.Transform with { Rotation = value.Scalar } },
            AnimationProperty.ROTATION_X => layer with { Transform = layer.Transform with { RotationX = value.Scalar } },
            AnimationProperty.ROTATION_Y => layer with { Transform = layer.Transform with { RotationY = value.Scalar } },
            AnimationProperty.PERSPECTIVE_DISTANCE => layer with { Transform = layer.Transform with { PerspectiveDistance = value.Scalar } },
            AnimationProperty.OPACITY => layer with { Opacity = value.Scalar },
            AnimationProperty.BLUR => layer with { Blur = value.Scalar },
            AnimationProperty.FILL => layer with { Fill = value.Color },
            AnimationProperty.STROKE => layer with { Stroke = value.Color },
            AnimationProperty.STROKE_WIDTH => layer with { StrokeWidth = value.Scalar },
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        return ReplaceLayer(document, layer, updated);
    }

    private static SubtitleLine SetStyleRange(SubtitleLine subtitle, int start, int length,
        AnimationTrackTarget target, AnimationValue value)
    {
        if (length <= 0)
        {
            throw new InvalidDataException("状态基础样式需要非空文字范围。");
        }
        var end = checked(start + length);
        if (target.State == SubtitleAnimationState.NORMAL)
        {
            var overlay = InlineOverride(target.Property, value);
            var boundaries = subtitle.InlineSpans.SelectMany(span => new[] { span.Utf16Start, span.Utf16Start + span.Utf16Length })
                .Append(start).Append(end).Distinct().Order().ToArray();
            var result = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
            var source = 0;
            for (var index = 0; index + 1 < boundaries.Length; index++)
            {
                var first = boundaries[index];
                var last = boundaries[index + 1];
                while (source < subtitle.InlineSpans.Length && subtitle.InlineSpans[source].Utf16Start + subtitle.InlineSpans[source].Utf16Length <= first)
                {
                    source++;
                }
                var original = source < subtitle.InlineSpans.Length && subtitle.InlineSpans[source].Utf16Start <= first
                    ? subtitle.InlineSpans[source].Style : new SubtitleInlineStyleOverride();
                var style = first >= start && last <= end ? original.Merge(overlay) : original;
                if (!style.HasOverrides)
                {
                    continue;
                }
                if (result.Count > 0 && result[^1].Utf16Start + result[^1].Utf16Length == first && result[^1].Style == style)
                {
                    result[^1] = result[^1] with { Utf16Length = last - result[^1].Utf16Start };
                }
                else
                {
                    result.Add(new(first, last - first, style));
                }
            }
            var spans = result.ToImmutable();
            return spans.SequenceEqual(subtitle.InlineSpans) ? subtitle : subtitle with { InlineSpans = spans };
        }
        var visual = VisualOverride(target.Property, value);
        var cuts = subtitle.KaraokeStyleSpans.SelectMany(span => new[] { span.Utf16Start, span.Utf16Start + span.Utf16Length })
            .Append(start).Append(end).Distinct().Order().ToArray();
        var visualResult = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
        var visualSource = 0;
        for (var index = 0; index + 1 < cuts.Length; index++)
        {
            var first = cuts[index];
            var last = cuts[index + 1];
            while (visualSource < subtitle.KaraokeStyleSpans.Length &&
                subtitle.KaraokeStyleSpans[visualSource].Utf16Start + subtitle.KaraokeStyleSpans[visualSource].Utf16Length <= first)
            {
                visualSource++;
            }
            var original = visualSource < subtitle.KaraokeStyleSpans.Length && subtitle.KaraokeStyleSpans[visualSource].Utf16Start <= first
                ? subtitle.KaraokeStyleSpans[visualSource] : null;
            var active = original?.ActiveStyle;
            var inactive = original?.InactiveStyle;
            if (first >= start && last <= end)
            {
                if (target.State == SubtitleAnimationState.ACTIVE)
                {
                    active = active?.Merge(visual) ?? visual;
                }
                else
                {
                    inactive = inactive?.Merge(visual) ?? visual;
                }
            }
            if (active is not { HasOverrides: true } && inactive is not { HasOverrides: true })
            {
                continue;
            }
            if (visualResult.Count > 0 && visualResult[^1].Utf16Start + visualResult[^1].Utf16Length == first &&
                visualResult[^1].ActiveStyle == active && visualResult[^1].InactiveStyle == inactive)
            {
                visualResult[^1] = visualResult[^1] with { Utf16Length = last - visualResult[^1].Utf16Start };
            }
            else
            {
                visualResult.Add(new(first, last - first, active, inactive));
            }
        }
        var visualSpans = visualResult.ToImmutable();
        return visualSpans.SequenceEqual(subtitle.KaraokeStyleSpans) ? subtitle : subtitle with { KaraokeStyleSpans = visualSpans };
    }

    private static SubtitleInlineStyleOverride InlineOverride(AnimationProperty property, AnimationValue value)
    {
        return property switch
        {
            AnimationProperty.FONT_SIZE => new() { FontSize = value.Scalar },
            AnimationProperty.LETTER_SPACING => new() { LetterSpacing = value.Scalar },
            AnimationProperty.FILL => new() { Fill = value.Color },
            AnimationProperty.STROKE => new() { Stroke = value.Color },
            AnimationProperty.STROKE_WIDTH => new() { StrokeWidth = value.Scalar },
            AnimationProperty.FILL_BLUR => new() { FillBlur = value.Scalar },
            AnimationProperty.STROKE_BLUR => new() { StrokeBlur = value.Scalar },
            AnimationProperty.SHADOW_OFFSET => new() { ShadowOffset = value.Vector },
            AnimationProperty.SHADOW_BLUR => new() { ShadowBlur = value.Scalar },
            AnimationProperty.SHADOW_COLOR => new() { ShadowColor = value.Color },
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }

    private static KaraokeVisualStyleOverride VisualOverride(AnimationProperty property, AnimationValue value)
    {
        return property switch
        {
            AnimationProperty.FILL => new() { Fill = value.Color },
            AnimationProperty.STROKE => new() { Stroke = value.Color },
            AnimationProperty.STROKE_WIDTH => new() { StrokeWidth = value.Scalar },
            AnimationProperty.FILL_BLUR => new() { FillBlur = value.Scalar },
            AnimationProperty.STROKE_BLUR => new() { StrokeBlur = value.Scalar },
            AnimationProperty.SHADOW_OFFSET => new() { ShadowOffset = value.Vector },
            AnimationProperty.SHADOW_BLUR => new() { ShadowBlur = value.Scalar },
            AnimationProperty.SHADOW_COLOR => new() { ShadowColor = value.Color },
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }

    private static void ValidateValue(AnimationProperty property, AnimationValue value)
    {
        if (value.Kind != AnimationPropertyMetadata.GetValueKind(property))
        {
            throw new InvalidDataException("基础值维度与动画属性不一致。");
        }
        for (var component = 0; component < value.ComponentCount; component++)
        {
            var number = value.GetComponent(component);
            if (!double.IsFinite(number) || number < AnimationPropertyMetadata.GetMinimum(property, component) ||
                number > AnimationPropertyMetadata.GetMaximum(property, component))
            {
                throw new InvalidDataException("动画基础值超出属性范围。");
            }
        }
    }

    private static ProjectDocument ReplaceLayer(ProjectDocument document, ProjectLayer original, ProjectLayer changed)
    {
        return original == changed ? document : document with { Layers = document.Layers.SetItem(document.Layers.IndexOf(original), changed) };
    }

    private static ProjectDocument ReplaceSubtitle(ProjectDocument document, SubtitleLine original, SubtitleLine changed)
    {
        return original == changed ? document : document with { Subtitles = document.Subtitles.SetItem(document.Subtitles.IndexOf(original), changed) };
    }
}
