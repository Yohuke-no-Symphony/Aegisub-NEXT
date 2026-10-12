using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class KaraokeHighlightStyleRenderingTests
{
    [Fact]
    public void CompletedHighlightReplacesFillStrokeAndShadowWithoutBaseVisualResidue()
    {
        var document = Document("TEST\nABC");
        var line = Assert.Single(document.Subtitles);
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Red HDR", new()
        {
            FontSize = 200, Fill = new(4, 0, 0, 0.5), Stroke = new(0, 1, 0), StrokeWidth = 2,
            ShadowColor = new(1, 0, 0, 0.5), ShadowOffset = new(-3, 5), ShadowBlur = 1
        });
        var highlighted = document with
        {
            Subtitles = [line with { KaraokeStyle = highlight, Karaoke = [new(0, line.Text.Length, new(0), new(1), SceneColor.White)] }]
        };
        var reference = document with
        {
            Subtitles = [line with { Style = line.Style with
            {
                Fill = highlight.Fill, Stroke = highlight.Stroke, StrokeWidth = highlight.StrokeWidth,
                ShadowColor = highlight.ShadowColor, ShadowOffset = highlight.ShadowOffset, ShadowBlur = highlight.ShadowBlur
            } }]
        };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var actual = Pixels(renderer, highlighted, new(1));
        var expected = Pixels(renderer, reference, new(1));
        Assert.Equal(expected, actual);
        Assert.Contains(actual.Where((_, index) => index % 4 == 0), value => (float)value > 1.5f);
        Assert.Equal(22, highlighted.Subtitles[0].Style.FontSize);
        Assert.Equal(renderer.MeasureSubtitlePlacement(document, line), renderer.MeasureSubtitlePlacement(highlighted, highlighted.Subtitles[0]));
    }

    [Fact]
    public void PartialHighlightKeepsUnreachedGlyphsAndUsesTheOriginalShapedMultilineLayout()
    {
        var document = Document("ABC\nDEF");
        var line = document.Subtitles[0];
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Red", new()
        {
            Fill = new(1, 0, 0), StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        });
        document = document with { Subtitles = [line with { KaraokeStyle = highlight, Karaoke = [new(0, 3, new(0), new(1), SceneColor.White)] }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var before = Pixels(renderer, document, MediaTime.Zero);
        var partial = Pixels(renderer, document, new(1, 2));
        var completeFirstLine = Pixels(renderer, document, new(1));
        Assert.NotEqual(before, partial);
        Assert.NotEqual(partial, completeFirstLine);
        var hasRed = false;
        var hasBlue = false;
        for (var index = 0; index < partial.Length; index += 4)
        {
            hasRed |= (float)partial[index] > 0.5f && (float)partial[index + 2] < 0.01f;
            hasBlue |= (float)partial[index + 2] > 0.5f && (float)partial[index] < 0.01f;
        }
        Assert.True(hasRed);
        Assert.True(hasBlue);
    }

    [Fact]
    public void SnapshotHasNoEffectBeforeItsFirstSegmentAndLegacyHighlightStillUsesSegmentColor()
    {
        var document = Document("ABC");
        var line = document.Subtitles[0];
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Red", new() { Fill = new(1, 0, 0) });
        var styled = document with { Subtitles = [line with { KaraokeStyle = highlight, Karaoke = [new(0, 3, new(1), new(3, 2), new(0, 1, 0))] }] };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        Assert.Equal(Pixels(renderer, document, new(1, 2)), Pixels(renderer, styled, new(1, 2)));
        var legacy = styled with { Subtitles = [styled.Subtitles[0] with { KaraokeStyle = null }] };
        var pixels = Pixels(renderer, legacy, new(3, 2));
        Assert.Contains(pixels.Where((_, index) => index % 4 == 1), value => (float)value > 0.5f);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    public void InactiveKaraokeNeverChangesRenderedPixelsOrPlacement(long numerator, long denominator)
    {
        var document = Document("ABC");
        var line = document.Subtitles[0];
        var disabledLine = line with
        {
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.Parse("d033c3c2-3c5a-4fa2-b9a1-e7c8515a763d"), "Disabled HDR", new()
            {
                Fill = new(4.123456789012345, 0.25, 0.5, 0.7312345678901234),
                Stroke = new(3.75, 0.25, 0.5),
                StrokeWidth = 1.125,
                ShadowColor = new(2.5, 0.25, 0.5, 0.6),
                ShadowOffset = new(-3.125, 5.75),
                ShadowBlur = 1.625
            }),
            KaraokeStyleSpans =
            [
                new(0, 1, new() { StrokeWidth = 0 }, new() { Fill = new(0.25, 3.75, 0.5) }),
                new(1, 1, new() { Fill = new(4.75, 0.25, 0.5), ShadowBlur = 0 }, new() { ShadowOffset = new(8.5, -5.25) }),
                new(2, 1, new() { Stroke = new(4.75, 0.25, 0.5), ShadowOffset = new(-4.5, 6.25) }, new() { Stroke = new(0.25, 4.75, 0.5) })
            ],
            InactiveKaraoke =
            [
                new(0, 1, new(1, 7), new(5, 6), new(4.75, 0.25, 0.5))
                {
                    Id = Guid.Parse("774ab0f0-7d97-4c1b-80a5-5b6b34e0f603"),
                    HighlightKind = KaraokeHighlightKind.SWEEP
                },
                new(1, 1, new(5, 6), new(4, 3), new(2.5, 0.25, 0.5))
                {
                    Id = Guid.Parse("5702a10d-b679-4fc4-88b1-4dcb6c892878"),
                    HighlightKind = KaraokeHighlightKind.STEP
                },
                new(2, 1, new(4, 3), new(11, 6), new(3.25, 0.25, 0.5))
                {
                    Id = Guid.Parse("97cf230d-51e8-47bc-958d-5bf8e0af84f5"),
                    HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
                }
            ]
        };
        var disabled = document with { Subtitles = [disabledLine] };
        ProjectValidator.Validate(disabled);
        Assert.Equal(SubtitleContentKind.PLAIN, disabledLine.ContentKind);
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var time = new MediaTime(numerator, denominator);
        Assert.Equal(Pixels(renderer, document, time), Pixels(renderer, disabled, time));
        Assert.Equal(renderer.MeasureSubtitlePlacement(document, line), renderer.MeasureSubtitlePlacement(disabled, disabledLine));
        var enabled = disabled with
        {
            Subtitles = [disabledLine with { Karaoke = disabledLine.InactiveKaraoke, InactiveKaraoke = [] }]
        };
        Assert.NotEqual(Pixels(renderer, document, new(3, 2)), Pixels(renderer, enabled, new(3, 2)));
    }

    private static Half[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document, MediaTime time)
    {
        using var surface = renderer.Render(document, time);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static ProjectDocument Document(string text)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine { Text = text, Style = new()
        {
            Scaling = new() { ReferenceWidth = 160, ReferenceHeight = 128 },
            FontAssetId = font.Id, FontSize = 22, Alignment = TextAlignment.MIDDLE_CENTER, Margins = new(4, 4, 4),
            Fill = new(0, 0, 1), Stroke = new(0, 0, 1), StrokeWidth = 4,
            ShadowColor = new(0, 0, 1), ShadowOffset = new(14, 10), ShadowBlur = 2
        } };
        return new()
        {
            Width = 160, Height = 128, Assets = [font], Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
