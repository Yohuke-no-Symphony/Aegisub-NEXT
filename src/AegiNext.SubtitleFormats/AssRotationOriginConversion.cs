using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssRotationOriginConversion
{
    internal static bool TryImport(ScenePoint origin, ScenePoint? basePosition, SubtitleLine line,
        LayerTransform transform, ImmutableArray<AnimationTrack> placementTracks, ImmutableArray<AnimationTrack> numericTracks,
        AssRotationOriginGeometry sourceGeometry, double canvasScaleX, double canvasScaleY,
        MediaTime contentOffset, MediaTime duration, out LayerTransform convertedTransform,
        out ImmutableArray<AnimationTrack> convertedPlacementTracks, out ImmutableArray<AnimationTrack> convertedNumericTracks,
        out string reason)
    {
        convertedTransform = transform;
        convertedPlacementTracks = placementTracks;
        convertedNumericTracks = numericTracks;
        reason = string.Empty;
        if (!ValidPoint(origin))
        {
            reason = "the rotation origin exceeds the native coordinate range";
            return false;
        }
        if (basePosition is not { } basis)
        {
            reason = "automatic or unpreserved placement cannot compensate for the rotation origin exactly";
            return false;
        }
        if (!ValidPoint(basis))
        {
            reason = "the measured or explicit placement exceeds the native coordinate range";
            return false;
        }
        if (!sourceGeometry.HasContent || !sourceGeometry.IsValid)
        {
            reason = "source text scaling or rotation exceeds the convertible native range";
            return false;
        }
        if (!sourceGeometry.IsComplete)
        {
            reason = "some source geometry transforms were not preserved, preventing exact rotation-origin compensation";
            return false;
        }
        if (!sourceGeometry.HasCommonScale || !sourceGeometry.HasCommonRotation || HasRangeGeometry(line, numericTracks))
        {
            reason = "text-range translation, scaling, or rotation cannot share this whole-line origin compensation";
            return false;
        }
        var scale = transform.Scale;
        var rotation = transform.Rotation;
        var rotationX = transform.RotationX;
        var rotationY = transform.RotationY;
        var animatedRotations = new HashSet<AnimationProperty>();
        var normalizedTracks = numericTracks.ToBuilder();
        for (var index = 0; index < numericTracks.Length; index++)
        {
            var track = numericTracks[index];
            if (track.Target.TextRangeId is not null || track.Property is not (AnimationProperty.SCALE or AnimationProperty.ROTATION or
                AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y))
            {
                continue;
            }
            if (track.Target.State != SubtitleAnimationState.NORMAL)
            {
                reason = "geometry for independent text states cannot share this whole-line origin compensation";
                return false;
            }
            var constant = AssGeometryAnimationWindow.TryConstant(track, contentOffset, contentOffset + duration, out var value);
            if (track.Property == AnimationProperty.SCALE)
            {
                if (!constant)
                {
                    reason = "animated scaling requires coordinated rotation-origin and position changes, which this conversion does not support";
                    return false;
                }
                scale = value.Vector;
            }
            else if (constant)
            {
                switch (track.Property)
                {
                    case AnimationProperty.ROTATION:
                        rotation = value.Scalar;
                        break;
                    case AnimationProperty.ROTATION_X:
                        rotationX = value.Scalar;
                        break;
                    case AnimationProperty.ROTATION_Y:
                        rotationY = value.Scalar;
                        break;
                }
            }
            else
            {
                animatedRotations.Add(track.Property);
            }
            if (constant && track.IsOrdered)
            {
                normalizedTracks[index] = track with
                {
                    Keyframes = [new(contentOffset, value)], InitialValue = null, Transforms = []
                };
            }
        }
        if (sourceGeometry.HasScaleAnimation)
        {
            reason = "animated scaling requires coordinated rotation-origin and position changes, which this conversion does not support";
            return false;
        }
        if (sourceGeometry.ConstantScale != scale ||
            sourceGeometry.ConstantRotation is { } expectedRotation &&
                (animatedRotations.Contains(AnimationProperty.ROTATION) || expectedRotation != rotation) ||
            sourceGeometry.ConstantRotationX is { } expectedRotationX &&
                (animatedRotations.Contains(AnimationProperty.ROTATION_X) || expectedRotationX != rotationX) ||
            sourceGeometry.ConstantRotationY is { } expectedRotationY &&
                (animatedRotations.Contains(AnimationProperty.ROTATION_Y) || expectedRotationY != rotationY))
        {
            reason = "fixed geometry within the source visible window differs from the imported result, preventing exact rotation-origin compensation";
            return false;
        }
        if (sourceGeometry.ConstantRotation is null && !animatedRotations.Contains(AnimationProperty.ROTATION) ||
            sourceGeometry.ConstantRotationX is null && !animatedRotations.Contains(AnimationProperty.ROTATION_X) ||
            sourceGeometry.ConstantRotationY is null && !animatedRotations.Contains(AnimationProperty.ROTATION_Y))
        {
            reason = "source rotation animation was not fully preserved, preventing exact rotation-origin compensation";
            return false;
        }
        if (!double.IsFinite(scale.X) || !double.IsFinite(scale.Y) || scale.X is <= 0 or > 10000 || scale.Y is <= 0 or > 10000)
        {
            reason = "rotation-origin compensation supports only fixed positive scaling; an axis with zero scaling cannot use this pivot conversion";
            return false;
        }
        if (!canvasScaleX.Equals(canvasScaleY) && (rotation != 0 || rotationX != 0 || rotationY != 0 || animatedRotations.Count > 0))
        {
            reason = "nonuniform canvas resampling combined with rotation produces shear, preventing exact rotation-origin compensation";
            return false;
        }
        if (placementTracks.Any(track => track.Property != AnimationProperty.POSITION || track.Target.TextRangeId is not null ||
            track.Target.State != SubtitleAnimationState.NORMAL || track.IsOrdered) || placementTracks.Length > 1)
        {
            reason = "rotation-origin compensation supports only a single whole-line straight-line movement";
            return false;
        }
        var movingPlacement = placementTracks.Any(track => !TryConstant(track, out var value) || value.Vector != default);
        var perspective = rotationX != 0 || rotationY != 0 || animatedRotations.Contains(AnimationProperty.ROTATION_X) ||
            animatedRotations.Contains(AnimationProperty.ROTATION_Y);
        if (perspective && movingPlacement)
        {
            reason = "perspective rotation and movement around a fixed origin require coordinated local depth and position changes, which this conversion does not support";
            return false;
        }
        if (animatedRotations.Count > 0 && movingPlacement)
        {
            reason = "rotation animation and movement coexist and require coordinated position changes, which this conversion does not support";
            return false;
        }
        var delta = new ScenePoint(origin.X - basis.X, origin.Y - basis.Y);
        var pivot = new ScenePoint(delta.X / scale.X, delta.Y / scale.Y);
        if (!ValidPoint(delta) || !ValidPoint(pivot))
        {
            reason = "translation or the local pivot after rotation-origin compensation exceeds the native coordinate range";
            return false;
        }
        var converted = ImmutableArray.CreateBuilder<AnimationTrack>(placementTracks.Length);
        foreach (var track in placementTracks)
        {
            var keys = ImmutableArray.CreateBuilder<Keyframe>(track.Keyframes.Length);
            foreach (var key in track.Keyframes)
            {
                var moved = Rotate(key.Value.Vector, rotation);
                var position = new ScenePoint(delta.X + moved.X, delta.Y + moved.Y);
                if (!ValidPoint(position))
                {
                    reason = "movement coordinates after rotation-origin compensation exceed the native range";
                    return false;
                }
                keys.Add(key with { Value = position });
            }
            converted.Add(track with { Keyframes = keys.MoveToImmutable() });
        }
        convertedTransform = transform with { Position = delta, Pivot = pivot };
        convertedPlacementTracks = converted.MoveToImmutable();
        convertedNumericTracks = normalizedTracks.ToImmutable();
        return true;
    }

    internal static bool ValidPoint(ScenePoint point) => double.IsFinite(point.X) && double.IsFinite(point.Y) &&
        Math.Abs(point.X) <= 1e9 && Math.Abs(point.Y) <= 1e9;

    internal static ScenePoint Rotate(ScenePoint point, double degrees)
    {
        var parts = AssTransformMath.SinCos(degrees);
        return new(point.X * parts.Cosine - point.Y * parts.Sine, point.X * parts.Sine + point.Y * parts.Cosine);
    }

    internal static bool HasRangeGeometry(SubtitleLine line, IEnumerable<AnimationTrack> tracks)
    {
        var geometryTracks = tracks.Where(track => track.Target.TextRangeId is not null &&
            track.Property is AnimationProperty.POSITION or AnimationProperty.SCALE or AnimationProperty.ROTATION or
                AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y or AnimationProperty.PERSPECTIVE_DISTANCE).ToArray();
        foreach (var range in line.AnimationRanges)
        {
            var position = range.Offset;
            var scale = range.Scale;
            var rotation = range.Rotation;
            var rotationX = range.RotationX;
            var rotationY = range.RotationY;
            var distance = range.PerspectiveDistance;
            foreach (var track in geometryTracks.Where(track => track.Target.TextRangeId == range.Id))
            {
                if (!TryConstant(track, out var value) || track.Target.State != SubtitleAnimationState.NORMAL)
                {
                    return true;
                }
                switch (track.Property)
                {
                    case AnimationProperty.POSITION:
                        position = value.Vector;
                        break;
                    case AnimationProperty.SCALE:
                        scale = value.Vector;
                        break;
                    case AnimationProperty.ROTATION:
                        rotation = value.Scalar;
                        break;
                    case AnimationProperty.ROTATION_X:
                        rotationX = value.Scalar;
                        break;
                    case AnimationProperty.ROTATION_Y:
                        rotationY = value.Scalar;
                        break;
                    case AnimationProperty.PERSPECTIVE_DISTANCE:
                        distance = value.Scalar;
                        break;
                }
            }
            if (position != default || scale != new ScenePoint(1, 1) || rotation != 0 || rotationX != 0 || rotationY != 0 ||
                distance != SceneProjection.DEFAULT_DISTANCE || range.ProjectionTranslation != default)
            {
                return true;
            }
        }
        return false;
    }

    internal static bool TryConstant(AnimationTrack track, out AnimationValue value)
    {
        return AssMoveConversion.TryConstant(track, out value);
    }
}
