using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class RectangleCornerGeometryTests
{
    [Fact]
    public void IndependentCornersUseCircularArcsWithTheirOwnRadii()
    {
        var shape = new LayerShape(ShapeKind.RECTANGLE, 120, 80) { CornerRadii = new(5, 10, 15, 20) };
        var path = ShapeGeometry.CreatePath(shape);

        Assert.Equal(new ScenePoint(5, 0), path.Start);
        Assert.Equal(new ScenePoint(110, 0), path.Segments[0].End);
        Assert.Equal(new ScenePoint(120, 10), path.Segments[1].End);
        Assert.Equal(new ScenePoint(120, 65), path.Segments[2].End);
        Assert.Equal(new ScenePoint(105, 80), path.Segments[3].End);
        Assert.Equal(new ScenePoint(20, 80), path.Segments[4].End);
        Assert.Equal(new ScenePoint(0, 60), path.Segments[5].End);
        Assert.Equal(new ScenePoint(0, 5), path.Segments[6].End);
        Assert.Equal(path.Start, path.Segments[7].End);
        Assert.True(path.Closed);
        foreach (var index in new[] { 1, 3, 5, 7 })
        {
            var start = path.Segments[index - 1].End;
            var arc = path.Segments[index];
            Assert.Equal(Math.Abs(arc.End.X - start.X), Math.Abs(arc.End.Y - start.Y), 9);
            Assert.Equal(Math.Abs(arc.Control1.X - start.X) + Math.Abs(arc.Control1.Y - start.Y),
                Math.Abs(arc.Control2.X - arc.End.X) + Math.Abs(arc.Control2.Y - arc.End.Y), 9);
        }
    }

    [Fact]
    public void OverlappingRadiiShareOneNormalizationFactorWithoutChangingStoredValues()
    {
        var radii = new ShapeCornerRadii(80, 40, 20, 0);
        var shape = new LayerShape(ShapeKind.RECTANGLE, 100, 60) { CornerRadii = radii };
        var path = ShapeGeometry.CreatePath(shape);

        Assert.Equal(new ScenePoint(60, 0), path.Start);
        Assert.Equal(new ScenePoint(70, 0), path.Segments[0].End);
        Assert.Equal(new ScenePoint(100, 30), path.Segments[1].End);
        Assert.Equal(new ScenePoint(85, 60), path.Segments[3].End);
        Assert.Same(radii, shape.CornerRadii);
        Assert.Equal(80, ShapeGeometry.CreatePath(ShapeGeometry.Resize(shape, 300, 200)).Start.X);
    }

    [Fact]
    public void ResizingRectangleKeepsCornerRadiusInLocalPixels()
    {
        var shape = new LayerShape(ShapeKind.RECTANGLE, 100, 40) { CornerRadii = ShapeCornerRadii.Uniform(8) };
        var resized = ShapeGeometry.Resize(shape, 300, 60);
        var path = ShapeGeometry.CreatePath(resized);

        Assert.Same(shape.CornerRadii, resized.CornerRadii);
        Assert.Equal(new ScenePoint(8, 0), path.Start);
        Assert.Equal(new ScenePoint(292, 0), path.Segments[0].End);
        Assert.Equal(new ScenePoint(300, 8), path.Segments[1].End);
    }

    [Fact]
    public void LegacyRadiusAppliesOnlyToLegacyRoundedRectanglesUnlessFourCornersAreExplicit()
    {
        var rectangle = new LayerShape(ShapeKind.RECTANGLE, 100, 40) { CornerRadius = 8 };
        Assert.Equal(4, ShapeGeometry.CreatePath(rectangle).Segments.Length);
        var rounded = rectangle with { Kind = ShapeKind.ROUNDED_RECTANGLE };
        Assert.Equal(ShapeCornerRadii.Uniform(8), rounded.EffectiveCornerRadii);
        var explicitCorners = rounded with { CornerRadii = new(1, 2, 3, 4) };
        Assert.Equal(new ScenePoint(1, 0), ShapeGeometry.CreatePath(explicitCorners).Start);
        var merged = explicitCorners with { Kind = ShapeKind.RECTANGLE };
        Assert.Equal(ShapeGeometry.CreatePath(explicitCorners).Segments.ToArray(), ShapeGeometry.CreatePath(merged).Segments.ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1000001)]
    public void EveryCornerRejectsInvalidRadii(double value)
    {
        foreach (var radii in new[]
        {
            new ShapeCornerRadii(value, 0, 0, 0), new(0, value, 0, 0), new(0, 0, value, 0), new(0, 0, 0, value)
        })
        {
            Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateLayerShape(
                new(ShapeKind.RECTANGLE, 100, 40) { CornerRadii = radii }));
        }
    }
}
