using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

public static partial class RotationAnimationTrackMigration
{
    /// <summary>重组内部拆轴结果；同目标的原始 XYZ 轨道提供来源证据，缺失证据仍执行旧标量预算校验。</summary>
    public static ImmutableArray<AnimationTrack> MergeProjected(ImmutableArray<AnimationTrack> tracks,
        AnimationTrackTarget target, SceneVector3 fallback, AnimationTrack? projectionSource = null)
    {
        if (projectionSource is not null)
        {
            ValidateProjectionSource(projectionSource, target);
        }

        return MergeCore(tracks, target, fallback, projectionSource);
    }

    /// <summary>重组一个图层的内部旋转分量；来源使用拆轴前的原轨道，并按范围、状态及节点完整身份隔离。</summary>
    public static ImmutableArray<AnimationTrack> NormalizeProjected(ProjectLayer layer, SubtitleLine? subtitle,
        ImmutableArray<AnimationTrack> projectionSources = default)
    {
        var sources = new Dictionary<AnimationTrackTarget, AnimationTrack>();
        if (!projectionSources.IsDefault)
        {
            foreach (var track in projectionSources)
            {
                if (track is null)
                {
                    throw new InvalidDataException("The rotation projection source collection is invalid.");
                }

                if (track.Property != AnimationProperty.ROTATION_3D)
                {
                    continue;
                }

                ValidateProjectionSource(track, track.Target);
                if (!sources.TryAdd(track.Target, track))
                {
                    throw new InvalidDataException("The canonical rotation projection source is duplicated.");
                }
            }
        }

        return NormalizeCore(layer, subtitle, sources);
    }

    private static void ValidateProjectionSource(AnimationTrack source, AnimationTrackTarget target)
    {
        if (source.Target != target || source.Property != AnimationProperty.ROTATION_3D)
        {
            throw new InvalidDataException("The canonical rotation projection source does not match the complete target.");
        }

        SubtitleAnimationTargetValidation.ValidateIdentity(source.Target);
        ProjectValidator.ValidateAnimationTrackData(source, true);
    }

    private static AnimationTrack MergeProjectedOrdered(AnimationTrack?[] axes, AnimationTrackTarget target,
        SceneVector3 fallback, AnimationTrack projectionSource)
    {
        var initial = AnimationValue.FromVector3(fallback);
        var operations = new List<AnimationTransformOperation>();
        var origins = new List<(int Component, int Index)>();
        var priorities = new List<(int SourceIndex, int NodeIndex)>();
        var sequences = Enumerable.Range(0, axes.Length).Select(_ => new List<int>()).ToArray();
        var sourceOperations = projectionSource.Transforms.Select((operation, index) => (operation, index))
            .ToDictionary(item => item.operation.Id);
        var sharedNodes = new Dictionary<Guid, int>();
        for (var component = 0; component < axes.Length; component++)
        {
            if (axes[component] is not { } axis)
            {
                continue;
            }

            initial = initial.WithComponent(component, axis.IsOrdered ? axis.InitialValue!.Value.Scalar : axis.Keyframes[0].Value.Scalar);
            if (axis.IsOrdered)
            {
                for (var index = 0; index < axis.Transforms.Length; index++)
                {
                    var operation = axis.Transforms[index];
                    var canCombine = sourceOperations.TryGetValue(operation.Id, out var source) &&
                        CanRecombine(source.operation, operation, component);
                    int node;
                    if (canCombine && sharedNodes.TryGetValue(operation.Id, out node))
                    {
                        var previous = operations[node];
                        operations[node] = previous with
                        {
                            Value = previous.Value.WithComponent(component, operation.Value.Scalar),
                            ComponentMask = previous.ComponentMask | (1 << component)
                        };
                    }
                    else
                    {
                        node = operations.Count;
                        operations.Add(operation with
                        {
                            Value = AnimationValue.FromVector3(fallback).WithComponent(component, operation.Value.Scalar),
                            ComponentMask = 1 << component
                        });
                        origins.Add((component, index));
                        priorities.Add((canCombine ? source.index : int.MaxValue, node));
                        if (canCombine)
                        {
                            sharedNodes.Add(operation.Id, node);
                        }
                    }

                    sequences[component].Add(node);
                }
            }
            else
            {
                for (var index = 0; index + 1 < axis.Keyframes.Length; index++)
                {
                    var first = axis.Keyframes[index];
                    var second = axis.Keyframes[index + 1];
                    var node = operations.Count;
                    operations.Add(new(Guid.Empty, first.Time, second.Time,
                        AnimationValue.FromVector3(fallback).WithComponent(component, second.Value.Scalar))
                    {
                        ComponentMask = 1 << component,
                        Curve = first.GetCurve(0)
                    });
                    origins.Add((component, index));
                    priorities.Add((int.MaxValue, node));
                    sequences[component].Add(node);
                }
            }
        }

        if (operations.Count > AnimationPropertyMetadata.GetMaximumTrackEntries(AnimationProperty.ROTATION_3D))
        {
            throw new InvalidDataException("The canonical rotation operation union exceeds its component budget.");
        }

        var order = OrderProjectedOperations(sequences, priorities);
        if (order is null)
        {
            return MergeOrdered(axes, target, fallback);
        }

        var ids = new HashSet<Guid>();
        var transforms = ImmutableArray.CreateBuilder<AnimationTransformOperation>(operations.Count);
        foreach (var node in order)
        {
            var operation = operations[node];
            var origin = origins[node];
            transforms.Add(operation with { Id = UniqueOperationId(operation.Id, target, origin.Component, origin.Index, ids) });
        }

        return new(target, []) { InitialValue = initial, Transforms = transforms.MoveToImmutable() };
    }

    private static bool CanRecombine(AnimationTransformOperation source, AnimationTransformOperation operation, int component)
    {
        return (source.ComponentMask == 0 || (source.ComponentMask & (1 << component)) != 0) &&
            source.Start == operation.Start && source.End == operation.End && source.Acceleration == operation.Acceleration &&
            source.Curve == operation.Curve && source.Mode == operation.Mode;
    }

    private static int[]? OrderProjectedOperations(List<int>[] sequences,
        List<(int SourceIndex, int NodeIndex)> priorities)
    {
        var successors = Enumerable.Range(0, priorities.Count).Select(_ => new HashSet<int>()).ToArray();
        var dependencies = new int[priorities.Count];
        foreach (var sequence in sequences)
        {
            for (var index = 1; index < sequence.Count; index++)
            {
                var previous = sequence[index - 1];
                var next = sequence[index];
                if (previous != next && successors[previous].Add(next))
                {
                    dependencies[next]++;
                }
            }
        }

        var available = new PriorityQueue<int, (int SourceIndex, int NodeIndex)>();
        for (var index = 0; index < dependencies.Length; index++)
        {
            if (dependencies[index] == 0)
            {
                available.Enqueue(index, priorities[index]);
            }
        }

        var order = new int[priorities.Count];
        var count = 0;
        while (available.TryDequeue(out var node, out _))
        {
            order[count++] = node;
            foreach (var next in successors[node])
            {
                dependencies[next]--;
                if (dependencies[next] == 0)
                {
                    available.Enqueue(next, priorities[next]);
                }
            }
        }

        return count == order.Length ? order : null;
    }
}
