using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    private static ImmutableArray<TimelineEffectRowIdentity> RemapEffectRowOrder(ImmutableArray<TimelineEffectRowIdentity> order,
        Dictionary<Guid, Guid>? rangeIds = null, Dictionary<Guid, Guid>? nodeIds = null,
        Dictionary<Guid, Guid>? instanceIds = null)
    {
        var result = ImmutableArray.CreateBuilder<TimelineEffectRowIdentity>();
        foreach (var identity in order)
        {
            if (identity.InstanceId is { } instanceId)
            {
                if (instanceIds is null)
                {
                    result.Add(identity);
                }
                else if (instanceIds.TryGetValue(instanceId, out var mapped))
                {
                    result.Add(new(InstanceId: mapped));
                }
                continue;
            }
            var target = identity.Target!.Value;
            if (target.TextRangeId is { } rangeId && rangeIds is not null)
            {
                if (!rangeIds.TryGetValue(rangeId, out var mapped))
                {
                    continue;
                }
                target = target with { TextRangeId = mapped };
            }
            if (target.NodeId is { } nodeId && nodeIds is not null)
            {
                if (!nodeIds.TryGetValue(nodeId, out var mapped))
                {
                    continue;
                }
                target = target with { NodeId = mapped };
            }
            result.Add(new(target));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<TimelineEffectRowIdentity> ReplaceInstanceRowOrder(ProjectLayer layer,
        SubtitleLine? subtitle, ImmutableArray<EffectInstance> instances)
    {
        if (layer.EffectRowOrder.IsEmpty)
        {
            return [];
        }
        var ids = new Queue<Guid>(instances.Reverse().Select(instance => instance.Id));
        return TimelineEffectRowOrdering.GetOrder(layer, subtitle)
            .Select(identity => identity.InstanceId.HasValue ? new TimelineEffectRowIdentity(InstanceId: ids.Dequeue()) : identity)
            .ToImmutableArray();
    }
}
