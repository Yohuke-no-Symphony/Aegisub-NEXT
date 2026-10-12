using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private readonly Dictionary<Guid, SubtitleScalingProjection> scalingProjections = [];

    private SubtitleLine CanvasSubtitle(ProjectDocument document, SubtitleLine subtitle)
    {
        var scale = SubtitleStyleScaling.GetScaleFactor(subtitle.Style.Scaling, document.Width, document.Height);
        return scale == 1 ? subtitle : ScalingProjection(document, subtitle, scale).Subtitle;
    }

    private SubtitleScalingProjection ScalingProjection(ProjectDocument document, SubtitleLine subtitle, double scale)
    {
        if (scalingProjections.TryGetValue(subtitle.Id, out var cached) && ReferenceEquals(cached.Source, subtitle) && cached.Scale == scale)
        {
            return cached;
        }
        if (scalingProjections.Count >= 256 && !scalingProjections.ContainsKey(subtitle.Id))
        {
            scalingProjections.Clear();
        }
        var projection = new SubtitleScalingProjection(subtitle,
            SubtitleStyleScaling.ToCanvasUnits(subtitle, document.Width, document.Height), scale);
        scalingProjections[subtitle.Id] = projection;
        return projection;
    }

    private EvaluatedLayer CanvasLayer(ProjectDocument document, EvaluatedLayer layer)
    {
        if (layer.Subtitle is not { } subtitle)
        {
            return layer;
        }
        var scale = SubtitleStyleScaling.GetScaleFactor(subtitle.Style.Scaling, document.Width, document.Height);
        if (scale == 1)
        {
            return layer;
        }
        var projection = ScalingProjection(document, subtitle, scale);
        var values = layer.AnimationValues;
        if (!ReferenceEquals(values, projection.Values) && (values.Count != projection.Values.Count ||
            values.Any(pair => !projection.Values.TryGetValue(pair.Key, out var previous) || previous != pair.Value)))
        {
            projection = projection with
            {
                Values = values,
                CanvasValues = values.ToImmutableDictionary(pair => pair.Key,
                    pair => SubtitleStyleScaling.ScaleDimensions(pair.Key.Property, pair.Value, scale))
            };
            scalingProjections[subtitle.Id] = projection;
        }
        return layer with
        {
            Subtitle = projection.Subtitle,
            FontSize = layer.FontSize * scale,
            LetterSpacing = layer.LetterSpacing * scale,
            StrokeWidth = layer.StrokeWidth * scale,
            FillBlur = layer.FillBlur * scale,
            StrokeBlur = layer.StrokeBlur * scale,
            ShadowOffset = new(layer.ShadowOffset.X * scale, layer.ShadowOffset.Y * scale),
            ShadowBlur = layer.ShadowBlur * scale,
            AnimationValues = projection.CanvasValues
        };
    }
}
