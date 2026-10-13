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
        if (SceneProjection.RequiresPerspective(transform) && (line.Style.StrokeWidth > 0 || line.Style.FillBlur > 0 ||
            line.Style.StrokeBlur > 0 || line.Style.ShadowColor.Alpha > 0 && line.Style.ShadowOffset != default ||
            line.InlineSpans.Any(span => span.Style.StrokeWidth > 0 || span.Style.FillBlur > 0 || span.Style.StrokeBlur > 0 ||
                span.Style.ShadowColor?.Alpha > 0 && span.Style.ShadowOffset is { } offset && offset != default)))
        {
            Report("ASS perspective and native layer projection apply edges, blur, and shadow at different stages. Rotation geometry was preserved; projected appearance may differ.", "Ass.PerspectiveAppearance");
        }
        if (transform.Scale.X == 0 || transform.Scale.Y == 0)
        {
            Report("Zero ASS scaling was preserved. Collapsed axes have no reversible stroke, blur, or shadow compensation; related appearance values were preserved at unit scale.");
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
            Report("ASS stroke scaling compensation exceeds the native range. The original stroke value was preserved; the transformed stroke appearance may change.");
            return width;
        }
        if (width > 0 && !transform.Scale.X.Equals(transform.Scale.Y))
        {
            Report("Stroke under nonuniform ASS glyph scaling was approximated using the geometric mean scale. Horizontal and vertical stroke thickness cannot both be preserved.");
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
                Report("ASS blur still exceeds the native range after layer scaling compensation. Only this blur value was omitted.", "Ass.BlurRange");
                return 0;
            }
            Report("ASS blur scaling compensation exceeds the native range. The original blur value was preserved; the transformed blur appearance may change.");
            return sigma;
        }
        if (sigma > 0 && !transform.Scale.X.Equals(transform.Scale.Y))
        {
            Report("Blur under nonuniform ASS glyph scaling was approximated using the geometric mean scale. Horizontal and vertical spread cannot both be preserved.");
        }
        return result;
    }

    private ScenePoint Shadow(ScenePoint offset)
    {
        var result = new ScenePoint((rotation.Cosine * offset.X + rotation.Sine * offset.Y) / (transform.Scale.X == 0 ? 1 : transform.Scale.X),
            (-rotation.Sine * offset.X + rotation.Cosine * offset.Y) / (transform.Scale.Y == 0 ? 1 : transform.Scale.Y));
        if (!double.IsFinite(result.X) || !double.IsFinite(result.Y) || Math.Abs(result.X) > 1e9 || Math.Abs(result.Y) > 1e9)
        {
            Report("ASS shadow translation transform compensation exceeds the native range. The original translation value was preserved; the transformed shadow position may change.");
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
