using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

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

    internal static string? Write(ProjectLayer layer, ScenePoint canvasSize, MediaTime timeOffset,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (layer.Kind == LayerKind.IMAGE)
        {
            diagnostics.Add(layer.Image!.Playback is null
                ? new("Ass.Image", "ASS does not support embedded images. This image clip was not exported.", LayerId: layer.Id)
                : new("Ass.AnimatedImage", "ASS does not support embedded animated images. This animated image clip and its playback settings were not exported.", LayerId: layer.Id));
            return null;
        }
        if (layer.Transform.PerspectiveDistance != SceneProjection.DEFAULT_DISTANCE ||
            layer.Tracks.Any(track => track.Property == AnimationProperty.PERSPECTIVE_DISTANCE))
        {
            diagnostics.Add(new("Ass.PerspectiveDistance", "ASS drawings use a fixed perspective distance. Native distance values and their animation cannot be preserved; static drawing export uses 312.5 project pixels.", LayerId: layer.Id));
        }
        if (layer.Transform.ProjectionTranslation != default)
        {
            diagnostics.Add(new("Ass.ProjectionTranslation", "ASS drawings cannot preserve native translation in three-dimensional projection space. Projection translation was omitted; native pivot compensation remains in the project.", LayerId: layer.Id));
        }
        var unsupported = false;
        if (!layer.Tracks.IsEmpty || layer.MotionPath is not null)
        {
            diagnostics.Add(new("Ass.ShapeAnimation", "ASS shape export does not yet support clip keyframes or motion paths. This shape clip was not exported.", LayerId: layer.Id));
            unsupported = true;
        }
        if (layer.Blend != BlendMode.NORMAL)
        {
            diagnostics.Add(new("Ass.ShapeBlend", "ASS does not support the blend mode of this shape clip. This shape clip was not exported.", LayerId: layer.Id));
            unsupported = true;
        }
        if (layer.Blur > 0)
        {
            diagnostics.Add(new("Ass.ShapeBlur", "ASS edge blur cannot preserve whole-layer blur for this shape. This shape clip was not exported.", LayerId: layer.Id));
            unsupported = true;
        }
        if (unsupported)
        {
            return null;
        }
        if (layer.Start + timeOffset < MediaTime.Zero)
        {
            throw new InvalidDataException($"ASS shape clip {layer.Id} has a negative external start time of {layer.Start + timeOffset}. Adjust the interval or playback origin; it will not be clipped automatically.");
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
                diagnostics.Add(new("Ass.ShapeOpenStroke", "ASS drawing closes contours and cannot preserve the stroke of this open path. This shape clip was not exported.", LayerId: layer.Id));
                return null;
            }
            else
            {
                var scaleX = Math.Abs(layer.Transform.ScaleX);
                var scaleY = Math.Abs(layer.Transform.ScaleY);
                border = layer.StrokeWidth / 2 * (scaleX + scaleY) / 2;
                diagnostics.Add(new("Ass.ShapeStrokePlacement", "ASS drawing uses outward contours, while native shapes use centered strokes. Export used an approximation with half the stroke width; inner boundaries and alpha where fill and stroke overlap may differ.", LayerId: layer.Id));
                if (scaleX != scaleY)
                {
                    diagnostics.Add(new("Ass.ShapeStrokeScale", "This shape has different horizontal and vertical scales. ASS stroke width was approximated using the average of their absolute values.", LayerId: layer.Id));
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
        var position = layer.Position;
        var basePosition = position is null ? new ScenePoint() : LayerPositionGeometry.BasePosition(position, canvasSize);
        var pivot = position is null ? new ScenePoint() : LayerPositionGeometry.LocalPivot(position, new(), LayerPositionGeometry.GraphicSize(layer));
        var perspective = layer.Transform.RotationX != 0 || layer.Transform.RotationY != 0;
        var drawingTags = "\\an7\\pos(0,0)\\fscx100\\fscy100\\frz0";
        var projectionValues = Array.Empty<double>();
        if (perspective)
        {
            var center = new ScenePoint(basePosition.X + layer.Transform.X, basePosition.Y + layer.Transform.Y);
            var placement = new ScenePoint(center.X - (pivot.X + layer.Transform.AnchorX) * layer.Transform.ScaleX,
                center.Y - (pivot.Y + layer.Transform.AnchorY) * layer.Transform.ScaleY);
            var scaleX = Math.Abs(layer.Transform.ScaleX);
            var scaleY = Math.Abs(layer.Transform.ScaleY);
            path = Transform(path, point => new(point.X * Math.Sign(layer.Transform.ScaleX), point.Y * Math.Sign(layer.Transform.ScaleY)));
            projectionValues = [center.X, center.Y, placement.X, placement.Y, scaleX * 100, scaleY * 100,
                layer.Transform.Rotation, layer.Transform.RotationX, layer.Transform.RotationY];
            drawingTags = "\\an7\\pos(" + AssFormatValues.Number(placement.X) + "," + AssFormatValues.Number(placement.Y) + ")" +
                "\\org(" + AssFormatValues.Number(center.X) + "," + AssFormatValues.Number(center.Y) + ")" +
                "\\fscx" + AssFormatValues.Number(scaleX * 100) + "\\fscy" + AssFormatValues.Number(scaleY * 100) +
                "\\frz" + AssFormatValues.Number(-layer.Transform.Rotation) +
                "\\frx" + AssFormatValues.Number(-layer.Transform.RotationX) +
                "\\fry" + AssFormatValues.Number(-layer.Transform.RotationY);
        }
        else
        {
            path = Transform(path, point =>
            {
                var x = (point.X - pivot.X - layer.Transform.AnchorX) * layer.Transform.ScaleX;
                var y = (point.Y - pivot.Y - layer.Transform.AnchorY) * layer.Transform.ScaleY;
                return new(basePosition.X + layer.Transform.X + cosine * x - sine * y,
                    basePosition.Y + layer.Transform.Y + sine * x + cosine * y);
            });
        }
        var values = Points(path).SelectMany(point => new[] { point.X, point.Y }).ToArray();
        if (!double.IsFinite(border) || border > MAX_DRAWING_COORDINATE ||
            values.Any(value => !double.IsFinite(value) || Math.Abs(value) > MAX_DRAWING_COORDINATE) ||
            projectionValues.Take(4).Any(value => !double.IsFinite(value) || Math.Abs(value) > MAX_DRAWING_COORDINATE))
        {
            diagnostics.Add(new("Ass.ShapeGeometryRange", "Transformed shape coordinates or stroke exceed the safe ASS drawing numeric range. This shape clip was not exported.", LayerId: layer.Id));
            return null;
        }
        var precision = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        AssExportPrecision.AddTime(layer.Start + timeOffset, layer.End + timeOffset, layer.Id, precision);
        AssExportPrecision.AddNumbers(layer.Id, precision, values.Append(border).Concat(projectionValues).ToArray());
        AssExportPrecision.AddColor(fill, layer.Id, precision);
        if (border > 0)
        {
            AssExportPrecision.AddColor(stroke, layer.Id, precision);
        }
        diagnostics.AddRange(precision.Select(item => item with { SubtitleId = null, LayerId = layer.Id }));
        if (fill.Red > 1 || fill.Green > 1 || fill.Blue > 1 || border > 0 && (stroke.Red > 1 || stroke.Green > 1 || stroke.Blue > 1))
        {
            diagnostics.Add(new("Ass.ColorRange", "ASS does not support HDR colors. Shape colors were clipped to the SDR range.", LayerId: layer.Id));
        }
        if (values.Any(value => Math.Abs(value * 64 - Math.Round(value * 64)) > 1e-8))
        {
            diagnostics.Add(new("Ass.DrawingPrecision", "ASS player drawing coordinates use limited subpixel precision. Curves or transformed shapes may have slight rounding differences.", LayerId: layer.Id));
        }
        var result = new StringBuilder("{" + drawingTags + "\\shad0\\blur0");
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
