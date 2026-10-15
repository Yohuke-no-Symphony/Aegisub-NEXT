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
        foreach (var instance in prepared.Instances)
        {
            if (time < instance.Source.Start || time > instance.Source.End ||
                time == instance.Source.Start && beforeDiscontinuity || time == instance.Source.End && !beforeDiscontinuity)
            {
                continue;
            }
            foreach (var channel in instance.Plan.Channels)
            {
                if (time < channel.Start || time > channel.End || time == channel.Start && beforeDiscontinuity ||
                    time == channel.End && !beforeDiscontinuity)
                {
                    continue;
                }
                var target = channel.Multiplier.Target;
                var multiplier = SceneEvaluator.EvaluateTrack(channel.Multiplier, time, beforeDiscontinuity);
                var offset = SceneEvaluator.EvaluateTrack(channel.Offset, time, beforeDiscontinuity);
                if (prepared.Subtitle is not null && IsStyleProperty(target.Property))
                {
                    var range = target.TextRangeId is { } id
                        ? prepared.Subtitle.AnimationRanges.Single(value => value.Id == id) : null;
                    styles.Add(new(target, range?.Utf16Start ?? 0, range?.Utf16Length ?? prepared.Subtitle.Text.Length,
                        multiplier, offset, instance.Source.Id, channel.Line, channel.Column));
                    continue;
                }
                ExpandRotation(target, values);
                var input = values.TryGetValue(target, out var upstream) ? upstream :
                    target.Property == AnimationProperty.PATH_PROGRESS && layer.MotionPath is { } path
                        ? MotionPathProgress.EvaluateNatural(path, time)
                        : SubtitleAnimationEvaluation.GetBaseValue(layer, prepared.Subtitle, target);
                values[target] = EffectValueEvaluation.Apply(input, multiplier, offset, target.Property,
                    instance.Source.Id, channel.Line, channel.Column);
            }
        }
        return styles.ToImmutable();
    }

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
        foreach (var operation in operations)
        {
            if (operation.Target.TextRangeId.HasValue || operation.Target.State != SubtitleAnimationState.NORMAL)
            {
                continue;
            }
            appearance ??= new(values);
            var input = appearance.TryGetValue(operation.Target, out var value) ? value :
                SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, operation.Target);
            appearance[operation.Target] = operation.Apply(input);
        }
        return appearance ?? values;
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
}
