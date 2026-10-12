using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssMaskGeometry
{
    internal static ScenePoint Transform(ScenePoint point, MaskTransform transform)
    {
        var angle = transform.Rotation * Math.PI / 180;
        var x = (point.X - transform.Pivot.X) * transform.Scale.X;
        var y = (point.Y - transform.Pivot.Y) * transform.Scale.Y;
        return new(x * Math.Cos(angle) - y * Math.Sin(angle) + transform.Pivot.X + transform.Position.X,
            x * Math.Sin(angle) + y * Math.Cos(angle) + transform.Pivot.Y + transform.Position.Y);
    }

    internal static ScenePoint Inverse(ScenePoint point, MaskTransform transform, bool handle = false)
    {
        if (transform.Scale.X == 0 || transform.Scale.Y == 0)
        {
            throw new InvalidOperationException("Base mask geometry cannot be recovered from ASS screen coordinates with zero scaling. Restore nonzero scaling first.");
        }
        var angle = transform.Rotation * Math.PI / 180;
        var x = point.X - (handle ? 0 : transform.Pivot.X + transform.Position.X);
        var y = point.Y - (handle ? 0 : transform.Pivot.Y + transform.Position.Y);
        return new((x * Math.Cos(angle) + y * Math.Sin(angle)) / transform.Scale.X + (handle ? 0 : transform.Pivot.X),
            (-x * Math.Sin(angle) + y * Math.Cos(angle)) / transform.Scale.Y + (handle ? 0 : transform.Pivot.Y));
    }

    internal static ClipMask Rebase(ClipMask mask, MaskTransform transform)
    {
        if (mask is RectangleClipMask rectangle && transform.Rotation % 360 == 0)
        {
            var first = Inverse(rectangle.TopLeft, transform);
            var second = Inverse(rectangle.BottomRight, transform);
            return rectangle with
            {
                TopLeft = new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
                BottomRight = new(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)), Transform = transform
            };
        }
        var vector = mask as VectorClipMask ?? RectangleVector((RectangleClipMask)mask);
        return vector with
        {
            Transform = transform,
            Contours = vector.Contours.Select(contour => contour with
            {
                Nodes = contour.Nodes.Select(node => node with
                {
                    Position = Inverse(node.Position, transform), InHandle = Inverse(node.InHandle, transform, true),
                    OutHandle = Inverse(node.OutHandle, transform, true)
                }).ToImmutableArray()
            }).ToImmutableArray()
        };
    }

    internal static VectorClipMask RectangleVector(RectangleClipMask rectangle)
    {
        return new()
        {
            Inverted = rectangle.Inverted, Transform = rectangle.Transform, Contours = [new() { Nodes = [
                new() { Position = rectangle.TopLeft }, new() { Position = new(rectangle.BottomRight.X, rectangle.TopLeft.Y) },
                new() { Position = rectangle.BottomRight }, new() { Position = new(rectangle.TopLeft.X, rectangle.BottomRight.Y) }] }]
        };
    }
}
