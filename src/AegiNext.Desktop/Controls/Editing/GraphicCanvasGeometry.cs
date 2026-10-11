using System.Collections.Immutable;
using AegiNext.Core.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal static class GraphicCanvasGeometry
{
    internal static ProjectLayer TransformLayer(CanvasTransformGesture gesture, Point point, bool shift)
    {
        var layer = gesture.Layer;
        if (gesture.HandleIndex == 4 || layer.Shape is not { Kind: ShapeKind.RECTANGLE or ShapeKind.ROUNDED_RECTANGLE } shape ||
            !gesture.LocalToScreen.TryInvert(out var inverse))
        {
            return layer with { Transform = Transform(gesture, point, shift) };
        }

        var factors = ResizeFactors(gesture, point * inverse, shift);
        var resized = ShapeGeometry.Resize(shape, Math.Clamp(shape.Width * factors.X, 0.001, 1e6),
            Math.Clamp(shape.Height * factors.Y, 0.001, 1e6));
        var fixedCorner = FixedCorner(gesture);
        var evaluated = gesture.EvaluatedTransform;
        var deltaX = fixedCorner.X * (1 - resized.Width / shape.Width) * evaluated.ScaleX;
        var deltaY = fixedCorner.Y * (1 - resized.Height / shape.Height) * evaluated.ScaleY;
        var radians = evaluated.Rotation * Math.PI / 180;
        return layer with
        {
            Shape = resized,
            Transform = layer.Transform with
            {
                X = layer.Transform.X + deltaX * Math.Cos(radians) - deltaY * Math.Sin(radians),
                Y = layer.Transform.Y + deltaX * Math.Sin(radians) + deltaY * Math.Cos(radians)
            }
        };
    }

    internal static LayerTransform Transform(CanvasTransformGesture gesture, Point point, bool shift)
    {
        var original = gesture.Layer.Transform;
        var evaluated = gesture.EvaluatedTransform;
        var geometry = gesture.Geometry;
        var pivot = new Point(geometry.LocalPivot.X, geometry.LocalPivot.Y);
        var center = pivot * gesture.LocalToScreen;
        if (gesture.HandleIndex == 4)
        {
            var start = Math.Atan2(gesture.Start.Y - center.Y, gesture.Start.X - center.X);
            var current = Math.Atan2(point.Y - center.Y, point.X - center.X);
            var delta = Math.IEEERemainder((current - start) * 180 / Math.PI, 360);
            var rotation = evaluated.Rotation + delta;
            if (shift)
            {
                rotation = Math.Round(rotation / 15) * 15;
            }
            return original with { Rotation = original.Rotation + rotation - evaluated.Rotation };
        }
        if (!gesture.LocalToScreen.TryInvert(out var inverse))
        {
            return original;
        }
        var fixedCorner = FixedCorner(gesture);
        var factors = ResizeFactors(gesture, point * inverse, shift);
        var newScale = new ScenePoint(Math.Clamp(evaluated.ScaleX * factors.X, -10000, 10000), Math.Clamp(evaluated.ScaleY * factors.Y, -10000, 10000));
        var radians = evaluated.Rotation * Math.PI / 180;
        var displacement = fixedCorner - pivot;
        var deltaX = displacement.X * (evaluated.ScaleX - newScale.X);
        var deltaY = displacement.Y * (evaluated.ScaleY - newScale.Y);
        return original with
        {
            ScaleX = original.ScaleX + newScale.X - evaluated.ScaleX,
            ScaleY = original.ScaleY + newScale.Y - evaluated.ScaleY,
            X = original.X + deltaX * Math.Cos(radians) - deltaY * Math.Sin(radians),
            Y = original.Y + deltaX * Math.Sin(radians) + deltaY * Math.Cos(radians)
        };
    }

    private static ScenePoint ResizeFactors(CanvasTransformGesture gesture, Point local, bool shift)
    {
        var fixedCorner = FixedCorner(gesture);
        var bounds = gesture.Geometry.LocalBounds;
        var moving = new Point(bounds.Left + bounds.Right - fixedCorner.X, bounds.Top + bounds.Bottom - fixedCorner.Y);
        var x = Math.Abs(moving.X - fixedCorner.X) < 1e-9 ? 1 : (local.X - fixedCorner.X) / (moving.X - fixedCorner.X);
        var y = Math.Abs(moving.Y - fixedCorner.Y) < 1e-9 ? 1 : (local.Y - fixedCorner.Y) / (moving.Y - fixedCorner.Y);
        if ((gesture.Layer.Image is not null) != shift)
        {
            var initial = moving - fixedCorner;
            var changed = local - fixedCorner;
            var ratio = (initial.X * changed.X + initial.Y * changed.Y) / Math.Max(1e-9, initial.X * initial.X + initial.Y * initial.Y);
            x = y = ratio;
        }
        return new(Math.Clamp(x, 0.01, 100), Math.Clamp(y, 0.01, 100));
    }

    private static Point FixedCorner(CanvasTransformGesture gesture)
    {
        var bounds = gesture.Geometry.LocalBounds;
        return ((gesture.HandleIndex + 2) % 4) switch
        {
            0 => new(bounds.Left, bounds.Top),
            1 => new(bounds.Right, bounds.Top),
            2 => new(bounds.Right, bounds.Bottom),
            _ => new(bounds.Left, bounds.Bottom)
        };
    }

    internal static NormalizedImageCrop Crop(NormalizedImageCrop source, Point start, Point current, int handle)
    {
        var delta = current - start;
        if (handle == 4)
        {
            return source with { X = Math.Clamp(source.X + delta.X, 0, 1 - source.Width),
                Y = Math.Clamp(source.Y + delta.Y, 0, 1 - source.Height) };
        }
        var left = source.X;
        var top = source.Y;
        var right = source.X + source.Width;
        var bottom = source.Y + source.Height;
        if (handle < 0)
        {
            left = Math.Min(start.X, current.X);
            right = Math.Max(start.X, current.X);
            top = Math.Min(start.Y, current.Y);
            bottom = Math.Max(start.Y, current.Y);
        }
        else
        {
            if (handle is 0 or 3)
            {
                left += delta.X;
            }
            else
            {
                right += delta.X;
            }
            if (handle is 0 or 1)
            {
                top += delta.Y;
            }
            else
            {
                bottom += delta.Y;
            }
        }
        left = Math.Clamp(left, 0, 0.999999);
        right = Math.Clamp(right, left + 0.000001, 1);
        top = Math.Clamp(top, 0, 0.999999);
        bottom = Math.Clamp(bottom, top + 0.000001, 1);
        return new() { X = left, Y = top, Width = right - left, Height = bottom - top };
    }

    internal static PathGeometry Path(IReadOnlyList<GraphicPathNode> nodes, bool closed, Point origin)
    {
        var segments = ImmutableArray.CreateBuilder<CubicBezierSegment>();
        for (var index = 1; index < nodes.Count; index++)
        {
            segments.Add(new(Relative(nodes[index - 1].Outgoing, origin), Relative(nodes[index].Incoming, origin),
                Relative(nodes[index].Position, origin)));
        }
        if (closed)
        {
            segments.Add(new(Relative(nodes[^1].Outgoing, origin), Relative(nodes[0].Incoming, origin), Relative(nodes[0].Position, origin)));
        }
        return new(Relative(nodes[0].Position, origin), segments.ToImmutable(), closed);
    }

    internal static PathGeometry MovePathPoint(PathGeometry path, int handle, Vector delta)
    {
        if (handle % 3 != 0)
        {
            return EffectCanvasControl.MoveHandle(path, handle, delta);
        }
        var node = handle / 3;
        var explicitClosure = path.Closed && path.Segments[^1].End == path.Start;
        if (explicitClosure && node == path.Segments.Length)
        {
            node = 0;
        }
        var moved = path;
        if (node == 0)
        {
            moved = moved with { Start = Offset(path.Start, delta) };
        }
        else
        {
            var previous = moved.Segments[node - 1];
            moved = moved with { Segments = moved.Segments.SetItem(node - 1,
                previous with { End = Offset(previous.End, delta), Control2 = Offset(previous.Control2, delta) }) };
        }
        if (node < moved.Segments.Length)
        {
            var next = moved.Segments[node];
            moved = moved with { Segments = moved.Segments.SetItem(node, next with { Control1 = Offset(next.Control1, delta) }) };
        }
        if (node == 0 && explicitClosure)
        {
            var previous = moved.Segments[^1];
            moved = moved with { Segments = moved.Segments.SetItem(moved.Segments.Length - 1,
                previous with { End = moved.Start, Control2 = Offset(previous.Control2, delta) }) };
        }
        return moved;
    }

    internal static PathGeometry SetSegmentCurve(PathGeometry path, int index, bool curved)
    {
        var start = index == 0 ? path.Start : path.Segments[index - 1].End;
        var segment = path.Segments[index];
        var delta = new Vector(segment.End.X - start.X, segment.End.Y - start.Y);
        var normal = curved ? new Vector(-delta.Y, delta.X) * 0.2 : default;
        return path with { Segments = path.Segments.SetItem(index, segment with
        {
            Control1 = new(start.X + delta.X / 3 + normal.X, start.Y + delta.Y / 3 + normal.Y),
            Control2 = new(start.X + delta.X * 2 / 3 + normal.X, start.Y + delta.Y * 2 / 3 + normal.Y)
        }) };
    }

    internal static PathGeometry ToggleClosed(PathGeometry path)
    {
        if (path.Closed)
        {
            return path with
            {
                Closed = false,
                Segments = path.Segments.Length > 1 && path.Segments[^1].End == path.Start
                    ? path.Segments.RemoveAt(path.Segments.Length - 1) : path.Segments
            };
        }
        var end = path.Segments[^1].End;
        return path with
        {
            Closed = true,
            Segments = end == path.Start ? path.Segments : path.Segments.Add(new(end, path.Start, path.Start))
        };
    }

    private static ScenePoint Offset(ScenePoint point, Vector offset) => new(point.X + offset.X, point.Y + offset.Y);

    private static ScenePoint Relative(Point point, Point origin) => new(point.X - origin.X, point.Y - origin.Y);
}
