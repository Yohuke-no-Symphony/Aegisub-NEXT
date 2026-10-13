using AegiNext.Core.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal static class ProjectedBezierGeometry
{
    private const double FLATNESS = 0.25;
    private const int MAX_DEPTH = 24;

    internal static IReadOnlyList<(double Progress, Point? Position)> Sample(ScenePoint start, CubicBezierSegment segment,
        ProjectionMatrix3x3 projection, Matrix fit, Rect? viewport = null)
    {
        var samples = new List<(double Progress, Point? Position)> { (0, Map(start, projection, fit)) };
        Subdivide(start, segment.Control1, segment.Control2, segment.End, 0, 1, 0);
        return samples;

        void Subdivide(ScenePoint first, ScenePoint control1, ScenePoint control2, ScenePoint last, double from, double to, int depth)
        {
            var minimumW = Math.Min(Math.Min(projection.W(first), projection.W(control1)),
                Math.Min(projection.W(control2), projection.W(last)));
            var maximumW = Math.Max(Math.Max(projection.W(first), projection.W(control1)),
                Math.Max(projection.W(control2), projection.W(last)));
            if (!double.IsFinite(minimumW) || !double.IsFinite(maximumW) || maximumW < SceneProjection.NEAR_PLANE_RATIO)
            {
                samples.Add((to, null));
                return;
            }
            if (minimumW >= SceneProjection.NEAR_PLANE_RATIO && Map(first, projection, fit) is { } firstScreen &&
                Map(control1, projection, fit) is { } control1Screen && Map(control2, projection, fit) is { } control2Screen &&
                Map(last, projection, fit) is { } lastScreen)
            {
                if (viewport is { } rectangle && Outside(rectangle, firstScreen, control1Screen, control2Screen, lastScreen))
                {
                    samples.Add((to, null));
                    return;
                }
                if (Math.Max(DistanceToLine(control1Screen, firstScreen, lastScreen),
                        DistanceToLine(control2Screen, firstScreen, lastScreen)) <= FLATNESS * FLATNESS)
                {
                    if (samples[^1].Position is null)
                    {
                        samples.Add((from, firstScreen));
                    }
                    samples.Add((to, lastScreen));
                    return;
                }
            }
            var p = Midpoint(first, control1);
            var q = Midpoint(control1, control2);
            var r = Midpoint(control2, last);
            var s = Midpoint(p, q);
            var t = Midpoint(q, r);
            var middle = Midpoint(s, t);
            var progress = (from + to) / 2;
            if (depth >= MAX_DEPTH)
            {
                samples.Add((progress, Map(middle, projection, fit)));
                samples.Add((to, Map(last, projection, fit)));
                return;
            }
            Subdivide(first, p, s, middle, from, progress, depth + 1);
            Subdivide(middle, t, r, last, progress, to, depth + 1);
        }
    }

    internal static (double Progress, Point Position, double DistanceSquared)? FindNearest(ScenePoint start,
        CubicBezierSegment segment, ProjectionMatrix3x3 projection, Matrix fit, Point pointer, double tolerance)
    {
        var samples = Sample(start, segment, projection, fit, new Rect(pointer - new Vector(tolerance + FLATNESS, tolerance + FLATNESS),
            new Size((tolerance + FLATNESS) * 2, (tolerance + FLATNESS) * 2)));
        var bestDistance = tolerance * tolerance;
        (double Progress, Point Position, double DistanceSquared)? nearest = null;
        foreach (var sample in samples)
        {
            Include(sample.Progress);
        }
        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            if (previous.Position is not { } first || current.Position is not { } last ||
                DistanceToLine(pointer, first, last) > (tolerance + FLATNESS) * (tolerance + FLATNESS))
            {
                continue;
            }
            var from = previous.Progress;
            var to = current.Progress;
            const double GOLDEN_RATIO = 0.6180339887498949;
            var a = to - (to - from) * GOLDEN_RATIO;
            var b = from + (to - from) * GOLDEN_RATIO;
            var distanceA = Distance(a);
            var distanceB = Distance(b);
            for (var iteration = 0; iteration < 64; iteration++)
            {
                if (distanceA < distanceB)
                {
                    to = b;
                    b = a;
                    distanceB = distanceA;
                    a = to - (to - from) * GOLDEN_RATIO;
                    distanceA = Distance(a);
                }
                else
                {
                    from = a;
                    a = b;
                    distanceA = distanceB;
                    b = from + (to - from) * GOLDEN_RATIO;
                    distanceB = Distance(b);
                }
            }
            Include((from + to) / 2);
        }
        return nearest;

        double Distance(double progress) => Map(Evaluate(start, segment, progress), projection, fit) is { } point
            ? DistanceSquared(point, pointer) : double.PositiveInfinity;

        void Include(double progress)
        {
            if (Map(Evaluate(start, segment, progress), projection, fit) is { } point)
            {
                var distance = DistanceSquared(point, pointer);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    nearest = (progress, point, distance);
                }
            }
        }
    }

    internal static bool TryProjectLine(ScenePoint start, ScenePoint end, ProjectionMatrix3x3 projection, Matrix fit,
        out Point first, out Point last)
    {
        first = last = default;
        var startW = projection.W(start);
        var endW = projection.W(end);
        if (!double.IsFinite(startW) || !double.IsFinite(endW) ||
            startW < SceneProjection.NEAR_PLANE_RATIO && endW < SceneProjection.NEAR_PLANE_RATIO)
        {
            return false;
        }
        if ((startW < SceneProjection.NEAR_PLANE_RATIO) != (endW < SceneProjection.NEAR_PLANE_RATIO))
        {
            var progress = (SceneProjection.NEAR_PLANE_RATIO - startW) / (endW - startW);
            var intersection = new ScenePoint(start.X + (end.X - start.X) * progress, start.Y + (end.Y - start.Y) * progress);
            if (startW < SceneProjection.NEAR_PLANE_RATIO)
            {
                start = intersection;
            }
            else
            {
                end = intersection;
            }
        }
        var mappedFirst = projection.Map(start);
        var mappedLast = projection.Map(end);
        first = new Point(mappedFirst.X, mappedFirst.Y) * fit;
        last = new Point(mappedLast.X, mappedLast.Y) * fit;
        return double.IsFinite(first.X) && double.IsFinite(first.Y) && double.IsFinite(last.X) && double.IsFinite(last.Y);
    }

    private static Point? Map(ScenePoint source, ProjectionMatrix3x3 projection, Matrix fit)
    {
        if (!projection.TryMap(source, out var mapped, SceneProjection.NEAR_PLANE_RATIO))
        {
            return null;
        }
        var result = new Point(mapped.X, mapped.Y) * fit;
        return double.IsFinite(result.X) && double.IsFinite(result.Y) ? result : null;
    }

    private static bool Outside(Rect rectangle, Point first, Point control1, Point control2, Point last) =>
        Math.Max(Math.Max(first.X, control1.X), Math.Max(control2.X, last.X)) < rectangle.Left ||
        Math.Min(Math.Min(first.X, control1.X), Math.Min(control2.X, last.X)) > rectangle.Right ||
        Math.Max(Math.Max(first.Y, control1.Y), Math.Max(control2.Y, last.Y)) < rectangle.Top ||
        Math.Min(Math.Min(first.Y, control1.Y), Math.Min(control2.Y, last.Y)) > rectangle.Bottom;

    private static ScenePoint Midpoint(ScenePoint first, ScenePoint second) => new((first.X + second.X) / 2, (first.Y + second.Y) / 2);

    private static ScenePoint Evaluate(ScenePoint start, CubicBezierSegment segment, double progress)
    {
        var u = 1 - progress;
        return new(u * u * u * start.X + 3 * u * u * progress * segment.Control1.X +
                   3 * u * progress * progress * segment.Control2.X + progress * progress * progress * segment.End.X,
            u * u * u * start.Y + 3 * u * u * progress * segment.Control1.Y +
            3 * u * progress * progress * segment.Control2.Y + progress * progress * progress * segment.End.Y);
    }

    private static double DistanceToLine(Point point, Point start, Point end)
    {
        var delta = new Vector(end.X - start.X, end.Y - start.Y);
        var offset = new Vector(point.X - start.X, point.Y - start.Y);
        var progress = delta.SquaredLength > 0 ? Math.Clamp(Vector.Dot(offset, delta) / delta.SquaredLength, 0, 1) : 0;
        return DistanceSquared(point, start + delta * progress);
    }

    private static double DistanceSquared(Point first, Point second) => new Vector(first.X - second.X, first.Y - second.Y).SquaredLength;
}
