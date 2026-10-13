using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

public sealed class AssDrawingImportReferenceTests
{
    [LibassReferenceTheory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 1, 0)]
    [InlineData(4, 1, 0)]
    [InlineData(5, 1, 0)]
    [InlineData(6, 1, 0)]
    [InlineData(7, 1, 0)]
    [InlineData(8, 1, 0)]
    [InlineData(9, 1, 0)]
    [InlineData(7, 2, 0)]
    [InlineData(5, 3, 0)]
    [InlineData(7, 1, 20)]
    [InlineData(7, 1, 80)]
    [InlineData(7, 1, -20)]
    [InlineData(1, 1, 20)]
    [InlineData(1, 1, -20)]
    [InlineData(5, 1, 80)]
    [InlineData(5, 2, 20)]
    public void DrawingMetricsAndBaselineOffsetMatchIndependentLibass(int alignment, int drawingScale, int baseline)
    {
        var source = ReferenceSubtitleProject.Script($"{{\\an{alignment}\\pos(240,180)\\pbo{baseline}\\p{drawingScale}}}m 10 20 l 70 20 70 60 10 60{{\\p0}}");
        Compare(source);
    }

    [LibassReferenceTheory]
    [InlineData(@"\fscx150\fscy75\frz30")]
    [InlineData(@"\fscx150\fscy75\frz90\org(300,200)")]
    [InlineData(@"\fscx200\fscy50\fsc\frz90")]
    public void StaticScaleRotationAndOriginPreserveDrawingPlacement(string tags)
    {
        var source = ReferenceSubtitleProject.Script(@"{\an5\pos(240,180)" + tags + @"\p1}m 0 0 l 60 0 60 40 0 40{\p0}");
        Compare(source);
    }

    [LibassReferenceTheory]
    [InlineData(@"m 0 0 l 30 0 30 30 0 30 m 60 0 l 90 0 90 30 60 30")]
    [InlineData(@"m 0 0 b 20 -20 40 -20 60 0 l 60 30 0 30")]
    public void IndependentContoursAndControlBoxesKeepTheirSharedGlyphLayout(string drawing)
    {
        Compare(ReferenceSubtitleProject.Script(@"{\an5\pos(240,180)\p1}" + drawing));
    }

    private static void Compare(string source)
    {
        var imported = AssSubtitleFormat.Parse(source, 640, 360);
        Assert.NotEmpty(imported.Layers);
        Assert.All(imported.Layers, layer => Assert.Equal(LayerKind.SHAPE, layer.Kind));
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, imported, "ASS");
        using var native = ReferenceSubtitleProject.Renderer();
        var actual = ReferenceSubtitleProject.Render(native, document, 1000);
        using var reference = new LibassReferenceRenderer();
        var expected = reference.Render(source, 1000);
        var first = actual.InkBounds();
        var second = expected.InkBounds();
        Assert.InRange(Math.Abs(first.Left - second.Left), 0, 1);
        Assert.InRange(Math.Abs(first.Top - second.Top), 0, 1);
        Assert.InRange(Math.Abs(first.Right - second.Right), 0, 1);
        Assert.InRange(Math.Abs(first.Bottom - second.Bottom), 0, 1);
        Assert.InRange(Math.Abs(actual.Energy(3) / expected.Energy(3) - 1), 0, 0.02);
    }
}
