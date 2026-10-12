using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleTextAlignmentRenderingTests
{
    public static TheoryData<TextAlignment, SubtitleTextAlignment, bool> Placements()
    {
        var rows = new TheoryData<TextAlignment, SubtitleTextAlignment, bool>();
        foreach (var alignment in Enum.GetValues<TextAlignment>())
        {
            foreach (var textAlign in Enum.GetValues<SubtitleTextAlignment>())
            {
                rows.Add(alignment, textAlign, false);
                rows.Add(alignment, textAlign, true);
            }
        }
        return rows;
    }

    public static TheoryData<TextAlignment> LegacyPlacements()
    {
        var rows = new TheoryData<TextAlignment>();
        foreach (var alignment in Enum.GetValues<TextAlignment>())
        {
            rows.Add(alignment);
        }
        return rows;
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void ShortRowsAlignInsideTheLongestInkBlockWithoutMovingItsPlacement(TextAlignment alignment,
        SubtitleTextAlignment textAlign, bool explicitPosition)
    {
        var document = Document("  WWWWWW  \n  Ajg  ", alignment);
        var original = document.Subtitles[0];
        using var renderer = Renderer();
        if (explicitPosition)
        {
            original = original with
            {
                Style = original.Style with
                {
                    Position = new() { Anchor = new(0.3, 0.6), Pivot = new(0.2, 0.8), Offset = new(7, -11) }
                }
            };
        }
        document = document with { Subtitles = [original] };
        var before = renderer.MeasureSubtitleTextLayout(document, original);
        var placement = renderer.MeasureSubtitlePlacement(document, original);
        var line = original with { Style = original.Style with { TextAlign = textAlign } };
        document = document with { Subtitles = [line] };
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var afterPlacement = renderer.MeasureSubtitlePlacement(document, line);
        var rows = RowBounds(layout);

        Assert.Equal(2, rows.Length);
        Assert.True(rows[0].Width > rows[1].Width);
        AssertRowsAligned(rows, textAlign);
        AssertRectNear(before.Bounds, layout.Bounds);
        AssertPointNear(before.BasePosition, layout.BasePosition);
        AssertPointNear(before.Pivot, layout.Pivot);
        Assert.Equal(placement.Position, afterPlacement.Position);
        Assert.Equal(original.Style.Position, line.Style.Position);
        Assert.Equal(original.Style.Alignment, line.Style.Alignment);
        Assert.Equal(original.Text.Length, layout.Graphemes.Sum(glyph => glyph.Utf16Length));
        Assert.Equal(2, layout.GetSelectionRects(0, line.Text.Length).Length);
    }

    [Theory]
    [MemberData(nameof(LegacyPlacements))]
    public void InheritedAlignmentMatchesItsExplicitModePixelForPixel(TextAlignment alignment)
    {
        var document = Document("WWW\nAjg", alignment);
        using var renderer = Renderer();
        var before = Pixels(renderer, document);
        var original = document.Subtitles[0];
        var line = original with { Style = original.Style with { TextAlign = (SubtitleTextAlignment)((int)alignment % 3) } };
        var changed = document with { Subtitles = [line] };

        Assert.Equal(before, Pixels(renderer, changed));
        var position = renderer.ResolveSubtitlePosition(changed, line);
        var positioned = changed with { Subtitles = [line with { Style = line.Style with { Position = position } }] };
        Assert.Equal(before, Pixels(renderer, positioned));
    }

    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void SoftWrappedRowsUseTheSameBlockAndRetainCaretHitTesting(SubtitleTextAlignment textAlign)
    {
        var document = Document("WWWWWWWWWWWWWWWWWWWWX", TextAlignment.MIDDLE_CENTER, width: 128);
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { TextAlign = textAlign } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var rows = RowBounds(layout);

        Assert.True(rows.Length > 2);
        Assert.True(rows[^1].Width < rows[0].Width);
        AssertRowsAligned(rows, textAlign);
        Assert.Equal(line.Text.Length, layout.Graphemes.Length);
        Assert.Equal(rows.Length, layout.GetSelectionRects(0, line.Text.Length).Length);
        foreach (var run in layout.Runs)
        {
            var caret = layout.GetCaretBounds(run.Utf16Start);
            Assert.Equal(run.Utf16Start, layout.HitTest(new(caret.Left, caret.MidY)).Utf16Offset);
        }
    }

    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void EmptyRowsPlaceTheirCaretsAtTheRequestedBlockEdge(SubtitleTextAlignment textAlign)
    {
        var document = Document("AAAAA\n\nAA\n", TextAlignment.TOP_CENTER);
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { TextAlign = textAlign } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var expected = layout.Bounds.Left + (int)textAlign / 2f * layout.Bounds.Width;

        AssertNear(expected, layout.GetCaretBounds(6).Left);
        AssertNear(expected, layout.GetCaretBounds(line.Text.Length).Left);
        Assert.Equal(line.Text.Length, layout.HitTest(new(expected, layout.GetCaretBounds(line.Text.Length).MidY)).Utf16Offset);
        AssertRowsAligned(RowBounds(layout), textAlign);
    }

    [Theory]
    [InlineData(TextAlignment.TOP_LEFT)]
    [InlineData(TextAlignment.TOP_CENTER)]
    [InlineData(TextAlignment.TOP_RIGHT)]
    public void InheritedPlacementAlsoHitTestsTheCaretOnItsTrailingEmptyLine(TextAlignment alignment)
    {
        var document = Document("AAAAA\n\nAA\n", alignment);
        var line = document.Subtitles[0];
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var caret = layout.GetCaretBounds(line.Text.Length);

        Assert.Null(line.Style.TextAlign);
        Assert.Equal(line.Text.Length, layout.HitTest(new(caret.Left, caret.MidY)).Utf16Offset);
    }

    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void WhitespaceOnlyRowsRetainFiniteGeometryAndExactAutomaticPlacement(SubtitleTextAlignment textAlign)
    {
        var document = Document("    \n \n", TextAlignment.BOTTOM_RIGHT);
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { TextAlign = textAlign } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var placement = renderer.MeasureSubtitlePlacement(document, line);

        Assert.False(layout.HasInk);
        Assert.True(layout.Bounds.Width > 1);
        AssertNear(document.Width - line.Style.Margins.Right, layout.BasePosition.X);
        AssertNear(layout.Bounds.Right, layout.Pivot.X);
        for (var offset = 0; offset <= line.Text.Length; offset++)
        {
            var caret = layout.GetCaretBounds(offset);
            Assert.True(float.IsFinite(caret.Left));
            Assert.True(float.IsFinite(caret.Top));
        }
        AssertNear(layout.Bounds.Left + (int)textAlign / 2f * layout.Bounds.Width,
            layout.GetCaretBounds(line.Text.Length).Left);
        var positioned = document with { Subtitles = [line with { Style = line.Style with { Position = placement.Position } }] };
        Assert.Equal(Pixels(renderer, document), Pixels(renderer, positioned));
    }

    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void RtlRowsKeepTheirLogicalCaretDirectionWhileTheirInkAligns(SubtitleTextAlignment textAlign)
    {
        var document = Document("ببببب\nبب", TextAlignment.TOP_CENTER, "Fixtures/NotoSansArabic.ttf");
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { TextAlign = textAlign } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);

        AssertRowsAligned(RowBounds(layout), textAlign);
        foreach (var glyph in layout.Graphemes.Where(glyph => line.Text[glyph.Utf16Start] != '\n'))
        {
            Assert.True(glyph.LeadingCaret.Left > glyph.TrailingCaret.Left);
            Assert.Equal(glyph.Utf16Start, layout.HitTest(new(glyph.LeadingCaret.Left, glyph.LeadingCaret.MidY)).Utf16Offset);
        }
        Assert.Equal(2, layout.GetSelectionRects(0, line.Text.Length).Length);
    }

    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void MixedFontSizesAndKaraokeUseTheAlignedRunGeometry(SubtitleTextAlignment textAlign)
    {
        var document = Document("ABCD\nAjg", TextAlignment.MIDDLE_CENTER);
        var line = document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with { TextAlign = textAlign },
            InlineSpans = [new(1, 2, new() { FontSize = 40, Italic = true, Underline = true })],
            Karaoke = [new(0, 4, MediaTime.Zero, new(1), new(1, 0, 0))]
        };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);

        AssertRowsAligned(RowBounds(layout), textAlign);
        Assert.Single(layout.Runs.Where(run => run.LineIndex == 0).Select(run => run.Baseline.Y).Distinct());
        Assert.Equal(40, layout.Runs[1].Style.FontSize);
        Assert.Equal(2, layout.GetSelectionRects(0, line.Text.Length).Length);
        var timed = Pixels(renderer, document);
        var ordinary = document with { Subtitles = [line with { Karaoke = [] }] };
        Assert.NotEqual(timed, Pixels(renderer, ordinary));
    }

    private static void AssertRowsAligned(SKRect[] rows, SubtitleTextAlignment textAlign)
    {
        Assert.NotEmpty(rows);
        var expected = textAlign switch
        {
            SubtitleTextAlignment.LEFT => rows[0].Left,
            SubtitleTextAlignment.CENTER => rows[0].MidX,
            _ => rows[0].Right
        };
        foreach (var row in rows)
        {
            var actual = textAlign switch
            {
                SubtitleTextAlignment.LEFT => row.Left,
                SubtitleTextAlignment.CENTER => row.MidX,
                _ => row.Right
            };
            AssertNear(expected, actual);
        }
    }

    private static SKRect[] RowBounds(SubtitleTextLayout layout) => layout.Runs
        .Where(run => !run.Bounds.IsEmpty).GroupBy(run => run.LineIndex).OrderBy(group => group.Key)
        .Select(group => group.Select(run => run.Bounds).Aggregate(SKRect.Union)).ToArray();

    private static void AssertNear(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 0.001);

    private static void AssertPointNear(SKPoint expected, SKPoint actual)
    {
        AssertNear(expected.X, actual.X);
        AssertNear(expected.Y, actual.Y);
    }

    private static void AssertRectNear(SKRect expected, SKRect actual)
    {
        AssertNear(expected.Left, actual.Left);
        AssertNear(expected.Top, actual.Top);
        AssertNear(expected.Right, actual.Right);
        AssertNear(expected.Bottom, actual.Bottom);
    }

    private static Half[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document)
    {
        using var surface = renderer.Render(document, new MediaTime(1, 2));
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static ProjectDocument Document(string text, TextAlignment alignment, string fontPath = "Fixtures/NotoSans.ttf", int width = 256)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, fontPath);
        var line = new SubtitleLine
        {
            Text = text,
            End = new(3),
            Style = new()
            {
                Scaling = new() { ReferenceWidth = width, ReferenceHeight = 240 },
                FontAssetId = font.Id,
                FontSize = 24,
                Alignment = alignment,
                Margins = new(8, 8, 8),
                Fill = new(0, 0, 1),
                StrokeWidth = 0,
                ShadowColor = SceneColor.Transparent
            }
        };
        return new()
        {
            Width = width,
            Height = 240,
            Assets = [font],
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
