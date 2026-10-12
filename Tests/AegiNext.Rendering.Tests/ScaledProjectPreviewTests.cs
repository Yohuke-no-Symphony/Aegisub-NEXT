using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class ScaledProjectPreviewTests
{
    [Theory]
    [InlineData(160, 90)]
    [InlineData(320, 240)]
    public void NaturalSubtitleAlignmentAndGlyphBoundsScaleWithTheProjectViewport(int width, int height)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var cue = new SubtitleLine
        {
            Text = "Natural\nposition",
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 640, ReferenceHeight = 360 },
                FontAssetId = font.Id, FontSize = 44, Alignment = TextAlignment.BOTTOM_RIGHT,
                Margins = new(20, 20, 20), StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, End = cue.End, Transform = new(-19, -11)
        };
        var document = new ProjectDocument
        {
            Width = 640, Height = 360, Assets = [font], Subtitles = [cue], Layers = [layer]
        };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var original = renderer.Render(document, MediaTime.Zero);
        var source = CopyPixels(original);
        var originalInk = InkBounds(document.Width, document.Height, pixel => (float)source[pixel * 4 + 3] >= 0.25f);
        var preview = renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, width, height);
        var previewInk = InkBounds(width, height, pixel => preview[pixel * 4 + 2] >= 128);
        var scale = Math.Min((double)width / document.Width, (double)height / document.Height);
        var left = (width - document.Width * scale) / 2;
        var top = (height - document.Height * scale) / 2;

        Assert.Null(cue.Style.Position);
        Assert.True(originalInk.Left > document.Width / 2);
        Assert.True(originalInk.Top > document.Height / 2);
        Assert.InRange(Math.Abs(previewInk.Left - (left + originalInk.Left * scale)), 0, 2);
        Assert.InRange(Math.Abs(previewInk.Top - (top + originalInk.Top * scale)), 0, 2);
        Assert.InRange(Math.Abs(previewInk.Right - (left + originalInk.Right * scale)), 0, 2);
        Assert.InRange(Math.Abs(previewInk.Bottom - (top + originalInk.Bottom * scale)), 0, 2);
        Assert.True(previewInk.Right - previewInk.Left > 20);
        Assert.True(previewInk.Bottom - previewInk.Top > 10);
    }

    [Fact]
    public void ReducedPreviewKeepsTheBlurEdgeWidthProportionalToTheFullResolutionScene()
    {
        var document = BlurDocument(SceneColor.White);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var original = renderer.Render(document, MediaTime.Zero);
        var source = CopyPixels(original);
        var preview = renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 256, 128);
        var offsets = new[] { -24, -16, -8, -4, 0, 4, 8, 16, 24 };

        foreach (var offset in offsets)
        {
            var originalX = 192 + offset;
            var expected = ((float)source[(128 * 512 + originalX) * 4 + 3] +
                (float)source[(128 * 512 + originalX + 1) * 4 + 3]) / 2;
            var actual = SrgbToLinear(preview[(64 * 256 + originalX / 2) * 4 + 2]);
            Assert.InRange(Math.Abs(actual - expected), 0, 0.06);
        }

        Assert.InRange(SrgbToLinear(preview[(64 * 256 + 88) * 4 + 2]), 0.005, 0.1);
        Assert.InRange(SrgbToLinear(preview[(64 * 256 + 108) * 4 + 2]), 0.95, 1);
    }

    [Fact]
    public void PreviewResizingLeavesFullResolutionHdrExportPixelsAndReferenceWhiteUnchanged()
    {
        var document = BlurDocument(new(4, 2, 0.5)) with { ReferenceWhiteNits = 406 };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        using var before = renderer.Render(document, MediaTime.Zero);
        var original = CopyPixels(before);

        renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 128, 64);
        renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 256, 128);
        renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 800, 400);
        using var after = renderer.Render(document, MediaTime.Zero);
        var exported = CopyPixels(after);

        Assert.Equal(512, after.Info.Width);
        Assert.Equal(256, after.Info.Height);
        Assert.Equal(406, after.Info.ReferenceWhiteNits);
        Assert.Equal(original, exported);
        Assert.InRange((float)exported[(128 * 512 + 256) * 4], 3.9f, 4.01f);
    }

    [Fact]
    public void CancellationRejectsEvenAWarmedPreviewAndLeavesItsNextSuccessfulResultUnchanged()
    {
        var document = new ProjectDocument
        {
            Width = 64, Height = 32,
            Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 64, 32), Fill = new(1, 0, 0) }]
        };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var expected = renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 32, 16);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 32, 16, cancellation.Token));

        Assert.Equal(expected, renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 32, 16));
    }

    [Fact]
    public void CancelledResourceStageCannotPoisonThePreviouslySuccessfulSceneAtAnotherTime()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var cue = new SubtitleLine
        {
            Start = new(1), End = new(2), Text = "New target",
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 128, ReferenceHeight = 64 },
                FontAssetId = font.Id, FontSize = 22, Alignment = TextAlignment.TOP_LEFT,
                Margins = new(2, 2, 2), Fill = new(0, 1, 0), StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        var document = new ProjectDocument
        {
            Width = 128, Height = 64, Assets = [font], Subtitles = [cue],
            Layers =
            [
                new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 128, 64), Fill = new(1, 0, 0), End = new(1) },
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End }
            ]
        };
        using var cancellation = new CancellationTokenSource();
        var resolver = new CancellingProjectAssetResolver(new DirectoryProjectAssetResolver(AppContext.BaseDirectory), cancellation);
        using var renderer = new ProjectSceneRenderer(resolver);
        var expected = renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 64, 32);
        Assert.Equal(255, expected[2]);

        Assert.Throws<OperationCanceledException>(() =>
            renderer.ComposePreview(document, new(1), BlackVideo(), 1, 1, 4, 64, 32, cancellation.Token));
        Assert.Equal(1, resolver.OpenCount);

        var recovered = renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 64, 32);
        Assert.Equal(expected, recovered);
        var next = renderer.ComposePreview(document, new(1), BlackVideo(), 1, 1, 4, 64, 32);
        Assert.Contains(Enumerable.Range(0, next.Length / 4), pixel => next[pixel * 4 + 1] > 128);
    }

    private static ProjectDocument BlurDocument(SceneColor fill)
    {
        return new()
        {
            Width = 512, Height = 256,
            Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 128, 64), Transform = new(192, 96), Fill = fill, Blur = 8 }]
        };
    }

    private static Half[] CopyPixels(LinearRenderSurface surface)
    {
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static byte[] BlackVideo() => [0, 0, 0, 255];

    private static double SrgbToLinear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static (int Left, int Top, int Right, int Bottom) InkBounds(int width, int height, Func<int, bool> hasInk)
    {
        var left = width;
        var top = height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (hasInk(y * width + x))
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }
        Assert.True(right >= left && bottom >= top, "The rendered subtitle must contain visible glyph ink.");
        return (left, top, right, bottom);
    }
}
