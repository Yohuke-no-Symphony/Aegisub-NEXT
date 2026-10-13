using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.SubtitleFormats.Tests;

public sealed class AssShapeImportTests
{
    private const string RECTANGLE = "m 0 0 l 20 0 20 40 0 40";

    [Fact]
    public void IsolatedDrawingCreatesEditableShapeAndNoEmptySubtitle()
    {
        var result = Parse("{\\an7\\pos(100,200)\\p1}" + RECTANGLE + "{\\p0}");
        var shape = Assert.Single(result.Layers);

        Assert.Empty(result.Lines);
        Assert.Empty(result.Clips);
        Assert.Equal(LayerKind.SHAPE, shape.Kind);
        Assert.Equal(ShapeKind.PATH, shape.Shape!.Kind);
        Assert.True(shape.Shape.Path!.Closed);
        Assert.Null(shape.Mask);
        Assert.Null(shape.SubtitleId);
        Assert.Equal(new(100, 200), shape.Transform.Position);
        Assert.Equal(20, shape.Shape.Width);
        Assert.Equal(40, shape.Shape.Height);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.Drawing");
    }

    [Theory]
    [InlineData(7, 100, 200)]
    [InlineData(5, 90, 180)]
    [InlineData(1, 100, 160)]
    public void ExplicitPositionUsesDrawingMetricsAndEventAlignment(int alignment, double x, double y)
    {
        var result = Parse("{\\an" + alignment + "\\pos(100,200)\\p1}" + RECTANGLE);

        Assert.Equal(new(x, y), Assert.Single(result.Layers).Transform.Position);
    }

    [Fact]
    public void NonzeroMoveCoordinatesArePreservedRelativeToExplicitPosition()
    {
        var shape = Assert.Single(Parse("{\\an7\\pos(100,200)\\p2}m 10 20 l 30 20 30 60 10 60").Layers);

        Assert.Equal(new(105, 210), shape.Transform.Position);
        Assert.Equal(10, shape.Shape!.Width);
        Assert.Equal(20, shape.Shape.Height);
    }

    [Theory]
    [InlineData(7, 20, 200)]
    [InlineData(7, 80, 240)]
    [InlineData(7, -20, 200)]
    [InlineData(1, 20, 160)]
    [InlineData(1, -20, 140)]
    [InlineData(5, 80, 200)]
    public void IsolatedBaselineOffsetUsesAscenderDescenderAndAlignment(int alignment, int baseline, double expectedY)
    {
        var result = Parse("{\\an" + alignment + "\\pos(100,200)\\pbo" + baseline + "\\p1}" + RECTANGLE);

        Assert.Equal(expectedY, Assert.Single(result.Layers).Transform.Y);
    }

    [Fact]
    public void SourceCanvasResamplingAndDrawingScaleApplyOnlyOnce()
    {
        var result = AssSubtitleFormat.Parse(File("{\\an7\\pos(100,200)\\p2}" + RECTANGLE), 1280, 720);
        var shape = Assert.Single(result.Layers);

        Assert.Equal(new(200, 400), shape.Transform.Position);
        Assert.Equal(20, shape.Shape!.Width);
        Assert.Equal(40, shape.Shape.Height);
    }

