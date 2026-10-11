using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class ImageClipRenderingTests
{
    [Fact]
    public void PreviewCanIsolateAnUnavailableImageWhileExportRemainsStrict()
    {
        var imageDocument = Document();
        var track = new ProjectTrack();
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, TrackId = track.Id,
            Shape = new(ShapeKind.RECTANGLE, 4, 2), Fill = new(0, 1, 0) };
        var document = imageDocument with { Tracks = [ProjectTrack.Default, track], Layers = [shape, imageDocument.Layers[0]] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory),
            skipUnavailableImagesInPreview: true);
        var preview = renderer.ComposePreview(document, new(1), new byte[32], 4, 2, 16);
        Assert.Equal(255, preview[1]);
        var warning = Assert.Single(renderer.PreviewImageFailures).Value;
        Assert.Equal(document.Assets[0].Id, warning.AssetId);
        var error = Assert.Throws<ProjectImageRenderException>(() => renderer.CopyCachedFramePixels(document, new(1), new float[32]));
        Assert.Equal(document.Assets[0].Id, error.AssetId);
        Assert.Contains("test.gif", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportFrameCacheUpdatesAtFrameBoundariesAndReusesWithinAFrame()
    {
        var document = Document();
        using var renderer = new ProjectSceneRenderer(new MemoryImageAssetResolver(ImageTestData.Gif()));
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.Equal(1, pixels[0]);
        Assert.False(renderer.CopyCachedFramePixels(document, new(21, 20), pixels));
        Assert.True(renderer.CopyCachedFramePixels(document, new(11, 10), pixels));
        Assert.Equal(1, pixels[1]);
        Assert.True(renderer.CopyCachedFramePixels(document, new(13, 10), pixels));
        Assert.Equal(1, pixels[0]);
        Assert.Equal(1, pixels[2 * 4 + 2]);
        Assert.False(renderer.CopyCachedFramePixels(document, new(7, 5), pixels));
        Assert.True(renderer.CopyCachedFramePixels(document, new(11, 10), pixels));
        Assert.Equal(1, pixels[1]);
        Assert.Equal(3UL, renderer.DecodedImageFrameCount);
    }

    [Fact]
    public void PreviewFrameCacheAdvancesWithoutGenericAnimationAndIgnoresAnimationOffset()
    {
        var document = Document();
        document = document with { Layers = [document.Layers[0] with { AnimationOffset = new(4, 5) }] };
        using var renderer = new ProjectSceneRenderer(new MemoryImageAssetResolver(ImageTestData.Gif()));
        var background = new byte[4 * 2 * 4];
        var red = renderer.ComposePreview(document, new(1), background, 4, 2, 16);
        var green = renderer.ComposePreview(document, new(11, 10), background, 4, 2, 16);
        Assert.Equal(255, red[2]);
        Assert.Equal(0, red[1]);
        Assert.Equal(255, green[1]);
        Assert.Equal(0, green[2]);
    }

    [Fact]
    public void SpeedAndSourceOffsetAreAppliedWhileLegacyImagesRemainStatic()
    {
        var document = Document();
        var layer = document.Layers[0];
        using var renderer = new ProjectSceneRenderer(new MemoryImageAssetResolver(ImageTestData.Gif()));
        var pixels = new float[document.Width * document.Height * 4];
        var accelerated = document with
        {
            Layers = [layer with { Image = layer.Image! with { Playback = new() { Speed = 2, SourceOffset = new(1, 10) } } }]
        };
        renderer.CopyCachedFramePixels(accelerated, new(11, 10), pixels);
        Assert.Equal(1, pixels[2 * 4 + 2]);
        var legacy = document with { Layers = [layer with { Image = layer.Image! with { Playback = null } }] };
        renderer.CopyCachedFramePixels(legacy, new(11, 10), pixels);
        Assert.Equal(1, pixels[0]);
        Assert.Equal(0, pixels[1]);
        Assert.False(renderer.CopyCachedFramePixels(legacy, new(3, 2), pixels));
    }

    [Fact]
    public void UnrelatedSnapshotsKeepDecodedImageFrames()
    {
        var resolver = new MemoryImageAssetResolver(ImageTestData.Gif());
        var document = Document();
        using var renderer = new ProjectSceneRenderer(resolver);
        using var original = renderer.Render(document, new(1));
        using var updated = renderer.Render(document with { ReferenceWhiteNits = 100 }, new(1));
        Assert.Equal(1, resolver.OpenCount);
        Assert.Equal(1UL, renderer.DecodedImageFrameCount);
    }

    [Fact]
    public void SourceCropSelectsOnlyTheSpecifiedNormalizedRectangle()
    {
        var document = Document();
        var layer = document.Layers[0];
        document = document with
        {
            Width = 8, Height = 8,
            Layers = [layer with
            {
                Image = layer.Image! with { Width = 8, Height = 8, Playback = null, Crop = new() { X = 0.5, Width = 0.5 } }
            }]
        };
        using var renderer = new ProjectSceneRenderer(new MemoryImageAssetResolver(ImageTestData.Png(SKColors.Red, SKColors.Blue)));
        using var surface = renderer.Render(document, new(1));
        var pixels = new float[8 * 8 * 4];
        surface.CopyPixels(pixels);
        Assert.Equal(0, pixels[(4 * 8 + 4) * 4]);
        Assert.Equal(1, pixels[(4 * 8 + 4) * 4 + 2]);
    }

    private static ProjectDocument Document()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "sources/test.gif");
        return new()
        {
            Width = 4, Height = 2, Assets = [asset],
            Layers = [new()
            {
                Kind = LayerKind.IMAGE, Start = new(1), End = new(10),
                Image = new(asset.Id, 4, 2, ImageFit.STRETCH) { Playback = new() }
            }]
        };
    }
}
