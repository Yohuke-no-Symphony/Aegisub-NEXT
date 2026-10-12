using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia;

namespace AegiNext.Desktop.Tests;

public sealed class MaskCanvasGeometryTests
{
    [Fact]
    public void RectangleBoundsPreserveDoublePrecisionThroughRotationAndTranslation()
    {
        var mask = new RectangleClipMask { TopLeft = new(100.123456789, 30.987654321), BottomRight = new(150.876543219, 75.123456789) };
        var matrix = Matrix.CreateRotation(0.4) * Matrix.CreateTranslation(600.987654321, 500.123456789);
        Point[] corners =
        [
            new(mask.TopLeft.X, mask.TopLeft.Y), new(mask.BottomRight.X, mask.TopLeft.Y),
            new(mask.BottomRight.X, mask.BottomRight.Y), new(mask.TopLeft.X, mask.BottomRight.Y)
        ];
        var projected = corners.Select(point => point * matrix).ToArray();

        var bounds = MaskCanvasGeometry.Bounds(mask, matrix);

        Assert.Equal(projected.Min(point => point.X), bounds.Left, 10);
        Assert.Equal(projected.Min(point => point.Y), bounds.Top, 10);
        Assert.Equal(projected.Max(point => point.X), bounds.Right, 10);
        Assert.Equal(projected.Max(point => point.Y), bounds.Bottom, 10);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void CubicBoundsUseTheVisibleCurveExtremaInsteadOfControlHandles(double direction)
    {
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes =
            [
                new() { Position = new(0, 0), OutHandle = new(0, 100 * direction) },
                new() { Position = new(100, 0), InHandle = new(0, 100 * direction) }
            ] }]
        };
        var matrix = Matrix.CreateScale(2, 3) * Matrix.CreateTranslation(17, 31);

        var bounds = MaskCanvasGeometry.Bounds(mask, matrix);

        Assert.Equal(17, bounds.Left, 10);
        Assert.Equal(217, bounds.Right, 10);
        Assert.Equal(direction > 0 ? 31 : -194, bounds.Top, 10);
        Assert.Equal(direction > 0 ? 256 : 31, bounds.Bottom, 10);
    }

    [Fact]
    public void MultipleContoursAndCubicRootsContributeToTheSameBounds()
    {
        var mask = new VectorClipMask
        {
            Contours =
            [
                new() { Nodes =
                [
                    new() { Position = new(0, 0), OutHandle = new(200, 100) },
                    new() { Position = new(0, 0), InHandle = new(-200, -100) }
                ] },
                new() { Nodes = [new() { Position = new(-100, 40) }, new() { Position = new(-80, 60) }] }
            ]
        };

        var bounds = MaskCanvasGeometry.Bounds(mask, Matrix.Identity);

        Assert.Equal(-100, bounds.Left, 10);
        Assert.Equal(100 / Math.Sqrt(3), bounds.Right, 10);
        Assert.Equal(-50 / Math.Sqrt(3), bounds.Top, 10);
        Assert.Equal(60, bounds.Bottom, 10);
    }
}
