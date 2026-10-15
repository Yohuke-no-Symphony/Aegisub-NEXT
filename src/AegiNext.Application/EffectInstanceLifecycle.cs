using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    private static ImmutableArray<EffectInstance> CloneEffectInstances(ImmutableArray<EffectInstance> instances,
        Dictionary<Guid, Guid>? rangeIds = null)
    {
        return instances.Where(instance => instance.TargetContext?.TextRangeId is not { } rangeId ||
                rangeIds is null || rangeIds.ContainsKey(rangeId))
            .Select(instance => instance with
            {
                Id = Guid.NewGuid(),
                TargetContext = RemapEffectContext(instance.TargetContext, rangeIds)
            }).ToImmutableArray();
    }

    private static ImmutableArray<EffectInstance> RebaseEffectInstances(ImmutableArray<EffectInstance> instances,
        MediaTime offset, Dictionary<Guid, Guid>? rangeIds = null)
    {
        return instances.Select(instance => instance with
        {
            Start = instance.Start + offset,
            End = instance.End + offset,
            TargetContext = RemapEffectContext(instance.TargetContext, rangeIds)
        }).ToImmutableArray();
    }

    private static AnimationTrackTarget? RemapEffectContext(AnimationTrackTarget? context,
        Dictionary<Guid, Guid>? rangeIds)
    {
        return context?.TextRangeId is { } rangeId && rangeIds is not null
            ? context.Value with { TextRangeId = rangeIds[rangeId] } : context;
    }

    internal static ProjectLayer PruneEffectInstanceTargets(ProjectLayer layer, SubtitleLine subtitle)
    {
        if (!layer.Effects.Any(instance => instance.TargetContext?.TextRangeId.HasValue == true))
        {
            return layer;
        }
        var rangeIds = subtitle.AnimationRanges.Select(range => range.Id).ToHashSet();
        var effects = layer.Effects.Where(instance => instance.TargetContext?.TextRangeId is not { } id || rangeIds.Contains(id))
            .ToImmutableArray();
        return effects.SequenceEqual(layer.Effects) ? layer : layer with { Effects = effects };
    }
}
