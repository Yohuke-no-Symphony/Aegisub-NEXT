using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssDrawingTopology
{
    private const double FLATNESS = 1d / 1024;
    private const int MAX_EDGE_COMPARISONS = 2000000;
    private const int MAX_FLATTENED_POINTS = 100000;

    internal static bool TrySeparate(ImmutableArray<MaskContour> contours, out string reason)
    {
        var polygons = new List<List<ScenePoint>>();
        var comparisons = 0;
        var pointCount = 0;
        foreach (var contour in contours)
        {
            var polygon = new List<ScenePoint>();
            for (var index = 0; index < contour.Nodes.Length; index++)
            {
                var first = contour.Nodes[index];
                var second = contour.Nodes[(index + 1) % contour.Nodes.Length];
                if (index == 0)
                {
                    Add(first.Position);
                }
                if (first.OutHandle == default && second.InHandle == default)
                {
                    Add(second.Position);
                }
                else if (!Flatten(first.Position, Sum(first.Position, first.OutHandle),
                    Sum(second.Position, second.InHandle), second.Position, 0))
                {
                    reason = "curve topology could not be resolved within the subdivision budget";
                    return false;
                }
            }
            if (polygon.Count > 1 && polygon[^1] == polygon[0])
            {
                polygon.RemoveAt(polygon.Count - 1);
            }
            if (polygon.Count < 3 || Math.Abs(Area(polygon)) < FLATNESS * FLATNESS)
            {
                reason = "a contour has no independently fillable area";
                return false;
            }
            polygons.Add(polygon);

            void Add(ScenePoint point)
            {
                if (++pointCount > MAX_FLATTENED_POINTS)
                {
                    throw new InvalidDataException("ASS drawing exceeds the topology point budget.");
                }
                if (polygon.Count == 0 || polygon[^1] != point)
                {
                    polygon.Add(point);
                }
            }

            bool Flatten(ScenePoint start, ScenePoint control1, ScenePoint control2, ScenePoint end, int depth)
            {
                if (DistanceToSegment(control1, start, end) <= FLATNESS &&
                    DistanceToSegment(control2, start, end) <= FLATNESS)
                {
                    Add(end);
                    return true;
                }
                if (depth >= 16)
                {
                    return false;
                }
                var a = Midpoint(start, control1);
                var b = Midpoint(control1, control2);
                var c = Midpoint(control2, end);
                var d = Midpoint(a, b);
                var e = Midpoint(b, c);
                var middle = Midpoint(d, e);
                return Flatten(start, a, d, middle, depth + 1) && Flatten(middle, e, c, end, depth + 1);
            }
        }
        for (var firstIndex = 0; firstIndex < polygons.Count; firstIndex++)
        {
            var first = polygons[firstIndex];
            for (var secondIndex = firstIndex; secondIndex < polygons.Count; secondIndex++)
            {
                var second = polygons[secondIndex];
                if (firstIndex != secondIndex && (Contains(first, second[0]) || Contains(second, first[0])))
                {
                    reason = "nested contours may form holes and cannot be separated into native single-contour shapes";
                    return false;
                }
                for (var firstEdge = 0; firstEdge < first.Count; firstEdge++)
                {
                    for (var secondEdge = firstIndex == secondIndex ? firstEdge + 1 : 0; secondEdge < second.Count; secondEdge++)
                    {
                        if (firstIndex == secondIndex && (secondEdge == firstEdge + 1 ||
                            firstEdge == 0 && secondEdge == first.Count - 1))
                        {
                            continue;
                        }
                        if (++comparisons > MAX_EDGE_COMPARISONS)
                        {
                            reason = "contour topology exceeds the safe comparison budget";
                            return false;
                        }
                        var a = first[firstEdge];
                        var b = first[(firstEdge + 1) % first.Count];
                        var c = second[secondEdge];
                        var d = second[(secondEdge + 1) % second.Count];
                        if (Intersects(a, b, c, d) || DistanceToSegment(a, c, d) <= FLATNESS * 2 ||
                            DistanceToSegment(b, c, d) <= FLATNESS * 2 || DistanceToSegment(c, a, b) <= FLATNESS * 2 ||
                            DistanceToSegment(d, a, b) <= FLATNESS * 2)
                        {
                            reason = "intersecting or touching contours cannot be separated without changing fill composition";
                            return false;
                        }
                    }
                }
            }
        }
        reason = string.Empty;
        return true;
    }

    private static double Area(List<ScenePoint> points)
    {
        var result = 0d;
        for (var index = 0; index < points.Count; index++)
        {
            var next = points[(index + 1) % points.Count];
            result += points[index].X * next.Y - next.X * points[index].Y;
        }
        return result / 2;
    }

    private static bool Contains(List<ScenePoint> points, ScenePoint point)
    {
        var result = false;
        for (var index = 0; index < points.Count; index++)
        {
            var first = points[index];
            var second = points[(index + 1) % points.Count];
            if ((first.Y > point.Y) != (second.Y > point.Y) &&
                point.X < (second.X - first.X) * (point.Y - first.Y) / (second.Y - first.Y) + first.X)
            {
                result = !result;
            }
        }
        return result;
    }

    private static bool Intersects(ScenePoint a, ScenePoint b, ScenePoint c, ScenePoint d)
    {
        return Cross(a, b, c) * Cross(a, b, d) < 0 && Cross(c, d, a) * Cross(c, d, b) < 0;
    }

    private static double Cross(ScenePoint a, ScenePoint b, ScenePoint c)
    {
        return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    }

    private static double DistanceToSegment(ScenePoint point, ScenePoint start, ScenePoint end)
    {
        var x = end.X - start.X;
        var y = end.Y - start.Y;
        var length = x * x + y * y;
        var ratio = length == 0 ? 0 : Math.Clamp(((point.X - start.X) * x + (point.Y - start.Y) * y) / length, 0, 1);
        return Math.Sqrt(Math.Pow(point.X - start.X - ratio * x, 2) + Math.Pow(point.Y - start.Y - ratio * y, 2));
    }

    private static ScenePoint Sum(ScenePoint first, ScenePoint second) => new(first.X + second.X, first.Y + second.Y);

    private static ScenePoint Midpoint(ScenePoint first, ScenePoint second) => new((first.X + second.X) / 2, (first.Y + second.Y) / 2);
}
