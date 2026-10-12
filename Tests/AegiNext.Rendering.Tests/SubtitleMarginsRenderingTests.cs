using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleMarginsRenderingTests
{
    private static readonly string[] expectedWrappedRows = ["WW", "WW", "W"];

    public static TheoryData<TextAlignment> Alignments()
    {
        var rows = new TheoryData<TextAlignment>();
        foreach (var alignment in Enum.GetValues<TextAlignment>())
        {
            rows.Add(alignment);
        }
        return rows;
    }

    public static TheoryData<TextAlignment, SubtitleTextAlignment> IndependentAlignments()
    {
        var rows = new TheoryData<TextAlignment, SubtitleTextAlignment>();
        foreach (var alignment in Enum.GetValues<TextAlignment>())
        {
            foreach (var textAlign in Enum.GetValues<SubtitleTextAlignment>())
            {
                rows.Add(alignment, textAlign);
            }
        }
        return rows;
    }

    [Theory]
    [MemberData(nameof(Alignments))]
    public void AsymmetricHorizontalMarginsPlaceTheInkAtTheSelectedBoundaryOrContentCenter(TextAlignment alignment)
    {
        var document = Document("  WWW  \n  Ajg  ", alignment, new(13, 47, 9));
        var line = document.Subtitles[0];
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var horizontal = (int)alignment % 3;
        var expected = horizontal switch
        {
            0 => 13,
            1 => 111,
            _ => 209
        };
        var actual = horizontal switch
        {
            0 => layout.Bounds.Left,
            1 => layout.Bounds.MidX,
            _ => layout.Bounds.Right
        };

        AssertNear(expected, actual);
        AssertNear(expected, layout.BasePosition.X);
        Assert.Equal(2, layout.Runs.Select(run => run.LineIndex).Distinct().Count());
        Assert.Null(line.Style.Position);
    }

    [Theory]
    [MemberData(nameof(Alignments))]
    public void VerticalMarginMovesTopAndBottomBaselinesWhileMiddlePlacementIsUnchanged(TextAlignment alignment)
    {
        var document = Document("Ajg\nABC", alignment, new(13, 47, 8));
        var original = document.Subtitles[0];
        var line = original with { Style = original.Style with { Margins = new(13, 47, 33) } };
        var changed = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var before = renderer.MeasureSubtitleTextLayout(document, original);
        var after = renderer.MeasureSubtitleTextLayout(changed, line);
        var expectedShift = ((int)alignment / 3) switch
        {
            0 => 25,
            1 => 0,
            _ => -25
        };

        AssertNear(before.Bounds.Left, after.Bounds.Left);
        AssertNear(before.Bounds.Right, after.Bounds.Right);
        AssertNear(before.Bounds.Top + expectedShift, after.Bounds.Top);
        AssertNear(before.Bounds.Bottom + expectedShift, after.Bounds.Bottom);
        AssertNear(before.BasePosition.X, after.BasePosition.X);
        AssertNear(before.BasePosition.Y + expectedShift, after.BasePosition.Y);
        Assert.Equal(before.Runs.Length, after.Runs.Length);
        for (var index = 0; index < before.Runs.Length; index++)
        {
            AssertNear(before.Runs[index].Baseline.Y + expectedShift, after.Runs[index].Baseline.Y);
        }
        if (expectedShift == 0)
        {
            Assert.Equal(Pixels(renderer, document), Pixels(renderer, changed));
        }
    }

    [Fact]
    public void WrappingUsesBothHorizontalMarginsAndPreservesGraphemesAndCaretBoundaries()
    {
        var document = Document("WWWWW", TextAlignment.TOP_LEFT, new(18, 190, 7));
        var original = document.Subtitles[0];
        var line = original with { Style = original.Style with { Margins = new(28, 180, 29) } };
        var changed = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var before = renderer.MeasureSubtitleTextLayout(document, original);
        var after = renderer.MeasureSubtitleTextLayout(changed, line);

        Assert.Equal(expectedWrappedRows, before.Runs.Select(run => original.Text.Substring(run.Utf16Start, run.Utf16Length)));
        Assert.Equal(before.Runs.Select(run => run.Utf16Start), after.Runs.Select(run => run.Utf16Start));
        Assert.Equal(original.Text.Length, before.Graphemes.Length);
        Assert.Equal(line.Text.Length, after.Graphemes.Length);
        Assert.Equal(3, after.GetSelectionRects(0, line.Text.Length).Length);
        foreach (var run in after.Runs)
        {
            var caret = after.GetCaretBounds(run.Utf16Start);
            Assert.Equal(run.Utf16Start, after.HitTest(new(caret.Left, caret.MidY)).Utf16Offset);
        }

        var wideLine = original with { Style = original.Style with { Margins = new(18, 18, 7) } };
        var wide = renderer.MeasureSubtitleTextLayout(document with { Subtitles = [wideLine] }, wideLine);
        Assert.Single(wide.Runs);
    }

    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void SoftWrappedTextKeepsTheAsymmetricAnchorWhenConvertedToExplicitPosition(SubtitleTextAlignment textAlign)
    {
        var document = Document("WWWWW", TextAlignment.MIDDLE_CENTER, new(18, 190, 7));
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { TextAlign = textAlign } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var placement = renderer.MeasureSubtitlePlacement(document, line);
        var positioned = document with { Subtitles = [line with { Style = line.Style with { Position = placement.Position } }] };

        Assert.Equal(3, layout.Runs.Select(run => run.LineIndex).Distinct().Count());
        AssertNear(42, layout.BasePosition.X);
        AssertNear(-86, placement.Position.Offset.X);
        AssertRowsAligned(layout, textAlign);
        Assert.Equal(Pixels(renderer, document), Pixels(renderer, positioned));
    }

    [Theory]
    [MemberData(nameof(IndependentAlignments))]
    public void IndependentTextAlignmentKeepsTheContentAnchorAndAutomaticToExplicitPixels(TextAlignment alignment,
        SubtitleTextAlignment textAlign)
    {
        var document = Document("  WWWWWW  \n  Ajg  \n", alignment, new(13, 47, 9));
        var original = document.Subtitles[0];
        var line = original with { Style = original.Style with { TextAlign = textAlign } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var inherited = renderer.MeasureSubtitleTextLayout(document with { Subtitles = [original] }, original);
        var automatic = renderer.MeasureSubtitleTextLayout(document, line);
        var placement = renderer.MeasureSubtitlePlacement(document, line);
        var positioned = document with { Subtitles = [line with { Style = line.Style with { Position = placement.Position } }] };

        AssertNear(inherited.BasePosition.X, automatic.BasePosition.X);
        AssertNear(inherited.BasePosition.Y, automatic.BasePosition.Y);
        AssertRowsAligned(automatic, textAlign);
        AssertNear(automatic.BasePosition.X, placement.Position.Anchor.X * document.Width + placement.Position.Offset.X);
        AssertNear(automatic.BasePosition.Y, placement.Position.Anchor.Y * document.Height + placement.Position.Offset.Y);
        Assert.Equal(Pixels(renderer, document), Pixels(renderer, positioned));

        var layerId = document.Layers[0].Id;
        var before = renderer.GetLayerGeometry(document, MediaTime.Zero, layerId)!;
        var after = renderer.GetLayerGeometry(positioned, MediaTime.Zero, layerId)!;
        AssertPointNear(before.WorldPivot, after.WorldPivot);
        for (var index = 0; index < before.WorldCorners.Count; index++)
        {
            AssertPointNear(before.WorldCorners[index], after.WorldCorners[index]);
        }
    }

    [Theory]
    [InlineData(SubtitleTextAlignment.LEFT)]
    [InlineData(SubtitleTextAlignment.CENTER)]
    [InlineData(SubtitleTextAlignment.RIGHT)]
    public void WhitespaceOnlyRowsRemainFiniteAndConvertToExplicitWithoutMovingTheirFallbackGeometry(SubtitleTextAlignment textAlign)
    {
        var document = Document("    \n \n", TextAlignment.BOTTOM_CENTER, new(13, 47, 9));
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { TextAlign = textAlign } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        var placement = renderer.MeasureSubtitlePlacement(document, line);
        var positioned = document with { Subtitles = [line with { Style = line.Style with { Position = placement.Position } }] };
        var explicitLayout = renderer.MeasureSubtitleTextLayout(positioned, positioned.Subtitles[0]);

        Assert.False(layout.HasInk);
        AssertNear(111, layout.BasePosition.X);
        Assert.Equal(layout.Bounds, explicitLayout.Bounds);
        Assert.Equal(layout.BasePosition, explicitLayout.BasePosition);
        for (var offset = 0; offset <= line.Text.Length; offset++)
        {
            var caret = layout.GetCaretBounds(offset);
            Assert.True(float.IsFinite(caret.Left));
            Assert.True(float.IsFinite(caret.Top));
        }
        var trailing = layout.GetCaretBounds(line.Text.Length);
        Assert.Equal(line.Text.Length, layout.HitTest(new(trailing.Left, trailing.MidY)).Utf16Offset);
    }

    [Fact]
    public void ExplicitPositionKeepsTheFullCanvasCoordinateSystemWhenMarginsChange()
    {
        var document = Document("Ajg\nABC", TextAlignment.BOTTOM_CENTER, new(13, 47, 9));
        var position = new SubtitlePosition { Anchor = new(0.3, 0.6), Pivot = new(0.2, 0.8), Offset = new(7, -11) };
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { Position = position } };
        document = document with { Subtitles = [line] };
        var changedLine = line with { Style = line.Style with { Margins = new(43, 17, 29) } };
        var changed = document with { Subtitles = [changedLine] };
        using var renderer = Renderer();
        var before = renderer.GetLayerGeometry(document, MediaTime.Zero, document.Layers[0].Id)!;
        var after = renderer.GetLayerGeometry(changed, MediaTime.Zero, changed.Layers[0].Id)!;

        AssertPointNear(new(83.8f, 85), before.BasePosition);
        Assert.Equal(before.BasePosition, after.BasePosition);
        AssertPointNear(before.WorldPivot, after.WorldPivot);
        Assert.Equal(Pixels(renderer, document), Pixels(renderer, changed));
        Assert.Equal(position, changedLine.Style.Position);
    }

    private static void AssertRowsAligned(SubtitleTextLayout layout, SubtitleTextAlignment textAlign)
    {
        var rows = layout.Runs.Where(run => !run.Bounds.IsEmpty).GroupBy(run => run.LineIndex)
            .Select(group => group.Select(run => run.Bounds).Aggregate(SKRect.Union)).ToArray();
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

    private static void AssertNear(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, 0.001);

    private static void AssertPointNear(SKPoint expected, SKPoint actual)
    {
        AssertNear(expected.X, actual.X);
        AssertNear(expected.Y, actual.Y);
    }

    private static Half[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document)
    {
        using var surface = renderer.Render(document, MediaTime.Zero);
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        return pixels;
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static ProjectDocument Document(string text, TextAlignment alignment, SubtitleMargins margins)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine
        {
            Text = text,
            Style = new()
            {
                Scaling = new() { ReferenceWidth = 256, ReferenceHeight = 160 },
                FontAssetId = font.Id, FontSize = 22, Margins = margins, Alignment = alignment,
                StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End };
        return new() { Width = 256, Height = 160, Assets = [font], Subtitles = [line], Layers = [layer] };
    }
}
