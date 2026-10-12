using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>将一组旧标量轨道合并为完整属性，保留时间并集和每个分量的原始插值相位。</summary>
public static class LegacyAnimationTrackMigration
{
    /// <summary>仅迁移指定属性组；缺失分量使用调用方提供的真实基础值。</summary>
    public static ImmutableArray<AnimationTrack> Merge(ImmutableArray<AnimationTrack> tracks,
        AnimationProperty property, AnimationValue fallback)
    {
        var components = AnimationPropertyMetadata.GetLegacyComponents(property);
        if (tracks.IsDefault || tracks.Any(track => track is null) || components.IsEmpty ||
            fallback.Kind != AnimationPropertyMetadata.GetValueKind(property))
        {
            throw new InvalidDataException("The migrated track collection, property, or base value is invalid.");
        }

        var sources = components.Select(component => tracks.Where(track => track.Property == component).ToArray()).ToArray();
        if (sources.All(source => source.Length == 0))
        {
            return tracks;
        }

        if (sources.Any(source => source.Length > 1) || tracks.Any(track => track.Property == property))
        {
            throw new InvalidDataException($"Legacy components of {property} are duplicated, or old and new tracks coexist in the same group.");
        }

        foreach (var source in sources.SelectMany(source => source))
        {
            Validate(source);
        }

        var times = sources.SelectMany(source => source).SelectMany(track => track.Keyframes)
            .Select(frame => frame.Time).Distinct().Order().ToArray();
        var frames = ImmutableArray.CreateBuilder<Keyframe>(times.Length);
        for (var index = 0; index < times.Length; index++)
        {
            var time = times[index];
            var end = index + 1 < times.Length ? times[index + 1] : time;
            var value = fallback;
            var curves = new AnimationCurve[components.Length];
            for (var component = 0; component < components.Length; component++)
            {
                var source = sources[component].SingleOrDefault();
                if (source is not null)
                {
                    value = value.WithComponent(component, SceneEvaluator.EvaluateScalarTrack(source, time));
                }

                curves[component] = GetCurve(source, time, end);
            }

            frames.Add(new(time, value, curves[0].Interpolation)
            {
                CurveStart = curves[0].CurveStart,
                CurveEnd = curves[0].CurveEnd,
                Exponent = curves[0].Exponent,
                Reverse = curves[0].Reverse,
                ComponentCurves = curves.Skip(1).All(curve => curve == curves[0]) ? [] :
                    curves.Skip(1).Select(curve => curve == curves[0] ? null : curve).ToImmutableArray()
            });
        }

        return tracks.Where(track => !components.Contains(track.Property)).Append(new(property, frames.MoveToImmutable()))
            .OrderBy(track => track.Property).ToImmutableArray();
    }

    private static void Validate(AnimationTrack track)
    {
        if (track.Keyframes.IsDefaultOrEmpty || track.Keyframes.Length > 10000)
        {
            throw new InvalidDataException("A legacy animation component is empty or exceeds the budget.");
        }

        MediaTime? previous = null;
        foreach (var frame in track.Keyframes)
        {
            if (frame is null || frame.Value.Kind != AnimationValueKind.SCALAR || !double.IsFinite(frame.Value.Scalar) ||
                frame.Value.Scalar < AnimationPropertyMetadata.GetMinimum(track.Property) ||
                frame.Value.Scalar > AnimationPropertyMetadata.GetMaximum(track.Property) || frame.Time < MediaTime.Zero ||
                previous.HasValue && frame.Time <= previous.Value || !Enum.IsDefined(frame.Interpolation) ||
                !double.IsFinite(frame.CurveStart) || !double.IsFinite(frame.CurveEnd) || frame.CurveStart < 0 ||
                frame.CurveStart >= frame.CurveEnd || frame.CurveEnd > 1 || !double.IsFinite(frame.Exponent) || frame.Exponent <= 0 ||
                frame.ComponentCurves.IsDefault || !frame.ComponentCurves.IsEmpty || track.IsOrdered || track.Target.NodeId.HasValue)
            {
                throw new InvalidDataException("A legacy animation component contains invalid timing, values, or interpolation.");
            }

            previous = frame.Time;
        }
    }

    private static AnimationCurve GetCurve(AnimationTrack? track, MediaTime time, MediaTime end)
    {
        if (track is not null && end > time && time >= track.Keyframes[0].Time && time < track.Keyframes[^1].Time)
        {
            var lower = 0;
            var upper = track.Keyframes.Length - 1;
            while (lower + 1 < upper)
            {
                var middle = lower + (upper - lower) / 2;
                if (time >= track.Keyframes[middle].Time)
                {
                    lower = middle;
                }
                else
                {
                    upper = middle;
                }
            }

            var first = track.Keyframes[lower];
            var second = track.Keyframes[lower + 1];
            var duration = Seconds(second.Time - first.Time);
            var range = first.CurveEnd - first.CurveStart;
            return new(first.Interpolation,
                Math.Clamp(first.CurveStart + range * Seconds(time - first.Time) / duration, first.CurveStart, first.CurveEnd),
                Math.Clamp(first.CurveStart + range * Seconds(end - first.Time) / duration, first.CurveStart, first.CurveEnd))
            {
                Exponent = first.Exponent,
                Reverse = first.Reverse
            };
        }

        return new(KeyframeInterpolation.HOLD);
    }

    private static double Seconds(MediaTime time) => (double)time.Numerator / time.Denominator;
}
