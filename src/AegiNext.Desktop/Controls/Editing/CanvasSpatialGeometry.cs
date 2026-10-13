using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using Avalonia;
using SkiaSharp;

namespace AegiNext.Desktop.Controls;

internal static class CanvasSpatialGeometry
{
    internal static AnimationProperty[] Properties { get; } =
    [
        AnimationProperty.ROTATION_X, AnimationProperty.ROTATION_Y,
        AnimationProperty.ROTATION, AnimationProperty.PERSPECTIVE_DISTANCE
    ];

    internal static double Value(LayerTransform transform, AnimationProperty property)
    {
        return property switch
        {
            AnimationProperty.ROTATION_X => transform.RotationX,
            AnimationProperty.ROTATION_Y => transform.RotationY,
            AnimationProperty.ROTATION => transform.Rotation,
            AnimationProperty.PERSPECTIVE_DISTANCE => transform.PerspectiveDistance,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }

    internal static LayerTransform RangeTransform(SubtitleAnimationRange range)
    {
        return new()
        {
            Position = range.Offset, Scale = range.Scale, Rotation = range.Rotation,
            RotationX = range.RotationX, RotationY = range.RotationY,
            PerspectiveDistance = range.PerspectiveDistance, ProjectionTranslation = range.ProjectionTranslation
        };
    }

    internal static bool TryProjectVector(SceneVector3 vector, LayerTransform transform,
        ProjectLayerGeometry geometry, Matrix fit, out Point screen)
    {
        screen = default;
        var rotated = SceneProjection.Rotate(vector, transform);
        var translation = transform.ProjectionTranslation;
        var depth = rotated.Z + translation.Z;
        var w = (transform.PerspectiveDistance + depth) / transform.PerspectiveDistance;
        if (!double.IsFinite(w) || w < SceneProjection.NEAR_PLANE_RATIO)
        {
            return false;
        }
        var center = new ScenePoint(geometry.BasePosition.X + transform.X, geometry.BasePosition.Y + transform.Y);
        var parent = new ScenePoint(center.X + (rotated.X + translation.X) / w,
            center.Y + (rotated.Y + translation.Y) / w);
        if (!geometry.TryMapParentPoint(new SKPoint((float)parent.X, (float)parent.Y), out _) ||
            !geometry.ParentProjection.TryMap(parent, out var world))
        {
            return false;
        }
        screen = new Point(world.X, world.Y) * fit;
        return double.IsFinite(screen.X) && double.IsFinite(screen.Y);
    }

    internal static SceneVector3 RingPoint(AnimationProperty property, double radius, double angle)
    {
        var cosine = Math.Cos(angle) * radius;
        var sine = Math.Sin(angle) * radius;
        return property switch
        {
            AnimationProperty.ROTATION_X => new(0, cosine, sine),
            AnimationProperty.ROTATION_Y => new(cosine, 0, sine),
            AnimationProperty.ROTATION => new(cosine, sine, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
    }

    internal static double DragValue(AnimationProperty property, double original, Point start, Point current,
        double accumulatedAngle, bool snap)
    {
        if (property == AnimationProperty.PERSPECTIVE_DISTANCE)
        {
            var exponent = Math.Clamp((current.X - start.X) / 120, -50, 50);
            return Math.Clamp(original * Math.Exp(exponent), SceneProjection.MIN_DISTANCE, SceneProjection.MAX_DISTANCE);
        }
        var delta = property switch
        {
            AnimationProperty.ROTATION_X => start.Y - current.Y,
            AnimationProperty.ROTATION_Y => current.X - start.X,
            AnimationProperty.ROTATION => accumulatedAngle,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
        var value = original + delta;
        if (snap)
        {
            value = Math.Round(value / 15) * 15;
        }
        return Math.Clamp(value, -1e9, 1e9);
    }

    internal static double PointerAngle(Point point, Point center)
    {
        return Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI;
    }

    internal static double AccumulateAngle(double accumulated, double previous, double current)
    {
        return accumulated + Math.IEEERemainder(current - previous, 360);
    }
}
