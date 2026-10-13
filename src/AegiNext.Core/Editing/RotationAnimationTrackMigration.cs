using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>将同一完整目标的标量旋转轴规范为 XYZ 轨道，精确保留独立曲线及有序变换语义。</summary>
public static partial class RotationAnimationTrackMigration
{
    /// <summary>合并指定三维旋转目标的旧轴轨道；缺失轴采用真实静态值，其他范围、状态及属性保持原样。</summary>
    public static ImmutableArray<AnimationTrack> Merge(ImmutableArray<AnimationTrack> tracks,
        AnimationTrackTarget target, SceneVector3 fallback)
    {
        return MergeCore(tracks, target, fallback, null);
    }

    private static ImmutableArray<AnimationTrack> MergeCore(ImmutableArray<AnimationTrack> tracks,
        AnimationTrackTarget target, SceneVector3 fallback, AnimationTrack? projectionSource)
    {
        if (tracks.IsDefault || tracks.Any(track => track is null) || target.Property != AnimationProperty.ROTATION_3D)
        {
            throw new InvalidDataException("The rotation track collection or canonical target is invalid.");
        }

        SubtitleAnimationTargetValidation.ValidateIdentity(target);
        ValidateFallback(fallback);
        var properties = AnimationPropertyMetadata.GetLegacyComponents(AnimationProperty.ROTATION_3D);
        var sources = properties.Select(property => tracks.Where(track => track.Target == target with { Property = property }).ToArray()).ToArray();
        var canonical = tracks.Where(track => track.Target == target).ToArray();
        if (sources.Any(source => source.Length > 1) || canonical.Length > 1 ||
            canonical.Length > 0 && sources.Any(source => source.Length > 0))
        {
            throw new InvalidDataException("Rotation axes are duplicated, or canonical and scalar rotation tracks coexist for the same target.");
        }

        foreach (var track in sources.SelectMany(source => source).Concat(canonical))
        {
            SubtitleAnimationTargetValidation.ValidateIdentity(track.Target);
            ProjectValidator.ValidateAnimationTrackData(track, true, projectionSource is null ? null :
                AnimationPropertyMetadata.GetMaximumTrackEntries(AnimationProperty.ROTATION_3D));
        }

        if (sources.All(source => source.Length == 0))
        {
            return tracks;
        }

        var axes = sources.Select(source => source.SingleOrDefault()).ToArray();
        var merged = axes.Any(source => source is { IsOrdered: true })
            ? projectionSource is null ? MergeOrdered(axes, target, fallback) : MergeProjectedOrdered(axes, target, fallback, projectionSource)
            : MergeKeyframes(axes, target, fallback);
        ProjectValidator.ValidateAnimationTrackData(merged, true);
        var result = ImmutableArray.CreateBuilder<AnimationTrack>(tracks.Length - axes.Count(axis => axis is not null) + 1);
        var inserted = false;
        foreach (var track in tracks)
        {
            if (axes.Contains(track))
            {
                if (!inserted)
                {
                    result.Add(merged);
                    inserted = true;
                }
            }
            else
            {
                result.Add(track);
            }
        }

        return result.MoveToImmutable();
    }

    /// <summary>规范一个图层的所有旋转目标，范围缺失轴读取所属范围的静态 XYZ，整层读取原生变换。</summary>
    public static ImmutableArray<AnimationTrack> Normalize(ProjectLayer layer, SubtitleLine? subtitle)
    {
        return NormalizeCore(layer, subtitle, []);
    }

