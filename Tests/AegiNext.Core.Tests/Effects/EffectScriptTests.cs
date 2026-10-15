using System.Globalization;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptTests
{
    private const string WEIGHTED = """
        effect "weighted" version 1
        short-clip compress
        segment enter fixed 300ms
            at 0 opacity 0
            at 1 opacity 1
        end
        segment first flex 1
            at 0 opacity 1
            at 1 opacity 0.8
        end
        segment second flex 3
            at 0 opacity 0.8
            at 1 opacity 0.5
        end
        segment exit fixed 300ms
            at 0 opacity 0.5
            at 1 opacity 0
        end
        """;

    [Fact]
    public void FixedSegmentsAndWeightedFlexUseExactRationalBoundaries()
    {
        var script = EffectScriptRegressionParser.Parse(WEIGHTED);
        var track = Assert.Single(EffectScriptCompiler.Compile(script, new() { End = new(1) }));

        Assert.Equal(new MediaTime[] { new(0), new(3, 10), new(2, 5), new(7, 10), new(1) }, track.Keyframes.Select(key => key.Time));
        Assert.Equal(new[] { 0d, 1, 0.8, 0.5, 0 }, track.Keyframes.Select(key => key.Value.Scalar));
    }

    [Fact]
    public void DecimalFlexWeightsAndCroppedOriginKeepTheSameExactAllocation()
    {
        var source = WEIGHTED.Replace("first flex 1", "first flex 0.1", StringComparison.Ordinal)
            .Replace("second flex 3", "second flex 0.3", StringComparison.Ordinal);
        var script = EffectScriptRegressionParser.Parse(source);
        var track = Assert.Single(EffectScriptCompiler.Compile(script, new() { Start = new(10), End = new(11), AnimationOffset = new(5) }));
        Assert.Equal(new MediaTime[] { new(5), new(53, 10), new(27, 5), new(57, 10), new(6) }, track.Keyframes.Select(key => key.Time));
    }

    [Fact]
    public void UnknownPropertyDiagnosticPointsToTheRealIndentedToken()
    {
        var source = "effect \"diagnostic\" version 1\nshort-clip compress\nsegment all flex 1\n    at 0 unknown 0\nend";
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source));
        Assert.Equal(4, error.Line);
        Assert.Equal(10, error.Column);
    }

    [Fact]
    public void TheDocumentedCustomSampleIsExecutableAtShortAndLongDurations()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "slide-pop.aegifx"));
        var script = EffectScriptRegressionParser.Parse(source);
        Assert.Equal("slide-pop", script.Id);
        foreach (var duration in new MediaTime[] { new(2, 5), new(5) })
        {
            var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = duration, Transform = new(100, 200, 2, 3) };
            var tracks = EffectScriptCompiler.Compile(script, layer);
            Assert.Equal(3, tracks.Length);
            ProjectValidator.Validate(new() { Layers = [layer with { Tracks = tracks }] });
            Assert.All(tracks, track => Assert.Equal(duration, track.Keyframes[^1].Time));
        }
    }

    [Theory]
    [InlineData("fade-in-out")]
    [InlineData("fade-in")]
    [InlineData("fade-out")]
    [InlineData("pop-in")]
    [InlineData("pop-out")]
    [InlineData("slide-in")]
    [InlineData("slide-out")]
    public void EveryEmbeddedSampleCompilesAtShortNormalAndLongDurations(string id)
    {
        var template = BuiltinEffectScripts.Get(id);
        Assert.Equal(id, EffectScriptRegressionParser.Parse(template.Source).Id);
        foreach (var duration in new MediaTime[] { new(1, 1000), new(2, 5), new(3, 5), new(10), new(1001, 30000) })
        {
            var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Start = new(7), End = new MediaTime(7) + duration, AnimationOffset = new(3) };
            var tracks = EffectScriptCompiler.Compile(template.Script, layer);
            foreach (var track in tracks)
            {
                Assert.Equal(new MediaTime(3), track.Keyframes[0].Time);
                Assert.Equal(new MediaTime(3) + duration, track.Keyframes[^1].Time);
                Assert.All(track.Keyframes.Zip(track.Keyframes.Skip(1)), pair => Assert.True(pair.First.Time < pair.Second.Time));
            }

            ProjectValidator.Validate(new() { Layers = [layer with { Tracks = tracks }] });
        }
    }

    [Fact]
    public void ShortFadeCompressesBothFixedEdgesWithoutLosingItsEndpoint()
    {
        var tracks = EffectScriptCompiler.Compile(BuiltinEffectScripts.Get("fade-in-out").Script, new() { End = new(2, 5) });
        var track = Assert.Single(tracks);

        Assert.Equal(new MediaTime[] { new(0), new(1, 5), new(2, 5) }, track.Keyframes.Select(key => key.Time));
        Assert.Equal(new[] { 0d, 1, 0 }, track.Keyframes.Select(key => key.Value.Scalar));
        Assert.Equal(KeyframeInterpolation.EASE_IN, track.Keyframes[1].Interpolation);
    }

    [Fact]
    public void AZeroLengthFlexWithChangingValuesIsRejectedInsteadOfSilentlyLosingKeys()
    {
        var script = EffectScriptRegressionParser.Parse(WEIGHTED);
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, new() { End = new(1, 2) }));
        Assert.Equal(9, error.Line);
        Assert.Contains("共享端点", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedBoundaryUsesTheRightSegmentsOutgoingInterpolation()
    {
        var script = EffectScriptRegressionParser.Parse("""
            effect "boundary" version 1
            short-clip compress
            segment first fixed 1s
                at 0 opacity 0 linear
                at 1 opacity 0.5 hold
            end
            segment second flex 1
                at 0 opacity 0.5 ease-in
                at 1 opacity 1
            end
            """);
        var track = Assert.Single(EffectScriptCompiler.Compile(script, new() { End = new(2) }));

        Assert.Equal(3, track.Keyframes.Length);
        Assert.Equal(KeyframeInterpolation.EASE_IN, track.Keyframes[1].Interpolation);
        Assert.Equal(0.53125, SceneEvaluator.EvaluateScalarTrack(track, new(5, 4)), 12);
    }

    [Fact]
    public void UnmentionedTimeHoldsAtBothEndsAndBetweenDeclaredSegments()
    {
        var script = EffectScriptRegressionParser.Parse("""
            effect "hold" version 1
            short-clip compress
            segment before flex 1
            end
            segment enter fixed 1s
                at 0 opacity base ease-out
                at 1 opacity 0.5 ease-in
            end
            segment middle flex 1
            end
            segment exit fixed 1s
                at 0 opacity 0.5 ease-in
                at 1 opacity 0
            end
            segment after flex 1
            end
            """);
        var track = Assert.Single(EffectScriptCompiler.Compile(script, new() { End = new(5), Opacity = 0.8 }));

        Assert.Equal(0.8, SceneEvaluator.EvaluateScalarTrack(track, new(1, 2)), 12);
        Assert.Equal(0.5, SceneEvaluator.EvaluateScalarTrack(track, new(5, 2)), 12);
        Assert.Equal(0, SceneEvaluator.EvaluateScalarTrack(track, new(9, 2)), 12);
        Assert.Equal(KeyframeInterpolation.HOLD, track.Keyframes[0].Interpolation);
        Assert.Equal(KeyframeInterpolation.HOLD, track.Keyframes[2].Interpolation);
        Assert.Equal(KeyframeInterpolation.HOLD, track.Keyframes[^2].Interpolation);
    }

    [Fact]
    public void VectorOffsetAndFactorReadTheOriginalNonUniformBase()
    {
        var script = EffectScriptRegressionParser.Parse("""
            effect "vector" version 1
            short-clip compress
            segment all flex 1
                at 0 position offset(-10, 20)
                at 0 scale factor(0.2, 0.5)
                at 0.5 position offset(30, 40)
                at 1 position base
                at 1 scale base
            end
            """);
        var tracks = EffectScriptCompiler.Compile(script, new() { Transform = new(100, 200, 2, 3), End = new(2) });

        Assert.Equal(2, tracks.Length);
        Assert.Equal(new[] { new ScenePoint(90, 220), new ScenePoint(130, 240), new ScenePoint(100, 200) },
            Find(tracks, AnimationProperty.POSITION).Keyframes.Select(frame => frame.Value.Vector));
        Assert.Equal(new ScenePoint(0.4, 1.5), Find(tracks, AnimationProperty.SCALE).Keyframes[0].Value.Vector);
    }

    [Fact]
    public void FineDecimalMillisecondsDoNotQuantizeToFramesOrTimeSpanTicks()
    {
        var source = BuiltinEffectScripts.Get("fade-in").Source.Replace("300ms", "0.000001ms", StringComparison.Ordinal);
        var track = Assert.Single(EffectScriptCompiler.Compile(EffectScriptRegressionParser.Parse(source), new() { End = new(1001, 30000) }));
        Assert.Equal(new MediaTime(1, 1000000000), track.Keyframes[1].Time);
        Assert.Equal(new MediaTime(1001, 30000), track.Keyframes[^1].Time);
    }

    [Fact]
    public void UnrepresentableExactTimeReportsACompilationErrorWithoutQuantizing()
    {
        var target = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(1, long.MaxValue) };
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(BuiltinEffectScripts.Get("fade-in-out").Script, target));
        Assert.IsType<OverflowException>(error.InnerException);
    }

    [Fact]
    public void EquivalentDecimalWeightsDoNotOverflowBecauseOfTrailingZeroScale()
    {
        var script = EffectScriptRegressionParser.Parse(EffectScriptRegressionParser.FADE_IN);
        script = script with
        {
            Segments = [script.Segments[0], script.Segments[1] with { FlexWeight = 1.0000000000000000000000000000m }]
        };
        var track = Assert.Single(EffectScriptCompiler.Compile(script, new() { End = new(1) }));
        Assert.Equal(new MediaTime(1), track.Keyframes[^1].Time);
    }

    [Fact]
    public void PositiveAndNegativeAnimationOffsetsUseTheLegalContentRange()
    {
        var script = BuiltinEffectScripts.Get("fade-in-out").Script;
        foreach (var offset in new MediaTime[] { new(5), new(-1), new(-3) })
        {
            var target = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Start = new(10), End = new(12), AnimationOffset = offset };
            var (minimum, maximum) = LayerAnimationTiming.GetRange(target);
            var track = Assert.Single(EffectScriptCompiler.Compile(script, target));
            Assert.Equal(offset, minimum);
            Assert.Equal(minimum, track.Keyframes[0].Time);
            Assert.Equal(maximum, track.Keyframes[^1].Time);
            var project = new ProjectDocument { Layers = [target with { Tracks = [track] }] };
            ProjectValidator.Validate(project);
            Assert.Equal(0, Assert.Single(SceneEvaluator.Evaluate(project, new(10))).Opacity);
            Assert.Empty(SceneEvaluator.Evaluate(project, new(12)));
        }

        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, new() { End = MediaTime.Zero, AnimationOffset = new(-1) }));
    }

    [Fact]
    public void ParserUsesInvariantNumbersAndAcceptsCommentsBomAndCrLf()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var source = "\uFEFF# source\r\n" + EffectScriptRegressionParser.POP_IN.Replace("\n", "\r\n", StringComparison.Ordinal);
            var script = EffectScriptRegressionParser.Parse(source);
            Assert.Equal(0.2, script.Segments[0].Keyframes[0].Value.X);
            Assert.Equal(new MediaTime(1, 4), script.Segments[0].FixedDuration);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("version 1", "version 2")]
    [InlineData("short-clip compress", "short-clip unknown")]
    [InlineData("250ms", "-250ms")]
    [InlineData("250ms", "0ms")]
    [InlineData("flex 1", "flex 0")]
    [InlineData("flex 1", "flex 1001")]
    [InlineData("at 0 scale", "at 1.1 scale")]
    [InlineData("at 1 scale", "at 0 scale")]
    [InlineData("at 1 scale", "at 0.9 scale")]
    [InlineData("scale", "scale-x")]
    [InlineData("factor(0.2, 0.2)", "0.2")]
    [InlineData("factor(0.2, 0.2)", "factor(NaN, 0.2)")]
    [InlineData("ease-out", "bounce")]
    [InlineData("segment stay", "segment enter")]
    [InlineData("at 0 scale", "at 0.0000001 scale")]
    public void InvalidSourceIsRejectedWithLineDiagnostics(string before, string after)
    {
        var source = EffectScriptRegressionParser.POP_IN.Replace(before, after, StringComparison.Ordinal);
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(source));
        Assert.True(error.Line > 0);
        Assert.True(error.Column > 0);
    }

    [Fact]
    public void MissingEndOversizedSourceUnknownBuiltinsAndMissingFlexAreRejected()
    {
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse("effect \"empty\" version 1\nshort-clip compress"));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse(new('x', EffectScriptParser.MAXIMUM_SOURCE_CHARACTERS + 1)));
        Assert.Throws<KeyNotFoundException>(() => BuiltinEffectScripts.Get("unknown"));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse("effect \"test\" version 1\nshort-clip compress\nsegment hold flex 1"));
        Assert.Throws<EffectScriptException>(() => EffectScriptRegressionParser.Parse("""
            effect "fixed" version 1
            short-clip compress
            segment only fixed 1s
                at 0 opacity 0
                at 1 opacity 1
            end
            """));
    }

    [Theory]
    [InlineData("opacity", "factor(2)")]
    [InlineData("scale", "factor(100000, 100000)")]
    [InlineData("position", "offset(1000000001, 0)")]
    [InlineData("blur", "-1")]
    [InlineData("path-progress", "1.1")]
    public void EvaluatedOutOfRangeValuesFailBeforeReturningTracks(string property, string value)
    {
        var script = EffectScriptRegressionParser.Parse($"""
            effect "range" version 1
            short-clip compress
            segment all flex 1
                at 0 {property} {value}
                at 1 {property} {value}
            end
            """);
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, new()));
        Assert.Equal(4, error.Line);
    }

    private static AnimationTrack Find(IEnumerable<AnimationTrack> tracks, AnimationProperty property) =>
        Assert.Single(tracks, track => track.Property == property);
}
