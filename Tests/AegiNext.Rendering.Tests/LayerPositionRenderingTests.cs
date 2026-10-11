using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class LayerPositionRenderingTests
{
    [Theory]
    [InlineData(LayerKind.SHAPE)]
    [InlineData(LayerKind.IMAGE)]
    public void GraphicRotationAndPixelsUseTheSelectedNormalizedPivot(LayerKind kind)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/image.png");
        var layer = Graphic(kind, asset.Id) with
        {
            Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5) },
            Transform = new(Rotation: 90)
        };
        var document = new ProjectDocument { Width = 100, Height = 100, Assets = [asset], Layers = [layer] };
        using var renderer = Renderer();

        var geometry = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;
        AssertPoint(new(10, 5), geometry.LocalPivot);
        AssertPoint(new(50, 50), geometry.WorldPivot);
        AssertPoint(new(55, 40), geometry.LocalToWorld.MapPoint(0, 0));

        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new float[100 * 100 * 4];
        surface.CopyPixels(pixels);
        Assert.Equal(1, pixels[(50 * 100 + 50) * 4 + 3]);
        Assert.Equal(0, pixels[(50 * 100 + 60) * 4 + 3]);
        Assert.True(geometry.ContainsWorldPoint(new(50, 50)));
        Assert.False(geometry.ContainsWorldPoint(new(60, 50)));
    }

    [Theory]
    [InlineData(LayerKind.SHAPE)]
    [InlineData(LayerKind.IMAGE)]
    public void LegacyPixelPivotRemainsAdditiveAndChangingAnchorPreservesWorldGeometry(LayerKind kind)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/image.png");
        var layer = Graphic(kind, asset.Id) with { Transform = new(150, 120, -2, 3, 31, 7, 11) };
        var document = new ProjectDocument { Width = 400, Height = 300, Assets = [asset], Layers = [layer] };
        using var renderer = Renderer();
        var original = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;
        var displacement = LayerPositionGeometry.PivotDisplacement(new(), new(0.5, 0.5), new(20, 10), layer.Transform);
        var position = new LayerPosition
        {
            Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5),
            Offset = new(displacement.X - 200, displacement.Y - 150)
        };
        var changed = renderer.GetLayerGeometry(document with { Layers = [layer with { Position = position }] },
            MediaTime.Zero, layer.Id)!;

        AssertPoint(new(17, 16), changed.LocalPivot);
        for (var index = 0; index < original.WorldCorners.Count; index++)
        {
            AssertPoint(original.WorldCorners[index], changed.WorldCorners[index]);
        }
    }

    [Fact]
    public void RotationAnimationKeepsTheSharedPivotFixedAndPositionAnimationAddsItsOffset()
    {
        var layer = Graphic(LayerKind.SHAPE, Guid.Empty) with
        {
            Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5), Offset = new(3, 4) },
            Tracks =
            [
                new(AnimationProperty.ROTATION, [new(new(0), 0), new(new(2), 180)]),
                new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(0, 0)), new(new(2), new ScenePoint(20, 40))])
            ]
        };
        var document = new ProjectDocument { Width = 100, Height = 100, Layers = [layer] };
        using var renderer = Renderer();

        var start = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;
        var middle = renderer.GetLayerGeometry(document, new(1), layer.Id)!;

        AssertPoint(new(53, 54), start.WorldPivot);
        AssertPoint(new(63, 74), middle.WorldPivot);
        AssertPoint(new(68, 64), middle.LocalToWorld.MapPoint(0, 0));
        Assert.Equal(layer.Tracks, document.Layers[0].Tracks);
    }

    [Fact]
    public void StrokeChangesAndSparsePathInkDoNotChangeTheLogicalPivot()
    {
        var path = new PathGeometry(new(1, 4), [new(new(2, 4), new(5, 4), new(7, 4))]);
        var layer = Graphic(LayerKind.SHAPE, Guid.Empty) with
        {
            Shape = new(ShapeKind.PATH, 20, 10, path), StrokeWidth = 2,
            Position = new() { Pivot = new(0.5, 0.5) }, Transform = new(40, 50, Rotation: 90)
        };
        var document = new ProjectDocument { Width = 100, Height = 100, Layers = [layer] };
        using var renderer = Renderer();
        var original = renderer.GetLayerGeometry(document, MediaTime.Zero, layer.Id)!;
        var thicker = renderer.GetLayerGeometry(document with { Layers = [layer with { StrokeWidth = 20 }] },
            MediaTime.Zero, layer.Id)!;

        AssertPoint(new(10, 5), original.LocalPivot);
        AssertPoint(original.WorldPivot, thicker.WorldPivot);
        Assert.Equal(original.LocalToWorld, thicker.LocalToWorld);
    }

    private static ProjectLayer Graphic(LayerKind kind, Guid assetId)
    {
        return new()
        {
            Kind = kind,
            Shape = kind == LayerKind.SHAPE ? new(ShapeKind.RECTANGLE, 20, 10) : null,
            Image = kind == LayerKind.IMAGE ? new(assetId, 20, 10) : null
        };
    }

    private static ProjectSceneRenderer Renderer()
    {
        return new(new MemoryImageAssetResolver(ImageTestData.Png(SKColors.Red, SKColors.Red)));
    }

    private static void AssertPoint(SKPoint expected, SKPoint actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, 0.0001);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 0.0001);
    }
}
