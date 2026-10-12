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
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        foreach (var path in Paths(mask))
        {
            var start = Project(path.Start, matrix);
            foreach (var segment in path.Segments)
            {
                var control1 = Project(segment.Control1, matrix);
                var control2 = Project(segment.Control2, matrix);
                var end = Project(segment.End, matrix);
                var x = CubicBounds(start.X, control1.X, control2.X, end.X);
                var y = CubicBounds(start.Y, control1.Y, control2.Y, end.Y);
                left = Math.Min(left, x.Minimum);
                top = Math.Min(top, y.Minimum);
                right = Math.Max(right, x.Maximum);
                bottom = Math.Max(bottom, y.Maximum);
                start = end;
            }
        }
        return double.IsPositiveInfinity(left) ? default : new(new Point(left, top), new Point(right, bottom));
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

    private static (double Minimum, double Maximum) CubicBounds(double start, double control1, double control2, double end)
    {
        var bounds = (Minimum: Math.Min(start, end), Maximum: Math.Max(start, end));
        var a = -start + 3 * control1 - 3 * control2 + end;
        var b = 2 * (start - 2 * control1 + control2);
        var c = control1 - start;
        if (a == 0)
        {
            return b == 0 ? bounds : IncludeExtremum(bounds, start, control1, control2, end, -c / b);
        }
        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
        {
            return bounds;
        }
        var q = -0.5 * (b + Math.CopySign(Math.Sqrt(discriminant), b));
        if (q == 0)
        {
            return IncludeExtremum(bounds, start, control1, control2, end, -b / (2 * a));
        }
        bounds = IncludeExtremum(bounds, start, control1, control2, end, q / a);
        return IncludeExtremum(bounds, start, control1, control2, end, c / q);
    }

    private static (double Minimum, double Maximum) IncludeExtremum((double Minimum, double Maximum) bounds,
        double start, double control1, double control2, double end, double progress)
    {
        if (progress is <= 0 or >= 1)
        {
            return bounds;
        }
        var remaining = 1 - progress;
        var value = remaining * remaining * remaining * start + 3 * remaining * remaining * progress * control1 +
            3 * remaining * progress * progress * control2 + progress * progress * progress * end;
        return (Math.Min(bounds.Minimum, value), Math.Max(bounds.Maximum, value));
    }

    private static CubicBezierSegment Line(ScenePoint start, ScenePoint end) => new(start, end, end);
    private static ScenePoint Add(ScenePoint first, ScenePoint second) => new(first.X + second.X, first.Y + second.Y);
    private static Point Project(ScenePoint point, Matrix matrix) => new Point(point.X, point.Y) * matrix;
}
