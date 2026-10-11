using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

internal static class TimelineEffectClipProjection
{
    internal const double COLLAPSED_HEIGHT = 40;
    private static readonly IReadOnlyDictionary<AnimationProperty, int> propertyOrder =
        AnimationPropertyMetadata.CurrentProperties.Select((property, index) => (property, index))
            .ToDictionary(value => value.property, value => value.index);

    internal static TimelineEffectClipLayout Create(IEnumerable<ProjectLayer> layers,
        IReadOnlyDictionary<Guid, SubtitleLine> subtitles, IReadOnlySet<TimelineEffectClipId> expanded)
    {
        var result = ImmutableDictionary.CreateBuilder<Guid, ImmutableArray<TimelineEffectClip>>();
        var maximum = 0d;
        foreach (var layer in layers)
        {
            var ranges = layer.SubtitleId is { } cueId && subtitles.TryGetValue(cueId, out var cue)
                ? cue.AnimationRanges.ToDictionary(range => range.Id) : [];
            var nodes = layer.Mask is VectorClipMask mask
                ? mask.Contours.SelectMany((contour, contourIndex) => contour.Nodes.Select((node, nodeIndex) =>
                    (node.Id, Contour: contourIndex + 1, Node: nodeIndex + 1)))
                    .ToDictionary(value => value.Id) : [];
            var blocks = ImmutableArray.CreateBuilder<TimelineEffectClip>();
            var height = 0d;
            foreach (var track in layer.Tracks.Where(track => !track.Keyframes.IsEmpty || !track.Transforms.IsEmpty)
                         .Where(track => track.Target.NodeId is not { } nodeId || nodes.ContainsKey(nodeId))
                         .OrderBy(track => propertyOrder.GetValueOrDefault(track.Property, int.MaxValue))
                         .ThenBy(track => track.Target.TextRangeId is { } id && ranges.TryGetValue(id, out var range) ? range.Utf16Start : -1)
                         .ThenBy(track => track.Target.TextRangeId is { } id && ranges.TryGetValue(id, out var range) ? range.Utf16Length : 0)
                         .ThenBy(track => track.Target.State)
                         .ThenBy(track => track.Target.NodeId is { } id ? nodes[id].Contour : 0)
                         .ThenBy(track => track.Target.NodeId is { } id ? nodes[id].Node : 0)
                         .ThenBy(track => track.Target.TextRangeId))
            {
                var id = new TimelineEffectClipId(layer.Id, track.Target);
                var collapsed = !expanded.Contains(id);
                var blockHeight = collapsed ? COLLAPSED_HEIGHT :
                    AnimationPropertyMetadata.GetValueKind(track.Property) == AnimationValueKind.COLOR ? 112 : 76;
                var range = track.Target.TextRangeId is { } rangeId ? ranges.GetValueOrDefault(rangeId) : null;
                var label = range is null ? null : $"{range.Utf16Start + 1}–{range.Utf16Start + range.Utf16Length}";
                var node = track.Target.NodeId is { } nodeId ? nodes[nodeId] : default;
                height += blockHeight;
                blocks.Add(new(id, collapsed, -height, blockHeight, label, node.Contour, node.Node));
            }
            result.Add(layer.Id, blocks.ToImmutable());
            maximum = Math.Max(maximum, height);
        }
        return new(result.ToImmutableDictionary(pair => pair.Key,
            pair => pair.Value.Select(block => block with { Top = maximum + block.Top }).ToImmutableArray()), maximum);
    }
}
