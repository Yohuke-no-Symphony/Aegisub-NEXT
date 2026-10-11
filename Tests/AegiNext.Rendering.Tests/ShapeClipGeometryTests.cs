using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class ShapeClipGeometryTests
{
    [Theory]
    [InlineData(ShapeKind.ELLIPSE)]
    [InlineData(ShapeKind.ROUNDED_RECTANGLE)]
    [InlineData(ShapeKind.ARROW)]
    public void ParameterizedShapeHitTestingUsesTheVisibleContour(ShapeKind kind)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(kind, 20, 20) { CornerRadius = 10 }, Fill = SceneColor.White,
            Transform = new(X: 20, Y: 10, Rotation: 30, ScaleX: 1.5, ScaleY: 2)
        };
        var document = new ProjectDocument { Width = 100, Height = 100, Layers = [layer] };
        using var renderer = Renderer();
        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;
        Assert.True(geometry.ContainsWorldPoint(geometry.LocalToWorld.MapPoint(10, 10)));
        Assert.False(geometry.ContainsWorldPoint(geometry.LocalToWorld.MapPoint(0.5f, 0.5f)));
    }

    [Fact]
    public void OpenContourHasNoImplicitFillAndLineHasSelectableStroke()
    {
        var path = new PathGeometry(new(2, 2),
            [new(new(2, 2), new(18, 2), new(18, 2)), new(new(18, 2), new(10, 18), new(10, 18))]);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.PATH, 20, 20, path), Fill = SceneColor.White,
            Stroke = SceneColor.White, StrokeWidth = 2
        };
        var document = new ProjectDocument { Width = 24, Height = 24, Layers = [layer] };
        using var renderer = Renderer();
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new float[24 * 24 * 4];
        surface.CopyPixels(pixels);
        Assert.Equal(0, pixels[(8 * 24 + 10) * 4 + 3]);
        Assert.False(renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!.ContainsWorldPoint(new(10, 8)));

        var line = layer with { Shape = new(ShapeKind.LINE, 20, 20) };
        var lineDocument = document with { Layers = [line] };
        var geometry = renderer.GetLayerGeometry(lineDocument, MediaTime.Zero, line.Id)!;
        Assert.True(geometry.LocalBounds.Height > 0);
        Assert.True(geometry.ContainsWorldPoint(new(10, 10)));
        Assert.False(geometry.ContainsWorldPoint(new(10, 15)));
    }

    private static ProjectSceneRenderer Renderer()
    {
        return new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
    }
}
