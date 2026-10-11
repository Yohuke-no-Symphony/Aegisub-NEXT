using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Projects;
using Avalonia;
using SkiaSharp;

namespace AegiNext.Desktop.Tests;

public sealed class GraphicCanvasGeometryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImageResizeKeepsOppositeCornerFixedAndShiftUnlocksAspect(bool shift)
    {
        var layer = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(Guid.NewGuid(), 100, 50) };
        var geometry = new ProjectLayerGeometry(new(0, 0, 100, 50), default, default, SKMatrix.Identity, SKMatrix.Identity);
        var gesture = new CanvasTransformGesture(layer, layer.Transform, geometry, Matrix.Identity, new(100, 50), 2);
        var changed = GraphicCanvasGeometry.Transform(gesture, new(200, 50), shift);
        Assert.Equal(0, changed.X);
        Assert.Equal(0, changed.Y);
        if (shift)
        {
            Assert.Equal(2, changed.ScaleX);
            Assert.Equal(1, changed.ScaleY);
        }
        else
        {
            Assert.Equal(changed.ScaleX, changed.ScaleY);
            Assert.True(changed.ScaleX > 1);
        }
    }

    [Fact]
    public void RotationUsesDisplayedPivotAndAddsDeltaToTheBaseTransform()
    {
        var layer = new ProjectLayer { Transform = new(Rotation: 10) };
        var geometry = new ProjectLayerGeometry(new(0, 0, 100, 100), new(50, 50), default, SKMatrix.Identity, SKMatrix.Identity);
        var gesture = new CanvasTransformGesture(layer, new(Rotation: 40), geometry, Matrix.Identity, new(50, 0), 4);
        var changed = GraphicCanvasGeometry.Transform(gesture, new(100, 50), false);
        Assert.Equal(100, changed.Rotation, 8);
        Assert.Equal(layer.Transform.Position, changed.Position);
    }

    [Fact]
    public void MovingCropPreservesItsSizeAndClampsToTheSource()
    {
        var crop = new NormalizedImageCrop { X = 0.2, Y = 0.3, Width = 0.4, Height = 0.5 };
        var moved = GraphicCanvasGeometry.Crop(crop, new(0.4, 0.5), new(1.4, -1), 4);
        Assert.Equal(0.6, moved.X, 8);
        Assert.Equal(0, moved.Y);
        Assert.Equal(crop.Width, moved.Width);
        Assert.Equal(crop.Height, moved.Height);
    }

    [Fact]
    public void MovingAClosedAnchorMovesItsAdjacentHandlesAndClosureTogether()
    {
        var path = ShapeGeometry.CreatePath(new(ShapeKind.RECTANGLE, 100, 50));
        var moved = GraphicCanvasGeometry.MovePathPoint(path, 0, new(10, 20));
        Assert.Equal(new ScenePoint(10, 20), moved.Start);
        Assert.Equal(moved.Start, moved.Segments[^1].End);
        Assert.Equal(path.Segments[0].Control1.X + 10, moved.Segments[0].Control1.X);
        Assert.Equal(path.Segments[^1].Control2.Y + 20, moved.Segments[^1].Control2.Y);
        Assert.Equal(path.Segments[1], moved.Segments[1]);
    }

    [Fact]
    public void OpeningAClosedPathRemovesItsClosingSegmentAndClosingRestoresIt()
    {
        var path = ShapeGeometry.CreatePath(new(ShapeKind.RECTANGLE, 100, 50));
        var open = GraphicCanvasGeometry.ToggleClosed(path);
        Assert.False(open.Closed);
        Assert.Equal(3, open.Segments.Length);
        Assert.NotEqual(open.Start, open.Segments[^1].End);
        var closed = GraphicCanvasGeometry.ToggleClosed(open);
        Assert.True(closed.Closed);
        Assert.Equal(4, closed.Segments.Length);
        Assert.Equal(closed.Start, closed.Segments[^1].End);
    }
}
