using System.Collections.Immutable;
using AegiNext.Core.Projects;
using Avalonia;
using Avalonia.Media;
using PathGeometry = AegiNext.Core.Projects.PathGeometry;

namespace AegiNext.Desktop.Controls;

internal static class MaskCanvasGeometry
{
    internal static StreamGeometry Create(ClipMask mask, Matrix matrix)
    {
        var geometry = new StreamGeometry();
        using var stream = geometry.Open();
        stream.SetFillRule(FillRule.NonZero);
        foreach (var path in Paths(mask))
        {
            stream.BeginFigure(Project(path.Start, matrix), true);
            foreach (var segment in path.Segments)
            {
                stream.CubicBezierTo(Project(segment.Control1, matrix), Project(segment.Control2, matrix), Project(segment.End, matrix));
            }
            stream.EndFigure(true);
        }
        return geometry;
    }

    internal static Rect Bounds(ClipMask mask, Matrix matrix)
    {
        var bounds = ClipMaskPlacementGeometry.GetBounds(mask, new(matrix.M11, matrix.M21, matrix.M31,
            matrix.M12, matrix.M22, matrix.M32, matrix.M13, matrix.M23, matrix.M33));
        return new(bounds.Origin.X, bounds.Origin.Y, bounds.Size.X, bounds.Size.Y);
    }

    private static IEnumerable<PathGeometry> Paths(ClipMask mask)
    {
        if (mask is RectangleClipMask rectangle)
        {
            var first = rectangle.TopLeft;
            var second = new ScenePoint(rectangle.BottomRight.X, rectangle.TopLeft.Y);
            var third = rectangle.BottomRight;
            var fourth = new ScenePoint(rectangle.TopLeft.X, rectangle.BottomRight.Y);
            yield return new(first, [Line(first, second), Line(second, third), Line(third, fourth), Line(fourth, first)], true);
        }
        else if (mask is VectorClipMask vector)
        {
            foreach (var contour in vector.Contours.Where(contour => !contour.Nodes.IsEmpty))
            {
                var segments = ImmutableArray.CreateBuilder<CubicBezierSegment>(contour.Nodes.Length);
                for (var index = 0; index < contour.Nodes.Length; index++)
                {
                    var first = contour.Nodes[index];
                    var next = contour.Nodes[(index + 1) % contour.Nodes.Length];
                    segments.Add(new(Add(first.Position, first.OutHandle), Add(next.Position, next.InHandle), next.Position));
                }
                yield return new(contour.Nodes[0].Position, segments.MoveToImmutable(), true);
            }
        }
    }

    private static CubicBezierSegment Line(ScenePoint start, ScenePoint end) => new(start, end, end);
    private static ScenePoint Add(ScenePoint first, ScenePoint second) => new(first.X + second.X, first.Y + second.Y);
    private static Point Project(ScenePoint point, Matrix matrix) => new Point(point.X, point.Y) * matrix;
}
