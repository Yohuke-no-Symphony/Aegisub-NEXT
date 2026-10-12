using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class ProjectSceneRendererTests
{
    [Fact]
    public void ClipOpacityPreservesLinearHdrValuesAndHalfOpenTime()
    {
        var child = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 4, 4), Fill = new(4, 2, 0), End = new(2) };
        var document = new ProjectDocument { Width = 8, Height = 8, Layers = [child with { Opacity = 0.5 }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var surface = renderer.Render(document, new(1));
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        Assert.InRange((float)pixels[0], 1.99f, 2.01f);
        Assert.InRange((float)pixels[1], 0.99f, 1.01f);
        Assert.InRange((float)pixels[3], 0.499f, 0.501f);
        renderer.RenderInto(document, new(2), surface);
        surface.CopyPixels(pixels);
        Assert.All(pixels, value => Assert.Equal((Half)0, value));
    }

    [Fact]
    public void GaussianBlurPreservesHdrInteriorAndSpreadsPremultipliedEdges()
    {
        var document = new ProjectDocument { Width = 32, Height = 32, Layers = [new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 16, 16), Transform = new(X: 8, Y: 8),
            Fill = new(4, 2, 0), Blur = 1
        }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        Assert.InRange((float)pixels[(16 * 32 + 16) * 4], 3.99f, 4.01f);
        var edge = (16 * 32 + 7) * 4;
        Assert.InRange((float)pixels[edge + 3], 0.05f, 0.49f);
        Assert.InRange((float)pixels[edge] / (float)pixels[edge + 3], 3.9f, 4.1f);
    }

    [Fact]
    public void LocalTransformAppliesToShapeWithoutSubtitleMasking()
    {
        var document = new ProjectDocument { Width = 8, Height = 8, Layers = [new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 4, 4), Transform = new(X: 2), Fill = new(1, 0, 0) }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        Assert.Equal((Half)0, pixels[3]);
        Assert.Equal((Half)1, pixels[(1 * 8 + 2) * 4]);
        Assert.Equal((Half)1, pixels[(1 * 8 + 5) * 4 + 3]);
        Assert.Equal((Half)0, pixels[(1 * 8 + 6) * 4 + 3]);
    }

    [Fact]
    public void AddBlendPreservesExtendedLinearHighlights()
    {
        var first = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 2, 2), Fill = new(2, 0, 0) };
        var frontTrack = new ProjectTrack();
        var second = first with { Id = Guid.NewGuid(), TrackId = frontTrack.Id, Fill = new(3, 0, 0), Blend = BlendMode.ADD };
        var document = new ProjectDocument { Width = 2, Height = 2, Tracks = [frontTrack, ProjectTrack.Default], Layers = [first, second] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        Assert.Equal((Half)5, pixels[0]);
    }

    [Theory]
    [InlineData(BlendMode.MULTIPLY, 6)]
    [InlineData(BlendMode.DARKEN, 2)]
    [InlineData(BlendMode.LIGHTEN, 3)]
    [InlineData(BlendMode.DIFFERENCE, 1)]
    public void ArtisticBlendsKeepExtendedLinearInput(BlendMode mode, float expected)
    {
        var first = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 2, 2), Fill = new(2, 0, 0) };
        var frontTrack = new ProjectTrack();
        var document = new ProjectDocument { Width = 2, Height = 2, Tracks = [frontTrack, ProjectTrack.Default], Layers = [first, first with { Id = Guid.NewGuid(), TrackId = frontTrack.Id, Fill = new(3, 0, 0), Blend = mode }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        Assert.Equal((Half)expected, pixels[0]);
    }

    [Fact]
    public void PreviewCacheStillUsesCurrentVideoTimeAndProjectSnapshot()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 2, 2), Fill = new(1, 0, 0), End = new(1) };
        var document = new ProjectDocument { Width = 2, Height = 2, Layers = [layer] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var background = new byte[16];
        for (var i = 3; i < background.Length; i += 4)
        {
            background[i] = 255;
        }

        var first = renderer.ComposePreview(document, new(1, 2), background, 2, 2, 8);
        Assert.Equal(255, first[2]);
        background[0] = 255;
        var expired = renderer.ComposePreview(document, new(2), background, 2, 2, 8);
        Assert.Equal(255, expired[0]);
        Assert.Equal(0, expired[2]);
        var updated = document with { Layers = [layer with { Fill = new(0, 1, 0) }] };
        var changed = renderer.ComposePreview(updated, new(1, 2), background, 2, 2, 8);
        Assert.Equal(255, changed[1]);
        Assert.Equal(0, changed[2]);
    }

    [Theory]
    [InlineData(100, 98)]
    [InlineData(203, 137)]
    [InlineData(406, 188)]
    public void PreviewRescalesProjectWhiteButKeepsBackgroundUnchanged(double referenceWhite, byte expected)
    {
        var document = new ProjectDocument { Width = 2, Height = 1, ReferenceWhiteNits = referenceWhite,
            Layers = [new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Fill = new(0.25, 0.25, 0.25) }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var result = renderer.ComposePreview(document, MediaTime.Zero, new byte[] { 128, 128, 128, 255, 64, 96, 128, 255 }, 2, 1, 8);
        Assert.InRange(Math.Abs(result[0] - expected), 0, 1);
        Assert.Equal(result[0], result[1]);
        Assert.Equal(result[0], result[2]);
        Assert.Equal(255, result[3]);
        Assert.Equal(new byte[] { 64, 96, 128, 255 }, result[4..]);
    }

    [Fact]
    public void PreviewReferenceWhiteCacheTracksSnapshotAndSizeChanges()
    {
        var document = new ProjectDocument { Width = 1, Height = 1, ReferenceWhiteNits = 100,
            Layers = [new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Fill = new(0.25, 0.25, 0.25) }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var background = new byte[] { 0, 0, 0, 255 };
        Assert.InRange(renderer.ComposePreview(document, MediaTime.Zero, background, 1, 1, 4)[0], (byte)97, (byte)99);
        Assert.InRange(renderer.ComposePreview(document with { ReferenceWhiteNits = 203 }, MediaTime.Zero, background, 1, 1, 4)[0], (byte)136, (byte)138);
        var bright = document with { ReferenceWhiteNits = 406 };
        Assert.InRange(renderer.ComposePreview(bright, MediaTime.Zero, background, 1, 1, 4)[0], (byte)187, (byte)189);
        var resized = renderer.ComposePreview(bright, MediaTime.Zero, new byte[] { 0, 0, 0, 255, 0, 0, 0, 255 }, 2, 1, 8);
        Assert.InRange(resized[0], (byte)187, (byte)189);
        Assert.Equal(resized[0], resized[4]);
    }

    [Fact]
    public void PreviewWhiteScalingPreservesPremultipliedHdrEdgesAndExportSurface()
    {
        var document = new ProjectDocument { Width = 1, Height = 1, ReferenceWhiteNits = 100,
            Layers = [new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Fill = new(4, 4, 4, 0.1) }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var result = renderer.ComposePreview(document, MediaTime.Zero, new byte[] { 0, 0, 0, 255 }, 1, 1, 4);
        Assert.InRange(result[0], (byte)122, (byte)124);
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[4];
        surface.CopyPixels(pixels);
        Assert.Equal(100, surface.Info.ReferenceWhiteNits);
        Assert.InRange((float)pixels[0], 0.399f, 0.401f);
        Assert.InRange((float)pixels[3], 0.0999f, 0.1001f);
    }

    [Fact]
    public void PreviewCompositesInLinearLight()
    {
        var document = new ProjectDocument { Width = 2, Height = 2, Layers = [new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 2, 2), Fill = new(1, 1, 1, 0.5) }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var background = new byte[16];
        for (var i = 3; i < background.Length; i += 4)
        {
            background[i] = 255;
        }

        var output = renderer.ComposePreview(document, MediaTime.Zero, background, 2, 2, 8);
        Assert.InRange(output[0], (byte)186, (byte)189);
        Assert.Equal(255, output[3]);
    }

    [Fact]
    public void MultilineSubtitleAndKaraokeReuseTheSameShapedLayout()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var style = new SubtitleStyle
        {
            Scaling = new() { ReferenceWidth = 128, ReferenceHeight = 96 },
            FontAssetId = font.Id, FontSize = 22, Alignment = TextAlignment.TOP_LEFT, Margins = new(2, 2, 2),
            StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        };
        var subtitle = new SubtitleLine { Text = "ABC\nDEF", Style = style, Karaoke = [new(0, 3, MediaTime.Zero, new(1), new(1, 0, 0))] };
        var document = new ProjectDocument { Width = 128, Height = 96, Assets = [font], Subtitles = [subtitle], Layers = [new ProjectLayer { Id = subtitle.Id, Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var surface = renderer.Render(document, new(1));
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        var red = false;
        var white = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            red |= (float)pixels[i] > 0.5f && (float)pixels[i + 1] < 0.01f;
            white |= (float)pixels[i] > 0.5f && (float)pixels[i + 1] > 0.5f;
        }

        Assert.True(red);
        Assert.True(white);
    }
}
