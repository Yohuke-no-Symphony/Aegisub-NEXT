using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class LayerPositionTests
{
    [Fact]
    public void GenericAndSubtitlePositionsShareGeometryWithoutChangingTheirDefaults()
    {
        var graphic = new LayerPosition();
        var subtitle = new SubtitlePosition();

        Assert.Equal(default, LayerPositionGeometry.BasePosition(graphic, new(1920, 1080)));
        Assert.Equal(default, LayerPositionGeometry.LocalPivot(graphic, new(), new(200, 100)));
        Assert.Equal(new ScenePoint(960, 1040), LayerPositionGeometry.BasePosition(subtitle, new(1920, 1080)));
        Assert.Equal(new ScenePoint(130, 110), LayerPositionGeometry.LocalPivot(subtitle, new(30, 10), new(200, 100)));
    }

    [Theory]
    [InlineData(1, 1, 0, 100, 25)]
    [InlineData(2, 3, 90, -75, 200)]
    [InlineData(-2, 3, 180, 200, -75)]
    public void PivotCompensationUsesTheEvaluatedScaleAndRotation(double scaleX, double scaleY,
        double rotation, double expectedX, double expectedY)
    {
        var actual = LayerPositionGeometry.PivotDisplacement(new(0, 0.25), new(0.5, 0.5), new(200, 100),
            new(ScaleX: scaleX, ScaleY: scaleY, Rotation: rotation));

        Assert.Equal(expectedX, actual.X, 8);
        Assert.Equal(expectedY, actual.Y, 8);
    }

    [Fact]
    public void GraphicAxesUseLogicalSizeIndependentlyOfStrokeAndPathInk()
    {
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.LINE, 200, 100), StrokeWidth = 50,
            Position = new() { Pivot = new(0.5, 0.5) }
        };

        Assert.Equal(new ScenePoint(200, 100), LayerPositionGeometry.GraphicSize(shape));
        Assert.Equal(new ScenePoint(100, 50), LayerPositionGeometry.LocalPivot(shape.Position, new(),
            LayerPositionGeometry.GraphicSize(shape)));
        Assert.Throws<ArgumentException>(() => LayerPositionGeometry.GraphicSize(new()));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void GraphicAnchorAndPivotRejectNonFiniteOrUnnormalizedValues(double value)
    {
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 20, 10) };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Layers = [shape with { Position = new() { Anchor = new(value, 0) } }]
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Layers = [shape with { Position = new() { Pivot = new(0, value) } }]
        }));
    }

    [Fact]
    public void SubtitleLayerCannotDeclareASecondPositionOutsideItsStyle()
    {
        var subtitle = new SubtitleLine { Text = "Text" };
        var layer = new ProjectLayer { SubtitleId = subtitle.Id, Position = new() };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Subtitles = [subtitle], Layers = [layer]
        }));
    }
}
