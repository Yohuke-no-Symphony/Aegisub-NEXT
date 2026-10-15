using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>冻结遮罩手势的原生当前值与实例仿射映射，避免将可见终值再次写入实例上游。</summary>
public sealed class ClipMaskEffectEditContext
{
    private readonly ClipMask? sourceMask;
    private readonly Dictionary<Guid, MaskNode> sourceNodes;
    private readonly Dictionary<Guid, MaskNode> nativeNodes;
    private readonly Dictionary<Guid, MaskNode> visibleNodes;
    private readonly HashSet<AnimationTrackTarget> originalTargets;
    private readonly Dictionary<AnimationTrackTarget, (AnimationValue Multiplier, AnimationValue Offset)> mappings = [];
    private readonly List<(AnimationTrackTarget Target, AnimationValue Multiplier, AnimationValue Offset)> operations = [];

    private ClipMaskEffectEditContext(ProjectLayer layer, SubtitleLine? subtitle, MediaTime contentTime,
        EffectCompilationCache cache)
    {
        sourceMask = layer.Mask;
        NativeMask = SceneEvaluator.EvaluateMask(layer with { Effects = [] }, contentTime);
        VisibleMask = SceneEvaluator.EvaluateMask(layer, contentTime, subtitle, cache);
        sourceNodes = Nodes(sourceMask);
        nativeNodes = Nodes(NativeMask);
        visibleNodes = Nodes(VisibleMask);
        originalTargets = NativeMask is null ? [] : Targets(NativeMask).ToHashSet();
        IsTopologyLocked = ClipMaskAnimation.IsTopologyLocked(layer);
        var prepared = PreparedEffectLayer.Create(layer, subtitle, cache);
        IsTopologyLocked |= prepared.Instances.Any(instance => instance.Plan.Channels.Any(channel =>
            AnimationPropertyMetadata.IsNodeProperty(channel.Target.Property)));
        foreach (var entry in prepared.ExecutionOrder)
        {
            if (entry.Track is { } track)
            {
                if (AnimationPropertyMetadata.IsMaskProperty(track.Property))
                {
                    mappings.Remove(track.Target);
                    operations.RemoveAll(operation => operation.Target == track.Target);
                }
                continue;
            }
            var instance = entry.Instance!;
            if (!EffectRuntimeEvaluation.IsActive(contentTime, instance.Source.Start, instance.Source.End))
            {
                continue;
            }
            foreach (var channel in instance.Plan.Channels)
            {
                var target = channel.Target;
                if (!AnimationPropertyMetadata.IsMaskProperty(target.Property) ||
                    !EffectRuntimeEvaluation.IsActive(contentTime, channel.Start, channel.End))
                {
                    continue;
                }
                var multiplier = SceneEvaluator.EvaluateTrack(channel.Multiplier, contentTime);
                var offset = SceneEvaluator.EvaluateTrack(channel.Offset, contentTime);
                operations.Add((target, multiplier, offset));
                if (mappings.TryGetValue(target, out var upstream))
                {
                    for (var component = 0; component < multiplier.ComponentCount; component++)
                    {
                        var factor = multiplier.GetComponent(component);
                        offset = offset.WithComponent(component, upstream.Offset.GetComponent(component) * factor + offset.GetComponent(component));
                        multiplier = multiplier.WithComponent(component, upstream.Multiplier.GetComponent(component) * factor);
                    }
                }
                mappings[target] = (multiplier, offset);
            }
        }
    }

    public ClipMask? NativeMask { get; }
    public ClipMask? VisibleMask { get; }
    public bool IsTopologyLocked { get; }

    /// <summary>使用场景同源的有界编译缓存，按片段内容时间冻结手势映射。</summary>
    public static ClipMaskEffectEditContext Create(ProjectLayer layer, SubtitleLine? subtitle, MediaTime contentTime,
        EffectCompilationCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new(layer, subtitle, contentTime, cache ?? new());
    }

    /// <summary>判断手势所需分量能否反解；零分量掩码表示整个目标。</summary>
    public bool CanEdit(AnimationTrackTarget target, int componentMask = 0)
    {
        if (!mappings.TryGetValue(target, out var mapping))
        {
            return true;
        }
        for (var component = 0; component < mapping.Multiplier.ComponentCount; component++)
        {
            if ((componentMask == 0 || (componentMask & (1 << component)) != 0) &&
                (mapping.Multiplier.GetComponent(component) == 0 || !double.IsFinite(mapping.Multiplier.GetComponent(component)) ||
                    !double.IsFinite(mapping.Offset.GetComponent(component))))
            {
                return false;
            }
        }
        return true;
    }

    private bool HasValidIntermediateValues(ClipMask nativeMask, Dictionary<Guid, MaskNode> nodes)
    {
        var values = new Dictionary<AnimationTrackTarget, AnimationValue>();
        var targets = Targets(nativeMask).ToHashSet();
        foreach (var operation in operations)
        {
            if (!targets.Contains(operation.Target))
            {
                return false;
            }
            var input = values.TryGetValue(operation.Target, out var current)
                ? current : Value(nativeMask, nodes, operation.Target);
            var result = input;
            for (var component = 0; component < input.ComponentCount; component++)
            {
                var value = input.GetComponent(component) * operation.Multiplier.GetComponent(component) + operation.Offset.GetComponent(component);
                if (!double.IsFinite(value) || value < AnimationPropertyMetadata.GetMinimum(operation.Target.Property, component) ||
                    value > AnimationPropertyMetadata.GetMaximum(operation.Target.Property, component))
                {
                    return false;
                }
                result = result.WithComponent(component, value);
            }
            values[operation.Target] = result;
        }
        return true;
    }

