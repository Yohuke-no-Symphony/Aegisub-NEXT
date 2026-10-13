using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Projects;
using Avalonia;

namespace AegiNext.Desktop.Tests;

public sealed class ProjectiveGraphicCanvasGeometryTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void SpatialResizePreservesTheOppositeCornerAndBasePosition(int handle, bool image)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "unused.png");
        var layer = new ProjectLayer
        {
            Kind = image ? LayerKind.IMAGE : LayerKind.SHAPE,
            Shape = image ? null : new(ShapeKind.RECTANGLE, 100, 50),
            Image = image ? new(asset.Id, 100, 50) : null,
            Position = new() { Anchor = new(0.25, 0.5), Pivot = new(0.3, 0.7), Offset = new(10, -20) },
            Transform = new(10, 20, 1.2, 0.8, 15, 3, -4)
            {
                RotationX = 10, RotationY = -15, PerspectiveDistance = 220, ProjectionTranslation = new(5, -7, 11)
            }
        };
        var evaluated = layer.Transform with { X = 80, Y = 90, ScaleX = 1.5, ScaleY = 0.7, Rotation = 55, RotationX = 32, RotationY = -24 };
        var document = new ProjectDocument
        {
            Width = 800, Height = 600, Assets = image ? [asset] : [], Layers = [layer with { Transform = evaluated }]
        };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(Path.GetTempPath()));
        var geometry = renderer.GetLayerGeometry(document, new(), layer.Id)!;
        Point[] corners = [new(0, 0), new(100, 0), new(100, 50), new(0, 50)];
        var moving = corners[handle];
        var opposite = corners[(handle + 2) % 4];
        var target = opposite + new Vector((moving.X - opposite.X) * 1.7, (moving.Y - opposite.Y) * 1.4);
        var fit = Matrix.CreateScale(0.6, 0.6) * Matrix.CreateTranslation(17, 23);
        var gesture = new CanvasTransformGesture(layer, evaluated, geometry, Matrix.Identity,
            Map(geometry, moving) * fit, handle) { WorldToScreen = fit };

        var changed = GraphicCanvasGeometry.TransformLayer(gesture, Map(geometry, target) * fit, image);

        Assert.Equal(layer.Transform.Position, changed.Transform.Position);
        Assert.Equal(layer.Transform.Rotation, changed.Transform.Rotation);
        Assert.Equal(layer.Transform.RotationX, changed.Transform.RotationX);
        Assert.Equal(layer.Transform.RotationY, changed.Transform.RotationY);
        Assert.Same(layer.Position, changed.Position);
        var pose = evaluated with
        {
            Scale = new(evaluated.ScaleX + changed.Transform.ScaleX - layer.Transform.ScaleX,
                evaluated.ScaleY + changed.Transform.ScaleY - layer.Transform.ScaleY),
            ProjectionTranslation = changed.Transform.ProjectionTranslation
        };
        var resized = changed with { Transform = pose };
        var after = renderer.GetLayerGeometry(document with { Layers = [resized] }, new(), layer.Id)!;
        var oppositeAfter = image ? opposite : new(opposite.X * resized.Shape!.Width / layer.Shape!.Width,
            opposite.Y * resized.Shape.Height / layer.Shape.Height);
        var movingAfter = image ? moving : new(moving.X * resized.Shape!.Width / layer.Shape!.Width,
            moving.Y * resized.Shape.Height / layer.Shape.Height);
        AssertPointEqual(Map(geometry, opposite), Map(after, oppositeAfter));
        AssertPointEqual(Map(geometry, target), Map(after, movingAfter));
        Assert.Equal(changed, GraphicCanvasGeometry.TransformLayer(gesture, Map(geometry, target) * fit, image));
    }

    [Fact]
    public void ProjectedBezierHitUsesTheSourceCurveParameter()
    {
        var start = new ScenePoint(0, 0);
        var segment = new CubicBezierSegment(new(10, 160), new(180, -70), new(220, 100));
        var projection = SceneProjection.CreateMatrix(new() { RotationX = 45, RotationY = -30, PerspectiveDistance = 160 },
            new(80, 40), new(250, 230));
        const double PROGRESS = 0.37;
        var source = Evaluate(start, segment, PROGRESS);
        var projected = projection.Map(source);

        var hit = ProjectedBezierGeometry.FindNearest(start, segment, projection, Matrix.Identity,
            new(projected.X, projected.Y), 2);

        Assert.NotNull(hit);
        Assert.Equal(PROGRESS, hit.Value.Progress, 5);
        Assert.InRange(hit.Value.DistanceSquared, 0, 0.00001);
        var samples = ProjectedBezierGeometry.Sample(start, segment, projection, Matrix.Identity);
        Assert.True(samples.Count > 2);
        Assert.All(samples.Where(sample => sample.Position is not null), sample =>
        {
            var expected = projection.Map(Evaluate(start, segment, sample.Progress));
            Assert.Equal(expected.X, sample.Position!.Value.X, 8);
            Assert.Equal(expected.Y, sample.Position.Value.Y, 8);
        });
    }

    [Fact]
    public void BehindNearPlaneCurveHasNoVisibleSamplesOrHit()
    {
        var segment = new CubicBezierSegment(new(10, 160), new(180, -70), new(220, 100));
        var projection = new ProjectionMatrix3x3(1, 0, 0, 0, 1, 0, 0, 0, -1);

        var samples = ProjectedBezierGeometry.Sample(new(), segment, projection, Matrix.Identity);

        Assert.All(samples, sample => Assert.Null(sample.Position));
        Assert.Null(ProjectedBezierGeometry.FindNearest(new(), segment, projection, Matrix.Identity, new(10, 10), 20));
    }

    [Fact]
    public void CurveApproachingTheNearPlaneSeparatesItsVisibleRuns()
    {
        var segment = new CubicBezierSegment(new(30, 80), new(60, 80), new(100, 0));
        var projection = new ProjectionMatrix3x3(1, 0, 0, 0, 1, 0, 0, -1d / 60, 1);

        var samples = ProjectedBezierGeometry.Sample(new(), segment, projection, Matrix.Identity, new(-500, -500, 1000, 1000));

        Assert.Contains(samples, sample => sample.Position is null);
        Assert.Contains(samples, sample => sample.Position is not null);
        Assert.All(samples.Where(sample => sample.Position is not null), sample =>
        {
            Assert.True(projection.TryMap(Evaluate(new(), segment, sample.Progress), out _, SceneProjection.NEAR_PLANE_RATIO));
            Assert.True(double.IsFinite(sample.Position!.Value.X));
            Assert.True(double.IsFinite(sample.Position.Value.Y));
        });
    }

    private static ScenePoint Evaluate(ScenePoint start, CubicBezierSegment segment, double progress)
    {
        var u = 1 - progress;
        return new(u * u * u * start.X + 3 * u * u * progress * segment.Control1.X +
                   3 * u * progress * progress * segment.Control2.X + progress * progress * progress * segment.End.X,
            u * u * u * start.Y + 3 * u * u * progress * segment.Control1.Y +
            3 * u * progress * progress * segment.Control2.Y + progress * progress * progress * segment.End.Y);
    }

    private static Point Map(ProjectLayerGeometry geometry, Point local)
    {
        Assert.True(geometry.TryMapLocalPoint(new((float)local.X, (float)local.Y), out var world));
        return new(world.X, world.Y);
    }

    private static void AssertPointEqual(Point expected, Point actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, 0.001);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 0.001);
    }
}
