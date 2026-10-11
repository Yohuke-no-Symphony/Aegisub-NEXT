using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>生成渲染、命中及转换自由路径共用的单轮廓三次贝塞尔几何。</summary>
public static class ShapeGeometry
{
    private const double QUARTER_CIRCLE_CONTROL = 0.5522847498307936;

    /// <summary>修改局部尺寸；自由路径按原尺寸同比例调整对应轴的全部控制点，参数形状保留圆角与箭头参数。</summary>
    public static LayerShape Resize(LayerShape shape, double width, double height)
    {
        ProjectValidator.ValidateLayerShape(shape);
        var resized = shape with { Width = width, Height = height };
        ProjectValidator.ValidateLayerShape(resized);
        if (width == shape.Width && height == shape.Height)
        {
            return shape;
        }
        if (shape.Path is { } path)
        {
            var scaleX = width / shape.Width;
            var scaleY = height / shape.Height;
            ScenePoint Scale(ScenePoint point) => new(point.X * scaleX, point.Y * scaleY);
            resized = resized with
            {
                Path = new(Scale(path.Start), path.Segments.Select(segment => new CubicBezierSegment(
                    Scale(segment.Control1), Scale(segment.Control2), Scale(segment.End))).ToImmutableArray(), path.Closed)
            };
            ProjectValidator.ValidateLayerShape(resized);
        }
        return resized;
    }

    /// <summary>将参数化图形转换为局部像素路径；已有自由路径保留其对象和坐标。</summary>
    public static PathGeometry CreatePath(LayerShape shape)
    {
        ProjectValidator.ValidateLayerShape(shape);
        var width = shape.Width;
        var height = shape.Height;
        return shape.Kind switch
        {
            ShapeKind.PATH => shape.Path!,
            ShapeKind.RECTANGLE or ShapeKind.ROUNDED_RECTANGLE => RoundedRectangle(width, height, shape.EffectiveCornerRadii),
            ShapeKind.ELLIPSE => Ellipse(width, height),
            ShapeKind.LINE => Polygon([new(0, height / 2), new(width, height / 2)], false),
            ShapeKind.ARROW => Arrow(shape),
            _ => throw new InvalidDataException("未知形状。")
        };
    }

    private static PathGeometry Polygon(ScenePoint[] points, bool closed = true)
    {
        var segments = ImmutableArray.CreateBuilder<CubicBezierSegment>();
        for (var index = 1; index < points.Length; index++)
        {
            segments.Add(Line(points[index - 1], points[index]));
        }
        if (closed)
        {
            segments.Add(Line(points[^1], points[0]));
        }
        return new(points[0], segments.ToImmutable(), closed);
    }

    private static CubicBezierSegment Line(ScenePoint start, ScenePoint end)
    {
        return new(new(start.X + (end.X - start.X) / 3, start.Y + (end.Y - start.Y) / 3),
            new(start.X + (end.X - start.X) * 2 / 3, start.Y + (end.Y - start.Y) * 2 / 3), end);
    }

    private static PathGeometry RoundedRectangle(double width, double height, ShapeCornerRadii radii)
    {
        if (radii is { IsUniform: true, TopLeft: 0 })
        {
            return Polygon([new(0, 0), new(width, 0), new(width, height), new(0, height)]);
        }
        var factor = Math.Min(1, Math.Min(
            Math.Min(CornerScale(width, radii.TopLeft + radii.TopRight), CornerScale(width, radii.BottomLeft + radii.BottomRight)),
            Math.Min(CornerScale(height, radii.TopLeft + radii.BottomLeft), CornerScale(height, radii.TopRight + radii.BottomRight))));
        var topLeft = radii.TopLeft * factor;
        var topRight = radii.TopRight * factor;
        var bottomRight = radii.BottomRight * factor;
        var bottomLeft = radii.BottomLeft * factor;
        var start = new ScenePoint(topLeft, 0);
        return new(start,
        [
            Line(start, new(width - topRight, 0)),
            new(new(width - topRight + topRight * QUARTER_CIRCLE_CONTROL, 0),
                new(width, topRight - topRight * QUARTER_CIRCLE_CONTROL), new(width, topRight)),
            Line(new(width, topRight), new(width, height - bottomRight)),
            new(new(width, height - bottomRight + bottomRight * QUARTER_CIRCLE_CONTROL),
                new(width - bottomRight + bottomRight * QUARTER_CIRCLE_CONTROL, height), new(width - bottomRight, height)),
            Line(new(width - bottomRight, height), new(bottomLeft, height)),
            new(new(bottomLeft - bottomLeft * QUARTER_CIRCLE_CONTROL, height),
                new(0, height - bottomLeft + bottomLeft * QUARTER_CIRCLE_CONTROL), new(0, height - bottomLeft)),
            Line(new(0, height - bottomLeft), new(0, topLeft)),
            new(new(0, topLeft - topLeft * QUARTER_CIRCLE_CONTROL),
                new(topLeft - topLeft * QUARTER_CIRCLE_CONTROL, 0), start)
        ], true);
    }

    private static double CornerScale(double length, double radii) => radii == 0 ? 1 : length / radii;

    private static PathGeometry Ellipse(double width, double height)
    {
        var x = width / 2;
        var y = height / 2;
        var controlX = x * QUARTER_CIRCLE_CONTROL;
        var controlY = y * QUARTER_CIRCLE_CONTROL;
        var start = new ScenePoint(x, 0);
        return new(start,
        [
            new(new(x + controlX, 0), new(width, y - controlY), new(width, y)),
            new(new(width, y + controlY), new(x + controlX, height), new(x, height)),
            new(new(x - controlX, height), new(0, y + controlY), new(0, y)),
            new(new(0, y - controlY), new(x - controlX, 0), start)
        ], true);
    }

    private static PathGeometry Arrow(LayerShape shape)
    {
        var neck = shape.Width * (1 - shape.ArrowHeadRatio);
        var top = shape.Height * (1 - shape.ArrowShaftRatio) / 2;
        var bottom = shape.Height - top;
        return Polygon([new(0, top), new(neck, top), new(neck, 0), new(shape.Width, shape.Height / 2),
            new(neck, shape.Height), new(neck, bottom), new(0, bottom)]);
    }
}
