using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssMaskGeometry
{
    internal static ProjectionMatrix3x3 InverseMatrix(ClipMask mask, ScenePoint canvasSize)
    {
        if (!ClipMaskPlacementGeometry.CreateMatrix(mask, canvasSize).TryInvert(out var inverse))
        {
            throw new InvalidOperationException("Base mask geometry cannot be recovered from ASS screen coordinates with zero scaling. Restore nonzero scaling first.");
        }
        return inverse;
    }

    internal static ClipMask Rebase(ClipMask mask, ClipMask original, ScenePoint canvasSize)
    {
        var matrix = InverseMatrix(original, canvasSize) * ClipMaskPlacementGeometry.CreateMatrix(mask, canvasSize);
        if (mask is RectangleClipMask rectangle && original.Transform.Rotation % 360 == 0)
        {
            var first = matrix.Map(rectangle.TopLeft);
            var second = matrix.Map(rectangle.BottomRight);
            return rectangle with
            {
                TopLeft = new(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
                BottomRight = new(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)),
                Placement = original.Placement, ReferenceFrame = original.ReferenceFrame, Transform = original.Transform
            };
        }
        var vector = mask as VectorClipMask ?? RectangleVector((RectangleClipMask)mask);
        return vector with
        {
            Placement = original.Placement, ReferenceFrame = original.ReferenceFrame, Transform = original.Transform,
            Contours = vector.Contours.Select(contour => contour with
            {
                Nodes = contour.Nodes.Select(node => node with
                {
                    Position = matrix.Map(node.Position), InHandle = MapHandle(node.InHandle, matrix),
                    OutHandle = MapHandle(node.OutHandle, matrix)
                }).ToImmutableArray()
            }).ToImmutableArray()
        };
    }

    internal static VectorClipMask RectangleVector(RectangleClipMask rectangle)
    {
        return new()
        {
            Inverted = rectangle.Inverted, Transform = rectangle.Transform,
            Placement = rectangle.Placement, ReferenceFrame = rectangle.ReferenceFrame, Contours = [new() { Nodes = [
                new() { Position = rectangle.TopLeft }, new() { Position = new(rectangle.BottomRight.X, rectangle.TopLeft.Y) },
                new() { Position = rectangle.BottomRight }, new() { Position = new(rectangle.TopLeft.X, rectangle.BottomRight.Y) }] }]
        };
    }

    private static ScenePoint MapHandle(ScenePoint handle, ProjectionMatrix3x3 matrix)
    {
        return new(matrix.M11 * handle.X + matrix.M12 * handle.Y,
            matrix.M21 * handle.X + matrix.M22 * handle.Y);
    }
}
