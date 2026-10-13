using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Projects;
using Avalonia;
using SkiaSharp;

namespace AegiNext.Desktop.Tests;

public sealed class CanvasSpatialGeometryTests
{
    [Fact]
    public void VirtualRingUsesNativeProjectionAndRejectsTheCameraBackPlane()
    {
        var geometry = new ProjectLayerGeometry(new(0, 0, 100, 50), default, new(100, 80),
            SKMatrix.Identity, SKMatrix.Identity);
        var transform = new LayerTransform { RotationX = 45, PerspectiveDistance = 100 };

        Assert.True(CanvasSpatialGeometry.TryProjectVector(new(0, 50, 0), transform, geometry, Matrix.Identity, out var point));
        var component = 50 / Math.Sqrt(2);
        Assert.Equal(100, point.X, 5);
        Assert.Equal(80 + component / (1 + component / 100), point.Y, 5);
        Assert.False(CanvasSpatialGeometry.TryProjectVector(new(0, -200, 0), transform with { RotationX = 90 },
            geometry, Matrix.Identity, out _));
    }

    [Fact]
    public void RotationDragAccumulatesAcrossTheWrapBoundaryAndRetainsMultipleTurns()
    {
        var accumulated = CanvasSpatialGeometry.AccumulateAngle(350, 170, -170);
        accumulated = CanvasSpatialGeometry.AccumulateAngle(accumulated, -170, -150);
        var value = CanvasSpatialGeometry.DragValue(AnimationProperty.ROTATION, 720, default, default, accumulated, false);

        Assert.Equal(390, accumulated);
        Assert.Equal(1110, value);
    }

    [Fact]
    public void AxisDragUsesTheFrozenValueAndShiftSnapsToFifteenDegrees()
    {
        Assert.Equal(750, CanvasSpatialGeometry.DragValue(AnimationProperty.ROTATION_X, 721,
            new(10, 90), new(10, 61), 0, true));
        Assert.Equal(-5, CanvasSpatialGeometry.DragValue(AnimationProperty.ROTATION_Y, -20,
            new(10, 90), new(25, 90), 0, false));
    }

    [Fact]
    public void DistanceDragIsPositiveContinuousAndClampedToTheNativeBudget()
    {
        var value = CanvasSpatialGeometry.DragValue(AnimationProperty.PERSPECTIVE_DISTANCE, 312.5,
            default, new(120, 0), 0, false);

        Assert.Equal(312.5 * Math.E, value, 10);
        Assert.Equal(SceneProjection.MIN_DISTANCE, CanvasSpatialGeometry.DragValue(AnimationProperty.PERSPECTIVE_DISTANCE,
            0.001, default, new(-1e9, 0), 0, false));
        Assert.Equal(SceneProjection.MAX_DISTANCE, CanvasSpatialGeometry.DragValue(AnimationProperty.PERSPECTIVE_DISTANCE,
            1e9, default, new(1e9, 0), 0, false));
    }
}