    [Fact]
    public void PrefixStyleAndStaticStrokeAreNativeAndApproximationHasShapeIdentity()
    {
        var result = Parse("{\\an7\\pos(100,200)\\1c&H0000FF&\\1a&H80&\\3c&H00FF00&\\bord3\\p1}" + RECTANGLE);
        var shape = Assert.Single(result.Layers);

        Assert.Equal(1, shape.Fill.Red);
        Assert.Equal(127d / 255, shape.Fill.Alpha, 8);
        Assert.Equal(1, shape.Stroke.Green);
        Assert.Equal(6, shape.StrokeWidth);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.ShapeStrokePlacement" &&
            diagnostic.LayerId == shape.Id && diagnostic.SubtitleId is null);
    }

    [Fact]
    public void IndependentContoursKeepTheSameGlyphLayoutWhenSeparated()
    {
        var result = Parse("{\\an5\\pos(100,200)\\p1}" + RECTANGLE + " m 40 0 l 60 0 60 40 40 40");

        Assert.Equal(2, result.Layers.Length);
        Assert.Equal(new(70, 180), result.Layers[0].Transform.Position);
        Assert.Equal(new(110, 180), result.Layers[1].Transform.Position);
        Assert.All(result.Layers, layer => Assert.Null(layer.Mask));
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void EqualAxisBordersUseTheNativeStaticStrokeApproximation()
    {
        var result = Parse("{\\an7\\pos(100,200)\\xbord3\\ybord3\\p1}" + RECTANGLE);

        Assert.Equal(6, Assert.Single(result.Layers).StrokeWidth);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code is "Ass.DrawingGeometry" or "Ass.BorderAxes");
    }

    [Fact]
    public void DifferentAxisBordersDoNotSilentlyBecomeOneScalarShapeStroke()
    {
        var result = Parse("{\\an7\\pos(100,200)\\xbord2\\ybord3\\p1}" + RECTANGLE);

        Assert.DoesNotContain(result.Layers, layer => layer.Kind == LayerKind.SHAPE);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.DrawingGeometry");
    }

    [Fact]
    public void DrawingBudgetFailureRejectsTheWholeImport()
    {
        var drawing = "m 0 0 l " + string.Join(' ', Enumerable.Repeat("1 1", 10000));

        Assert.Throws<InvalidDataException>(() => Parse("{\\an7\\pos(100,200)\\p1}" + drawing));
    }

    [Theory]
    [InlineData("m 5 5 l 5 15 15 15 15 5")]
    [InlineData("m 10 10 l 30 10 30 30 10 30")]
    [InlineData("m 20 0 l 40 0 40 40 20 40")]
    public void NestedIntersectingOrTouchingContoursAreRejectedWithoutFilledHole(string extra)
    {
        var result = Parse("{\\an7\\pos(100,200)\\p1}" + RECTANGLE + " " + extra);

        Assert.DoesNotContain(result.Layers, layer => layer.Kind == LayerKind.SHAPE);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.DrawingTopology");
    }

    [Theory]
    [InlineData("{\\p1}", "Ass.DrawingPlacement")]
    [InlineData("{\\pos(100,200)\\clip(0,0,200,200)\\p1}", "Ass.DrawingMask")]
    [InlineData("{\\pos(100,200)\\move(100,200,200,200)\\p1}", "Ass.DrawingAnimation")]
    [InlineData("{\\pos(100,200)\\t(0,1000,\\frz30)\\p1}", "Ass.DrawingAnimation")]
    [InlineData("{\\pos(100,200)\\blur2\\p1}", "Ass.DrawingEffects")]
    [InlineData("{\\pos(100,200)\\4a&H00&\\shad2\\p1}", "Ass.DrawingEffects")]
    public void UnsupportedDrawingDependenciesHavePreciseDiagnostics(string prefix, string code)
    {
        var result = Parse(prefix + RECTANGLE);

        Assert.DoesNotContain(result.Layers, layer => layer.Kind == LayerKind.SHAPE);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code && diagnostic.SubtitleId is null);
    }

    [Fact]
    public void DrawingMixedWithOrdinaryTextPreservesTextWithoutInventingFlow()
    {
        var result = Parse("before{\\an7\\pos(100,200)\\p1}" + RECTANGLE + "{\\p0}after");

        Assert.Equal("beforeafter", Assert.Single(result.Lines).Text);
        Assert.DoesNotContain(result.Layers, layer => layer.Kind == LayerKind.SHAPE);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.DrawingMixedFlow");
    }

    [Fact]
    public void DrawingInternalStyleOverridesDoNotBecomeIndependentPositionedGlyphs()
    {
        var result = Parse("{\\an7\\pos(100,200)\\p1}m 0 0 l 20 0{\\1c&H0000FF&}l 20 40 0 40");

        Assert.DoesNotContain(result.Layers, layer => layer.Kind == LayerKind.SHAPE);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.DrawingStyleRuns");
    }

    [Theory]
    [InlineData(@"\N")]
    [InlineData(@"\n")]
    [InlineData(@"\h")]
    public void DrawingLineBreaksAndSpacingRequireFlowConversionInsteadOfJoiningGeometry(string separator)
    {
        var result = Parse(@"{\an7\pos(100,200)\p1}" + RECTANGLE + separator + @"m 60 0 l 80 0 80 20 60 20");

        Assert.Empty(result.Lines);
        Assert.Empty(result.Layers);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "Ass.DrawingMixedFlow" && diagnostic.SubtitleId is null);
    }

    [Fact]
    public void ShapeWriterOutputImportsAsNativeShapeRatherThanTagCache()
    {
        var source = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 20, 40),
            Transform = new(100, 200),
            End = new(3),
            Fill = new(1, 0, 0)
        };
        var exported = AssSubtitleFormat.Write(new() { Width = 640, Height = 360, Layers = [source] });
        var imported = AssSubtitleFormat.Parse(exported.Text, 640, 360);
        var shape = Assert.Single(imported.Layers);

        Assert.Equal(LayerKind.SHAPE, shape.Kind);
        Assert.Equal(source.Transform.Position, shape.Transform.Position);
        Assert.Equal(source.Fill, shape.Fill);
        Assert.Empty(imported.Lines);
    }

    private static AssImportResult Parse(string text) => AssSubtitleFormat.Parse(File(text), 640, 360);

    private static string File(string text)
    {
        return """
            [Script Info]
            ScriptType: v4.00+
            PlayResX: 640
            PlayResY: 360
            WrapStyle: 2
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
            Style: Default,Arial,20,&H00FFFFFF,&H000000FF,&H00000000,&HFF000000,0,0,0,0,100,100,0,0,1,0,0,7,0,0,0,1
            [Events]
            Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
            Dialogue: 0,0:00:00.00,0:00:03.00,Default,,0,0,0,,
            """ + text;
    }
}
