using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleStylePreviewRenderingTests
{
    [Fact]
    public void PreviewUsesApplicationFontAndChangesRealPixelsWithoutChangingThePreset()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var font = new EmbeddedSubtitleFont("Sample.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "", new()
        {
            Scaling = new() { ReferenceWidth = 320, ReferenceHeight = 180 },
            FontFamily = "Noto Sans", FontSize = 44, Margins = new(12, 12, 12),
            StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        });
        var catalog = new SystemFontCatalog([new FontFamilyBundle("Noto Sans", [font])], SystemFontCatalog.Empty);
        var request = new SubtitleStylePreviewRequest(1, preset, "ABC\nDEF", 320, 180, catalog);

        var first = SubtitleStylePreviewRenderer.Render(request, CancellationToken.None);
        var red = SubtitleStylePreviewRenderer.Render(request with
        {
            Preset = preset with { Style = preset.Style with { Fill = new(1, 0, 0) } }
        }, CancellationToken.None);
        var shadow = SubtitleStylePreviewRenderer.Render(request with
        {
            Preset = preset with { Style = preset.Style with { ShadowColor = SceneColor.Black, ShadowOffset = new(15, 8), ShadowBlur = 4 } }
        }, CancellationToken.None);
        var differentText = SubtitleStylePreviewRenderer.Render(request with { Text = "X" }, CancellationToken.None);

        Assert.Contains(first.Pixels.ToArray().Chunk(4), pixel => pixel[0] > 100 && pixel[1] > 100 && pixel[2] > 100);
        Assert.NotEqual(first.Pixels.ToArray(), red.Pixels.ToArray());
        Assert.NotEqual(first.Pixels.ToArray(), shadow.Pixels.ToArray());
        Assert.NotEqual(first.Pixels.ToArray(), differentText.Pixels.ToArray());
        Assert.All(first.Pixels.ToArray().Chunk(4), pixel => Assert.Equal(255, pixel[3]));
        Assert.Null(preset.Style.FontAssetId);
        Assert.Equal(SceneColor.White, preset.Style.Fill);
        Assert.Same(font, Assert.Single(Assert.Single(catalog.ApplicationFonts).Files));
    }

    [Fact]
    public void EmptySampleStaysEmptyAndCancelledRenderingProducesNoFrame()
    {
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Example", new());
        var request = new SubtitleStylePreviewRequest(1, preset, "", 1920, 1080, new());
        var frame = SubtitleStylePreviewRenderer.Render(request, CancellationToken.None);

        Assert.Single(frame.Pixels.ToArray().Chunk(4).Select(pixel => Convert.ToHexString(pixel)).Distinct());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SubtitleStylePreviewRenderer.Render(request, cancellation.Token));
    }
}
