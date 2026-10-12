using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssShapeExportTests
{
    [Fact]
    public void SharedAnchorAndNormalizedPivotUseTheSameTransformAsSceneRendering()
    {
        var shape = Shape() with
        {
            Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5), Offset = new(3, 4) },
            Transform = new(10, 20, 2, 3, 90, 5, 10)
        };
        var result = AssSubtitleFormat.Write(new() { Width = 400, Height = 200, Layers = [shape] });

        Assert.Contains("\\p1}m 288 94", Assert.Single(Dialogues(result.Text)), StringComparison.Ordinal);
    }

    [Fact]
    public void ShapeOnlyExportBakesPositionPivotScaleAndRotationIntoDrawingCoordinates()
    {
        var shape = Shape() with { Transform = new(100, 200, 2, 3, 90, 5, 10) };
        var result = AssSubtitleFormat.Write(new() { Layers = [shape] });
        var dialogue = Assert.Single(Dialogues(result.Text));
        Assert.Contains("\\an7\\pos(0,0)", dialogue, StringComparison.Ordinal);
        Assert.Contains("\\p1}m 130 190", dialogue, StringComparison.Ordinal);
        Assert.Contains(" 130 230", dialogue, StringComparison.Ordinal);
        Assert.Contains("Style: AegiNext_Graphics,", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Subtitle.Composition");
    }

    [Fact]
    public void ShapesAndSubtitlesUseTheSameDrawingOrder()
    {
        var background = new ProjectTrack { Name = "Background" };
        var subtitle = new SubtitleLine { Text = "foreground", End = new(2) };
        var subtitleLayer = new ProjectLayer { Id = subtitle.Id, SubtitleId = subtitle.Id, End = subtitle.End };
        var shape = Shape() with { TrackId = background.Id };
        var result = AssSubtitleFormat.Write(new() { Tracks = [ProjectTrack.Default, background], Subtitles = [subtitle], Layers = [subtitleLayer, shape] });
        var dialogues = Dialogues(result.Text);
        Assert.Equal(2, dialogues.Length);
        Assert.StartsWith("Dialogue: 0,", dialogues[0], StringComparison.Ordinal);
        Assert.Contains("\\p1}", dialogues[0], StringComparison.Ordinal);
        Assert.StartsWith("Dialogue: 1,", dialogues[1], StringComparison.Ordinal);
        Assert.Contains("foreground", dialogues[1], StringComparison.Ordinal);
    }

    [Fact]
    public void FillStrokeAndLayerAlphaRemainExplicitAndStrokeApproximationIsReported()
    {
        var shape = Shape() with { Fill = new(1, 0, 0, 0.5), Stroke = new(0, 1, 0, 0.25), StrokeWidth = 4, Opacity = 0.5 };
        var result = AssSubtitleFormat.Write(new() { Layers = [shape] });
        var dialogue = Assert.Single(Dialogues(result.Text));
        Assert.Contains("\\1c&H0000FF&\\1a&HBF&", dialogue, StringComparison.Ordinal);
        Assert.Contains("\\3c&H00FF00&\\3a&HDF&", dialogue, StringComparison.Ordinal);
        Assert.Contains("\\bord2", dialogue, StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShapeStrokePlacement" && diagnostic.LayerId == shape.Id);
    }

    [Fact]
    public void LineStrokeBecomesAFilledQuadrilateralIncludingNonUniformScale()
    {
        var line = Shape() with { Shape = new(ShapeKind.LINE, 100, 40), StrokeWidth = 4, Transform = new(ScaleX: 2, ScaleY: 3), Fill = SceneColor.Transparent };
        var result = AssSubtitleFormat.Write(new() { Layers = [line] });
        var dialogue = Assert.Single(Dialogues(result.Text));
        Assert.Contains("\\bord0", dialogue, StringComparison.Ordinal);
        Assert.Contains("\\p1}m 0 54", dialogue, StringComparison.Ordinal);
        Assert.Contains(" 200 66", dialogue, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("Ass.ShapeStroke", StringComparison.Ordinal));
    }

    [Fact]
    public void UnsupportedGraphicsAndEachImageHaveIndividualDiagnosticIdentity()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/image.gif");
        var animatedShape = Shape() with { Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0), new(new(2), 1)])] };
        var blendShape = Shape() with { Start = new(2), End = new(4), Blend = BlendMode.MULTIPLY };
        var blurredShape = Shape() with { Start = new(4), End = new(6), Blur = 2 };
        var stillImage = new ProjectLayer { Name = "Still", Kind = LayerKind.IMAGE, Start = new(6), End = new(8), Image = new(asset.Id, 10, 10) };
        var animatedImage = stillImage with { Id = Guid.NewGuid(), Name = "Animated", Start = new(8), End = new(10), Image = stillImage.Image! with { Playback = new() } };
        var result = AssSubtitleFormat.Write(new() { Assets = [asset], Layers = [animatedShape, blendShape, blurredShape, stillImage, animatedImage] });
        Assert.Empty(Dialogues(result.Text));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShapeAnimation" && diagnostic.LayerId == animatedShape.Id);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShapeBlend" && diagnostic.LayerId == blendShape.Id);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShapeBlur" && diagnostic.LayerId == blurredShape.Id);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.Image" && diagnostic.LayerId == stillImage.Id);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.AnimatedImage" && diagnostic.LayerId == animatedImage.Id);
    }

    [Fact]
    public void RepeatedImageLossesRemainSeparateForEachClip()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/image.png");
        var first = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 10, 10), End = new(2) };
        var second = first with { Id = Guid.NewGuid(), Start = new(2), End = new(4) };
        var result = AssSubtitleFormat.Write(new() { Assets = [asset], Layers = [first, second] });

        Assert.Equal([first.Id, second.Id], result.Diagnostics.Where(item => item.Code == "Ass.Image").Select(item => item.LayerId!.Value));
        Assert.All(result.Diagnostics, item => Assert.Null(item.SubtitleId));
    }

    [Theory]
    [InlineData(ShapeKind.RECTANGLE)]
    [InlineData(ShapeKind.ROUNDED_RECTANGLE)]
    [InlineData(ShapeKind.ELLIPSE)]
    [InlineData(ShapeKind.ARROW)]
    public void ParametricShapesUseTheSharedClosedGeometry(ShapeKind kind)
    {
        var geometry = new LayerShape(kind, 20, 30) { CornerRadius = 4 };
        var path = ShapeGeometry.CreatePath(geometry);
        var result = AssSubtitleFormat.Write(new() { Layers = [Shape() with { Shape = geometry }] });
        var dialogue = Assert.Single(Dialogues(result.Text));
        var start = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"\\p1}}m {path.Start.X} {path.Start.Y}");

        Assert.Contains(start, dialogue, StringComparison.Ordinal);
        Assert.Equal(path.Segments.Length, dialogue.Split(" b ", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void IndependentRectangleCornersExportTheSameEightSegmentContour()
    {
        var geometry = new LayerShape(ShapeKind.RECTANGLE, 100, 60) { CornerRadii = new(4, 8, 12, 16) };
        var result = AssSubtitleFormat.Write(new() { Layers = [Shape() with { Shape = geometry }] });
        var dialogue = Assert.Single(Dialogues(result.Text));

        Assert.Contains("\\p1}m 4 0", dialogue, StringComparison.Ordinal);
        Assert.Equal(8, dialogue.Split(" b ", StringSplitOptions.None).Length - 1);
        Assert.Contains(" 100 8 b ", dialogue, StringComparison.Ordinal);
        Assert.Contains(" 88 60 b ", dialogue, StringComparison.Ordinal);
        Assert.Contains(" 0 44 b ", dialogue, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedFreePathKeepsCubicControlsAndOpenPathLossDoesNotInventAClosingEdge()
    {
        var path = new PathGeometry(new(1, 2), [new(new(3, 4), new(5, 6), new(7, 8))], true);
        var closed = Shape() with { Shape = new(ShapeKind.PATH, 20, 30, path) };
        var open = closed with { Id = Guid.NewGuid(), Start = new(2), End = new(4), StrokeWidth = 2, Shape = closed.Shape! with { Path = path with { Closed = false } } };
        var result = AssSubtitleFormat.Write(new() { Layers = [closed, open] });

        Assert.Contains("m 1 2 b 3 4 5 6 7 8", Assert.Single(Dialogues(result.Text)), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.ShapeOpenStroke" && item.LayerId == open.Id);
    }

    [Fact]
    public void NonUniformStrokeAndHdrColorHaveSpecificLossNotices()
    {
        var shape = Shape() with { Fill = new(2, 0, 0), StrokeWidth = 4, Transform = new(ScaleX: -2, ScaleY: 3) };
        var result = AssSubtitleFormat.Write(new() { Layers = [shape] });

        Assert.Contains("\\bord5", Assert.Single(Dialogues(result.Text)), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.ShapeStrokeScale" && item.LayerId == shape.Id);
        Assert.Contains(result.Diagnostics, item => item.Code == "Ass.ColorRange" && item.LayerId == shape.Id);
    }

    [Fact]
    public void ShapeTimeQuantizationUsesExistingOutwardRoundingAndRejectsNegativeExternalTime()
    {
        var shape = Shape() with { Start = new(1, 300), End = new(301, 300) };
        var result = AssSubtitleFormat.Write(new() { Layers = [shape] });
        Assert.Contains(",0:00:00.00,0:00:01.01,", Assert.Single(Dialogues(result.Text)), StringComparison.Ordinal);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.TimeQuantization" && diagnostic.LayerId == shape.Id && diagnostic.SubtitleId is null);
        Assert.Throws<InvalidDataException>(() => AssSubtitleFormat.Write(new() { Layers = [shape] }, new(-1)));
    }

    [Fact]
    public void GraphicsStyleCannotCollideWithAnExistingSubtitleStyleName()
    {
        var subtitle = new SubtitleLine { Text = "text", StyleName = "AegiNext_Graphics", Start = new(3), End = new(4) };
        var subtitleLayer = new ProjectLayer { Id = subtitle.Id, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End };
        var result = AssSubtitleFormat.Write(new() { Subtitles = [subtitle], Layers = [Shape(), subtitleLayer] });
        Assert.Contains("Style: AegiNext_Graphics_2,", result.Text, StringComparison.Ordinal);
        Assert.Contains(",AegiNext_Graphics_2,", Dialogues(result.Text)[0], StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleName");
    }

    private static ProjectLayer Shape()
    {
        return new() { Name = "Shape", Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 30), End = new(2) };
    }

    private static string[] Dialogues(string text)
    {
        return text.Split('\n').Where(line => line.StartsWith("Dialogue:", StringComparison.Ordinal)).ToArray();
    }
}
