using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>字幕动画静态继承与逐状态绘制求值，不依赖渲染资源或编辑历史。</summary>
public static class SubtitleAnimationEvaluation
{
    private static readonly ImmutableArray<AnimationProperty> styleProperties =
    [
        AnimationProperty.FONT_SIZE, AnimationProperty.LETTER_SPACING, AnimationProperty.FILL,
        AnimationProperty.STROKE, AnimationProperty.STROKE_WIDTH, AnimationProperty.FILL_BLUR,
        AnimationProperty.STROKE_BLUR, AnimationProperty.SHADOW_OFFSET, AnimationProperty.SHADOW_BLUR,
        AnimationProperty.SHADOW_COLOR
    ];

    /// <summary>在字素的静态样式上按统一栈执行指定状态的整句及范围操作。</summary>
    public static SubtitleStyle ApplyStyleAnimations(EvaluatedLayer layer, SubtitleStyle style, int utf16Offset,
        SubtitleAnimationState state, bool includeTypography = true)
    {
        return ApplyAnimations(layer, style, utf16Offset, state, true, includeTypography, false);
    }

    /// <summary>在排版前计算当前字素的字号和字距，不重复应用绘制属性。</summary>
    public static SubtitleStyle ApplyTypographyAnimations(EvaluatedLayer layer, SubtitleStyle style, int utf16Offset,
        bool includeRanges = true)
    {
        return ApplyAnimations(layer, style, utf16Offset, SubtitleAnimationState.NORMAL, includeRanges, true, true);
    }