    private static ImmutableArray<AnimationTrack> NormalizeCore(ProjectLayer layer, SubtitleLine? subtitle,
        Dictionary<AnimationTrackTarget, AnimationTrack> projectionSources)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Tracks.IsDefault || layer.Tracks.Any(track => track is null))
        {
            throw new InvalidDataException("The rotation track collection is invalid.");
        }

        var properties = AnimationPropertyMetadata.GetLegacyComponents(AnimationProperty.ROTATION_3D);
        var targets = layer.Tracks.Where(track => track.Property == AnimationProperty.ROTATION_3D || properties.Contains(track.Property))
            .Select(track => track.Target with { Property = AnimationProperty.ROTATION_3D }).Distinct().ToArray();
        var tracks = layer.Tracks;
        foreach (var target in targets)
        {
            var fallback = SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, target).Vector3;
            tracks = MergeCore(tracks, target, fallback, projectionSources.GetValueOrDefault(target));
        }

        return tracks;
    }

    private static AnimationTrack MergeKeyframes(AnimationTrack?[] axes, AnimationTrackTarget target, SceneVector3 fallback)
    {
        var times = axes.Where(axis => axis is not null).SelectMany(axis => axis!.Keyframes)
            .Select(frame => frame.Time).Distinct().Order().ToArray();
        if (times.Length > AnimationPropertyMetadata.GetMaximumTrackEntries(AnimationProperty.ROTATION_3D))
        {
            throw new InvalidDataException("The canonical rotation time union exceeds its component budget.");
        }

        var frames = ImmutableArray.CreateBuilder<Keyframe>(times.Length);
        for (var index = 0; index < times.Length; index++)
        {
            var time = times[index];
            var end = index + 1 < times.Length ? times[index + 1] : time;
            var value = AnimationValue.FromVector3(fallback);
            var curves = new AnimationCurve[axes.Length];
            for (var component = 0; component < axes.Length; component++)
            {
                if (axes[component] is { } source)
                {
                    value = value.WithComponent(component, SceneEvaluator.EvaluateScalarTrack(source, time));
                }

                curves[component] = GetCurve(axes[component], time, end);
            }

            var primary = curves[0];
            frames.Add(new(time, value, primary.Interpolation)
            {
                CurveStart = primary.CurveStart,
                CurveEnd = primary.CurveEnd,
                Exponent = primary.Exponent,
                Reverse = primary.Reverse,
                ComponentCurves = curves.Skip(1).All(curve => curve == primary) ? [] :
                    curves.Skip(1).Select(curve => curve == primary ? null : curve).ToImmutableArray()
            });
        }

        return new(target, frames.MoveToImmutable());
    }

    private static AnimationTrack MergeOrdered(AnimationTrack?[] axes, AnimationTrackTarget target, SceneVector3 fallback)
    {
        var initial = AnimationValue.FromVector3(fallback);
        var transforms = ImmutableArray.CreateBuilder<AnimationTransformOperation>();
        var ids = new HashSet<Guid>();
        for (var component = 0; component < axes.Length; component++)
        {
            if (axes[component] is not { } source)
            {
                continue;
            }

            initial = initial.WithComponent(component, source.IsOrdered ? source.InitialValue!.Value.Scalar : source.Keyframes[0].Value.Scalar);
            if (source.IsOrdered)
            {
                for (var index = 0; index < source.Transforms.Length; index++)
                {
                    var operation = source.Transforms[index];
                    transforms.Add(operation with
                    {
                        Id = UniqueOperationId(operation.Id, target, component, index, ids),
                        Value = AnimationValue.FromVector3(fallback).WithComponent(component, operation.Value.Scalar),
                        ComponentMask = 1 << component
                    });
                }
            }
            else
            {
                for (var index = 0; index + 1 < source.Keyframes.Length; index++)
                {
                    var first = source.Keyframes[index];
                    var second = source.Keyframes[index + 1];
                    transforms.Add(new(UniqueOperationId(Guid.Empty, target, component, index, ids), first.Time, second.Time,
                        AnimationValue.FromVector3(fallback).WithComponent(component, second.Value.Scalar))
                    {
                        ComponentMask = 1 << component,
                        Curve = first.GetCurve(0)
                    });
                }
            }
        }

        return new(target, []) { InitialValue = initial, Transforms = transforms.ToImmutable() };
    }

    private static AnimationCurve GetCurve(AnimationTrack? track, MediaTime time, MediaTime end)
    {
        if (track is null || end <= time || time < track.Keyframes[0].Time || time >= track.Keyframes[^1].Time)
        {
            return new(KeyframeInterpolation.HOLD);
        }

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
        var curve = first.GetCurve(0);
        var duration = Seconds(second.Time - first.Time);
        var range = curve.CurveEnd - curve.CurveStart;
        return curve with
        {
            CurveStart = Math.Clamp(curve.CurveStart + range * Seconds(time - first.Time) / duration, curve.CurveStart, curve.CurveEnd),
            CurveEnd = Math.Clamp(curve.CurveStart + range * Seconds(end - first.Time) / duration, curve.CurveStart, curve.CurveEnd)
        };
    }

    private static Guid UniqueOperationId(Guid preferred, AnimationTrackTarget target, int component, int index, HashSet<Guid> ids)
    {
        if (preferred != Guid.Empty && ids.Add(preferred))
        {
            return preferred;
        }

        for (var attempt = 0; ; attempt++)
        {
            var identity = string.Create(CultureInfo.InvariantCulture, $"{target.TextRangeId}:{target.State}:{component}:{index}:{preferred}:{attempt}");
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            var id = new Guid(hash.AsSpan(0, 16));
            if (id != Guid.Empty && ids.Add(id))
            {
                return id;
            }
        }
    }

    private static void ValidateFallback(SceneVector3 fallback)
    {
        var value = AnimationValue.FromVector3(fallback);
        for (var component = 0; component < value.ComponentCount; component++)
        {
            var number = value.GetComponent(component);
            if (!double.IsFinite(number) || number < AnimationPropertyMetadata.GetMinimum(AnimationProperty.ROTATION_3D, component) ||
                number > AnimationPropertyMetadata.GetMaximum(AnimationProperty.ROTATION_3D, component))
            {
                throw new InvalidDataException("The rotation fallback value is invalid.");
            }
        }
    }

    private static double Seconds(MediaTime time) => (double)time.Numerator / time.Denominator;
}
