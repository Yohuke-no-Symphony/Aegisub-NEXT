using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleRangePositionRenderingTests
{
    [Fact]
    public void LocalPositionMovesOnlyOwnedGraphemesCaretsAndHitGeometryWithoutReflow()
    {
        var document = Document("ABCD");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2) { Offset = new(40, 35) };
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        using var renderer = Renderer();
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(document, new(0)));
        var plain = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var moved = renderer.MeasureSubtitleTextLayout(document, evaluated);

        Assert.Equal(plain.Runs, moved.Runs);
        Assert.Equal(plain.BasePosition, moved.BasePosition);
        Assert.Equal(plain.Bounds, renderer.MeasureSubtitlePlacement(document, evaluated).Bounds);
        Assert.Equal(plain.Graphemes[0], moved.Graphemes[0] with { LocalToVisible = SKMatrix.Identity, UntransformedBounds = null });
        Assert.Equal(plain.Graphemes[3].Bounds, moved.Graphemes[3].Bounds);
        var matrix = SKMatrix.CreateTranslation(40, 35);
        Assert.Equal(matrix.MapRect(plain.Graphemes[1].Bounds), moved.Graphemes[1].Bounds);
        Assert.Equal(matrix.MapRect(plain.Graphemes[1].LeadingCaret), moved.GetCaretBounds(1));
        Assert.Equal(matrix.MapRect(plain.Graphemes[2].TrailingCaret), moved.Graphemes[2].TrailingCaret);
        Assert.Equal(1, moved.HitTest(new(moved.Graphemes[1].Bounds.MidX, moved.Graphemes[1].Bounds.MidY)).GraphemeStart);
        var geometry = renderer.GetLayerGeometry(document, new(0), document.Layers[0].Id)!;
        Assert.True(geometry.ContainsWorldPoint(geometry.LocalToWorld.MapPoint(moved.Graphemes[1].Bounds.MidX, moved.Graphemes[1].Bounds.MidY)));
        Assert.False(geometry.ContainsWorldPoint(geometry.LocalToWorld.MapPoint(plain.Graphemes[1].Bounds.MidX, plain.Graphemes[1].Bounds.MidY)));
    }

    [Fact]
    public void OffsetIsAppliedAfterScaleAndRotationAroundTheSharedCrossLineCenter()
    {
        var document = Document("AB\nCD");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 5)
        {
            Offset = new(30, 40), Scale = new(1.5, 0.75), Rotation = 30
        };
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        using var renderer = Renderer();
        var plain = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var transformed = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));
        var matrix = Matrix(plain.Bounds, range);

        Assert.Equal(plain.BasePosition, transformed.BasePosition);
        for (var index = 0; index < plain.Graphemes.Length; index++)
        {
            Assert.Equal(matrix.MapRect(plain.Graphemes[index].Bounds), transformed.Graphemes[index].Bounds);
            Assert.Equal(matrix.MapRect(plain.Graphemes[index].LeadingCaret), transformed.Graphemes[index].LeadingCaret);
        }
    }

    [Fact]
    public void OverlappingOffsetsAndOtherRangeTransformsComposeInSavedOrder()
    {
        var document = Document("ABCD");
        var offset = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2) { Offset = new(40, 30) };
        var rotated = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2) { Scale = new(2, 1), Rotation = 30 };
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [offset, rotated] }] };
        using var renderer = Renderer();
        var plain = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var transformed = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));
        var bounds = plain.GetSelectionRects(rotated.Utf16Start, rotated.Utf16Length).Aggregate(SKRect.Union);
        var expected = SKMatrix.Concat(Matrix(bounds, rotated), SKMatrix.CreateTranslation(40, 30));

        Assert.Equal(expected.MapRect(plain.Graphemes[1].Bounds), transformed.Graphemes[1].Bounds);
        var reversed = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [rotated, offset] }] };
        var result = renderer.MeasureSubtitleTextLayout(reversed, Assert.Single(SceneEvaluator.Evaluate(reversed, new(0))));
        Assert.NotEqual(transformed.Graphemes[1].Bounds, result.Graphemes[1].Bounds);
    }

    [Theory]
    [InlineData("ffi", "NotoSans.ttf")]
    [InlineData("ببب", "NotoSansArabic.ttf")]
    [InlineData("e\u0301X", "NotoSans.ttf")]
    public void PartialPositionKeepsOriginalShapingAndPaintsAtItsTranslatedLocation(string text, string font)
    {
        var document = Document(text, font);
        var boundaries = new SubtitleTextBoundaries(text);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), boundaries[1], boundaries[2] - boundaries[1]) { Offset = new(30, 35) };
        using var renderer = Renderer();
        var baselinePixels = Pixels(renderer, document, new(0));
        document = document with { Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }] };
        var plain = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var moved = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));

        Assert.Equal(plain.Runs, moved.Runs);
        Assert.Equal(plain.Graphemes.Length, moved.Graphemes.Length);
        Assert.NotEqual(baselinePixels, Pixels(renderer, document, new(0)));
        Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
    }

    [Fact]
    public void AnimatedLocalPositionInvalidatesPixelsAndReusesTheShapingCache()
    {
        var document = Document("ffi");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        document = document with
        {
            Subtitles = [document.Subtitles[0] with { AnimationRanges = [range] }],
            Layers = [document.Layers[0] with
            {
                Tracks = [new(new AnimationTrackTarget(AnimationProperty.POSITION, TextRangeId: range.Id),
                    [new(new(0), new ScenePoint(0, 0)), new(new(2), new ScenePoint(30, 40))])]
            }]
        };
        using var renderer = Renderer();
        var first = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));
        var shapers = renderer.CachedTextShaperCount;
        var before = new float[document.Width * document.Height * 4];
        var after = new float[before.Length];

        Assert.True(renderer.CopyCachedFramePixels(document, new(0), before));
        Assert.False(renderer.CopyCachedFramePixels(document, new(0), after));
        Assert.True(renderer.CopyCachedFramePixels(document, new(1), after));
        Assert.NotEqual(before, after);
        for (var frame = 1; frame <= 20; frame++)
        {
            var layout = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(frame, 10))));
            Assert.Equal(first.Runs, layout.Runs);
            Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
            Assert.Equal(shapers, renderer.CachedTextShaperCount);
        }
    }

    private static SKMatrix Matrix(SKRect bounds, SubtitleAnimationRange range)
    {
        var matrix = SKMatrix.CreateTranslation(bounds.MidX + (float)range.Offset.X, bounds.MidY + (float)range.Offset.Y);
        matrix = SKMatrix.Concat(matrix, SKMatrix.CreateRotationDegrees((float)range.Rotation));
        matrix = SKMatrix.Concat(matrix, SKMatrix.CreateScale((float)range.Scale.X, (float)range.Scale.Y));
        return SKMatrix.Concat(matrix, SKMatrix.CreateTranslation(-bounds.MidX, -bounds.MidY));
    }

    private static ProjectDocument Document(string text, string fontName = "NotoSans.ttf")
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/" + fontName);
        var line = new SubtitleLine
        {
            Text = text, End = new(3), Style = new()
            {
                Scaling = new() { ReferenceWidth = 384, ReferenceHeight = 160 },
                FontAssetId = font.Id, FontSize = 24, Alignment = TextAlignment.TOP_LEFT, Margins = new(8, 8, 8),
                WrapMode = SubtitleWrapMode.NO_WRAP, Fill = SceneColor.White, StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        return new() { Width = 384, Height = 160, Assets = [font], Subtitles = [line], Layers = [new() { SubtitleId = line.Id, End = line.End }] };
    }

    private static ProjectSceneRenderer Renderer()
    {
        return new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
    }

    private static byte[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document, MediaTime time)
    {
        using var surface = renderer.Render(document, time);
        return surface.CopySrgbBgra();
    }
}
