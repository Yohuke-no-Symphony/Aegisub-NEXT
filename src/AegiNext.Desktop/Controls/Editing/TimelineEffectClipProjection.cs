using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

internal static class TimelineEffectClipProjection
{
    internal const double COLLAPSED_HEIGHT = 40;
    internal static TimelineEffectClipLayout Create(IEnumerable<ProjectLayer> layers,
        IReadOnlyDictionary<Guid, SubtitleLine> subtitles, IReadOnlySet<TimelineEffectClipId> expanded)
    {
        var result = ImmutableDictionary.CreateBuilder<Guid, ImmutableArray<TimelineEffectClip>>();
        var maximum = 0d;
        foreach (var layer in layers)
        {
            var cue = layer.SubtitleId is { } cueId ? subtitles.GetValueOrDefault(cueId) : null;
            var layerRanges = cue?.AnimationRanges ?? [];
            var ranges = layerRanges.ToDictionary(range => range.Id);
            var tracks = layer.Tracks.ToDictionary(track => track.Target);
            var instances = layer.Effects.ToDictionary(instance => instance.Id);
            var nodes = layer.Mask is VectorClipMask mask
                ? mask.Contours.SelectMany((contour, contourIndex) => contour.Nodes.Select((node, nodeIndex) =>
                    (node.Id, Contour: contourIndex + 1, Node: nodeIndex + 1)))
                    .ToDictionary(value => value.Id) : [];
            var blocks = ImmutableArray.CreateBuilder<TimelineEffectClip>();
            var height = 0d;
            foreach (var identity in TimelineEffectRowOrdering.GetOrder(layer, cue).Reverse())
            {
                if (identity.InstanceId is { } instanceId)
                {
                    var instance = instances[instanceId];
                    height += COLLAPSED_HEIGHT;
                    blocks.Add(new(new(layer.Id, instance.TargetContext ?? new(AnimationProperty.OPACITY), instance.Id),
                        true, -height, COLLAPSED_HEIGHT, InstanceName: instance.Name, IsEnabled: instance.IsEnabled));
                    continue;
                }
                var track = tracks[identity.Target!.Value];
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
