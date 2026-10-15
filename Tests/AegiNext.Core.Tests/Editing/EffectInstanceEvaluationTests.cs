using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Editing;

public sealed class EffectInstanceEvaluationTests
{
    [Fact]
    public void AnimatedFactorMultipliesTheCurrentNonlinearUpstreamWithoutChangingSourceTracks()
    {
        var track = new AnimationTrack(AnimationProperty.SCALE,
        [
            new(MediaTime.Zero, new ScenePoint(0, 0), KeyframeInterpolation.POWER) { Exponent = 2 },
            new(new(1), new ScenePoint(1, 1))
        ]);
        var layer = Shape() with
        {
            Tracks = [track],
            Effects = [Instance("at 0 scale factor(1, 1)\nat 1 scale factor(2, 2)")]
        };

        var evaluated = SceneEvaluator.EvaluateLayer(layer, null, new(1, 2));

        Assert.Equal(new ScenePoint(0.375, 0.375), evaluated.Transform.Scale);
        Assert.Same(track, Assert.Single(layer.Tracks));
        Assert.Empty(layer.Effects[0].Parameters);
    }

    [Fact]
    public void InstanceOrderAndDisablingRecoverTheUnmodifiedUpstream()
    {
        var factor = Instance("at 0 scale factor(2, 2)\nat 1 scale factor(2, 2)");
        var absolute = Instance("at 0 scale (3, 3)\nat 1 scale (3, 3)");
        var layer = Shape() with { Effects = [factor, absolute] };

        Assert.Equal(new ScenePoint(3, 3), SceneEvaluator.EvaluateLayer(layer, null, new(1, 2)).Transform.Scale);
        Assert.Equal(new ScenePoint(6, 6), SceneEvaluator.EvaluateLayer(layer with { Effects = [absolute, factor] }, null, new(1, 2)).Transform.Scale);
        Assert.Equal(new ScenePoint(2, 2), SceneEvaluator.EvaluateLayer(layer with { Effects = [factor, absolute with { IsEnabled = false }] }, null, new(1, 2)).Transform.Scale);
        Assert.Equal(new ScenePoint(1, 1), SceneEvaluator.EvaluateLayer(layer with { Effects = [] }, null, new(1, 2)).Transform.Scale);
    }

    [Fact]
    public void SharedSegmentEndpointAppliesTheAffineOperationOnlyOnce()
    {
        var effect = Instance("at 0 scale factor(2, 2)\nat 1 scale factor(2, 2)") with
        {
            Source = "effect \"segments\" version 3\nshort-clip compress\nscope whole current\n" +
                "segment first flex 1\nat 0 scale factor(2, 2)\nat 1 scale factor(2, 2)\nend\n" +
                "segment second flex 1\nat 0 scale factor(2, 2)\nat 1 scale factor(2, 2)\nend\nend"
        };
        var layer = Shape() with { Effects = [effect] };

        Assert.Equal(new ScenePoint(2, 2), SceneEvaluator.EvaluateLayer(layer, null, new(1, 2)).Transform.Scale);
    }

    [Fact]
    public void EffectWindowIsHalfOpenAndDoesNotHoldItsResultOutsideTheWindow()
    {
        var effect = Instance("at 0 scale factor(2, 2)\nat 1 scale factor(2, 2)") with { Start = new(1), End = new(2) };
        var layer = Shape() with { End = new(3), Effects = [effect] };

        Assert.Equal(new ScenePoint(1, 1), SceneEvaluator.EvaluateLayer(layer, null, new(1, 2)).Transform.Scale);
        Assert.Equal(new ScenePoint(2, 2), SceneEvaluator.EvaluateLayer(layer, null, new(1)).Transform.Scale);
        Assert.Equal(new ScenePoint(1, 1), SceneEvaluator.EvaluateLayer(layer, null, new(2)).Transform.Scale);
    }

    [Fact]
    public void PreparedScenesReuseTheCompiledPlanAcrossFramesAndUnrelatedNativeEdits()
    {
        var layer = Shape() with { Effects = [Instance("at 0 scale base\nat 1 scale factor(2, 2)")] };
        var document = new ProjectDocument { Layers = [layer] };
        var cache = new EffectCompilationCache();
        var prepared = new PreparedProjectScene(document, cache);

        for (var index = 0; index < 10; index++)
        {
            SceneEvaluator.Evaluate(prepared, new(index, 10));
        }
        var edited = new PreparedProjectScene(document with
        {
            Layers = [layer with { Transform = layer.Transform with { Rotation = 30 } }]
        }, cache);
        SceneEvaluator.Evaluate(edited, new(1, 2));

        Assert.Equal(1, cache.CompilationCount);
    }

