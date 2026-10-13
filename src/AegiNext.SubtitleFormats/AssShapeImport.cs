using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssShapeImport
{
    internal static AssDrawingImportResult Convert(string source, SubtitleLine original, AssStyleDefinition definition,
        AssTextEditResult parsed, double scaleX, double scaleY)
    {
        if (parsed.Drawings.IsEmpty)
        {
            return new([], [], false);
        }
        var firstRun = parsed.Drawings[0];
        if (parsed.Line.Text.Length > 0)
        {
            return Reject("Ass.DrawingMixedFlow", "ASS drawing mixed with ordinary text was not imported as shapes. Ordinary text was preserved; shared glyph-flow layout requires a separate adapter.");
        }
        if (parsed.Drawings.Length != 1)
        {
            return Reject("Ass.DrawingStyleRuns", "ASS drawing interrupted by override blocks was not imported as shapes. Drawing-run flow and style boundaries cannot be represented by one isolated glyph adapter.");
        }
        var drawingText = source.Substring(firstRun.SourceStart, firstRun.SourceLength);
        if (drawingText.Contains("\\N", StringComparison.Ordinal) || drawingText.Contains("\\n", StringComparison.Ordinal) ||
            drawingText.Contains("\\h", StringComparison.Ordinal))
        {
            return Reject("Ass.DrawingMixedFlow", "ASS drawing with line breaks or spacing glyphs was not imported as one isolated shape. Shared glyph-flow layout requires a separate adapter.");
        }
        var tags = SourceTags(source).ToArray();
        if (parsed.Mask is not null || tags.Any(tag => tag.Name is "clip" or "iclip"))
        {
            return Reject("Ass.DrawingMask", "Clipped ASS drawings were not imported as shapes. Native shape layers cannot carry subtitle masks.");
        }
        if (tags.Any(tag => tag.Name is "t" or "move" or "fad" or "fade" or "k" or "K" or "kf" or "ko" or "kt") ||
            !parsed.PlacementTracks.IsEmpty || !parsed.OpacityTracks.IsEmpty || !parsed.NumericTracks.IsEmpty)
        {
            return Reject("Ass.DrawingAnimation", "Animated or karaoke ASS drawings were not imported as static shapes. Drawing animation requires native shape-track adaptation.");
        }
        if (tags.Any(tag => tag.Name is "frx" or "fry" or "fax" or "fay") ||
            parsed.Diagnostics.Any(item => item.Code is "Ass.OpaqueBoxStyle" or "Ass.OpaqueBoxBorder" or "Ass.BorderAxes"))
        {
            return Reject("Ass.DrawingGeometry", "Perspective, shear, axis-specific borders or opaque-box drawing styles were not imported as shapes.");
        }
        if (!tags.Any(tag => tag.Name == "pos") || parsed.Line.Style.Position is not { Anchor: { X: 0, Y: 0 } })
        {
            return Reject("Ass.DrawingPlacement", "ASS drawing without an explicit valid static position was not imported as shapes. Automatic drawing placement requires full event layout.");
        }
        var run = firstRun;
        if (run.Style.FillBlur != 0 || run.Style.StrokeBlur != 0 ||
            run.Style.ShadowColor.Alpha > 0 && run.Style.ShadowOffset != default)
        {
            return Reject("Ass.DrawingEffects", "ASS drawing blur or visible shadow was not imported as shapes. Native shapes require separate effect and shadow-layer adaptation.");
        }
        var geometry = AssDrawingParser.Parse(drawingText);
        if (!geometry.HasDrawableGeometry)
        {
            return Reject("Ass.DrawingEmpty", "ASS drawing contains no drawable contour; no native shape was created.");
        }
        if (!AssDrawingTopology.TrySeparate(geometry.Contours, out var reason))
        {
            return Reject("Ass.DrawingTopology", "ASS drawing was not imported as separate native shapes: " + reason + ".");
        }
        try
        {
            var originParser = new AssRotationOriginParser(original.Id, scaleX, scaleY);
            foreach (var tag in tags.Where(tag => tag.Name == "org"))
            {
                originParser.Apply(tag.Value, tag.Start, tag.Length);
            }
            if (originParser.Diagnostics.Any())
            {
                return Reject("Ass.DrawingPlacement", "ASS drawing has an invalid rotation origin and was not imported as shapes.");
            }
            var placement = new AssDrawingPlacement(geometry, run, parsed.Line.Style, scaleX, scaleY, originParser.Origin);
            var layers = ImmutableArray.CreateBuilder<ProjectLayer>();
            var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
            foreach (var contour in geometry.Contours)
            {
                var path = CreatePath(contour, placement);
                var points = PathPoints(path).ToArray();
                var minimum = new ScenePoint(points.Min(point => point.X), points.Min(point => point.Y));
                var maximum = new ScenePoint(points.Max(point => point.X), points.Max(point => point.Y));
                var width = Math.Max(0.001, maximum.X - minimum.X);
                var height = Math.Max(0.001, maximum.Y - minimum.Y);
                if (width > 1000000 || height > 1000000 || points.Any(point => !double.IsFinite(point.X) ||
                    !double.IsFinite(point.Y) || Math.Abs(point.X) > 1000000000 || Math.Abs(point.Y) > 1000000000))
                {
                    return Reject("Ass.DrawingGeometryRange", "ASS drawing geometry exceeds native shape dimensions or coordinate limits; no partial shapes were created.");
                }
                var localPath = new PathGeometry(Subtract(path.Start, minimum), path.Segments.Select(segment =>
                    new CubicBezierSegment(Subtract(segment.Control1, minimum), Subtract(segment.Control2, minimum),
                        Subtract(segment.End, minimum))).ToImmutableArray(), true);
                var layer = new ProjectLayer
                {
                    Name = definition.Name + " Drawing" + (geometry.Contours.Length > 1 ? " " + (layers.Count + 1) : string.Empty),
                    Kind = LayerKind.SHAPE,
                    Start = original.Start,
                    End = original.End,
                    Shape = new(ShapeKind.PATH, width, height, localPath),
                    Transform = new() { Position = minimum },
                    Fill = run.Style.Fill,
                    Stroke = run.Style.Stroke,
                    StrokeWidth = Math.Min(4096, run.Style.StrokeWidth * 2)
                };
                layers.Add(layer);
                if (run.Style.StrokeWidth > 0 && run.Style.Stroke.Alpha > 0)
                {
                    diagnostics.Add(new("Ass.ShapeStrokePlacement", "ASS drawings use outward borders, while native shapes use centered strokes. Twice the ASS border width was used; inner boundaries and translucent fill/stroke composition may differ.",
                        run.SourceStart, run.SourceLength, LayerId: layer.Id));
                    if (run.Style.StrokeWidth > 2048)
                    {
                        diagnostics.Add(new("Ass.DrawingStrokeRange", "The approximate centered drawing stroke exceeds the native 4096-pixel limit and was clamped.",
                            run.SourceStart, run.SourceLength, LayerId: layer.Id));
                    }
                }
            }
            return new(layers.ToImmutable(), diagnostics.ToImmutable(), true);
        }
        catch (InvalidDataException exception)
        {
            return Reject("Ass.DrawingGeometry", "ASS drawing could not be converted to native shape geometry: " + exception.Message);
        }

        AssDrawingImportResult Reject(string code, string message)
        {
            return new([], [new(code, message, firstRun.SourceStart, firstRun.SourceLength, original.Id)], true);
        }
    }

    private static IEnumerable<AssOverrideTag> SourceTags(string source)
    {
        for (var cursor = 0; cursor < source.Length; cursor++)
        {
            if (source[cursor] == '\\' && cursor + 1 < source.Length && source[cursor + 1] is '{' or '}')
            {
                cursor++;
                continue;
            }
            if (source[cursor] != '{')
            {
                continue;
            }
            var end = source.IndexOf('}', cursor + 1);
            if (end < 0)
            {
                throw new InvalidDataException("An ASS override block is missing its closing brace.");
            }
            foreach (var tag in AssOverrideTags.Parse(source[(cursor + 1)..end]))
            {
                yield return tag with { Start = cursor + 1 + tag.Start };
            }
            cursor = end;
        }
    }

    private static PathGeometry CreatePath(MaskContour contour, AssDrawingPlacement placement)
    {
        var segments = ImmutableArray.CreateBuilder<CubicBezierSegment>();
        for (var index = 0; index < contour.Nodes.Length; index++)
        {
            var first = contour.Nodes[index];
            var second = contour.Nodes[(index + 1) % contour.Nodes.Length];
            segments.Add(new(placement.Transform(Add(first.Position, first.OutHandle)),
                placement.Transform(Add(second.Position, second.InHandle)), placement.Transform(second.Position)));
        }
        return new(placement.Transform(contour.Nodes[0].Position), segments.ToImmutable(), true);
    }

    private static IEnumerable<ScenePoint> PathPoints(PathGeometry path)
    {
        yield return path.Start;
        foreach (var segment in path.Segments)
        {
            yield return segment.Control1;
            yield return segment.Control2;
            yield return segment.End;
        }
    }

    private static ScenePoint Add(ScenePoint first, ScenePoint second) => new(first.X + second.X, first.Y + second.Y);

    private static ScenePoint Subtract(ScenePoint first, ScenePoint second) => new(first.X - second.X, first.Y - second.Y);
}
