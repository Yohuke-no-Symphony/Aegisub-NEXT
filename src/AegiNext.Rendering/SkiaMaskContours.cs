using System.Collections.Immutable;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering;

internal static class SkiaMaskContours
{
    internal static bool TryCreate(SKPath path, out ImmutableArray<MaskContour> contours, out string? reason)
    {
        var result = ImmutableArray.CreateBuilder<MaskContour>();
        var nodes = new List<MaskNode>();
        var pointCount = 0;
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        for (var verb = iterator.Next(points); verb != SKPathVerb.Done; verb = iterator.Next(points))
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    Flush();
                    nodes.Add(new() { Position = Point(points[0]) });
                    break;
                case SKPathVerb.Line:
                    nodes.Add(new() { Position = Point(points[1]) });
                    break;
                case SKPathVerb.Quad:
                    var start = Point(points[0]);
                    var control = Point(points[1]);
                    var end = Point(points[2]);
                    Cubic(new(start.X + (control.X - start.X) * 2 / 3, start.Y + (control.Y - start.Y) * 2 / 3),
                        new(end.X + (control.X - end.X) * 2 / 3, end.Y + (control.Y - end.Y) * 2 / 3), end);
                    break;
                case SKPathVerb.Cubic:
                    Cubic(Point(points[1]), Point(points[2]), Point(points[3]));
                    break;
                case SKPathVerb.Close:
                    Flush();
                    break;
                case SKPathVerb.Conic:
                    contours = [];
                    reason = "The clipping operation produced a rational curve that cannot be stored as an exact native cubic path.";
                    return false;
                default:
                    throw new InvalidDataException("The clipping path contains an unknown segment.");
            }
            if (pointCount + nodes.Count > 10000)
            {
                throw new InvalidDataException("Combined clipping exceeds the 10,000-point budget.");
            }
        }
        Flush();
        contours = result.ToImmutable();
        reason = null;
        return true;

        void Cubic(ScenePoint first, ScenePoint second, ScenePoint end)
        {
            if (nodes.Count == 0)
            {
                throw new InvalidDataException("A clipping curve is missing its starting point.");
            }
            var previous = nodes[^1];
            nodes[^1] = previous with { OutHandle = new(first.X - previous.Position.X, first.Y - previous.Position.Y) };
            nodes.Add(new() { Position = end, InHandle = new(second.X - end.X, second.Y - end.Y) });
        }

        void Flush()
        {
            if (nodes.Count == 0)
            {
                return;
            }
            if (nodes.Count > 1 && nodes[^1].Position == nodes[0].Position && nodes[^1].OutHandle == default)
            {
                nodes[0] = nodes[0] with { InHandle = nodes[^1].InHandle };
                nodes.RemoveAt(nodes.Count - 1);
            }
            if (nodes.Count >= 2)
            {
                result.Add(new() { Nodes = nodes.ToImmutableArray() });
                pointCount += nodes.Count;
            }
            nodes.Clear();
        }
    }

    private static ScenePoint Point(SKPoint point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || Math.Abs(point.X) > 1e9 || Math.Abs(point.Y) > 1e9)
        {
            throw new InvalidDataException("Combined clipping exceeds the native coordinate range.");
        }
        return new(point.X, point.Y);
    }
}