    private static SubtitleStyle ApplyAnimations(EvaluatedLayer layer, SubtitleStyle style, int utf16Offset,
        SubtitleAnimationState state, bool includeRanges, bool includeTypography, bool typographyOnly)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(style);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        var subtitle = layer.Subtitle;
        if (subtitle is null || utf16Offset < 0 || utf16Offset > subtitle.Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(utf16Offset));
        }
        if (layer.AnimationValues.IsEmpty && layer.EffectStyleOperations.IsEmpty)
        {
            return style;
        }
        if (!layer.HasOrderedAnimationStack)
        {
            style = ApplyScope(layer, style, null, state, includeTypography, typographyOnly);
            if (includeRanges)
            {
                foreach (var range in subtitle.AnimationRanges)
                {
                    if (utf16Offset >= range.Utf16Start && utf16Offset < range.Utf16Start + range.Utf16Length)
                    {
                        style = ApplyScope(layer, style, range.Id, state, includeTypography, typographyOnly);
                    }
                }
            }
        }
        foreach (var operation in layer.EffectStyleOperations)
        {
            var typography = operation.Target.Property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING;
            if (operation.Target.State != state || !includeTypography && typography || typographyOnly && !typography ||
                !includeRanges && operation.Target.TextRangeId.HasValue ||
                utf16Offset < operation.Utf16Start || utf16Offset >= operation.Utf16Start + operation.Utf16Length)
            {
                continue;
            }
            style = WithStyleValue(style, operation.Target.Property,
                operation.Apply(GetStyleValue(style, operation.Target.Property)));
        }
        return style;
    }

    /// <summary>读取完整目标的静态基础值；文本范围采用首字素的局部及高亮继承，局部变换读取范围自身。</summary>
    public static AnimationValue GetBaseValue(ProjectLayer layer, SubtitleLine? subtitle, AnimationTrackTarget target)
    {
        ArgumentNullException.ThrowIfNull(layer);
        SubtitleAnimationTargetValidation.Validate(target, subtitle, layer.Mask);
        if (AnimationPropertyMetadata.IsMaskProperty(target.Property))
        {
            return ClipMaskAnimation.GetBaseValue(layer.Mask!, target);
        }
        if (target.TextRangeId is { } rangeId)
        {
            var range = subtitle!.AnimationRanges.Single(range => range.Id == rangeId);
            if (target.Property == AnimationProperty.POSITION)
            {
                return range.Offset;
            }
            if (target.Property == AnimationProperty.SCALE)
            {
                return range.Scale;
            }
            if (target.Property == AnimationProperty.ROTATION_3D)
            {
                return new SceneVector3(range.RotationX, range.RotationY, range.Rotation);
            }
            if (target.Property == AnimationProperty.ROTATION)
            {
                return range.Rotation;
            }
            if (target.Property == AnimationProperty.ROTATION_X)
            {
                return range.RotationX;
            }
            if (target.Property == AnimationProperty.ROTATION_Y)
            {
                return range.RotationY;
            }
            if (target.Property == AnimationProperty.PERSPECTIVE_DISTANCE)
            {
                return range.PerspectiveDistance;
            }
            return GetStyleValue(StaticStyleAt(subtitle, range.Utf16Start, target.State), target.Property);
        }
        if (subtitle is not null && styleProperties.Contains(target.Property))
        {
            var style = target.State == SubtitleAnimationState.NORMAL ? subtitle.Style : StaticStyleAt(subtitle, 0, target.State);
            return GetStyleValue(style, target.Property);
        }
        return target.Property switch
        {
            AnimationProperty.POSITION => layer.Transform.Position,
            AnimationProperty.SCALE => layer.Transform.Scale,
            AnimationProperty.ROTATION_3D => new SceneVector3(layer.Transform.RotationX, layer.Transform.RotationY, layer.Transform.Rotation),
            AnimationProperty.ROTATION => layer.Transform.Rotation,
            AnimationProperty.ROTATION_X => layer.Transform.RotationX,
            AnimationProperty.ROTATION_Y => layer.Transform.RotationY,
            AnimationProperty.PERSPECTIVE_DISTANCE => layer.Transform.PerspectiveDistance,
            AnimationProperty.OPACITY => layer.Opacity,
            AnimationProperty.BLUR => layer.Blur,
            AnimationProperty.FILL => layer.Fill,
            AnimationProperty.STROKE => layer.Stroke,
            AnimationProperty.STROKE_WIDTH => layer.StrokeWidth,
            AnimationProperty.PATH_PROGRESS => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }

    /// <summary>判断文字范围或整行指定视觉状态的各字素是否具有同一基础值，供面板及脚本识别混合状态。</summary>
    public static bool IsBaseValueUniform(ProjectLayer layer, SubtitleLine? subtitle, AnimationTrackTarget target)
    {
        var first = GetBaseValue(layer, subtitle, target);
        if (!styleProperties.Contains(target.Property) ||
            target.TextRangeId is null && target.State == SubtitleAnimationState.NORMAL)
        {
            return true;
        }
        var range = target.TextRangeId is { } rangeId
            ? subtitle!.AnimationRanges.Single(range => range.Id == rangeId) : null;
        foreach (var offset in StringInfo.ParseCombiningCharacters(subtitle!.Text))
        {
            if (range is not null && (offset < range.Utf16Start || offset >= range.Utf16Start + range.Utf16Length))
            {
                continue;
            }
            if (GetStyleValue(StaticStyleAt(subtitle, offset, target.State), target.Property) != first)
            {
                return false;
            }
        }
        return true;
    }

    internal static SubtitleStyle WithStyleValue(SubtitleStyle style, AnimationProperty property, AnimationValue value)
    {
        return property switch
        {
            AnimationProperty.FONT_SIZE => style with { FontSize = value.Scalar },
            AnimationProperty.LETTER_SPACING => style with { LetterSpacing = value.Scalar },
            AnimationProperty.FILL => style with { Fill = value.Color },
            AnimationProperty.STROKE => style with { Stroke = value.Color },
            AnimationProperty.STROKE_WIDTH => style with { StrokeWidth = value.Scalar },
            AnimationProperty.FILL_BLUR => style with { FillBlur = value.Scalar },
            AnimationProperty.STROKE_BLUR => style with { StrokeBlur = value.Scalar },
            AnimationProperty.SHADOW_OFFSET => style with { ShadowOffset = value.Vector },
            AnimationProperty.SHADOW_BLUR => style with { ShadowBlur = value.Scalar },
            AnimationProperty.SHADOW_COLOR => style with { ShadowColor = value.Color },
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }

    private static SubtitleStyle ApplyScope(EvaluatedLayer layer, SubtitleStyle style, Guid? rangeId,
        SubtitleAnimationState state, bool includeTypography, bool typographyOnly)
    {
        foreach (var property in styleProperties)
        {
            var typography = property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING;
            if (!includeTypography && typography || typographyOnly && !typography)
            {
                continue;
            }
            if (layer.AnimationValues.TryGetValue(new(property, TextRangeId: rangeId, State: state), out var value))
            {
                style = WithStyleValue(style, property, value);
            }
        }
        return style;
    }

    private static SubtitleStyle StaticStyleAt(SubtitleLine subtitle, int offset, SubtitleAnimationState state)
    {
        var style = subtitle.Style;
        foreach (var span in subtitle.InlineSpans)
        {
            if (span.Utf16Start <= offset && offset < span.Utf16Start + span.Utf16Length)
            {
                style = span.Style.ApplyTo(style);
                break;
            }
        }
        if (state == SubtitleAnimationState.NORMAL)
        {
            return style;
        }
        var segment = subtitle.Karaoke.Concat(subtitle.InactiveKaraoke)
            .FirstOrDefault(segment => segment.Utf16Start <= offset && offset < segment.Utf16Start + segment.Utf16Length);
        var rangeStyle = KaraokeVisualStyleResolver.RangeStyleAt(subtitle, offset, state == SubtitleAnimationState.ACTIVE
            ? KaraokeVisualState.ACTIVE : KaraokeVisualState.INACTIVE);
        return state == SubtitleAnimationState.ACTIVE
            ? KaraokeVisualStyleResolver.ResolveActive(style, subtitle.KaraokeStyle, segment, rangeStyle)
            : rangeStyle?.ApplyTo(style) ?? style;
    }

    private static AnimationValue GetStyleValue(SubtitleStyle style, AnimationProperty property)
    {
        return property switch
        {
            AnimationProperty.FONT_SIZE => style.FontSize,
            AnimationProperty.LETTER_SPACING => style.LetterSpacing,
            AnimationProperty.FILL => style.Fill,
            AnimationProperty.STROKE => style.Stroke,
            AnimationProperty.STROKE_WIDTH => style.StrokeWidth,
            AnimationProperty.FILL_BLUR => style.FillBlur,
            AnimationProperty.STROKE_BLUR => style.StrokeBlur,
            AnimationProperty.SHADOW_OFFSET => style.ShadowOffset,
            AnimationProperty.SHADOW_BLUR => style.ShadowBlur,
            AnimationProperty.SHADOW_COLOR => style.ShadowColor,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }
}
