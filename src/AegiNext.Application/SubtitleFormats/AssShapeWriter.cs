using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.SubtitleFormats;

internal static class AssShapeWriter
{
    private const double MAX_DRAWING_COORDINATE = 16777215;

    internal static string WriteStyle(IEnumerable<string> subtitleStyleNames, StringBuilder output)
    {
        var usedNames = subtitleStyleNames.ToHashSet(StringComparer.Ordinal);
        var name = "AegiNext_Graphics";
        var suffix = 2;
        while (usedNames.Contains(name))
        {
            name = "AegiNext_Graphics_" + (suffix++).ToString(CultureInfo.InvariantCulture);
        }
        output.Append("Style: ").Append(name)
            .AppendLine(",Arial,20,&H00FFFFFF,&H00FFFFFF,&H00000000,&HFF000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1");
        return name;
    }

    internal static string? Write(ProjectLayer layer, MediaTime timeOffset,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (layer.Kind == LayerKind.IMAGE)
        {
            diagnostics.Add(layer.Image!.Playback is null
                ? new("Ass.Image", "ASS 不支持嵌入图片，此图片片段未导出。", LayerId: layer.Id)
                : new("Ass.AnimatedImage", "ASS 不支持嵌入动图，此动图片段及其播放设置未导出。", LayerId: layer.Id));
            return null;
        }
        var unsupported = false;
        if (!layer.Tracks.IsEmpty || layer.MotionPath is not null)
        {
            diagnostics.Add(new("Ass.ShapeAnimation", "ASS 图形导出暂不支持片段关键帧或运动路径，此形状片段未导出。", LayerId: layer.Id));
            unsupported = true;
        }
        if (layer.Blend != BlendMode.NORMAL)
        {
            diagnostics.Add(new("Ass.ShapeBlend", "ASS 不支持此形状片段的混合模式，此形状片段未导出。", LayerId: layer.Id));
            unsupported = true;
        }
        if (layer.Blur > 0)
        {
            diagnostics.Add(new("Ass.ShapeBlur", "ASS 边缘模糊无法保留此形状的整层模糊，此形状片段未导出。", LayerId: layer.Id));
            unsupported = true;
        }
        if (unsupported)
        {
            return null;
        }
        if (layer.Start + timeOffset < MediaTime.Zero)
        {
            throw new InvalidDataException($"ASS 图形片段 {layer.Id} 的外部开始时间 {layer.Start + timeOffset} 为负；请调整区间或播放零点，不会自动裁剪。");
        }
        if (layer.Opacity == 0 || layer.Transform.ScaleX == 0 || layer.Transform.ScaleY == 0)
        {
            return null;
        }
        var path = ShapeGeometry.CreatePath(layer.Shape!);
        var fill = layer.Fill;
        var stroke = layer.Stroke;
        var border = 0d;
        if (layer.Shape!.Kind == ShapeKind.LINE)
        {
            if (layer.StrokeWidth == 0 || stroke.Alpha == 0)
            {
                return null;
            }
            var top = layer.Shape.Height / 2 - layer.StrokeWidth / 2;
            path = ShapeGeometry.CreatePath(new(ShapeKind.RECTANGLE, layer.Shape.Width, 1));
            path = Transform(path, point => point with { Y = point.Y * layer.StrokeWidth + top });
            fill = stroke;
        }
        else if (layer.StrokeWidth > 0 && stroke.Alpha > 0)
        {
            if (!path.Closed)
            {
                diagnostics.Add(new("Ass.ShapeOpenStroke", "ASS 绘图会闭合轮廓，不能保留此开放路径的描边，此形状片段未导出。", LayerId: layer.Id));
                return null;
            }
            else
            {
                var scaleX = Math.Abs(layer.Transform.ScaleX);
                var scaleY = Math.Abs(layer.Transform.ScaleY);
                border = layer.StrokeWidth / 2 * (scaleX + scaleY) / 2;
                diagnostics.Add(new("Ass.ShapeStrokePlacement", "ASS 绘图采用外扩轮廓，原生形状采用居中描边；已按半描边宽度近似导出，内侧边界及填充与描边重叠处的透明度可能不同。", LayerId: layer.Id));
                if (scaleX != scaleY)
                {
                    diagnostics.Add(new("Ass.ShapeStrokeScale", "此形状的横纵缩放不一致，ASS 描边宽度已按两个缩放绝对值的平均值近似导出。", LayerId: layer.Id));
                }
            }
        }
        if (!path.Closed)
        {
            return null;
        }
        fill = fill with { Alpha = fill.Alpha * layer.Opacity };
        stroke = stroke with { Alpha = stroke.Alpha * layer.Opacity };
        if (fill.Alpha == 0 && border == 0)
        {
            return null;
        }
        var (sine, cosine) = AssTransformMath.SinCos(layer.Transform.Rotation);
        path = Transform(path, point =>
        {
            var x = (point.X - layer.Transform.AnchorX) * layer.Transform.ScaleX;
            var y = (point.Y - layer.Transform.AnchorY) * layer.Transform.ScaleY;
            return new(layer.Transform.X + cosine * x - sine * y, layer.Transform.Y + sine * x + cosine * y);
        });
        var values = Points(path).SelectMany(point => new[] { point.X, point.Y }).ToArray();
        if (!double.IsFinite(border) || border > MAX_DRAWING_COORDINATE ||
            values.Any(value => !double.IsFinite(value) || Math.Abs(value) > MAX_DRAWING_COORDINATE))
        {
            diagnostics.Add(new("Ass.ShapeGeometryRange", "变换后的形状坐标或描边超出 ASS 绘图的安全数值范围，此形状片段未导出。", LayerId: layer.Id));
            return null;
        }
        var precision = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        AssExportPrecision.AddTime(layer.Start + timeOffset, layer.End + timeOffset, layer.Id, precision);
        AssExportPrecision.AddNumbers(layer.Id, precision, values.Append(border).ToArray());
        AssExportPrecision.AddColor(fill, layer.Id, precision);
        if (border > 0)
        {
            AssExportPrecision.AddColor(stroke, layer.Id, precision);
        }
        diagnostics.AddRange(precision.Select(item => item with { SubtitleId = null, LayerId = layer.Id }));
        if (fill.Red > 1 || fill.Green > 1 || fill.Blue > 1 || border > 0 && (stroke.Red > 1 || stroke.Green > 1 || stroke.Blue > 1))
        {
            diagnostics.Add(new("Ass.ColorRange", "ASS 不支持 HDR 颜色，形状颜色已裁剪至 SDR 范围。", LayerId: layer.Id));
        }
        if (values.Any(value => Math.Abs(value * 64 - Math.Round(value * 64)) > 1e-8))
        {
            diagnostics.Add(new("Ass.DrawingPrecision", "ASS 播放器的绘图坐标采用有限亚像素精度，曲线或变换后的形状可能出现细微取整差异。", LayerId: layer.Id));
        }
        var result = new StringBuilder("{\\an7\\pos(0,0)\\fscx100\\fscy100\\frz0\\shad0\\blur0");
        result.Append("\\1c").Append(AssFormatValues.Color(fill, false)).Append("\\1a").Append(AssFormatValues.Alpha(fill))
            .Append("\\3c").Append(AssFormatValues.Color(stroke, false)).Append("\\3a").Append(AssFormatValues.Alpha(stroke))
            .Append("\\bord").Append(AssFormatValues.Number(border)).Append("\\p1}m ");
        AppendPoint(result, path.Start);
        foreach (var segment in path.Segments)
        {
            result.Append(" b ");
            AppendPoint(result, segment.Control1);
            result.Append(' ');
            AppendPoint(result, segment.Control2);
            result.Append(' ');
            AppendPoint(result, segment.End);
        }
        return result.Append("{\\p0}").ToString();
    }

    private static void AppendPoint(StringBuilder result, ScenePoint point)
    {
        result.Append(AssFormatValues.Number(point.X)).Append(' ').Append(AssFormatValues.Number(point.Y));
    }

    private static PathGeometry Transform(PathGeometry path, Func<ScenePoint, ScenePoint> transform)
    {
        return new(transform(path.Start), path.Segments.Select(segment => new CubicBezierSegment(
            transform(segment.Control1), transform(segment.Control2), transform(segment.End))).ToImmutableArray(), path.Closed);
    }

    private static IEnumerable<ScenePoint> Points(PathGeometry path)
    {
        yield return path.Start;
        foreach (var segment in path.Segments)
        {
            yield return segment.Control1;
            yield return segment.Control2;
            yield return segment.End;
        }
    }
}
