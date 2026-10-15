using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

internal sealed record PreparedEffectLayer(SubtitleLine? Subtitle, ImmutableArray<PreparedEffectInstance> Instances,
    ImmutableArray<PreparedAnimationStackEntry> ExecutionOrder)
{
    internal static PreparedEffectLayer Create(ProjectLayer layer, SubtitleLine? subtitle, EffectCompilationCache cache,
        CancellationToken cancellationToken = default)
    {
        var instances = ImmutableArray.CreateBuilder<PreparedEffectInstance>();
        var ranges = subtitle?.AnimationRanges.ToBuilder() ?? ImmutableArray.CreateBuilder<SubtitleAnimationRange>();
        var targets = layer.Tracks.Select(track => track.Target).ToHashSet();
        var executionOrder = TimelineEffectRowOrdering.GetOrder(layer, subtitle).Reverse().ToImmutableArray();
        var enabledInstances = layer.Effects.Where(instance => instance.IsEnabled).ToDictionary(instance => instance.Id);
        foreach (var identity in executionOrder)
        {
            if (identity.InstanceId is not { } instanceId || !enabledInstances.TryGetValue(instanceId, out var instance))
            {
                continue;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var plan = cache.GetOrCompile(instance, layer, subtitle, cancellationToken);
            ranges.AddRange(plan.Ranges);
            targets.UnionWith(plan.Channels.Select(channel => channel.Multiplier.Target));
            if (ranges.Count > 256 || targets.Count(target => target.TextRangeId.HasValue || target.State != SubtitleAnimationState.NORMAL) > 8192)
            {
                throw new EffectScriptException("特效实例栈超出文字范围或动画目标预算。");
            }
            instances.Add(new(instance, plan));
        }
        var projected = subtitle is null || ranges.Count == subtitle.AnimationRanges.Length
            ? subtitle : subtitle with { AnimationRanges = ranges.ToImmutable() };
        var preparedInstances = instances.ToImmutable();
        var instanceIndex = preparedInstances.ToDictionary(instance => instance.Source.Id);
        var tracks = layer.Tracks.ToDictionary(track => track.Target);
        var order = ImmutableArray.CreateBuilder<PreparedAnimationStackEntry>();
        foreach (var identity in executionOrder)
        {
            if (identity.Target is { } target)
            {
                order.Add(new(Track: tracks[target]));
            }
            else if (identity.InstanceId is { } id && instanceIndex.TryGetValue(id, out var instance))
            {
                order.Add(new(Instance: instance));
            }
        }
        return new(projected, preparedInstances, order.ToImmutable());
    }
}
