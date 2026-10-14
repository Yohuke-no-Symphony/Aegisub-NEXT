using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class ClipMaskPlacementGeometryTests
{
    [Fact]
    public void PlacementUsesCanvasAnchorLocalReferencePivotAndAnimatedTranslation()
    {
        var placement = new LayerPosition
        {
            Anchor = new(0.25, 0.75),
            Pivot = new(0.25, 0.5),
            Offset = new(15, -12)
        };
        var frame = new MaskReferenceFrame { Origin = new(20, 30), Size = new(100, 80) };
        var transform = new MaskTransform { Position = new(5, -8), Scale = new(2, 0.5), Rotation = 90 };
        var matrix = ClipMaskPlacementGeometry.CreateMatrix(placement, frame, transform, new(800, 600));

        AssertPoint(new(210, 450), matrix.Map(new(55, 90)));
        Assert.True(matrix.TryInvert(out var inverse));
        AssertPoint(new(55, 90), inverse.Map(new(210, 450)));
    }

    [Theory]
    [InlineData(2, 0.5, 90)]
    [InlineData(-2, 3, -27)]
    [InlineData(0, 2, 180)]
    public void NormalizedOutsidePivotPreservesLegacyGeometryForEveryAnimatedPose(double scaleX, double scaleY, double rotation)
    {
        var oldPivot = new ScenePoint(-120, 450);
        var canvasSize = new ScenePoint(800, 600);
        var frame = new MaskReferenceFrame { Origin = new(10, 20), Size = new(100, 80) };
        var anchor = new ScenePoint(Math.Clamp(oldPivot.X / canvasSize.X, 0, 1), Math.Clamp(oldPivot.Y / canvasSize.Y, 0, 1));
        var placement = new LayerPosition
        {
            Anchor = anchor,
            Pivot = new((oldPivot.X - frame.Origin.X) / frame.Size.X, (oldPivot.Y - frame.Origin.Y) / frame.Size.Y),
            Offset = new(oldPivot.X - anchor.X * canvasSize.X, oldPivot.Y - anchor.Y * canvasSize.Y)
        };
        foreach (var translation in new ScenePoint[] { new(0, 0), new(25, -30), new(-70, 20) })
        {
            var transform = new MaskTransform { Position = translation, Scale = new(scaleX, scaleY), Rotation = rotation };
            var matrix = ClipMaskPlacementGeometry.CreateMatrix(placement, frame, transform, canvasSize);
            foreach (var point in new ScenePoint[] { new(10, 20), new(110, 100), new(75, 65) })
            {
                var angle = rotation * Math.PI / 180;
                var x = (point.X - oldPivot.X) * scaleX;
                var y = (point.Y - oldPivot.Y) * scaleY;
                AssertPoint(new(oldPivot.X + translation.X + x * Math.Cos(angle) - y * Math.Sin(angle),
                    oldPivot.Y + translation.Y + x * Math.Sin(angle) + y * Math.Cos(angle)), matrix.Map(point));
            }
        }
    }

    [Fact]
    public void ReferenceFrameCapturesBezierExtremaRatherThanControlHull()
    {
        var mask = new VectorClipMask
        {
            Contours = [new() { Nodes = [
                new() { Position = new(0, 0), OutHandle = new(0, 100) },
                new() { Position = new(100, 0), InHandle = new(0, 100) }] }]
        };

        var frame = ClipMaskPlacementGeometry.CaptureReferenceFrame(mask);

        AssertPoint(new(0, 0), frame.Origin);
        AssertPoint(new(100, 75), frame.Size);
        var rotated = ClipMaskPlacementGeometry.GetBounds(mask, new(0, -1, 30, 1, 0, 40, 0, 0, 1));
        AssertPoint(new(-45, 40), rotated.Origin);
        AssertPoint(new(75, 100), rotated.Size);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.01, 0.001)]
    public void DegenerateAndSubpixelGeometryRetainsOriginWithOnePixelLogicalReference(double width, double height)
    {
        var mask = new RectangleClipMask { TopLeft = new(-30, 40), BottomRight = new(-30 + width, 40 + height) };

        var frame = ClipMaskPlacementGeometry.CaptureReferenceFrame(mask);
        var bounds = ClipMaskPlacementGeometry.GetBounds(mask, ProjectionMatrix3x3.Identity);

        AssertPoint(new(-30, 40), frame.Origin);
        AssertPoint(new(1, 1), frame.Size);
        AssertPoint(new(width, height), bounds.Size);
    }

    [Fact]
    public void SingularScaleStillMapsForwardAndCannotBeInverted()
    {
        var matrix = ClipMaskPlacementGeometry.CreateMatrix(new() { Anchor = new(0.5, 0.5) },
            new(), new() { Scale = new(0, -2), Position = new(10, 20) }, new(800, 600));

        AssertPoint(new(410, 280), matrix.Map(new(35, 20)));
        Assert.False(matrix.TryInvert(out _));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, -1)]
    [InlineData(double.NaN, 600)]
    public void PlacementRequiresAnExplicitFinitePositiveCanvasSize(double width, double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClipMaskPlacementGeometry.CreateMatrix(new(), new(), new(), new(width, height)));
    }

    private static void AssertPoint(ScenePoint expected, ScenePoint actual)
    {
        Assert.Equal(expected.X, actual.X, 8);
        Assert.Equal(expected.Y, actual.Y, 8);
    }
}
