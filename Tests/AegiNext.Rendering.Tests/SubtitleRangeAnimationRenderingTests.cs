using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleRangeAnimationRenderingTests
{
    [Fact]
    public void OverlappingRangeTransformsComposeInSavedOrderAndColorsDoNotReshapeLigatures()
    {
        var document = Document("ffi");
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { Scale = new(2, 1) };
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2) { Rotation = 45 };
        document = document with
        {
            Subtitles = [document.Subtitles[0] with { AnimationRanges = [first, second] }],
            Layers = [document.Layers[0] with { Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: first.Id), [new(new(0), new SceneColor(1, 0, 0))]),
                new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: second.Id), [new(new(0), new SceneColor(0, 0, 1))])
            ] }]
        };
        using var renderer = Renderer();
        var plain = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var result = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));
        var pixels = Pixels(renderer, document, new(0));

        Assert.Equal(plain.Runs.Length, result.Runs.Length);
        Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
        Assert.Contains(pixels.Where((_, index) => index % 4 == 0), value => value > 150);
        Assert.Contains(pixels.Where((_, index) => index % 4 == 2), value => value > 150);
        var reverse = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [second, first] }] };
        var reversed = renderer.MeasureSubtitleTextLayout(reverse, Assert.Single(SceneEvaluator.Evaluate(reverse, new(0))));
        Assert.NotEqual(result.Graphemes[1].Bounds, reversed.Graphemes[1].Bounds);
        Assert.NotEqual(pixels, Pixels(renderer, reverse, new(0)));
    }

    [Fact]
    public void RangeFontSizeReflowsOnlyOwnedTextWhilePaintAnimationReusesLayout()
    {
        var document = Document("ABCD");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2);
        document = document with
        {
            Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }],
            Layers = [document.Layers[0] with { Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id), [new(new(0), 24), new(new(2), 48)]),
                new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id), [new(new(0), SceneColor.White), new(new(2), SceneColor.Black)])
            ] }]
        };
        using var renderer = Renderer();
        var layout = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(1))));

        Assert.Equal(24, layout.Runs.First().Style.FontSize);
        Assert.Equal(36, layout.Runs.Single(run => run.Utf16Start == 1).Style.FontSize);
        Assert.Equal(24, layout.Runs.Last().Style.FontSize);
        Assert.NotEqual(Pixels(renderer, document, new(0)), Pixels(renderer, document, new(1)));
        Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
    }

    [Fact]
    public void RangeTransformSharesCrossLinePivotAndPreservesStaticPlacement()
    {
        var document = Document("AB\nCD");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 5) { Scale = new(2, 2), Rotation = 30 };
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        using var renderer = Renderer();
        var plain = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var transformed = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));
        var placement = renderer.MeasureSubtitlePlacement(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));

        Assert.Equal(plain.Bounds, placement.Bounds);
        Assert.Equal(plain.BasePosition, transformed.BasePosition);
        Assert.True(transformed.Bounds.Width > plain.Bounds.Width);
        Assert.Equal(plain.Graphemes.Length, transformed.Graphemes.Length);
        var expected = SKMatrix.CreateTranslation(plain.Bounds.MidX, plain.Bounds.MidY);
        expected = SKMatrix.Concat(expected, SKMatrix.CreateRotationDegrees(30));
        expected = SKMatrix.Concat(expected, SKMatrix.CreateScale(2, 2));
        expected = SKMatrix.Concat(expected, SKMatrix.CreateTranslation(-plain.Bounds.MidX, -plain.Bounds.MidY));
        Assert.Equal(expected.MapRect(plain.Graphemes[0].Bounds), transformed.Graphemes[0].Bounds);
        Assert.Equal(expected.MapRect(plain.Graphemes[^1].Bounds), transformed.Graphemes[^1].Bounds);
        var geometry = renderer.GetLayerGeometry(document, new(0), document.Layers[0].Id)!;
        var local = expected.MapPoint(plain.Graphemes[0].Bounds.MidX, plain.Graphemes[0].Bounds.MidY);
        Assert.True(geometry.ContainsWorldPoint(geometry.LocalToWorld.MapPoint(local)));
        Assert.False(geometry.ContainsWorldPoint(geometry.LocalToWorld.MapPoint(transformed.Bounds.Left, transformed.Bounds.Top)));
    }

    [Fact]
    public void PartialLigatureTransformKeepsOriginalShapingAndZeroScaleIsSafe()
    {
        var document = Document("ffi");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1) { Scale = new(0, 0), Rotation = 60 };
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        using var renderer = Renderer();
        var plain = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var layout = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));

        Assert.Equal(plain.Runs.Length, layout.Runs.Length);
        Assert.Equal(plain.Graphemes[0].Bounds, layout.Graphemes[0].Bounds);
        Assert.Equal(0, layout.Graphemes[1].Bounds.Width);
        Assert.Equal(plain.Graphemes[2].Bounds, layout.Graphemes[2].Bounds);
        Assert.Contains(Pixels(renderer, document, new(0)), value => value > 0);
        Assert.InRange(layout.HitTest(new(layout.Bounds.MidX, layout.Bounds.MidY)).Utf16Offset, 0, 3);
    }

    [Fact]
    public void StateAnimationInvalidatesFrameCacheAndOutlineStepHidesInactiveStrokeLast()
    {
        var document = Document("A");
        var line = document.Subtitles[0] with
        {
            Karaoke = [new(0, 1, new(1), new(2), SceneColor.White) { HighlightKind = KaraokeHighlightKind.OUTLINE_STEP }],
            Style = document.Subtitles[0].Style with { Fill = SceneColor.Transparent, Stroke = SceneColor.White }
        };
        document = document with { Subtitles = [line], Layers = [document.Layers[0] with { Tracks =
            [new(new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, State: SubtitleAnimationState.INACTIVE), [new(new(0), 4), new(new(2), 8)])] }] };
        using var renderer = Renderer();
        Assert.DoesNotContain(Pixels(renderer, document, new(0)), value => value > 0);
        var pixels = new float[document.Width * document.Height * 4];
        Assert.True(renderer.CopyCachedFramePixels(document, new(0), pixels));
        Assert.True(renderer.CopyCachedFramePixels(document, new(1, 2), pixels));
    }

    private static ProjectDocument Document(string text)
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine { Text = text, End = new(3), Style = new()
        {
            Scaling = new() { ReferenceWidth = 384, ReferenceHeight = 160 },
            FontAssetId = font.Id, FontSize = 24, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
            WrapMode = SubtitleWrapMode.NO_WRAP, Fill = SceneColor.White, StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        } };
        return new() { Width = 384, Height = 160, Assets = [font], Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End }] };
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static byte[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document, MediaTime time)
    {
        using var surface = renderer.Render(document, time);
        return surface.CopySrgbBgra();
    }
}
