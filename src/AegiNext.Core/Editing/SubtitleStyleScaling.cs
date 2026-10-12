using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>字幕参考单位与工程像素之间的纯换算；不修改来源快照、运动坐标或颜色。</summary>
public static class SubtitleStyleScaling
{
    /// <summary>在对数空间混合宽高比例，返回所有字幕尺寸值共用的等比缩放系数。</summary>
    public static double GetScaleFactor(SubtitleScalingSettings settings, int canvasWidth, int canvasHeight)
    {
        ProjectValidator.ValidateSubtitleScaling(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);
        var widthRatio = (double)canvasWidth / settings.ReferenceWidth;
        var heightRatio = (double)canvasHeight / settings.ReferenceHeight;
        if (widthRatio == heightRatio)
        {
            return widthRatio;
        }
        return settings.MatchWidthOrHeight switch
        {
            0 => widthRatio,
            1 => heightRatio,
            _ => Math.Exp(Math.Log(widthRatio) * (1 - settings.MatchWidthOrHeight) +
                Math.Log(heightRatio) * settings.MatchWidthOrHeight)
        };
    }

    /// <summary>投影为目标画布像素，结果的参考尺寸等于画布，重复投影不会累计缩放。</summary>
    public static SubtitleStyle ToCanvasUnits(SubtitleStyle style, int canvasWidth, int canvasHeight)
    {
        return ConvertUnits(style, CanvasSettings(canvasWidth, canvasHeight), canvasWidth, canvasHeight);
    }

    /// <summary>投影字幕及其内联、高亮外观为画布像素，保留时间、文字身份与范围运动坐标。</summary>
    public static SubtitleLine ToCanvasUnits(SubtitleLine line, int canvasWidth, int canvasHeight)
    {
        return ConvertUnits(line, CanvasSettings(canvasWidth, canvasHeight), canvasWidth, canvasHeight);
    }

    /// <summary>将样式换算到另一套参考单位，保持它在指定画布中的实际外观。</summary>
    public static SubtitleStyle ConvertUnits(SubtitleStyle style, SubtitleScalingSettings target,
        int canvasWidth, int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(style);
        var sourceScale = GetScaleFactor(style.Scaling, canvasWidth, canvasHeight);
        var targetScale = GetScaleFactor(target, canvasWidth, canvasHeight);
        return style.Scaling == target ? style : ScaleDimensions(style, sourceScale / targetScale) with { Scaling = target };
    }

    /// <summary>换算整行及全部局部外观到目标参考单位；图层轨道由其拥有者独立换算。</summary>
    public static SubtitleLine ConvertUnits(SubtitleLine line, SubtitleScalingSettings target,
        int canvasWidth, int canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(line);
        var sourceScale = GetScaleFactor(line.Style.Scaling, canvasWidth, canvasHeight);
        var targetScale = GetScaleFactor(target, canvasWidth, canvasHeight);
        if (line.Style.Scaling == target)
        {
            return line;
        }
        var scaled = ScaleDimensions(line, sourceScale / targetScale);
        return scaled with { Style = scaled.Style with { Scaling = target } };
    }

    /// <summary>缩放整行的全部尺寸外观，参考设置与文字范围位移保持原值。</summary>
    public static SubtitleLine ScaleDimensions(SubtitleLine line, double factor)
    {
        ArgumentNullException.ThrowIfNull(line);
        CheckFactor(factor);
        if (factor == 1)
        {
            return line;
        }
        return line with
        {
            Style = ScaleDimensions(line.Style, factor),
            InlineSpans = line.InlineSpans.Select(span => span with
            {
                Style = ScaleDimensions(span.Style, factor)
            }).ToImmutableArray(),
            KaraokeStyle = line.KaraokeStyle is { } highlight ? ScaleDimensions(highlight, factor) : null,
            KaraokeStyleSpans = line.KaraokeStyleSpans.Select(span => span with
            {
                ActiveStyle = span.ActiveStyle is { } active ? ScaleDimensions(active, factor) : null,
                InactiveStyle = span.InactiveStyle is { } inactive ? ScaleDimensions(inactive, factor) : null
            }).ToImmutableArray()
        };
    }

    /// <summary>仅缩放尺寸属性，保留参考设置、锚点、轴心、行高倍率及颜色。</summary>
    public static SubtitleStyle ScaleDimensions(SubtitleStyle style, double factor)
    {
        ArgumentNullException.ThrowIfNull(style);
        CheckFactor(factor);
        if (factor == 1)
        {
            return style;
        }
        return style with
        {
            FontSize = style.FontSize * factor,
            LetterSpacing = style.LetterSpacing * factor,
            FillBlur = style.FillBlur * factor,
            StrokeWidth = style.StrokeWidth * factor,
            StrokeBlur = style.StrokeBlur * factor,
            Margins = new(style.Margins.Left * factor, style.Margins.Right * factor, style.Margins.Vertical * factor),
            ShadowOffset = ScalePoint(style.ShadowOffset, factor),
            ShadowBlur = style.ShadowBlur * factor,
            Position = style.Position is { } position ? position with { Offset = ScalePoint(position.Offset, factor) } : null
        };
    }

    /// <summary>缩放局部尺寸覆盖，保留继承的 null、显式零以及字体和颜色。</summary>
    public static SubtitleInlineStyleOverride ScaleDimensions(SubtitleInlineStyleOverride style, double factor)
    {
        ArgumentNullException.ThrowIfNull(style);
        CheckFactor(factor);
        return factor == 1 ? style : style with
        {
            FontSize = style.FontSize * factor,
            LetterSpacing = style.LetterSpacing * factor,
            FillBlur = style.FillBlur * factor,
            StrokeWidth = style.StrokeWidth * factor,
            StrokeBlur = style.StrokeBlur * factor,
            ShadowOffset = style.ShadowOffset is { } offset ? ScalePoint(offset, factor) : null,
            ShadowBlur = style.ShadowBlur * factor
        };
    }

    /// <summary>缩放整句高亮尺寸，保留预设身份及颜色。</summary>
    public static KaraokeHighlightStyle ScaleDimensions(KaraokeHighlightStyle style, double factor)
    {
        ArgumentNullException.ThrowIfNull(style);
        CheckFactor(factor);
        return factor == 1 ? style : style with
        {
            FillBlur = style.FillBlur * factor,
            StrokeWidth = style.StrokeWidth * factor,
            StrokeBlur = style.StrokeBlur * factor,
            ShadowOffset = ScalePoint(style.ShadowOffset, factor),
            ShadowBlur = style.ShadowBlur * factor
        };
    }

    /// <summary>缩放高亮状态的尺寸覆盖，保留继承的 null 和显式零。</summary>
    public static KaraokeVisualStyleOverride ScaleDimensions(KaraokeVisualStyleOverride style, double factor)
    {
        ArgumentNullException.ThrowIfNull(style);
        CheckFactor(factor);
        return factor == 1 ? style : style with
        {
            FillBlur = style.FillBlur * factor,
            StrokeWidth = style.StrokeWidth * factor,
            StrokeBlur = style.StrokeBlur * factor,
            ShadowOffset = style.ShadowOffset is { } offset ? ScalePoint(offset, factor) : null,
            ShadowBlur = style.ShadowBlur * factor
        };
    }

    private static SubtitleScalingSettings CanvasSettings(int width, int height) => new()
    {
        ReferenceWidth = width, ReferenceHeight = height
    };

    private static ScenePoint ScalePoint(ScenePoint point, double factor) => new(point.X * factor, point.Y * factor);

    private static void CheckFactor(double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "字幕尺寸换算系数必须为有限正数。");
        }
    }
}
