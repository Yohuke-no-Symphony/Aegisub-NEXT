using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Projects;
using Avalonia;
using SkiaSharp;

namespace AegiNext.Desktop.Tests;

public sealed class GraphicCanvasGeometryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RectangleResizeCompensatesTheMovingNormalizedPivot(int handle)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 100, 50),
            Position = new() { Pivot = new(0.5, 0.75) },
            Transform = new(10, 20, 1, 1, 15, 30, 20)
        };
        var evaluated = layer.Transform with { X = 80, Y = 90, ScaleX = -2, ScaleY = 3, Rotation = 60 };
        var oldPivot = new ScenePoint(80, 57.5);
        var matrix = MatrixFor(evaluated with { Pivot = oldPivot });
        Point[] corners = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)];
        var moving = corners[handle];
        var fixedCorner = corners[(handle + 2) % 4];
        var geometry = new ProjectLayerGeometry(new(0, 0, 100, 50), new((float)oldPivot.X, (float)oldPivot.Y), default, SKMatrix.Identity, SKMatrix.Identity);
        var gesture = new CanvasTransformGesture(layer, evaluated, geometry, matrix, moving * matrix, handle);
        var target = fixedCorner + new Vector((moving.X - fixedCorner.X) * 2, (moving.Y - fixedCorner.Y) * 1.5);

        var changed = GraphicCanvasGeometry.TransformLayer(gesture, target * matrix, false);

        Assert.InRange(Math.Abs(200 - changed.Shape!.Width), 0, 0.0001);
        Assert.InRange(Math.Abs(75 - changed.Shape.Height), 0, 0.0001);
        Assert.Same(layer.Position, changed.Position);
        var adjusted = evaluated with
        {
            X = evaluated.X + changed.Transform.X - layer.Transform.X,
            Y = evaluated.Y + changed.Transform.Y - layer.Transform.Y,
            AnchorX = layer.Transform.AnchorX + layer.Position.Pivot.X * changed.Shape.Width,
            AnchorY = layer.Transform.AnchorY + layer.Position.Pivot.Y * changed.Shape.Height
        };
        var expected = fixedCorner * matrix;
        var actual = new Point(fixedCorner.X * changed.Shape.Width / layer.Shape.Width,
            fixedCorner.Y * changed.Shape.Height / layer.Shape.Height) * MatrixFor(adjusted);
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RectangleResizeKeepsTheOppositeCornerFixedWithRotationPivotAndEvaluatedAnimation(int handle)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE,
            Shape = new(ShapeKind.RECTANGLE, 100, 50) { CornerRadii = new(4, 8, 12, 16) },
            Transform = new(10, 20, 1, 1, 15, 30, 20)
        };
        var evaluated = layer.Transform with { X = 80, Y = 90, ScaleX = -2, ScaleY = 3, Rotation = 60 };
        var matrix = MatrixFor(evaluated);
        Point[] corners = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)];
        var moving = corners[handle];
        var fixedCorner = corners[(handle + 2) % 4];
        var geometry = new ProjectLayerGeometry(new(0, 0, 100, 50), new(30, 20), default, SKMatrix.Identity, SKMatrix.Identity);
        var gesture = new CanvasTransformGesture(layer, evaluated, geometry, matrix, moving * matrix, handle);
        var target = fixedCorner + new Vector((moving.X - fixedCorner.X) * 2, (moving.Y - fixedCorner.Y) * 1.5);

        var changed = GraphicCanvasGeometry.TransformLayer(gesture, target * matrix, false);

        Assert.InRange(Math.Abs(200 - changed.Shape!.Width), 0, 0.0001);
        Assert.InRange(Math.Abs(75 - changed.Shape.Height), 0, 0.0001);
        Assert.Same(layer.Shape.CornerRadii, changed.Shape.CornerRadii);
        Assert.Equal(layer.Transform.Scale, changed.Transform.Scale);
        Assert.Equal(layer.Transform.Rotation, changed.Transform.Rotation);
        Assert.Equal(layer.Transform.Pivot, changed.Transform.Pivot);
        var adjusted = evaluated with
        {
            X = evaluated.X + changed.Transform.X - layer.Transform.X,
            Y = evaluated.Y + changed.Transform.Y - layer.Transform.Y
        };
        var expected = fixedCorner * matrix;
        var actual = new Point(fixedCorner.X * changed.Shape.Width / layer.Shape.Width,
            fixedCorner.Y * changed.Shape.Height / layer.Shape.Height) * MatrixFor(adjusted);
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    [Fact]
    public void LegacyRoundedRectangleResizeKeepsCircularRadiiAndShiftLocksItsDimensions()
    {
        var shape = new LayerShape(ShapeKind.ROUNDED_RECTANGLE, 100, 50) { CornerRadius = 10 };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = shape };
        var geometry = new ProjectLayerGeometry(new(0, 0, 100, 50), default, default, SKMatrix.Identity, SKMatrix.Identity);
        var gesture = new CanvasTransformGesture(layer, layer.Transform, geometry, Matrix.Identity, new(100, 50), 2);

        var changed = GraphicCanvasGeometry.TransformLayer(gesture, new(200, 50), false);
        var locked = GraphicCanvasGeometry.TransformLayer(gesture, new(200, 50), true);

        Assert.Equal(200, changed.Shape!.Width);
        Assert.Equal(50, changed.Shape.Height);
        Assert.Equal(layer.Transform, changed.Transform);
        var path = ShapeGeometry.CreatePath(changed.Shape);
        Assert.Equal(new ScenePoint(190, 0), path.Segments[0].End);
        Assert.Equal(new ScenePoint(200, 10), path.Segments[1].End);
        Assert.Equal(locked.Shape!.Width / shape.Width, locked.Shape.Height / shape.Height, 8);
        Assert.Equal(layer.Transform, locked.Transform);
    }

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

    private static Matrix MatrixFor(LayerTransform transform)
    {
        var radians = transform.Rotation * Math.PI / 180;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new(transform.ScaleX * cosine, transform.ScaleX * sine, -transform.ScaleY * sine, transform.ScaleY * cosine,
            transform.X - transform.AnchorX * transform.ScaleX * cosine + transform.AnchorY * transform.ScaleY * sine,
            transform.Y - transform.AnchorX * transform.ScaleX * sine - transform.AnchorY * transform.ScaleY * cosine);
    }
}
