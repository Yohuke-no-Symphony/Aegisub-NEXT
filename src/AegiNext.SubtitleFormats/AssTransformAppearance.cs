using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed class AssTransformAppearance(LayerTransform transform, Guid subtitleId)
{
    private readonly List<SubtitleFormatDiagnostic> diagnostics = [];
    private readonly HashSet<string> messages = [];
    private readonly (double Sine, double Cosine) rotation = AssTransformMath.SinCos(transform.Rotation);
    private readonly double strokeScale = transform.Scale.X == 0 || transform.Scale.Y == 0 ? 1 : Math.Sqrt(transform.Scale.X * transform.Scale.Y);

    internal IEnumerable<SubtitleFormatDiagnostic> Diagnostics => diagnostics;

    internal SubtitleLine Import(SubtitleLine line)
    {
        if (transform.Scale.X == 0 || transform.Scale.Y == 0)
        {
            Report("ASS 零缩放已保留；折叠轴没有可逆的描边、模糊或阴影补偿，相关外观数值按单位比例保留。");
        }
        return line with
        {
            Style = line.Style with
            {
                StrokeWidth = Stroke(line.Style.StrokeWidth), ShadowOffset = Shadow(line.Style.ShadowOffset),
                FillBlur = Blur(line.Style.FillBlur), StrokeBlur = Blur(line.Style.StrokeBlur), ShadowBlur = Blur(line.Style.ShadowBlur)
            },
            InlineSpans = line.InlineSpans.Select(span => span with { Style = Inline(span.Style) }).ToImmutableArray(),
            KaraokeStyleSpans = line.KaraokeStyleSpans.Select(span => span with
            {
                ActiveStyle = Visual(span.ActiveStyle), InactiveStyle = Visual(span.InactiveStyle)
            }).ToImmutableArray()
        };
    }

    private SubtitleInlineStyleOverride Inline(SubtitleInlineStyleOverride style)
    {
        return style with
        {
            StrokeWidth = style.StrokeWidth is { } stroke ? Stroke(stroke) : null,
            FillBlur = style.FillBlur is { } fillBlur ? Blur(fillBlur) : null,
            StrokeBlur = style.StrokeBlur is { } strokeBlur ? Blur(strokeBlur) : null,
            ShadowBlur = style.ShadowBlur is { } shadowBlur ? Blur(shadowBlur) : null,
            ShadowOffset = style.ShadowOffset is { } shadow ? Shadow(shadow) : null
        };
    }

    private KaraokeVisualStyleOverride? Visual(KaraokeVisualStyleOverride? style)
    {
        return style is null ? null : style with
        {
            StrokeWidth = style.StrokeWidth is { } stroke ? Stroke(stroke) : null,
            FillBlur = style.FillBlur is { } fillBlur ? Blur(fillBlur) : null,
            StrokeBlur = style.StrokeBlur is { } strokeBlur ? Blur(strokeBlur) : null,
            ShadowBlur = style.ShadowBlur is { } shadowBlur ? Blur(shadowBlur) : null,
            ShadowOffset = style.ShadowOffset is { } shadow ? Shadow(shadow) : null
        };
    }

    private double Stroke(double width)
    {
        var result = width / strokeScale;
        if (!double.IsFinite(result) || result > 4096)
        {
            Report("ASS 描边的缩放补偿超出原生范围，保留原描边数值，变换后的描边外观可能改变。");
            return width;
        }
        if (width > 0 && !transform.Scale.X.Equals(transform.Scale.Y))
        {
            Report("ASS 非等比字形缩放的描边按几何平均比例近似转换，水平与垂直描边厚度不能同时保持。");
        }
        return result;
    }

    private double Blur(double sigma)
    {
        var result = sigma / strokeScale;
        if (!double.IsFinite(result) || result > 512)
        {
            if (!double.IsFinite(sigma) || sigma > 512)
            {
                Report("ASS 模糊在图层缩放补偿后仍超出原生范围，已仅省略该模糊数值。", "Ass.BlurRange");
                return 0;
            }
            Report("ASS 模糊的缩放补偿超出原生范围，保留原模糊数值，变换后的模糊外观可能改变。");
            return sigma;
        }
        if (sigma > 0 && !transform.Scale.X.Equals(transform.Scale.Y))
        {
            Report("ASS 非等比字形缩放的模糊按几何平均比例近似转换，水平与垂直扩散范围不能同时保持。");
        }
        return result;
    }

    private ScenePoint Shadow(ScenePoint offset)
    {
        var result = new ScenePoint((rotation.Cosine * offset.X + rotation.Sine * offset.Y) / (transform.Scale.X == 0 ? 1 : transform.Scale.X),
            (-rotation.Sine * offset.X + rotation.Cosine * offset.Y) / (transform.Scale.Y == 0 ? 1 : transform.Scale.Y));
        if (!double.IsFinite(result.X) || !double.IsFinite(result.Y) || Math.Abs(result.X) > 1e9 || Math.Abs(result.Y) > 1e9)
        {
            Report("ASS 阴影位移的变换补偿超出原生范围，保留原位移数值，变换后的阴影位置可能改变。");
            return offset;
        }
        return result;
    }

    private void Report(string message, string code = "Ass.TransformAppearance")
    {
        if (messages.Add(message))
        {
            diagnostics.Add(new(code, message, SubtitleId: subtitleId));
        }
    }
}
