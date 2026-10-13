using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssRotationTracks
{
    private const int MAXIMUM_CURVE_MERGE_ULPS = 8;

    internal static ProjectLayer Expand(ProjectLayer layer, SubtitleLine? subtitle)
    {
        if (!layer.Tracks.Any(track => track.Property == AnimationProperty.ROTATION_3D))
        {
            return layer;
        }
        var (origin, _) = LayerAnimationTiming.GetRange(layer);
        var tracks = ImmutableArray.CreateBuilder<AnimationTrack>();
        foreach (var track in RotationAnimationTrackMigration.Normalize(layer, subtitle))
        {
            if (track.Property != AnimationProperty.ROTATION_3D)
            {
                tracks.Add(track);
                continue;
            }
            for (var component = 0; component < 3; component++)
            {
                tracks.Add(Component(track, component, origin));
            }
        }
        return layer with { Tracks = tracks.ToImmutable() };
    }

    internal static ImmutableArray<AnimationTrack> Normalize(ProjectLayer layer, SubtitleLine line,
        ImmutableArray<AnimationTrack> tracks, ProjectLayer? original = null)
    {
        var normalized = RotationAnimationTrackMigration.NormalizeProjected(layer with { Tracks = tracks }, line,
            original?.Tracks ?? default);
        if (original is null)
        {
            return normalized;
        }
        var preserved = layer.Tracks.Where(IsScalarRotation).GroupBy(track => CanonicalTarget(track.Target))
            .Where(group => group.All(component => tracks.Any(track => ReferenceEquals(track, component))))
            .Select(group => group.Key).ToHashSet();
        var originals = original.Tracks.Where(track => track.Property == AnimationProperty.ROTATION_3D)
            .ToDictionary(track => track.Target);
        return normalized.Select(track => preserved.Contains(track.Target) && originals.TryGetValue(track.Target, out var previous)
            ? previous : track).ToImmutableArray();
    }

    private static bool IsScalarRotation(AnimationTrack track) =>
        track.Property is AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y or AnimationProperty.ROTATION;

    private static AnimationTrackTarget CanonicalTarget(AnimationTrackTarget target) =>
        target with { Property = AnimationProperty.ROTATION_3D };

    private static AnimationTrack Component(AnimationTrack track, int component, MediaTime origin)
    {
        var property = component switch
        {
            0 => AnimationProperty.ROTATION_X,
            1 => AnimationProperty.ROTATION_Y,
            _ => AnimationProperty.ROTATION
        };
        var target = track.Target with { Property = property };
        if (track.IsOrdered)
        {
            var initial = AnimationValue.FromScalar(track.InitialValue!.Value.GetComponent(component));
            var operations = track.Transforms.Where(operation => operation.ComponentMask == 0 ||
                (operation.ComponentMask & (1 << component)) != 0).Select(operation => operation with
                {
                    Value = AnimationValue.FromScalar(operation.Value.GetComponent(component)), ComponentMask = 0
                }).ToImmutableArray();
            return operations.IsEmpty
                ? new(target, [new(origin, initial, KeyframeInterpolation.HOLD)])
                : new(target, []) { InitialValue = initial, Transforms = operations };
        }
        var frames = track.Keyframes.Select(frame =>
        {
            var curve = frame.GetCurve(component);
            return frame with
            {
                Value = AnimationValue.FromScalar(frame.Value.GetComponent(component)),
                Interpolation = curve.Interpolation, CurveStart = curve.CurveStart, CurveEnd = curve.CurveEnd,
                Exponent = curve.Exponent, Reverse = curve.Reverse, ComponentCurves = []
            };
        }).ToImmutableArray();
        return Compact(new(target, frames));
    }

    private static AnimationTrack Compact(AnimationTrack track)
    {
        var frames = new List<Keyframe>();
        foreach (var frame in track.Keyframes)
        {
            frames.Add(frame);
            while (frames.Count >= 3 && TryJoin(track.Target, frames[^3], frames[^2], frames[^1], out var joined))
            {
                frames[^3] = joined;
                frames.RemoveAt(frames.Count - 2);
            }
        }
        return track with { Keyframes = frames.ToImmutableArray() };
    }

    private static bool TryJoin(AnimationTrackTarget target, Keyframe first, Keyframe middle, Keyframe last, out Keyframe joined)
    {
        joined = first;
        if (first.Value == middle.Value && middle.Value == last.Value)
        {
            return true;
        }
        var before = first.GetCurve(0);
        var after = middle.GetCurve(0);
        if (before.Interpolation != after.Interpolation || before.Exponent != after.Exponent || before.Reverse != after.Reverse)
        {
            return false;
        }
        if (before.Interpolation == KeyframeInterpolation.HOLD && !before.Reverse && first.Value == middle.Value)
        {
            return true;
        }
        if (before.Interpolation == KeyframeInterpolation.LINEAR)
        {
            joined = first with { CurveStart = 0, CurveEnd = 1 };
        }
        else
        {
            if (before.CurveEnd != after.CurveStart ||
                !NearlyEqual((before.CurveEnd - before.CurveStart) * Seconds(last.Time - middle.Time),
                    (after.CurveEnd - after.CurveStart) * Seconds(middle.Time - first.Time)))
            {
                return false;
            }
            joined = first with { CurveEnd = after.CurveEnd };
        }
        return NearlyEqual(SceneEvaluator.EvaluateTrack(new(target, [joined, last]), middle.Time).Scalar, middle.Value.Scalar);
    }

    private static bool NearlyEqual(double first, double second)
    {
        if (first == second)
        {
            return true;
        }
        if (!double.IsFinite(first) || !double.IsFinite(second))
        {
            return false;
        }
        var minimum = Math.Min(first, second);
        var maximum = Math.Max(first, second);
        for (var index = 0; index < MAXIMUM_CURVE_MERGE_ULPS; index++)
        {
            minimum = Math.BitIncrement(minimum);
            if (minimum >= maximum)
            {
                return true;
            }
        }
        return false;
    }

    private static double Seconds(MediaTime value) => (double)value.Numerator / value.Denominator;
}
