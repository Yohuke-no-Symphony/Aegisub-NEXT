using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptTextRangeTests
{
    [Theory]
    [InlineData("font-size", "factor(2)", AnimationProperty.FONT_SIZE)]
    [InlineData("shadow-offset", "offset(2, -3)", AnimationProperty.SHADOW_OFFSET)]
    [InlineData("shadow-blur", "factor(2)", AnimationProperty.SHADOW_BLUR)]
    [InlineData("shadow-color", "rgba(2, 0.5, -0.1, 0.4)", AnimationProperty.SHADOW_COLOR)]
    public void AddedPropertiesUseSubtitleBaseValuesAndNativeDimensions(string name, string value, AnimationProperty property)
    {
        var style = new SubtitleStyle { FontSize = 40, ShadowOffset = new(3, 4), ShadowBlur = 6 };
        var script = EffectScriptParser.Parse(Source(name, value));
        var layer = new ProjectLayer { SubtitleId = Guid.NewGuid() };
        var track = Assert.Single(EffectScriptCompiler.Compile(script, layer, style));

        Assert.Equal(property, track.Property);
        Assert.Equal(AnimationPropertyMetadata.GetValueKind(property), track.Keyframes[0].Value.Kind);
        var expected = property switch
        {
            AnimationProperty.FONT_SIZE => AnimationValue.FromScalar(80),
            AnimationProperty.SHADOW_OFFSET => AnimationValue.FromVector(new(5, 1)),
            AnimationProperty.SHADOW_BLUR => AnimationValue.FromScalar(12),
            AnimationProperty.SHADOW_COLOR => AnimationValue.FromColor(new(2, 0.5, -0.1, 0.4)),
            _ => throw new InvalidOperationException()
        };
        Assert.Equal(expected, SceneEvaluator.EvaluateTrack(track, new(1)));
        Assert.True(EffectScriptPropertyMetadata.TryGetProperty(name, out var scriptProperty));
        Assert.Equal(property, EffectScriptPropertyMetadata.GetAnimationProperty(scriptProperty));
        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, layer));
        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, new() { Kind = LayerKind.SHAPE }, style));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 5)]
    [InlineData(1, 10000)]
    public void ScopedTypographyAndTransformsResolveInheritedStyleAndKeepExactClipTiming(int numerator, int denominator)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2) { Scale = new(2, 3), Rotation = 10 };
        var line = new SubtitleLine
        {
            Text = "ABCD", AnimationRanges = [range],
            InlineSpans = [new(1, 2, new() { FontSize = 30 })]
        };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = new(numerator, denominator), AnimationOffset = new(1, 4) };
        var context = new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id);
        var script = EffectScriptParser.Parse("""
            effect "range-entrance" version 1
            short-clip compress
            segment enter fixed 200ms
                at 0 font-size factor(2)
                at 1 font-size base
                at 0 scale factor(0.5, 0.5)
                at 1 scale base
                at 0 rotation offset(15)
                at 1 rotation base
            end
            segment stay flex 1
            end
            """);
        var tracks = EffectScriptCompiler.Compile(script, layer, line.Style, context, line);

        Assert.Equal(3, tracks.Length);
        Assert.All(tracks, track =>
        {
            Assert.Equal(range.Id, track.Target.TextRangeId);
            Assert.Equal(layer.AnimationOffset, track.Keyframes[0].Time);
            Assert.Equal(layer.AnimationOffset + layer.End, track.Keyframes[^1].Time);
        });
        Assert.Equal(60, SceneEvaluator.EvaluateScalarTrack(tracks.Single(track => track.Property == AnimationProperty.FONT_SIZE), layer.AnimationOffset));
        Assert.Equal(new ScenePoint(1, 1.5), SceneEvaluator.EvaluateVectorTrack(tracks.Single(track => track.Property == AnimationProperty.SCALE), layer.AnimationOffset));
        Assert.Equal(25, SceneEvaluator.EvaluateVector3Track(tracks.Single(track => track.Property == AnimationProperty.ROTATION_3D), layer.AnimationOffset).Z);
        Assert.Equal(30, tracks.Single(track => track.Property == AnimationProperty.FONT_SIZE).Keyframes[^1].Value.Scalar);
    }

    [Theory]
    [InlineData("opacity", "1", SubtitleAnimationState.NORMAL)]
    [InlineData("position", "(1, 2)", SubtitleAnimationState.ACTIVE)]
    [InlineData("font-size", "40", SubtitleAnimationState.ACTIVE)]
    [InlineData("scale", "(1, 1)", SubtitleAnimationState.INACTIVE)]
    public void ScopePropertiesAreValidatedAtTheirSourceLocation(string property, string value, SubtitleAnimationState state)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine { Text = "A", AnimationRanges = [range] };
        var context = new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id, State: state);
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(
            EffectScriptParser.Parse(Source(property, value)), new() { SubtitleId = line.Id }, line.Style, context, line));

        Assert.Equal(4, error.Line);
    }

    [Theory]
    [InlineData("base")]
    [InlineData("offset(1)")]
    [InlineData("factor(2)")]
    public void MixedInheritedRangeBaseProducesALocatedDiagnostic(string value)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range], InlineSpans = [new(1, 1, new() { FontSize = 50 })] };
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(
            EffectScriptParser.Parse(Source("font-size", value)), new() { SubtitleId = line.Id }, line.Style,
            new(AnimationProperty.FONT_SIZE, TextRangeId: range.Id), line));

        Assert.Equal(4, error.Line);
        Assert.Contains("混合", error.Message);
    }

    [Fact]
    public void ExplicitValuesCanReplaceMixedRangeStylesWithoutReadingTheirBase()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range], InlineSpans = [new(1, 1, new() { FontSize = 50 })] };
        var track = Assert.Single(EffectScriptCompiler.Compile(EffectScriptParser.Parse(Source("font-size", "42")),
            new() { SubtitleId = line.Id }, line.Style, new(AnimationProperty.FONT_SIZE, TextRangeId: range.Id), line));

        Assert.Equal(42, SceneEvaluator.EvaluateScalarTrack(track, new(1)));
    }

    [Fact]
    public void MixedBaseCannotSilentlySeedAnUndeclaredLeadingInterval()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range], InlineSpans = [new(1, 1, new() { FontSize = 50 })] };
        var source = """
            effect "late-range" version 1
            short-clip compress
            segment before fixed 1s
            end
            segment later flex 1
                at 0 font-size 42
                at 1 font-size 42
            end
            """;
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(EffectScriptParser.Parse(source),
            new() { SubtitleId = line.Id }, line.Style, new(AnimationProperty.FONT_SIZE, TextRangeId: range.Id), line));

        Assert.Equal(6, error.Line);
        Assert.Contains("混合", error.Message);
    }

    [Theory]
    [InlineData("font-size", "0")]
    [InlineData("font-size", "4097")]
    [InlineData("font-size", "factor(-1)")]
    [InlineData("shadow-blur", "-1")]
    [InlineData("shadow-blur", "513")]
    [InlineData("shadow-offset", "(1000000001, 0)")]
    public void AddedPropertiesRejectValuesOutsideNativeBounds(string property, string value)
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(
            EffectScriptParser.Parse(Source(property, value)), new() { SubtitleId = Guid.NewGuid() }, new()));

        Assert.Equal(4, error.Line);
    }

    [Theory]
    [InlineData("font-size", "(1, 2)")]
    [InlineData("shadow-offset", "1")]
    [InlineData("shadow-blur", "rgba(0, 0, 0, 1)")]
    [InlineData("shadow-color", "factor(1)")]
    [InlineData("shadow-color", "rgba(0, 0, 0, 2)")]
    public void AddedPropertiesRejectWrongDimensionsAndColorFunctions(string property, string value)
    {
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse(Source(property, value)));

        Assert.Equal(4, error.Line);
    }

    [Fact]
    public void ExplicitWholeLineNormalContextKeepsTheExistingStyleOnlyCompilerContract()
    {
        var script = EffectScriptParser.Parse(Source("font-size", "base"));
        var layer = new ProjectLayer { SubtitleId = Guid.NewGuid() };
        var style = new SubtitleStyle { FontSize = 40 };
        var original = Assert.Single(EffectScriptCompiler.Compile(script, layer, style));
        var contextual = Assert.Single(EffectScriptCompiler.Compile(script, layer, style, new(AnimationProperty.FILL)));

        Assert.Equal(original.Target, contextual.Target);
        Assert.Equal(original.Keyframes.Select(frame => (frame.Time, frame.Value, frame.Interpolation)),
            contextual.Keyframes.Select(frame => (frame.Time, frame.Value, frame.Interpolation)));
    }

    [Fact]
    public void PartialLinearOverlayOfSrgbColorIsRejectedButFullCoverageReplacesItsColorSpace()
    {
        var existing = new AnimationTrack(AnimationProperty.FILL,
            [new(new(0), SceneColor.Black), new(new(2), SceneColor.White)]) { ColorSpace = AnimationColorSpace.SRGB };
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Tracks = [existing] };
        var partial = EffectScriptParser.Parse("""
            effect "partial-color" version 1
            short-clip compress
            segment enter fixed 1s
                at 0 fill rgba(0, 0, 0, 1)
                at 1 fill rgba(0.21404114048223255, 0.21404114048223255, 0.21404114048223255, 1)
            end
            segment stay flex 1
            end
            """);
        Assert.Throws<EffectScriptException>(() => EffectScriptComposer.Compose(partial, layer));
        var full = EffectScriptParser.Parse(Source("fill", "rgba(0.5, 0.5, 0.5, 1)"));
        var replaced = Assert.Single(EffectScriptComposer.Compose(full, layer));

        Assert.Equal(AnimationColorSpace.LINEAR_RGB, replaced.ColorSpace);
        Assert.Equal(new SceneColor(0.5, 0.5, 0.5), SceneEvaluator.EvaluateColorTrack(replaced, new(1)));
    }

    private static string Source(string property, string value) => $"""
        effect "range-check" version 1
        short-clip compress
        segment stay flex 1
            at 0 {property} {value}
            at 1 {property} {value}
        end
        """;
}
