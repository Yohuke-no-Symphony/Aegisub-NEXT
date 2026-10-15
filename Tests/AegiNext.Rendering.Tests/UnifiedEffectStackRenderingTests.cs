using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.Tests;

public sealed class UnifiedEffectStackRenderingTests
{
    [Theory]
    [InlineData(true, 24)]
    [InlineData(false, 48)]
    public void InterleavedNativeTypographyPaintsItsFinalValueExactlyOnceForMixedInlineFonts(bool nativeAbove, int expectedSize)
    {
        var document = TypographyDocument(nativeAbove);
        var line = document.Subtitles[0];
        var expected = document with
        {
            Layers = [document.Layers[0] with { Effects = [], Tracks = [], EffectRowOrder = [] }],
            Subtitles = [line with { Style = line.Style with { FontSize = expectedSize }, InlineSpans = [] }]
        };
        using var renderer = Renderer();
        using var reference = Renderer();

        var layout = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(1, 2))));

        Assert.All(layout.Runs, run => Assert.Equal(expectedSize, run.Style.FontSize));
        Assert.Equal(Pixels(reference, expected), Pixels(renderer, document));
    }

    [Fact]
    public void ManualOnlyScopeReorderingInvalidatesThePaintCacheAndMatchesAnOrdinaryStaticStyle()
    {
        var document = Document();
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = document.Subtitles[0] with { AnimationRanges = [range] };
        var whole = new AnimationTrack(AnimationProperty.FILL, [new(new(0), new SceneColor(0, 1, 0, 1))]);
        var scoped = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id),
            [new(new(0), new SceneColor(1, 0, 0, 1))]);
        var layer = document.Layers[0] with { Tracks = [whole, scoped], EffectRowOrder = [new(whole.Target), new(scoped.Target)] };
        document = document with { Subtitles = [line], Layers = [layer] };
        var expected = document with
        {
            Layers = [layer with { Tracks = [], EffectRowOrder = [] }],
            Subtitles = [line with { Style = line.Style with { Fill = new(0, 1, 0, 1) } }]
        };
        using var renderer = Renderer();
        using var reference = Renderer();
        var first = Pixels(renderer, document);

        Assert.Equal(Pixels(reference, expected), first);
        var reordered = document with { Layers = [layer with { EffectRowOrder = [new(scoped.Target), new(whole.Target)] }] };
        Assert.NotEqual(first, Pixels(renderer, reordered));
        Assert.Equal(first, Pixels(renderer, document));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AssSamplingUsesTheSameReorderedNativeAndScriptStackWithoutChangingItsSource(bool nativeAbove)
    {
        var document = TypographyDocument(nativeAbove);
        var layer = document.Layers[0];
        var native = layer.Tracks[0] with { Keyframes =
            [new(new(0), 20d, KeyframeInterpolation.POWER) { Exponent = 2 }, new(new(1), 28d)] };
        document = document with { Layers = [layer with { Tracks = [native] }] };
        var written = AssSubtitleFormat.Write(document);
        var imported = Assert.Single(AssSubtitleFormat.Parse(written.Text, document.Width, document.Height).Clips);
        var importedLayer = new ProjectLayer { SubtitleId = imported.Line.Id, Start = imported.Line.Start, End = imported.Line.End,
            AnimationOffset = imported.ContentOffset, Transform = imported.Transform, Mask = imported.Mask, Tracks = imported.Tracks };
        var prepared = new PreparedProjectScene(document);

        foreach (var time in new[] { new MediaTime(1, 4), new(1, 2), new(3, 4) })
        {
            var original = SceneEvaluator.EvaluateLayer(prepared, layer.Id, time);
            var exported = SceneEvaluator.EvaluateLayer(importedLayer, imported.Line, time + imported.ContentOffset);
            var expected = SubtitleAnimationEvaluation.ApplyTypographyAnimations(original, document.Subtitles[0].Style, 0).FontSize;
            var actual = SubtitleAnimationEvaluation.ApplyTypographyAnimations(exported, imported.Line.Style, 0).FontSize;
            Assert.InRange(Math.Abs(expected - actual), 0, 0.05);
        }
        Assert.Contains(written.Diagnostics, diagnostic => diagnostic.Code == "Ass.EffectInstances");
        Assert.Same(native, document.Layers[0].Tracks[0]);
        Assert.Single(document.Layers[0].Effects);
        Assert.Equal<TimelineEffectRowIdentity>(layer.EffectRowOrder, document.Layers[0].EffectRowOrder);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssManualOnlyStateProjectionHonorsWholeAndRangeRowOrder(bool rangeAbove)
    {
        var document = Document();
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = document.Subtitles[0] with { AnimationRanges = [range], Karaoke =
            [new(0, 2, MediaTime.Zero, new(1), SceneColor.White) { HighlightKind = KaraokeHighlightKind.SWEEP }] };
        var whole = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, State: SubtitleAnimationState.INACTIVE),
            [new(new(0), 2d)]);
        var scoped = new AnimationTrack(whole.Target with { TextRangeId = range.Id }, [new(new(0), 6d)]);
        TimelineEffectRowIdentity wholeRow = new(whole.Target);
        TimelineEffectRowIdentity rangeRow = new(scoped.Target);
        var layer = document.Layers[0] with { Tracks = [whole, scoped],
            EffectRowOrder = rangeAbove ? [rangeRow, wholeRow] : [wholeRow, rangeRow] };
        document = document with { Subtitles = [line], Layers = [layer] };
        var written = AssSubtitleFormat.Write(document);
        var clip = Assert.Single(AssSubtitleFormat.Parse(written.Text, document.Width, document.Height).Clips);
        var imported = new ProjectLayer { SubtitleId = clip.Line.Id, Start = clip.Line.Start, End = clip.Line.End,
            AnimationOffset = clip.ContentOffset, Transform = clip.Transform, Mask = clip.Mask, Tracks = clip.Tracks };
        var source = SceneEvaluator.EvaluateLayer(new PreparedProjectScene(document), layer.Id, new(1, 2));
        var exported = SceneEvaluator.EvaluateLayer(imported, clip.Line, clip.ContentOffset + new MediaTime(1, 2));

        foreach (var offset in new[] { 0, 1 })
        {
            var expected = SubtitleAnimationEvaluation.ApplyStyleAnimations(source, line.Style, offset, SubtitleAnimationState.INACTIVE).StrokeWidth;
            var staticStyle = clip.Line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= offset &&
                offset < span.Utf16Start + span.Utf16Length)?.Style.ApplyTo(clip.Line.Style) ?? clip.Line.Style;
            var actual = SubtitleAnimationEvaluation.ApplyStyleAnimations(exported, staticStyle, offset, SubtitleAnimationState.NORMAL).StrokeWidth;
            Assert.Equal(rangeAbove && offset == 0 ? 6 : 2, expected);
            Assert.Equal(expected, actual, 6);
        }
        Assert.Empty(layer.Effects);
        Assert.Same(whole, layer.Tracks[0]);
        Assert.Same(range, line.AnimationRanges[0]);
    }

    private static ProjectDocument TypographyDocument(bool nativeAbove)
    {
        var document = Document();
        var native = new AnimationTrack(AnimationProperty.FONT_SIZE, [new(new(0), 24d)]);
        var effect = new EffectInstance { Name = "Typography", End = new(1), Source = """
            effect "type-stack" version 3
            short-clip compress
            scope whole current
                segment all flex 1
                    at 0 font-size factor(2)
                    at 1 font-size factor(2)
                end
            end
            """ };
        TimelineEffectRowIdentity manual = new(native.Target);
        TimelineEffectRowIdentity script = new(InstanceId: effect.Id);
        return document with { Layers = [document.Layers[0] with { Tracks = [native], Effects = [effect],
            EffectRowOrder = nativeAbove ? [manual, script] : [script, manual] }] };
    }

    private static ProjectDocument Document()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine { Text = "AB", End = new(1), Style = new()
        {
            Scaling = new() { ReferenceWidth = 384, ReferenceHeight = 180 }, FontAssetId = font.Id,
            FontFamily = "Noto Sans", FontSize = 18, Alignment = TextAlignment.TOP_LEFT, Margins = new(24, 24, 8),
            WrapMode = SubtitleWrapMode.NO_WRAP, StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        }, InlineSpans = [new(1, 1, new() { FontSize = 36 })] };
        return new() { Width = 384, Height = 180, Assets = [font], Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, End = line.End }] };
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static float[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document)
    {
        var pixels = new float[document.Width * document.Height * 4];
        renderer.CopyCachedFramePixels(document, new(1, 2), pixels);
        Assert.Contains(pixels, value => value > 0);
        return pixels;
    }
}
