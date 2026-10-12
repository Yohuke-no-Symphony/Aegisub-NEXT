using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleScalingRenderingTests
{
    [Fact]
    public void ReferenceAppearanceAndAnimationMatchLiteralCanvasSizesWithoutScalingMotion()
    {
        var source = Document();
        var line = source.Subtitles[0];
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1) { Offset = new(15, -5) };
        line = line with
        {
            InlineSpans = [new(1, 1, new() { FontSize = 36, StrokeWidth = 2 })],
            AnimationRanges = [range],
            Karaoke = [new(0, 2, new(0), new(2), new SceneColor(1, 0.5, 0))],
            KaraokeStyle = new() { PresetId = Guid.NewGuid(), PresetName = "Highlight", StrokeWidth = 3, ShadowOffset = new(-2, 3), ShadowBlur = 1 },
            KaraokeStyleSpans = [new(0, 1, new() { StrokeBlur = 0.5 }, new() { StrokeWidth = 0 })]
        };
        var layer = source.Layers[0] with
        {
            Transform = new(X: 11, Y: 7), Blur = 1,
            Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id), [new(new(0), 36), new(new(2), 48)]),
                new(AnimationProperty.STROKE_WIDTH, [new(new(0), 1), new(new(2), 3)]),
                new(AnimationProperty.SHADOW_OFFSET, [new(new(0), new ScenePoint(-2, 3)), new(new(2), new ScenePoint(2, 5))]),
                new(AnimationProperty.POSITION, [new(new(0), new ScenePoint(11, 7))]),
                new(AnimationProperty.BLUR, [new(new(0), 1)])
            ]
        };
        source = source with { Subtitles = [line], Layers = [layer] };
        var literal = source with
        {
            Subtitles = [line with
            {
                Style = line.Style with
                {
                    Scaling = new() { ReferenceWidth = 800, ReferenceHeight = 480 },
                    FontSize = 48, LetterSpacing = 4, StrokeWidth = 2, FillBlur = 1, StrokeBlur = 1,
                    ShadowOffset = new(4, 6), ShadowBlur = 2, Margins = new(20, 24, 28)
                },
                InlineSpans = [new(1, 1, new() { FontSize = 72, StrokeWidth = 4 })],
                KaraokeStyle = line.KaraokeStyle with { StrokeWidth = 6, ShadowOffset = new(-4, 6), ShadowBlur = 2 },
                KaraokeStyleSpans = [new(0, 1, new() { StrokeBlur = 1 }, new() { StrokeWidth = 0 })]
            }],
            Layers = [layer with { Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id), [new(new(0), 72), new(new(2), 96)]),
                new(AnimationProperty.STROKE_WIDTH, [new(new(0), 2), new(new(2), 6)]),
                new(AnimationProperty.SHADOW_OFFSET, [new(new(0), new ScenePoint(-4, 6)), new(new(2), new ScenePoint(4, 10))]),
                layer.Tracks[3], layer.Tracks[4]
            ] }]
        };
        using var renderer = Renderer();
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(source, new(1)));
        var measured = renderer.MeasureSubtitleTextLayout(source, evaluated);
        var placement = renderer.MeasureSubtitlePlacement(source, evaluated);
        var geometry = renderer.GetLayerGeometry(source, new(1), layer.Id)!;
        var pixels = Pixels(renderer, source, new(1));

        Assert.Equal(48, measured.Runs[0].Style.FontSize);
        Assert.Equal(84, measured.Runs[1].Style.FontSize);
        Assert.Contains(pixels, value => value > 0);
        Assert.Equal(pixels, Pixels(renderer, literal, new(1)));
        var expectedLayer = Assert.Single(SceneEvaluator.Evaluate(literal, new(1)));
        Assert.Equal(measured.Bounds, renderer.MeasureSubtitleTextLayout(literal, expectedLayer).Bounds);
        Assert.Equal(placement, renderer.MeasureSubtitlePlacement(literal, expectedLayer));
        Assert.Equal(geometry.WorldCorners, renderer.GetLayerGeometry(literal, new(1), layer.Id)!.WorldCorners);
        Assert.Equal(new ScenePoint(15, -5), source.Subtitles[0].AnimationRanges[0].Offset);
        Assert.Equal(24, source.Subtitles[0].Style.FontSize);
        Assert.Equal(36, source.Layers[0].Tracks[0].Keyframes[0].Value.Scalar);
    }

    [Fact]
    public void AutomaticPlacementConvertsBackToReferenceUnitsWithoutMovingPixels()
    {
        var document = Document();
        using var renderer = Renderer();
        var measurement = renderer.MeasureSubtitlePlacement(document, document.Subtitles[0]);
        var position = renderer.ResolveSubtitlePosition(document, document.Subtitles[0]);
        var explicitDocument = document with
        {
            Subtitles = [document.Subtitles[0] with { Style = document.Subtitles[0].Style with { Position = position } }]
        };

        Assert.Equal(measurement.Position.Offset.X / 2, position.Offset.X);
        Assert.Equal(measurement.Position.Offset.Y / 2, position.Offset.Y);
        Assert.Equal(Pixels(renderer, document, new(0)), Pixels(renderer, explicitDocument, new(0)));
    }

    [Theory]
    [InlineData(0, 14.4)]
    [InlineData(0.5, 24)]
    [InlineData(1, 40)]
    public void PortraitMatchWeightControlsActualTypography(double weight, double expectedSize)
    {
        var document = Document();
        document = document with
        {
            Width = 240, Height = 400,
            Subtitles = [document.Subtitles[0] with { Style = document.Subtitles[0].Style with
            {
                Scaling = new() { ReferenceWidth = 400, ReferenceHeight = 240, MatchWidthOrHeight = weight }
            } }]
        };
        using var renderer = Renderer();

        var layout = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);

        Assert.Equal(expectedSize, layout.Runs[0].Style.FontSize, 10);
        Assert.Same(layout, renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]));
        Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
    }

    [Fact]
    public void CanvasChangeInvalidatesProjectionAndHitGeometryFollowsNewSize()
    {
        var document = Document();
        using var renderer = Renderer();
        var first = renderer.MeasureSubtitleTextLayout(document, document.Subtitles[0]);
        var changed = document with { Width = 400, Height = 240 };
        var second = renderer.MeasureSubtitleTextLayout(changed, changed.Subtitles[0]);
        var geometry = renderer.GetLayerGeometry(changed, MediaTime.Zero, changed.Layers[0].Id)!;
        var point = geometry.LocalToWorld.MapPoint(new SKPoint(second.Graphemes[0].Bounds.MidX, second.Graphemes[0].Bounds.MidY));

        Assert.Equal(48, first.Runs[0].Style.FontSize);
        Assert.Equal(24, second.Runs[0].Style.FontSize);
        Assert.Equal(changed.Layers[0].Id, renderer.HitTestLayer(changed, MediaTime.Zero, point));
        Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
    }

    private static ProjectDocument Document()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine
        {
            Text = "AB", End = new(3), Style = new()
            {
                Scaling = new() { ReferenceWidth = 400, ReferenceHeight = 240 },
                FontAssetId = font.Id, FontSize = 24, LetterSpacing = 2, StrokeWidth = 1,
                FillBlur = 0.5, StrokeBlur = 0.5, ShadowOffset = new(2, 3), ShadowBlur = 1,
                Margins = new(10, 12, 14), WrapMode = SubtitleWrapMode.NO_WRAP
            }
        };
        return new() { Width = 800, Height = 480, Assets = [font], Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End }] };
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static byte[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document, MediaTime time)
    {
        using var surface = renderer.Render(document, time);
        return surface.CopySrgbBgra();
    }
}
