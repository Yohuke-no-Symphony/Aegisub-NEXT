using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests.Reference;

public sealed class AssThreeDimensionalReferenceTests
{
    [LibassReferenceTheory]
    [InlineData(40, 0, 0)]
    [InlineData(-40, 0, 0)]
    [InlineData(0, 40, 0)]
    [InlineData(0, -40, 0)]
    [InlineData(0, 0, 30)]
    [InlineData(35, -25, 40)]
    [InlineData(-20, 45, -30)]
    public void HandwrittenDrawingAxesAndMixedRotationOrderMatchTheNativePlane(double x, double y, double z)
    {
        var document = RectangleDocument(x, y, z, SceneProjection.DEFAULT_DISTANCE);
        var source = DrawingScript(x, y, z, 360);
        using var renderer = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();
        var actual = ReferenceSubtitleProject.Render(renderer, document, 1000);
        var expected = reference.Render(source, 1000);

        AssertFrames(actual, expected);
        var bounds = expected.InkBounds();
        if (x != 0 && y == 0 && z == 0)
        {
            Assert.Equal(Math.Sign(-x), Math.Sign(bounds.Top + bounds.Bottom - 360));
        }
        if (y != 0 && x == 0 && z == 0)
        {
            Assert.Equal(Math.Sign(y), Math.Sign(bounds.Left + bounds.Right - 640));
        }
    }

    [LibassReferenceTheory]
    [InlineData(360, 0, true, 312.5)]
    [InlineData(180, 0, true, 312.5)]
    [InlineData(720, 0, true, 312.5)]
    [InlineData(360, 180, true, 625)]
    [InlineData(360, 720, true, 156.25)]
    [InlineData(180, 0, false, 625)]
    [InlineData(720, 0, false, 156.25)]
    public void CameraDistanceUsesLayoutBeforeStorageAndFallsBackToPlayResolution(int playHeight, int layoutHeight,
        bool useStorageResolution, double distance)
    {
        var document = RectangleDocument(35, 40, 15, distance);
        var source = DrawingScript(35, 40, 15, playHeight, layoutHeight);
        using var renderer = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer(useStorageResolution: useStorageResolution);

        AssertFrames(ReferenceSubtitleProject.Render(renderer, document, 1000), reference.Render(source, 1000));
    }

    [LibassReferenceTheory]
    [InlineData(360, 0, 312.5)]
    [InlineData(180, 0, 312.5)]
    [InlineData(720, 0, 312.5)]
    [InlineData(360, 180, 625)]
    [InlineData(360, 720, 156.25)]
    public void ImportedDrawingKeepsTheExternalCameraAndScaledSourcePlane(int playHeight, int layoutHeight, double distance)
    {
        var source = DrawingScript(35, 40, 15, playHeight, layoutHeight);
        var imported = AssSubtitleFormat.Parse(source, 640, 360);
        Assert.Equal(distance, Assert.Single(imported.Layers).Transform.PerspectiveDistance);
        var document = ProjectEditingOperations.ImportSubtitleLines(new() { Width = 640, Height = 360 }, imported, "3D camera");
        using var renderer = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();

        AssertFrames(ReferenceSubtitleProject.Render(renderer, document, 1000), reference.Render(source, 1000));
    }

    [LibassReferenceTheory]
    [InlineData(20, -15, 22)]
    [InlineData(-30, 10, -18)]
    public void ExportedBezierDrawingRetainsOriginalCurveWithRotationAndNativeOrigin(double x, double y, double z)
    {
        var curve = new PathGeometry(new(0, 20),
        [
            new(new(10, -10), new(70, -10), new(80, 20)),
            new(new(80, 20), new(80, 70), new(80, 70)),
            new(new(80, 70), new(0, 70), new(0, 70)),
            new(new(0, 70), new(0, 20), new(0, 20))
        ], true);
        var document = new ProjectDocument
        {
            Width = 640, Height = 360, Layers = [new()
            {
                Kind = LayerKind.SHAPE, Shape = new(ShapeKind.PATH, 80, 80, curve), Fill = new(0, 1, 0),
                Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.25, 0.75), Offset = new(7, -11) },
                Transform = new()
                {
                    Position = new(13, 5), Pivot = new(4, -3), Scale = new(1.2, 0.8),
                    RotationX = x, RotationY = y, Rotation = z
                }
            }]
        };
        var exported = AssSubtitleFormat.Write(document);
        Assert.Contains(@"\org(340,174)", exported.Text, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"\\frx{-x}"), exported.Text, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"\\fry{-y}"), exported.Text, StringComparison.Ordinal);
        Assert.Contains(" b 10 -10 70 -10 80 20", exported.Text, StringComparison.Ordinal);
        using var renderer = ReferenceSubtitleProject.Renderer();
        using var reference = new LibassReferenceRenderer();

        AssertFrames(ReferenceSubtitleProject.Render(renderer, document, 1000), reference.Render(exported.Text, 1000), 2, 0.025);
    }

    private static ProjectDocument RectangleDocument(double x, double y, double z, double distance) => new()
    {
        Width = 640, Height = 360, Layers = [new()
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80), Fill = new(0, 1, 0),
            Transform = new()
            {
                Position = new(320, 180), Pivot = new(50, 40), RotationX = x, RotationY = y,
                Rotation = z, PerspectiveDistance = distance
            }
        }]
    };

    private static string DrawingScript(double x, double y, double z, int playHeight, int layoutHeight = 0)
    {
        var scale = (double)playHeight / 360;
        var tags = FormattableString.Invariant($"{{\\an5\\pos({320 * scale},{180 * scale})\\org({320 * scale},{180 * scale})\\frx{-x}\\fry{-y}\\frz{-z}\\p1}}");
        var drawing = string.Create(CultureInfo.InvariantCulture, $"m 0 0 l {100 * scale} 0 {100 * scale} {80 * scale} 0 {80 * scale}");
        var source = ReferenceSubtitleProject.Script(tags + drawing, playWidth: (int)(640 * scale), playHeight: playHeight);
        return layoutHeight == 0 ? source : source.Replace("[Script Info]",
            FormattableString.Invariant($"[Script Info]\nLayoutResX: {layoutHeight * 16 / 9}\nLayoutResY: {layoutHeight}"), StringComparison.Ordinal);
    }

    private static void AssertFrames(SubtitleReferenceFrame actual, SubtitleReferenceFrame expected, int pixelTolerance = 1,
        double energyTolerance = 0.02)
    {
        Assert.True(actual.Energy(3) > 0);
        Assert.True(expected.Energy(3) > 0);
        var first = actual.InkBounds();
        var second = expected.InkBounds();
        Assert.InRange(Math.Abs(first.Left - second.Left), 0, pixelTolerance);
        Assert.InRange(Math.Abs(first.Top - second.Top), 0, pixelTolerance);
        Assert.InRange(Math.Abs(first.Right - second.Right), 0, pixelTolerance);
        Assert.InRange(Math.Abs(first.Bottom - second.Bottom), 0, pixelTolerance);
        Assert.InRange(Math.Abs(actual.Energy(3) / expected.Energy(3) - 1), 0, energyTolerance);
    }
}
