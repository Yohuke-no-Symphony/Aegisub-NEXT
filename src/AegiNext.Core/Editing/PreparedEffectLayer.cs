using System.Collections.Immutable;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

internal sealed record PreparedEffectLayer(SubtitleLine? Subtitle, ImmutableArray<PreparedEffectInstance> Instances)
{
    internal static PreparedEffectLayer Create(ProjectLayer layer, SubtitleLine? subtitle, EffectCompilationCache cache,
        CancellationToken cancellationToken = default)
    {
        var instances = ImmutableArray.CreateBuilder<PreparedEffectInstance>();
        var ranges = subtitle?.AnimationRanges.ToBuilder() ?? ImmutableArray.CreateBuilder<SubtitleAnimationRange>();
        var targets = layer.Tracks.Select(track => track.Target).ToHashSet();
        foreach (var instance in layer.Effects.Where(value => value.IsEnabled))
        {
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
        return new(projected, instances.ToImmutable());
    }
}
