using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>为时间轴显示和统一特效求值提供从上到下的稳定行身份；求值方反向遍历。</summary>
public static class TimelineEffectRowOrdering
{
    private static readonly IReadOnlyDictionary<AnimationProperty, int> propertyOrder =
        AnimationPropertyMetadata.CurrentProperties.Select((property, index) => (property, index))
            .ToDictionary(value => value.property, value => value.index);

    /// <summary>优先应用已有显示提示，忽略暂缺目标，并将新出现的行按既有默认顺序追加。</summary>
    public static ImmutableArray<TimelineEffectRowIdentity> GetOrder(ProjectLayer layer, SubtitleLine? subtitle)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var ranges = subtitle?.AnimationRanges.Select((range, index) => (range.Id, index))
            .ToDictionary(value => value.Id, value => value.index) ?? [];
        var nodes = layer.Mask is VectorClipMask mask
            ? mask.Contours.SelectMany((contour, contourIndex) => contour.Nodes.Select((node, nodeIndex) =>
                (node.Id, Contour: contourIndex, Node: nodeIndex))).ToDictionary(value => value.Id) : [];
        var native = layer.Tracks.Where(track => !track.Keyframes.IsEmpty || !track.Transforms.IsEmpty)
            .Where(track => track.Target.NodeId is not { } id || nodes.ContainsKey(id))
            .OrderBy(track => track.Target.TextRangeId is { } id ? ranges.GetValueOrDefault(id, int.MaxValue) : -1)
            .ThenBy(track => DefaultPropertyOrder(track.Property))
            .ThenBy(track => track.Property == AnimationProperty.ROTATION_3D)
            .ThenBy(track => track.Target.State)
            .ThenBy(track => track.Target.NodeId is { } id ? nodes[id].Contour : 0)
            .ThenBy(track => track.Target.NodeId is { } id ? nodes[id].Node : 0)
            .ThenBy(track => track.Target.TextRangeId)
            .Select(track => new TimelineEffectRowIdentity(track.Target)).Reverse();
        var defaults = layer.Effects.Reverse().Select(instance => new TimelineEffectRowIdentity(InstanceId: instance.Id))
            .Concat(native).ToImmutableArray();
        var remaining = defaults.ToHashSet();
        var result = ImmutableArray.CreateBuilder<TimelineEffectRowIdentity>();
        foreach (var identity in layer.EffectRowOrder)
        {
            if (remaining.Remove(identity))
            {
                result.Add(identity);
            }
        }
        result.AddRange(defaults.Where(remaining.Contains));
        return result.ToImmutable();
    }

    private static int DefaultPropertyOrder(AnimationProperty property) => propertyOrder.GetValueOrDefault(
        property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y
            ? AnimationProperty.ROTATION_3D : property, int.MaxValue);
}