    [Fact]
    public void ReassigningNodesBetweenContoursRebindsOrdinalMaskSelectors()
    {
        var first = new MaskNode { Position = new(1, 0) };
        var second = new MaskNode { Position = new(2, 0) };
        var third = new MaskNode { Position = new(3, 0) };
        var fourth = new MaskNode { Position = new(4, 0) };
        var firstContour = new MaskContour { Nodes = [first, second] };
        var secondContour = new MaskContour { Nodes = [third, fourth] };
        var layer = Shape() with
        {
            Mask = new VectorClipMask { Contours = [firstContour, secondContour] },
            Effects = [Instance("at 0 mask-node(2,1).position offset(10, 0)\nat 1 mask-node(2,1).position offset(10, 0)")]
        };
        var cache = new EffectCompilationCache();

        var original = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(layer, new(1, 2), effectCache: cache));
        var edited = layer with
        {
            Mask = new VectorClipMask
            {
                Contours = [firstContour with { Nodes = [first] }, secondContour with { Nodes = [second, third, fourth] }]
            }
        };
        var rebound = Assert.IsType<VectorClipMask>(SceneEvaluator.EvaluateMask(edited, new(1, 2), effectCache: cache));

        Assert.Equal(new ScenePoint(13, 0), original.Contours[1].Nodes[0].Position);
        Assert.Equal(new ScenePoint(12, 0), rebound.Contours[1].Nodes[0].Position);
        Assert.Equal(third.Position, rebound.Contours[1].Nodes[1].Position);
        Assert.Equal(2, cache.CompilationCount);
    }

    [Fact]
    public void GroupedColorBaseIsResolvedPerGraphemeAndNeverPersistsGeneratedRanges()
    {
        var line = new SubtitleLine
        {
            Text = "AB", End = new(1),
            Style = new() { Fill = new(1, 0, 0, 1) },
            InlineSpans = [new(1, 1, new() { Fill = new(0, 0, 1, 1) })]
        };
        var effect = Instance("at 0 fill base\nat 1 fill rgba(0, 0, 0, 1)") with
        {
            Source = Source("at 0 fill base\nat 1 fill rgba(0, 0, 0, 1)", "unit grapheme")
        };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End, Effects = [effect] };

        var evaluated = SceneEvaluator.EvaluateLayer(layer, line, new(1, 2));
        var first = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, line.Style, 0, SubtitleAnimationState.NORMAL);
        var second = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, line.InlineSpans[0].Style.ApplyTo(line.Style), 1, SubtitleAnimationState.NORMAL);

        Assert.Equal(new SceneColor(0.5, 0, 0, 1), first.Fill);
        Assert.Equal(new SceneColor(0, 0, 0.5, 1), second.Fill);
        Assert.Empty(line.AnimationRanges);
        Assert.Empty(layer.Tracks);
        Assert.Equal(2, evaluated.AnimationRanges.Length);
    }

    [Fact]
    public void WholeSubtitleFieldsExposeTheFinalValueWhileStyleEvaluationKeepsItsOriginalInput()
    {
        var line = new SubtitleLine { Text = "AB", End = new(1), Style = new() { FontSize = 20 } };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, End = line.End,
            Tracks = [new(AnimationProperty.FONT_SIZE, [new(MediaTime.Zero, 20d), new(new(1), 40d)])],
            Effects = [Instance("at 0 font-size factor(2)\nat 1 font-size factor(2)")]
        };

        var evaluated = SceneEvaluator.EvaluateLayer(layer, line, new(1, 2));

        Assert.Equal(60, evaluated.FontSize);
        Assert.True(evaluated.HasFontSizeAnimation);
        Assert.Equal(60, SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, line.Style, 0,
            SubtitleAnimationState.NORMAL).FontSize);
    }

    [Fact]
    public void UpdatingOneRotationAxisKeepsTheOtherAnimatedAxes()
    {
        var layer = Shape() with
        {
            Tracks = [new(AnimationProperty.ROTATION_3D,
            [
                new(MediaTime.Zero, new SceneVector3(10, 20, 30)),
                new(new(1), new SceneVector3(30, 40, 50))
            ])],
            Effects = [Instance("at 0 rotation-x offset(720)\nat 1 rotation-x offset(720)")]
        };

        var result = SceneEvaluator.EvaluateLayer(layer, null, new(1, 2)).Transform;

        Assert.Equal(740, result.RotationX);
        Assert.Equal(30, result.RotationY);
        Assert.Equal(40, result.Rotation);
    }

    private static ProjectLayer Shape() => new()
    {
        Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 40), End = new(1)
    };

    private static EffectInstance Instance(string frames) => new()
    {
        Name = "Test", Source = Source(frames), Start = MediaTime.Zero, End = new(1),
        Parameters = ImmutableDictionary<string, EffectScriptParameterValue>.Empty
    };

    private static string Source(string frames, string fields = "") => $$"""
        effect "runtime-test" version 3
        short-clip compress
        scope whole current
            {{fields}}
            segment animate flex 1
                {{frames}}
            end
        end
        """;
}
