using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Fonts;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleFontVariantRenderingTests
{
    [MacInstalledNotoFontFact]
    public void SameDocumentAndFormattingKeepSeparateVariantFacesAndLayoutCaches()
    {
        var catalog = new SystemFontCatalog(SKFontManager.Default, ["Noto Sans"]);
        var semiBold = Assert.Single(catalog.Faces, face => face.Variant.Name == "SemiBold");
        var black = Assert.Single(catalog.Faces, face => face.Variant.Name == "Black");
        var first = Line(semiBold);
        var second = Line(black) with { Start = new(3), End = new(6) };
        var document = Document(first, second);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory), catalog);
        var semiBoldLayout = renderer.MeasureSubtitleTextLayout(document, first);
        var blackLayout = renderer.MeasureSubtitleTextLayout(document, second);
        Assert.Equal(600, Assert.Single(semiBoldLayout.Runs).ResolvedFontVariant!.Value.Weight);
        Assert.Equal(900, Assert.Single(blackLayout.Runs).ResolvedFontVariant!.Value.Weight);
        Assert.True(blackLayout.GetCaretBounds(first.Text.Length).Left > semiBoldLayout.GetCaretBounds(first.Text.Length).Left);
        var again = renderer.MeasureSubtitleTextLayout(document, first);
        Assert.Equal(semiBoldLayout.Graphemes, again.Graphemes);
        Assert.Equal(semiBoldLayout.Runs, again.Runs);
    }

    [MacInstalledNotoFontFact]
    public void InlineVariantChangesInvalidateTheGlyphFaceWithoutChangingTheFamily()
    {
        var catalog = new SystemFontCatalog(SKFontManager.Default, ["Noto Serif SC"]);
        var semiBold = Assert.Single(catalog.Faces, face => face.Variant.Name == "SemiBold");
        var black = Assert.Single(catalog.Faces, face => face.Variant.Name == "Black");
        var line = Line(black) with { Text = "WWW", InlineSpans = [new(0, 1, new() { FontVariant = semiBold.Variant })] };
        var document = Document(line);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory), catalog);
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(2, layout.Runs.Length);
        Assert.Equal(600, layout.Runs[0].ResolvedFontVariant!.Value.Weight);
        Assert.Equal(900, layout.Runs[1].ResolvedFontVariant!.Value.Weight);
        Assert.All(layout.Runs, run => Assert.Equal("Noto Serif SC", run.ResolvedFontFamily));
        Assert.Single(layout.GetSelectionRects(0, line.Text.Length));
    }

    [MacInstalledNotoFontFact]
    public void PreviewAndFullRenderKeepTheSelectedStaticOrVariableTypeface()
    {
        var catalog = new SystemFontCatalog(SKFontManager.Default, ["Noto Sans SC", "Noto Serif SC"]);
        foreach (var family in new[] { "Noto Sans SC", "Noto Serif SC" })
        {
            var pixels = new List<byte[]>();
            using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory), catalog);
            foreach (var name in new[] { "SemiBold", "Black" })
            {
                var face = Assert.Single(catalog.Faces, face => face.FamilyName == family && face.Variant.Name == name);
                var line = Line(face) with { Text = "AegiNext 字重", Style = Line(face).Style with { Fill = new(1, 1, 1) } };
                var document = new ProjectDocument
                {
                    Width = 512, Height = 160, Subtitles = [line],
                    Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
                };
                var layout = renderer.MeasureSubtitleTextLayout(document, line);
                Assert.Equal(face.Variant.Weight, Assert.Single(layout.Runs).ResolvedFontVariant!.Value.Weight);
                using var full = renderer.Render(document, MediaTime.Zero);
                using var preview = renderer.RenderSubtitlePreview(document, line, MediaTime.Zero, layout.Bounds);
                Assert.Contains(preview.CopySrgbBgra().Where((_, index) => index % 4 == 3), alpha => alpha > 0);
                pixels.Add(full.CopySrgbBgra());
            }
            Assert.False(pixels[0].SequenceEqual(pixels[1]));
        }
    }

    private static SubtitleLine Line(SystemFontFace face)
    {
        return new()
        {
            Text = "WWWW", End = new(3), Style = new()
            {
                Scaling = new() { ReferenceWidth = 512, ReferenceHeight = 160 },
                FontFamily = face.FamilyName, FontVariant = face.Variant, Bold = false, FontSize = 64,
                Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8), StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
    }

    private static ProjectDocument Document(params SubtitleLine[] lines)
    {
        return new()
        {
            Width = 512, Height = 160, Subtitles = [.. lines],
            Layers = [.. lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            })]
        };
    }
}
