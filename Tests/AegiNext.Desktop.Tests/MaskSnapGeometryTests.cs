using AegiNext.Desktop.Controls;
using Avalonia;

namespace AegiNext.Desktop.Tests;

public sealed class MaskSnapGeometryTests
{
    [Theory]
    [InlineData(100, 0, 100, 40)]
    [InlineData(200, 0, 200, 40)]
    [InlineData(300, 0, 300, 40)]
    [InlineData(0, 20, 140, 20)]
    [InlineData(0, 60, 140, 60)]
    [InlineData(0, 100, 140, 100)]
    public void PointSnapsToEachSubtitleEdgeAndCenterLine(double xGuide, double yGuide, double x, double y)
    {
        var geometry = MaskSnapGeometry.Create(new(100, 20, 200, 80), Matrix.Identity)!;
        var point = new Point(x + (xGuide == 0 ? 0 : 3), y + (yGuide == 0 ? 0 : 3));

        var result = geometry.SnapPoint(point, 8);

        AssertPoint(new(x, y), point + result.Offset);
        Assert.Equal(xGuide == 0 ? (double?)null : xGuide, result.XGuide);
        Assert.Equal(yGuide == 0 ? (double?)null : yGuide, result.YGuide);
    }

    [Fact]
    public void PointCanSnapBothAxesAndLeavesDistantPointsUnchanged()
    {
        var geometry = MaskSnapGeometry.Create(new(100, 20, 200, 80), Matrix.Identity)!;
        var near = new Point(204, 63);
        var far = new Point(240, 40);

        var snapped = geometry.SnapPoint(near, 8);
        var unsnapped = geometry.SnapPoint(far, 8);

        AssertPoint(new(200, 60), near + snapped.Offset);
        Assert.True(snapped.HasSnap);
        Assert.Equal(default, unsnapped.Offset);
        Assert.False(unsnapped.HasSnap);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1)]
    [InlineData(3)]
    public void ThresholdUsesScreenDistanceAtEveryCanvasScale(double scale)
    {
        var matrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(35, 17);
        var geometry = MaskSnapGeometry.Create(new(100, 20, 200, 400), matrix)!;
        var edge = new Point(100, 100) * matrix;

        var within = geometry.SnapPoint(edge + new Vector(7, 0), 8);
        var outside = geometry.SnapPoint(edge + new Vector(9, 0), 8);

        AssertPoint(edge, edge + new Vector(7, 0) + within.Offset);
        Assert.Equal(100, within.XGuide);
        Assert.Null(outside.XGuide);
    }

    [Fact]
    public void RotationAndMirroredNonUniformScaleUseTheDisplayedSubtitleLines()
    {
        var matrix = Matrix.CreateScale(-2, 3) * Matrix.CreateRotation(Math.PI / 3) * Matrix.CreateTranslation(410, 220);
        var geometry = MaskSnapGeometry.Create(new(100, 20, 200, 80), matrix)!;
        var target = new Point(200, 60) * matrix;
        var point = new Point(202, 61) * matrix;

        var result = geometry.SnapPoint(point, 8);

        AssertPoint(target, point + result.Offset);
        Assert.Equal(200, result.XGuide);
        Assert.Equal(60, result.YGuide);
    }

    [Fact]
    public void SkewedSubtitleUsesPerpendicularScreenDistanceAndTheNearestProjection()
    {
        var matrix = new Matrix(1, 0, 2, 1, 0, 0);
        var geometry = MaskSnapGeometry.Create(new(100, 20, 200, 80), matrix)!;
        var point = new Point(115, 40) * matrix;

        var result = geometry.SnapPoint(point, 8);
        var snapped = point + result.Offset;
        var local = snapped * geometry.ScreenToLocal;

        Assert.Equal(100, local.X, 8);
        Assert.Equal(100, result.XGuide);
        Assert.Null(result.YGuide);
        Assert.InRange(result.Offset.Length, 6.7, 6.8);
        Assert.Equal(0, result.Offset.X * 2 + result.Offset.Y, 8);
    }

    [Fact]
    public void BoundsSnapTheirNearestEdgesWithOneTranslation()
    {
        var geometry = MaskSnapGeometry.Create(new(100, 20, 200, 80), Matrix.Identity)!;
        var moving = new Rect(33, 32, 64, 65);

        var result = geometry.SnapBounds(moving, 8);
        var translated = moving.Translate(result.Offset);

        Assert.Equal(new Vector(3, 3), result.Offset);
        Assert.Equal(100, translated.Right);
        Assert.Equal(100, translated.Bottom);
        Assert.Equal(moving.Size, translated.Size);
        Assert.Equal(100, result.XGuide);
        Assert.Equal(100, result.YGuide);
    }

    [Fact]
    public void BoundsCanAlignTheirCenterToSubtitleHalfWidthAndHalfHeight()
    {
        var matrix = Matrix.CreateScale(2, 3) * Matrix.CreateRotation(Math.PI / 5);
        var geometry = MaskSnapGeometry.Create(new(100, 20, 200, 80), matrix)!;
        var moving = new Rect(185, 44, 34, 34);

        var result = geometry.SnapBounds(moving, 8);
        var center = moving.Center * matrix + result.Offset;

        AssertPoint(new Point(200, 60) * matrix, center);
        Assert.Equal(200, result.XGuide);
        Assert.Equal(60, result.YGuide);
    }

    [Fact]
    public void ThinSubtitleChoosesTheClosestLineWithStableTies()
    {
        var geometry = MaskSnapGeometry.Create(new(100, 20, 10, 80), Matrix.Identity)!;

        var nearest = geometry.SnapPoint(new(104, 40), 8);
        var tied = geometry.SnapPoint(new(102.5, 40), 8);

        Assert.Equal(105, nearest.XGuide);
        Assert.Equal(100, tied.XGuide);
    }

    [Fact]
    public void NearlyParallelGuidesCannotPullThePointToADistantIntersection()
    {
        var matrix = new Matrix(1, 0, 1, 0.01, 0, 0);
        var geometry = MaskSnapGeometry.Create(new(0, 0, 100, 100), matrix)!;
        var point = new Point(-100, -100) * matrix;

        var result = geometry.SnapPoint(point, 8);

        Assert.InRange(result.Offset.Length, 0.99, 1);
        Assert.Equal(0, result.XGuide);
        Assert.Null(result.YGuide);
        Assert.Equal(0, ((point + result.Offset) * geometry.ScreenToLocal).X, 8);
    }

    [Fact]
    public void DegenerateOrNonFiniteReferencesCannotSnap()
    {
        Assert.Null(MaskSnapGeometry.Create(new(0, 0, 0, 40), Matrix.Identity));
        Assert.Null(MaskSnapGeometry.Create(new(0, 0, 100, 40), Matrix.CreateScale(0, 1)));
        Assert.Null(MaskSnapGeometry.Create(new(0, 0, double.NaN, 40), Matrix.Identity));
        Assert.Null(MaskSnapGeometry.Create(new(0, 0, 100, 40), new(double.NaN, 0, 0, 1, 0, 0)));
    }

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
