using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleStyleReplacement
{
    internal static ProjectDocument Apply(ProjectDocument document, IReadOnlySet<Guid> subtitleIds, SubtitleStyle style)
    {
        var sources = document.Subtitles.Where(line => subtitleIds.Contains(line.Id)).ToDictionary(line => line.Id);
        var targetScale = SubtitleStyleScaling.GetScaleFactor(style.Scaling, document.Width, document.Height);
        ImmutableArray<ProjectLayer>.Builder? changedLayers = null;
        for (var index = 0; index < document.Layers.Length; index++)
        {
            var layer = document.Layers[index];
            if (layer.SubtitleId is not { } id || !sources.TryGetValue(id, out var source))
            {
                continue;
            }
            var rebased = RebaseLayer(layer, source.Style.Scaling, targetScale, document.Width, document.Height);
            if (ReferenceEquals(rebased, layer))
            {
                continue;
            }
            changedLayers ??= document.Layers.ToBuilder();
            changedLayers[index] = rebased;
        }
        return document with
        {
            Subtitles = document.Subtitles.Select(line => sources.ContainsKey(line.Id)
                ? SubtitleStyleScaling.ConvertUnits(line, style.Scaling, document.Width, document.Height) with
                {
                    Style = style, InlineSpans = []
                } : line).ToImmutableArray(),
            Layers = changedLayers?.ToImmutable() ?? document.Layers
        };
    }

    private static ProjectLayer RebaseLayer(ProjectLayer layer, SubtitleScalingSettings source, double targetScale,
        int width, int height)
    {
        var factor = SubtitleStyleScaling.GetScaleFactor(source, width, height) / targetScale;
        return factor.Equals(1d) || !layer.Tracks.Any(track => SubtitleStyleScaling.IsStyleDimension(track.Property))
            ? layer : layer with
        {
            Tracks = SubtitleStyleScaling.ScaleDimensions(layer.Tracks, factor)
        };
    }
}
