using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

internal static class EffectRuntimeEvaluation
{
    internal static ImmutableArray<EffectStyleOperation> Apply(ProjectLayer layer, PreparedEffectLayer prepared,
        MediaTime time, Dictionary<AnimationTrackTarget, AnimationValue> values, bool beforeDiscontinuity)
    {
        var styles = ImmutableArray.CreateBuilder<EffectStyleOperation>();
        foreach (var entry in prepared.ExecutionOrder)
        {
            if (entry.Track is { } track)
            {
                var value = SceneEvaluator.EvaluateTrack(track, time, beforeDiscontinuity);
                Assign(track.Target, value, values);
                if (prepared.Subtitle is not null && IsStyleProperty(track.Property))
                {
                    styles.Add(StyleOperation(prepared.Subtitle, track.Target, Zero(value), value, null, 0, 0));
                }
                continue;
            }
            var instance = entry.Instance!;
            if (!IsActive(time, instance.Source.Start, instance.Source.End, beforeDiscontinuity))
            {
                continue;
            }
            foreach (var channel in instance.Plan.Channels)
            {
                if (!IsActive(time, channel.Start, channel.End, beforeDiscontinuity))
                {
                    continue;
                }
                var target = channel.Target;
                var multiplier = SceneEvaluator.EvaluateTrack(channel.Multiplier, time, beforeDiscontinuity);
                var offset = SceneEvaluator.EvaluateTrack(channel.Offset, time, beforeDiscontinuity);
                if (prepared.Subtitle is not null && IsStyleProperty(target.Property))
                {
                    styles.Add(StyleOperation(prepared.Subtitle, target, multiplier, offset,
                        instance.Source.Id, channel.Line, channel.Column));
                    continue;
                }
                var input = Input(layer, prepared.Subtitle, target, values, time);
                Assign(target, EffectValueEvaluation.Apply(input, multiplier, offset, target.Property,
                    instance.Source.Id, channel.Line, channel.Column), values);
            }
        }
        return styles.ToImmutable();
    }

    internal static bool IsActive(MediaTime time, MediaTime start, MediaTime end, bool beforeDiscontinuity = false) =>
        time >= start && time <= end && !(time == start && beforeDiscontinuity) &&
        !(time == end && !beforeDiscontinuity);

    internal static bool IsStyleProperty(AnimationProperty property) => property is
        AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING or AnimationProperty.FILL or
        AnimationProperty.STROKE or AnimationProperty.STROKE_WIDTH or AnimationProperty.FILL_BLUR or
        AnimationProperty.STROKE_BLUR or AnimationProperty.SHADOW_OFFSET or AnimationProperty.SHADOW_BLUR or
        AnimationProperty.SHADOW_COLOR;

    internal static Dictionary<AnimationTrackTarget, AnimationValue> WholeStyleValues(ProjectLayer layer,
        SubtitleLine? subtitle, Dictionary<AnimationTrackTarget, AnimationValue> values,
        ImmutableArray<EffectStyleOperation> operations)
    {
        Dictionary<AnimationTrackTarget, AnimationValue>? appearance = null;
        var assigned = new HashSet<AnimationTrackTarget>();
        foreach (var operation in operations)
        {
            if (operation.Target.TextRangeId.HasValue || operation.Target.State != SubtitleAnimationState.NORMAL)
            {
                continue;
            }
            appearance ??= new(values);
            var input = assigned.Add(operation.Target)
                ? SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, operation.Target)
                : appearance[operation.Target];
            appearance[operation.Target] = operation.Apply(input);
        }
        return appearance ?? values;
    }

    private static EffectStyleOperation StyleOperation(SubtitleLine subtitle, AnimationTrackTarget target,
        AnimationValue multiplier, AnimationValue offset, Guid? instanceId, int line, int column)
    {
        var range = target.TextRangeId is { } id ? subtitle.AnimationRanges.Single(value => value.Id == id) : null;
        return new(target, range?.Utf16Start ?? 0, range?.Utf16Length ?? subtitle.Text.Length,
            multiplier, offset, instanceId, line, column);
    }

    private static AnimationValue Input(ProjectLayer layer, SubtitleLine? subtitle, AnimationTrackTarget target,
        Dictionary<AnimationTrackTarget, AnimationValue> values, MediaTime time)
    {
        ExpandRotation(target, values);
        if (values.TryGetValue(target, out var upstream))
        {
            return upstream;
        }
        if (target.Property == AnimationProperty.PATH_PROGRESS && layer.MotionPath is { } path)
        {
            return MotionPathProgress.EvaluateNatural(path, time);
        }
        var basis = SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, target);
        if (target.Property != AnimationProperty.ROTATION_3D)
        {
            return basis;
        }
        return new SceneVector3(
            values.TryGetValue(target with { Property = AnimationProperty.ROTATION_X }, out var x) ? x.Scalar : basis.Vector3.X,
            values.TryGetValue(target with { Property = AnimationProperty.ROTATION_Y }, out var y) ? y.Scalar : basis.Vector3.Y,
            values.TryGetValue(target with { Property = AnimationProperty.ROTATION }, out var z) ? z.Scalar : basis.Vector3.Z);
    }

    private static void Assign(AnimationTrackTarget target, AnimationValue value,
        Dictionary<AnimationTrackTarget, AnimationValue> values)
    {
        ExpandRotation(target, values);
        if (target.Property == AnimationProperty.ROTATION_3D)
        {
            values.Remove(target with { Property = AnimationProperty.ROTATION_X });
            values.Remove(target with { Property = AnimationProperty.ROTATION_Y });
            values.Remove(target with { Property = AnimationProperty.ROTATION });
        }
        values[target] = value;
    }

    private static void ExpandRotation(AnimationTrackTarget target, Dictionary<AnimationTrackTarget, AnimationValue> values)
    {
        if (target.Property is not (AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y or AnimationProperty.ROTATION))
        {
            return;
        }
        var canonical = target with { Property = AnimationProperty.ROTATION_3D };
        if (!values.Remove(canonical, out var rotation))
        {
            return;
        }
        values[target with { Property = AnimationProperty.ROTATION_X }] = rotation.Vector3.X;
        values[target with { Property = AnimationProperty.ROTATION_Y }] = rotation.Vector3.Y;
        values[target with { Property = AnimationProperty.ROTATION }] = rotation.Vector3.Z;
    }

    private static AnimationValue Zero(AnimationValue value)
    {
        for (var component = 0; component < value.ComponentCount; component++)
        {
            value = value.WithComponent(component, 0);
        }
        return value;
    }
}
