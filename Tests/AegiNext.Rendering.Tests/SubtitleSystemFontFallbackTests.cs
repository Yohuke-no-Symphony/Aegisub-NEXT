using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleSystemFontFallbackTests
{
    [MacSystemFontFact]
    public void ChineseLatinAndZwjEmojiUseWholeGraphemeFallbackAndKeepLigaturesTogether()
    {
        var document = Document("A你好👩‍💻ffiB");
        var line = document.Subtitles[0];
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(StringInfo.ParseCombiningCharacters(line.Text).Length, layout.Graphemes.Length);
        Assert.Equal(4, layout.Runs.Length);
        Assert.True(layout.Runs.Select(run => run.ResolvedFontFamily).Distinct().Count() >= 3);
        Assert.All(layout.Runs, run => Assert.Equal("Arial", run.Style.FontFamily));
        var emoji = Assert.Single(layout.Graphemes, glyph => glyph.Utf16Start == 3);
        Assert.Equal(5, emoji.Utf16Length);
        var emojiRun = Assert.Single(layout.Runs, run => run.Utf16Start == 3);
        Assert.Equal(5, emojiRun.Utf16Length);
        Assert.True(emoji.Bounds.Width > 0);
        Assert.Equal(8, layout.Runs[^1].Utf16Start);
        Assert.Equal(4, layout.Runs[^1].Utf16Length);
        Assert.True(layout.GetCaretBounds(8).Left < layout.GetCaretBounds(9).Left);
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GetCaretBounds(4));
        var trailing = layout.HitTest(new(emoji.TrailingCaret.Left, emoji.TrailingCaret.MidY));
        Assert.Equal(8, trailing.Utf16Offset);
        Assert.Single(layout.GetSelectionRects(3, 5));
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = surface.CopySrgbBgra();
        Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
    }

    [MacSystemFontFact]
    public void RichStylesAndKaraokeKeepFallbackGeometryAcrossAllRenderingEntrypoints()
    {
        var document = Document("A你好👩‍💻ffiB");
        var line = document.Subtitles[0] with
        {
            InlineSpans = [new(3, 5, new() { FontSize = 44 }), new(8, 4, new() { Underline = true, Italic = true })]
        };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var before = renderer.MeasureSubtitleTextLayout(document, line);
        line = line with { Karaoke =
        [
            new(0, 3, MediaTime.Zero, new(1), new(0, 1, 0)),
            new(3, 5, new(1), new(2), new(0, 1, 0)) { HighlightKind = KaraokeHighlightKind.STEP },
            new(8, 4, new(2), new(3), new(0, 1, 0))
        ], KaraokeStyleSpans = [new(0, 3, new() { Fill = new(0, 1, 0) }),
            new(8, 4, new() { Fill = new(0, 1, 0) })] };
        document = document with { Subtitles = [line] };
        var after = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(before.Graphemes.ToArray(), after.Graphemes.ToArray());
        Assert.Equal(before.Runs.ToArray(), after.Runs.ToArray());
        Assert.Single(after.Runs.Select(run => run.Baseline.Y).Distinct());
        Assert.Equal(44, Assert.Single(after.Runs, run => run.Utf16Start == 3).Style.FontSize);
        Assert.True(after.Runs[^1].Style.Underline);
        var crop = after.Bounds;
        crop.Inflate(8, 8);
        using var preview = renderer.RenderSubtitlePreview(document, line, new(3), crop);
        using var full = renderer.Render(document, new(3));
        Assert.Contains(preview.CopySrgbBgra().Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        var pixels = new Half[full.Info.ChannelCount];
        full.CopyPixels(pixels);
        var green = false;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            green |= (float)pixels[index + 1] > 0.5f && (float)pixels[index] < 0.01f && (float)pixels[index + 2] < 0.01f;
        }
        Assert.True(green);
        var background = new byte[document.Width * document.Height * 4];
        var composed = renderer.ComposePreview(document, new(3), background, document.Width, document.Height, document.Width * 4);
        Assert.Equal(background.Length, composed.Length);
    }

    [MacSystemFontFact]
    public void ReorderedMixedContentReusesActualFacesWithoutInvalidatingPreviouslyShapedRuns()
    {
        var document = Document("A你好👩‍💻ffiB");
        SubtitleTextLayout snapshot;
        using (var renderer = Renderer())
        {
            snapshot = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
            for (var index = 0; index < 20; index++)
            {
                var line = document.Subtitles[0] with { Text = index % 2 == 0 ? "你A👩‍💻好B" : "👩‍💻A好B你" };
                var layout = renderer.MeasureSubtitleTextLayout(document, line);
                Assert.Equal(StringInfo.ParseCombiningCharacters(line.Text).Length, layout.Graphemes.Length);
                using var preview = renderer.RenderSubtitlePreview(document, line, MediaTime.Zero, layout.Bounds);
                Assert.True(preview.Info.Width > 0);
            }
            using var original = renderer.Render(document, MediaTime.Zero);
            Assert.Contains(original.CopySrgbBgra().Where((_, index) => index % 4 == 3), alpha => alpha > 0);
        }
        Assert.Equal(5, snapshot.Graphemes[3].Utf16Length);
        Assert.Single(snapshot.GetSelectionRects(3, 5));
    }

    [Fact]
    public void ExplicitEmbeddedFontMissingGlyphDoesNotSilentlyFallBackToTheSystem()
    {
        var document = Document("A你好👩‍💻");
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { FontAssetId = font.Id } };
        document = document with { Assets = [font], Subtitles = [line] };
        using var renderer = Renderer();
        Assert.Throws<InvalidOperationException>(() => renderer.MeasureSubtitleTextLayout(document, line));
    }

    [MacSystemFontFact]
    public void ExplicitFontCanBeClearedLocallyForChineseAndEmojiWithoutChangingLatinInheritance()
    {
        var document = Document("A你好👩‍💻B");
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with { FontAssetId = font.Id },
            InlineSpans = [new(1, 7, new() { ClearFontAsset = true })]
        };
        document = document with { Assets = [font], Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(font.Id, layout.Runs[0].Style.FontAssetId);
        Assert.Equal(font.Id, layout.Runs[^1].Style.FontAssetId);
        Assert.All(layout.Runs.Where(run => run.Utf16Start is >= 1 and < 8), run => Assert.Null(run.Style.FontAssetId));
        using var full = renderer.Render(document, MediaTime.Zero);
        Assert.Contains(full.CopySrgbBgra().Where((_, index) => index % 4 == 3), alpha => alpha > 0);
    }

    private static ProjectSceneRenderer Renderer()
    {
        return new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
    }

    private static ProjectDocument Document(string text)
    {
        var line = new SubtitleLine
        {
            Text = text, End = new(4), Style = new()
            {
                Scaling = new() { ReferenceWidth = 512, ReferenceHeight = 160 },
                FontFamily = "Arial", FontSize = 24, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
                Fill = new(0, 0, 1), StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        return new()
        {
            Width = 512, Height = 160, Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
