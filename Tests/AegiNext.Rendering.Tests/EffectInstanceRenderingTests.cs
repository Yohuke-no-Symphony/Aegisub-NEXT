using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class EffectInstanceRenderingTests
{
    [Fact]
    public void TransientGroupGeometryMatchesNativeRangesWithoutPersistingThem()
    {
        var document = Document();
        var effect = Instance("at 0 scale factor(2, 2)\nat 1 scale factor(2, 2)", "unit grapheme");
        document = document with { Layers = [document.Layers[0] with { Effects = [effect] }] };
        var native = document with
        {
            Layers = [document.Layers[0] with { Effects = [] }],
            Subtitles = [document.Subtitles[0] with
            {
                AnimationRanges =
                [
                    new(Guid.NewGuid(), 0, 1) { Scale = new(2, 2) },
                    new(Guid.NewGuid(), 1, 1) { Scale = new(2, 2) }
                ]
            }]
        };
        using var actualRenderer = Renderer();
        using var expectedRenderer = Renderer();

        Assert.Equal(Pixels(expectedRenderer, native), Pixels(actualRenderer, document));
        Assert.Empty(document.Subtitles[0].AnimationRanges);
        Assert.Empty(document.Layers[0].Tracks);
        Assert.Equal(2, Assert.Single(SceneEvaluator.Evaluate(document, new(1, 2))).AnimationRanges.Length);
    }

    [Fact]
    public void TypographyFactorsPreserveMixedFontsAndAreAppliedExactlyOnceDuringPainting()
    {
        var document = Document();
        var line = document.Subtitles[0] with { InlineSpans = [new(1, 1, new() { FontSize = 36 })] };
        document = document with
        {
            Subtitles = [line],
            Layers = [document.Layers[0] with { Effects = [Instance("at 0 font-size factor(2)\nat 1 font-size factor(2)", "unit grapheme")] }]
        };
        var native = document with
        {
            Layers = [document.Layers[0] with { Effects = [] }],
            Subtitles = [line with { Style = line.Style with { FontSize = 48 }, InlineSpans = [new(1, 1, new() { FontSize = 72 })] }]
        };
        using var actualRenderer = Renderer();
        using var expectedRenderer = Renderer();
        var actual = actualRenderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(1, 2))));
        var expected = expectedRenderer.MeasureSubtitleTextLayout(native, native.Subtitles[0]);

        Assert.Equal(expected.Bounds, actual.Bounds);
        Assert.Equal(new double[] { 48, 72 }, actual.Runs.Select(run => run.Style.FontSize).ToArray());
        Assert.Equal(Pixels(expectedRenderer, native), Pixels(actualRenderer, document));
    }

    [Fact]
    public void CanvasScalingValidatesEffectValuesInTheirNativeReferenceUnits()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            Style = document.Subtitles[0].Style with
            {
                FontSize = 900,
                Scaling = new() { ReferenceWidth = 64, ReferenceHeight = 30 }
            }
        };
        document = document with
        {
            Subtitles = [line],
            Layers = [document.Layers[0] with { Effects = [Instance("at 0 font-size factor(1)\nat 1 font-size factor(1)")] }]
        };
        using var renderer = Renderer();

        var layout = renderer.MeasureSubtitleTextLayout(document, Assert.Single(SceneEvaluator.Evaluate(document, new(1, 2))));

        Assert.All(layout.Runs, run => Assert.Equal(5400, run.Style.FontSize));
    }

    [Fact]
    public void AnimatedStyleInvalidatesForegroundCacheAndIdenticalSamplesReuseIt()
    {
        var document = Document();
        document = document with
        {
            Layers = [document.Layers[0] with
            {
                Effects = [Instance("at 0 fill rgba(1, 0, 0, 1)\nat 1 fill rgba(0, 0, 1, 1)", "unit grapheme")]
            }]
        };
        using var renderer = Renderer();
        var before = new float[document.Width * document.Height * 4];
        var after = new float[before.Length];

        Assert.True(renderer.CopyCachedFramePixels(document, new(1, 4), before));
        Assert.True(renderer.CopyCachedFramePixels(document, new(3, 4), after));
        Assert.NotEqual(before, after);
        Assert.False(renderer.CopyCachedFramePixels(document, new(3, 4), after));
    }

    [Fact]
    public void ChangingAParameterInvalidatesSceneAndTypographyCaches()
    {
        var document = Document();
        var effect = Instance("at 0 font-size factor(amount)\nat 1 font-size factor(amount)") with
        {
            Source = "effect \"render-test\" version 3\nshort-clip compress\nvar amount = 1\n" +
                "scope whole current\nsegment animate flex 1\nat 0 font-size factor(amount)\nat 1 font-size factor(amount)\nend\nend"
        };
        document = document with { Layers = [document.Layers[0] with { Effects = [effect] }] };
        using var renderer = Renderer();
        var before = Pixels(renderer, document);
        var changed = effect with
        {
            Parameters = effect.Parameters.Add("amount", AegiNext.Core.Effects.EffectScriptParameterValue.FromNumber(2))
        };
        var after = Pixels(renderer, document with { Layers = [document.Layers[0] with { Effects = [changed] }] });

        Assert.NotEqual(before, after);
        Assert.Equal(before, Pixels(renderer, document));
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static float[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document)
    {
        var pixels = new float[document.Width * document.Height * 4];
        renderer.CopyCachedFramePixels(document, new(1, 2), pixels);
        Assert.Contains(pixels, value => value > 0);
        return pixels;
    }

    private static EffectInstance Instance(string frames, string fields = "") => new()
    {
        Name = "Render test", Start = MediaTime.Zero, End = new(1),
        Source = $$"""
            effect "render-test" version 3
            short-clip compress
            scope whole current
                {{fields}}
                segment animate flex 1
                    {{frames}}
                end
            end
            """
    };

    private static ProjectDocument Document()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine
        {
            Text = "AB", End = new(1), Style = new()
            {
                Scaling = new() { ReferenceWidth = 384, ReferenceHeight = 180 },
                FontAssetId = font.Id, FontSize = 24, Alignment = TextAlignment.TOP_LEFT, Margins = new(32, 32, 8),
                WrapMode = SubtitleWrapMode.NO_WRAP, StrokeWidth = 0, ShadowColor = SceneColor.Transparent
            }
        };
        return new() { Width = 384, Height = 180, Assets = [font], Subtitles = [line], Layers = [new() { SubtitleId = line.Id, End = line.End }] };
    }
}