    /// <summary>将可见草稿反解为原生当前姿态；未改动及被完全覆盖的分量保留原生当前值。</summary>
    public bool TryGetNativePose(ClipMask visibleDraft, out ClipMask nativePose)
    {
        return TryMap(visibleDraft, false, out nativePose);
    }

    /// <summary>为纯拓扑编辑保留已有目标的静态值与原生轨道，只接收新增几何。</summary>
    public bool TryGetTopologyDraft(ClipMask visibleDraft, out ClipMask nativeMask)
    {
        nativeMask = visibleDraft;
        return !IsTopologyLocked && TryMap(visibleDraft, true, out nativeMask);
    }

    private bool TryMap(ClipMask visibleDraft, bool preserveStaticValues, out ClipMask nativeMask)
    {
        ArgumentNullException.ThrowIfNull(visibleDraft);
        nativeMask = visibleDraft;
        if (IsTopologyLocked && !ClipMaskAnimation.HasSameTopology(VisibleMask, visibleDraft))
        {
            return false;
        }
        if (NativeMask is null || VisibleMask is null || sourceMask is null)
        {
            return true;
        }
        var nodes = Nodes(visibleDraft);
        var changedNodes = false;
        foreach (var target in Targets(visibleDraft).Where(originalTargets.Contains))
        {
            var original = Value(VisibleMask, visibleNodes, target);
            var desired = Value(visibleDraft, nodes, target);
            var upstream = Value(NativeMask, nativeNodes, target);
            var result = preserveStaticValues ? Value(sourceMask, sourceNodes, target) : upstream;
            for (var component = 0; component < desired.ComponentCount; component++)
            {
                if (desired.GetComponent(component) == original.GetComponent(component))
                {
                    continue;
                }
                if (preserveStaticValues)
                {
                    return false;
                }
                var value = desired.GetComponent(component);
                if (mappings.TryGetValue(target, out var mapping))
                {
                    var multiplier = mapping.Multiplier.GetComponent(component);
                    if (multiplier == 0)
                    {
                        return false;
                    }
                    value = (value - mapping.Offset.GetComponent(component)) / multiplier;
                }
                if (!double.IsFinite(value) || value < AnimationPropertyMetadata.GetMinimum(target.Property, component) ||
                    value > AnimationPropertyMetadata.GetMaximum(target.Property, component))
                {
                    return false;
                }
                result = result.WithComponent(component, value);
            }
            if (target.NodeId is { } nodeId)
            {
                if (desired == result)
                {
                    continue;
                }
                var node = nodes[nodeId];
                nodes[nodeId] = target.Property switch
                {
                    AnimationProperty.MASK_NODE_POSITION => node with { Position = result.Vector },
                    AnimationProperty.MASK_NODE_IN_HANDLE => node with { InHandle = result.Vector },
                    _ => node with { OutHandle = result.Vector }
                };
                changedNodes = true;
            }
            else
            {
                nativeMask = ClipMaskAnimation.SetBaseValue(nativeMask, target, result);
            }
        }
        if (changedNodes)
        {
            nativeMask = ApplyNodes((VectorClipMask)nativeMask, nodes);
        }
        return preserveStaticValues || HasValidIntermediateValues(nativeMask, nodes);
    }

    private static Dictionary<Guid, MaskNode> Nodes(ClipMask? mask) => mask is VectorClipMask vector
        ? vector.Contours.SelectMany(contour => contour.Nodes).ToDictionary(node => node.Id) : [];

    private static AnimationValue Value(ClipMask mask, Dictionary<Guid, MaskNode> nodes, AnimationTrackTarget target)
    {
        if (target.NodeId is not { } nodeId)
        {
            return ClipMaskAnimation.GetBaseValue(mask, target);
        }
        var node = nodes[nodeId];
        return target.Property switch
        {
            AnimationProperty.MASK_NODE_POSITION => node.Position,
            AnimationProperty.MASK_NODE_IN_HANDLE => node.InHandle,
            _ => node.OutHandle
        };
    }

    private static VectorClipMask ApplyNodes(VectorClipMask mask, Dictionary<Guid, MaskNode> values)
    {
        var contours = mask.Contours.ToBuilder();
        for (var contourIndex = 0; contourIndex < contours.Count; contourIndex++)
        {
            var contour = contours[contourIndex];
            ImmutableArray<MaskNode>.Builder? nodes = null;
            for (var nodeIndex = 0; nodeIndex < contour.Nodes.Length; nodeIndex++)
            {
                var node = values[contour.Nodes[nodeIndex].Id];
                if (!ReferenceEquals(node, contour.Nodes[nodeIndex]))
                {
                    nodes ??= contour.Nodes.ToBuilder();
                    nodes[nodeIndex] = node;
                }
            }
            if (nodes is not null)
            {
                contours[contourIndex] = contour with { Nodes = nodes.ToImmutable() };
            }
        }
        return mask with { Contours = contours.ToImmutable() };
    }

    private static IEnumerable<AnimationTrackTarget> Targets(ClipMask mask)
    {
        yield return new(AnimationProperty.MASK_POSITION);
        yield return new(AnimationProperty.MASK_SCALE);
        yield return new(AnimationProperty.MASK_ROTATION);
        if (mask is RectangleClipMask)
        {
            yield return new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
            yield return new(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT);
        }
        else if (mask is VectorClipMask vector)
        {
            foreach (var node in vector.Contours.SelectMany(contour => contour.Nodes))
            {
                yield return new(AnimationProperty.MASK_NODE_POSITION, node.Id);
                yield return new(AnimationProperty.MASK_NODE_IN_HANDLE, node.Id);
                yield return new(AnimationProperty.MASK_NODE_OUT_HANDLE, node.Id);
            }
        }
    }
}
