using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SceneProjectionTests
{
    [Fact]
    public void NativePositiveRotationXMovesTheBottomAwayAndCreatesPerspective()
    {
        var transform = new LayerTransform { RotationX = 45, PerspectiveDistance = 100 };
        var matrix = SceneProjection.CreateMatrix(transform, new(), new(300, 200));
        var upper = matrix.Map(new(20, -20));
        var lower = matrix.Map(new(20, 20));

        Assert.True(upper.X - 300 > lower.X - 300);
        Assert.Equal(300 + 2000 / (100 - Math.Sqrt(200)), upper.X, 10);
        Assert.Equal(200 + Math.Sqrt(200) * 100 / (100 + Math.Sqrt(200)), lower.Y, 10);
        Assert.True(matrix.TryInvert(out var inverse));
        Assert.Equal(20, inverse.Map(lower).X, 10);
        Assert.Equal(20, inverse.Map(lower).Y, 10);
    }

    [Fact]
    public void RotationsApplyZThenXThenYAndKeepMultiTurnValues()
    {
        var transform = new LayerTransform { Rotation = 450, RotationX = 90, RotationY = 90, Scale = new(2, 3) };
        var rotated = SceneProjection.RotateScaled(new(1, 0), transform);

        Assert.Equal(2, rotated.X, 10);
        Assert.Equal(0, rotated.Y, 10);
        Assert.Equal(0, rotated.Z, 10);
        Assert.Equal(450, transform.Rotation);
    }

    [Theory]
    [InlineData(23, -41, 17, 2, 0.7, 120)]
    [InlineData(-80, 60, 170, -2, 3, 800)]
    public void PivotCompensationPreservesEveryProjectedPoint(double x, double y, double z,
        double scaleX, double scaleY, double distance)
    {
        var transform = new LayerTransform
        {
            RotationX = x, RotationY = y, Rotation = z, Scale = new(scaleX, scaleY),
            PerspectiveDistance = distance, ProjectionTranslation = new(7, -9, 31)
        };
        var previous = new ScenePoint(30, 15);
        var next = new ScenePoint(50, 37);
        var delta = SceneProjection.RotateScaled(new(next.X - previous.X, next.Y - previous.Y), transform);
        var adjusted = transform with { ProjectionTranslation = transform.ProjectionTranslation + delta };
        var before = SceneProjection.CreateMatrix(transform, previous, new(310, 240));
        var after = SceneProjection.CreateMatrix(adjusted, next, new(310, 240));

        foreach (var point in new ScenePoint[] { new(0, 0), new(20, 15), new(50, 30), new(100, 60) })
        {
            Assert.Equal(before.Map(point).X, after.Map(point).X, 9);
            Assert.Equal(before.Map(point).Y, after.Map(point).Y, 9);
            Assert.Equal(before.W(point), after.W(point), 10);
        }
    }

    [Fact]
    public void NearPlaneClipsCrossingPolygonsAndRejectsTheHiddenHalfPlane()
    {
        var matrix = new ProjectionMatrix3x3(1, 0, 0, 0, 1, 0, 1, 0, 1);
        var clipped = matrix.Clip([new(-2, -1), new(2, -1), new(2, 1), new(-2, 1)]);

        Assert.Equal(4, clipped.Count);
        Assert.All(clipped, point => Assert.True(matrix.W(point) >= SceneProjection.NEAR_PLANE_RATIO - 1e-12));
        Assert.False(matrix.TryMap(new(-2, 0), out _, SceneProjection.NEAR_PLANE_RATIO));
        Assert.True(matrix.TryMap(new(2, 0), out var visible, SceneProjection.NEAR_PLANE_RATIO));
        Assert.Equal(2d / 3, visible.X, 10);
        Assert.Empty(matrix.Clip([new(-4, -1), new(-2, -1), new(-2, 1), new(-4, 1)]));
    }

    [Fact]
    public void ExactRightAngleProducesASingularEdgeOnPlane()
    {
        var matrix = SceneProjection.CreateMatrix(new() { RotationX = 90 }, new(), new());

        Assert.False(matrix.TryInvert(out _));
        Assert.Equal(0, matrix.Map(new(20, 30)).Y);
    }

    [Fact]
    public void SpatialTracksEvaluateWholeLayerAndRangeIndependently()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { RotationX = 5, PerspectiveDistance = 900 };
        var line = new SubtitleLine { Text = "AB", End = new(2), AnimationRanges = [range] };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Transform = new() { RotationY = 17, ProjectionTranslation = new(7, 8, 9) },
            Tracks =
            [
                new(AnimationProperty.ROTATION_X, [new(new(0), 0), new(new(2), 720)]),
                new(new AnimationTrackTarget(AnimationProperty.ROTATION_X, TextRangeId: range.Id), [new(new(0), 5), new(new(2), 65)]),
                new(new AnimationTrackTarget(AnimationProperty.PERSPECTIVE_DISTANCE, TextRangeId: range.Id), [new(new(0), 900), new(new(2), 300)])
            ]
        };
        var evaluated = SceneEvaluator.EvaluateLayer(layer, line, new(1));

        Assert.Equal(360, evaluated.Transform.RotationX);
        Assert.Equal(17, evaluated.Transform.RotationY);
        Assert.Equal(layer.Transform.ProjectionTranslation, evaluated.Transform.ProjectionTranslation);
        Assert.Equal(35, evaluated.AnimationRanges[0].RotationX);
        Assert.Equal(600, evaluated.AnimationRanges[0].PerspectiveDistance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDistancesRejectLayersAndRanges(double distance)
    {
        var line = new SubtitleLine { Text = "AB" };
        var layer = new ProjectLayer { SubtitleId = line.Id, Transform = new() { PerspectiveDistance = distance } };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Subtitles = [line], Layers = [layer] }));
        line = line with { AnimationRanges = [new(Guid.NewGuid(), 0, 2) { PerspectiveDistance = distance }] };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Subtitles = [line], Layers = [layer with { Transform = new() }]
        }));
    }
}
