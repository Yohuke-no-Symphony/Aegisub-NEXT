using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

internal static class EffectScriptRotationTracks
{
    internal static ProjectLayer Expand(ProjectLayer layer, SubtitleLine? subtitle)
    {
        if (!layer.Tracks.Any(track => track.Property == AnimationProperty.ROTATION_3D))
        {
            return layer;
        }
        var normalized = RotationAnimationTrackMigration.Normalize(layer, subtitle);
        var (origin, _) = LayerAnimationTiming.GetRange(layer);
        var tracks = ImmutableArray.CreateBuilder<AnimationTrack>();
        foreach (var track in normalized)
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

    internal static EffectScriptCompilation Normalize(EffectScriptCompilation compilation, ProjectLayer source, SubtitleLine? subtitle)
    {
        var layer = compilation.PreparedLayer ?? source;
        var line = compilation.Subtitle ?? subtitle;
        var tracks = NormalizeLayer(layer with { Tracks = compilation.Tracks }, line, source.Tracks);
        var declared = compilation.Intervals.Select(interval => CanonicalTarget(interval.Target)).ToHashSet();
        var unchanged = source.Tracks.Where(track => track.Property == AnimationProperty.ROTATION_3D &&
            !declared.Contains(track.Target)).ToDictionary(track => track.Target);
        if (unchanged.Count > 0)
        {
            tracks = tracks.Select(track => unchanged.GetValueOrDefault(track.Target, track)).ToImmutableArray();
        }
        var preparedTracks = NormalizeLayer(layer, line, source.Tracks);
        var prepared = preparedTracks.SequenceEqual(layer.Tracks) ? layer : layer with { Tracks = preparedTracks };
        return compilation with
        {
            Tracks = tracks,
            PreparedLayer = compilation.PreparedLayer is null ? null : prepared,
            Intervals = compilation.Intervals.Select(interval => IsRotation(interval.Target.Property)
                ? interval with
                {
                    Target = interval.Target with { Property = AnimationProperty.ROTATION_3D },
                    ComponentMask = 1 << ComponentIndex(interval.Target.Property)
                } : interval).ToImmutableArray()
        };
    }

    internal static AnimationTrackTarget CanonicalTarget(AnimationTrackTarget target) =>
        IsRotation(target.Property) ? target with { Property = AnimationProperty.ROTATION_3D } : target;

    private static ImmutableArray<AnimationTrack> NormalizeLayer(ProjectLayer layer, SubtitleLine? subtitle,
        ImmutableArray<AnimationTrack> projectionSources)
    {
        try
        {
            return RotationAnimationTrackMigration.NormalizeProjected(layer, subtitle, projectionSources);
        }
        catch (InvalidDataException error)
        {
            throw new EffectScriptException("三维旋转分量无法合并；轨道数据无效或时间并集、操作数量超出预算。", innerException: error);
        }
    }

    private static bool IsRotation(AnimationProperty property) =>
        property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y;

    private static int ComponentIndex(AnimationProperty property) => property switch
    {
        AnimationProperty.ROTATION_X => 0,
        AnimationProperty.ROTATION_Y => 1,
        AnimationProperty.ROTATION => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(property))
    };

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
        return new(target, frames);
    }
}
