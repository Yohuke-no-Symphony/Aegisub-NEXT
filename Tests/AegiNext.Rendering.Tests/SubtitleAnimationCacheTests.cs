using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitleAnimationCacheTests
{
    [Fact]
    public void PaintAndLocalTransformAnimationReuseShapingAcrossManyFrames()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 1);
        var subtitle = new SubtitleLine
        {
            Text = "ffi", End = new(3), AnimationRanges = [range], Style = new()
            {
                Scaling = new() { ReferenceWidth = 256, ReferenceHeight = 128 },
                FontAssetId = font.Id, FontSize = 48, StrokeWidth = 0, ShadowColor = SceneColor.Transparent,
                Alignment = TextAlignment.TOP_LEFT, Margins = new(16, 16, 16), WrapMode = SubtitleWrapMode.NO_WRAP
            }
        };
        var document = new ProjectDocument
        {
            Width = 256, Height = 128, Assets = [font], Subtitles = [subtitle], Layers = [new()
            {
                SubtitleId = subtitle.Id, End = subtitle.End, Tracks =
                [
                    new(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id),
                        [new(new(0), SceneColor.White), new(new(2), new SceneColor(0, 0, 1, 0.5))]),
                    new(new AnimationTrackTarget(AnimationProperty.ROTATION, TextRangeId: range.Id),
                        [new(new(0), 0), new(new(2), 90)]),
                    new(new AnimationTrackTarget(AnimationProperty.SHADOW_OFFSET, TextRangeId: range.Id),
                        [new(new(0), new ScenePoint(0, 0)), new(new(2), new ScenePoint(10, 10))])
                ]
            }]
        };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var initial = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(0))));
        var shapers = renderer.CachedTextShaperCount;
        for (var frame = 1; frame < 240; frame++)
        {
            var evaluated = Assert.Single(SceneEvaluator.Evaluate(document, new(frame, 120)));
            var layout = renderer.MeasureSubtitleTextLayout(document, evaluated);
            Assert.Equal(initial.Runs, layout.Runs);
            Assert.Equal(1, renderer.CachedSubtitleLayoutCount);
            Assert.Equal(shapers, renderer.CachedTextShaperCount);
        }
    }
}
