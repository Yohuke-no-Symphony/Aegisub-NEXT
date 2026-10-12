using System.Globalization;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleTypographyRenderingTests
{
    [Theory]
    [InlineData(8)]
    [InlineData(-30)]
    public void InlineRunsAddOneInterRunGapWithoutAddingASuffixGap(double spacing)
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { LetterSpacing = spacing, WrapMode = SubtitleWrapMode.NO_WRAP } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var uniform = renderer.MeasureSubtitleTextLayout(document, line);
        var rich = line with { InlineSpans = [new(2, 2, new() { Fill = new(1, 0, 0) })] };
        var mixed = renderer.MeasureSubtitleTextLayout(document, rich);

        Assert.Equal(uniform.Bounds.Left, mixed.Bounds.Left, 3);
        Assert.Equal(uniform.Bounds.Width, mixed.Bounds.Width, 3);
        for (var index = 0; index <= line.Text.Length; index++)
        {
            Assert.Equal(uniform.GetCaretBounds(index).Left, mixed.GetCaretBounds(index).Left, 3);
        }
        Assert.True(mixed.Bounds.Width > 0);
        Assert.Single(mixed.GetSelectionRects(0, 4));
    }

    [Fact]
    public void NegativeAdvanceKeepsGlyphOrderInCaretGeometryAndPreservesVisibleInk()
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { LetterSpacing = -100, WrapMode = SubtitleWrapMode.NO_WRAP } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);

        Assert.True(layout.GetCaretBounds(0).Left > layout.GetCaretBounds(1).Left);
        Assert.True(layout.GetCaretBounds(1).Left > layout.GetCaretBounds(2).Left);
        Assert.All(layout.Graphemes, grapheme => Assert.True(grapheme.Bounds.Width > 0));
        Assert.Equal(4, layout.Graphemes.Length);
        Assert.True(layout.Bounds.Width > 250);
        Assert.Contains(Pixels(renderer, document, MediaTime.Zero), value => value > 0);
        var caret = layout.GetCaretBounds(1);
        Assert.Equal(1, layout.HitTest(new(caret.Left, caret.MidY)).Utf16Offset);
    }

    [Fact]
    public void NaturalWrappingPrefersWordBoundariesAndNoWrapKeepsOnlyHardNewlines()
    {
        var document = Document("one two three\nABCD", width: 94);
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { WrapMode = SubtitleWrapMode.NATURAL } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var natural = renderer.MeasureSubtitleTextLayout(document, line);
        var rows = natural.Graphemes.GroupBy(grapheme => grapheme.LineIndex).ToArray();
        Assert.True(rows.Length >= 4);
        Assert.All(rows.Where(row => row.First().Utf16Start < 13), row =>
        {
            var start = row.First().Utf16Start;
            Assert.True(start is 0 or 4 or 8 or 13);
        });
        var unwrapped = line with { Style = line.Style with { WrapMode = SubtitleWrapMode.NO_WRAP } };
        var layout = renderer.MeasureSubtitleTextLayout(document, unwrapped);
        Assert.Equal(2, layout.Runs.Select(run => run.LineIndex).Distinct().Count());
        Assert.True(layout.Bounds.Width > document.Width);
    }

    [Theory]
    [InlineData("\u00a0")]
    [InlineData("\u2060")]
    public void NaturalWrappingDoesNotSplitAnOversizedGluedGroup(string glue)
    {
        var text = new string('W', 80) + glue + new string('W', 80);
        var document = Document(text, width: 96);
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { WrapMode = SubtitleWrapMode.NATURAL } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);

        Assert.Single(layout.Runs.Select(run => run.LineIndex).Distinct());
        Assert.Equal(StringInfo.ParseCombiningCharacters(text).Length, layout.Graphemes.Length);
        Assert.True(layout.Bounds.Width > document.Width);
    }

    [Fact]
    public void LongNegativeSpacingWrapKeepsEveryGraphemeWithoutNegativeOrEmptyLayoutBounds()
    {
        var document = Document(new string('W', 4096), width: 96);
        var line = document.Subtitles[0] with { Style = document.Subtitles[0].Style with { LetterSpacing = -40 } };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);

        Assert.Equal(line.Text.Length, layout.Graphemes.Length);
        Assert.True(layout.Runs.Length > 100);
        Assert.All(layout.Runs, run => Assert.True(run.Bounds.Width > 0));
        Assert.All(layout.Runs, run => Assert.True(run.Bounds.Width <= document.Width - 16 + 1));
    }

    [MacSystemFontFact]
    public void LongCjkNegativeSpacingUsesNaturalBoundariesWithoutDroppingCharacters()
    {
        var document = Document(string.Concat(Enumerable.Repeat("中文标点，测试。", 128)), width: 128) with { Assets = [] };
        var line = document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with { FontAssetId = null, FontFamily = "Arial", LetterSpacing = -2, WrapMode = SubtitleWrapMode.NATURAL }
        };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(line.Text.Length, layout.Graphemes.Length);
        Assert.All(layout.Runs, run => Assert.True(run.Bounds.Width > 0));
        Assert.DoesNotContain(layout.Graphemes.GroupBy(grapheme => grapheme.LineIndex), row => line.Text[row.First().Utf16Start] is '，' or '。');
    }

    [Fact]
    public void NegativeSpacingRechecksActualInkWhenWrappingSplitsALigature()
    {
        using var shaper = new TextShaper(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf")));
        using var joined = shaper.Shape("ffi", 100, TextDirection.LEFT_TO_RIGHT, "en", -1);
        using var split = shaper.Shape("ff", 100, TextDirection.LEFT_TO_RIGHT, "en", -1);
        Assert.Single(joined.Clusters.ToArray());
        var available = (int)Math.Ceiling(joined.AdvanceWidth * 2 / 3);
        Assert.True(split.InkBounds.Width > available);
        var document = Document("ffi", width: available + 16);
        var line = document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with { FontSize = 100, LetterSpacing = -1 }
        };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, line);

        Assert.Equal(3, layout.Graphemes.Length);
        Assert.All(layout.Runs, run => Assert.True(run.Bounds.Width <= available));
        Assert.Equal(1, layout.Runs[0].Utf16Length);
    }

    [Fact]
    public void AnimatedSpacingSharesDrawingHitGeometryAndPivotWhileStaticPreviewKeepsItsContract()
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0] with { InlineSpans = [new(1, 2, new() { LetterSpacing = 30 })] };
        var layer = document.Layers[0] with
        {
            Tracks = [new(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(2), 20)])]
        };
        document = document with { Subtitles = [line], Layers = [layer] };
        using var renderer = Renderer();
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(document, new(1)));
        var animated = renderer.MeasureSubtitleTextLayout(document, evaluated);
        var geometry = renderer.GetLayerGeometry(document, new(1), layer.Id)!;
        var placement = renderer.MeasureSubtitlePlacement(document, evaluated);

        Assert.All(animated.Runs, run => Assert.Equal(10, run.Style.LetterSpacing));
        Assert.Equal(animated.Bounds, geometry.LocalBounds);
        Assert.Equal(animated.Pivot, geometry.LocalPivot);
        Assert.Equal(animated.Bounds, placement.Bounds);
        var crop = new SKRect(0, 0, document.Width, document.Height);
        using var before = renderer.RenderSubtitlePreview(document, line, new(0), crop);
        using var after = renderer.RenderSubtitlePreview(document, line, new(1), crop);
        Assert.Equal(before.CopySrgbBgra(), after.CopySrgbBgra());
        Assert.NotEqual(Pixels(renderer, document, new(0)), Pixels(renderer, document, new(1)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FillAndStrokeBlurBroadenOnlyTheirOwnPaintAndPreserveTheInkPivot(bool fill)
    {
        var document = Document("A");
        var line = document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with { Fill = fill ? SceneColor.White : SceneColor.Transparent,
                Stroke = fill ? SceneColor.Transparent : SceneColor.White, StrokeWidth = fill ? 0 : 3 }
        };
        document = document with { Subtitles = [line] };
        using var renderer = Renderer();
        var baseline = Pixels(renderer, document, MediaTime.Zero);
        var before = renderer.MeasureSubtitleTextLayout(document, line);
        var blurred = line with { Style = line.Style with { FillBlur = fill ? 3 : 0, StrokeBlur = fill ? 0 : 3 } };
        var changed = document with { Subtitles = [blurred] };
        var pixels = Pixels(renderer, changed, MediaTime.Zero);
        var after = renderer.MeasureSubtitleTextLayout(changed, blurred);

        Assert.Equal(before.Bounds, after.Bounds);
        Assert.Equal(before.Pivot, after.Pivot);
        Assert.True(NonzeroAlpha(pixels) > NonzeroAlpha(baseline));
        Assert.True(AlphaMaximum(pixels) <= AlphaMaximum(baseline));
        var irrelevant = line with { Style = line.Style with { FillBlur = fill ? 0 : 4, StrokeBlur = fill ? 4 : 0 } };
        Assert.Equal(baseline, Pixels(renderer, document with { Subtitles = [irrelevant] }, MediaTime.Zero));
    }

    [Fact]
    public void BlurAndSpacingAnimationInvalidateFrameCacheAndAnimatedLayoutsRemainBounded()
    {
        var document = Document("ABCD");
        var line = document.Subtitles[0];
        var layer = document.Layers[0] with
        {
            Tracks =
            [
                new(AnimationProperty.LETTER_SPACING, [new(new(0), 0), new(new(2), 20)]),
                new(AnimationProperty.FILL_BLUR, [new(new(0), 0), new(new(2), 4)]),
                new(AnimationProperty.STROKE_BLUR, [new(new(0), 0), new(new(2), 5)])
            ]
        };
        document = document with { Layers = [layer] };
        using var renderer = Renderer();
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, new(0), pixels));
        Assert.True(renderer.CopyCachedFramePixels(document, new(1), pixels));
        Assert.False(renderer.CopyCachedFramePixels(document, new(1), pixels));
        var shapers = renderer.CachedTextShaperCount;
        for (var index = 0; index < 320; index++)
        {
            var evaluated = Assert.Single(SceneEvaluator.Evaluate(document, new(index, 200)));
            renderer.MeasureSubtitleTextLayout(document, evaluated);
        }
        Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
        Assert.Equal(shapers, renderer.CachedTextShaperCount);
        renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(2, renderer.CachedSubtitleLayoutCount);
        Assert.Equal(0, line.Style.LetterSpacing);
    }

    private static ProjectDocument Document(string text, int width = 384)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine { Text = text, End = new(2), Style = new()
        {
            Scaling = new() { ReferenceWidth = width, ReferenceHeight = 160 },
            FontAssetId = font.Id, FontSize = 24, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
            Fill = SceneColor.White, StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        } };
        return new() { Width = width, Height = 160, Assets = [font], Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End }] };
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static byte[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document, MediaTime time)
    {
        using var surface = renderer.Render(document, time);
        return surface.CopySrgbBgra();
    }

    private static int NonzeroAlpha(byte[] pixels) => pixels.Where((value, index) => index % 4 == 3 && value > 0).Count();
    private static byte AlphaMaximum(byte[] pixels) => pixels.Where((_, index) => index % 4 == 3).Max();
}
