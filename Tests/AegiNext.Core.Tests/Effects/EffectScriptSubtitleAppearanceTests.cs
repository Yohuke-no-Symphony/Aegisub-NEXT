using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptSubtitleAppearanceTests
{
    private const string SCRIPT = """
        effect "subtitle-appearance" version 1
        short-clip compress
        segment enter fixed 100ms
            at 0 letter-spacing offset(-2)
            at 1 letter-spacing base
            at 0 fill-blur factor(0)
            at 1 fill-blur base
            at 0 stroke-blur factor(0)
            at 1 stroke-blur base
        end
        segment stay flex 1
            at 0 letter-spacing base hold
            at 1 letter-spacing base
            at 0 fill-blur base hold
            at 1 fill-blur base
            at 0 stroke-blur base hold
            at 1 stroke-blur base
        end
        segment exit fixed 100ms
            at 0 letter-spacing base
            at 1 letter-spacing offset(-2)
            at 0 fill-blur base
            at 1 fill-blur factor(0)
            at 0 stroke-blur base
            at 1 stroke-blur factor(0)
        end
        """;

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 5)]
    [InlineData(1, 10)]
    [InlineData(1, 10000)]
    public void BaseRelativeSubtitlePropertiesKeepFixedFlexTimingAndContentOffset(int numerator, int denominator)
    {
        var duration = new MediaTime(numerator, denominator);
        var layer = new ProjectLayer { SubtitleId = Guid.NewGuid(), End = duration, AnimationOffset = new(1, 4) };
        var style = new SubtitleStyle { LetterSpacing = -3, FillBlur = 4, StrokeBlur = 7 };
        var tracks = EffectScriptCompiler.Compile(EffectScriptRegressionParser.Parse(SCRIPT), layer, style);

        Assert.Equal(3, tracks.Length);
        Assert.All(tracks, track =>
        {
            Assert.Equal(layer.AnimationOffset, track.Keyframes[0].Time);
            Assert.Equal(layer.AnimationOffset + duration, track.Keyframes[^1].Time);
            Assert.All(track.Keyframes, key => Assert.Equal(AnimationValueKind.SCALAR, key.Value.Kind));
        });
        Assert.Equal(-5, SceneEvaluator.EvaluateScalarTrack(tracks.Single(track => track.Property == AnimationProperty.LETTER_SPACING), layer.AnimationOffset));
        foreach (var (property, expected) in new[]
                 {
                     (AnimationProperty.LETTER_SPACING, -3d), (AnimationProperty.FILL_BLUR, 4d), (AnimationProperty.STROKE_BLUR, 7d)
                 })
        {
            Assert.Equal(expected, SceneEvaluator.EvaluateScalarTrack(tracks.Single(track => track.Property == property), layer.AnimationOffset + duration / 2));
        }
    }

    [Theory]
    [InlineData("letter-spacing", EffectScriptProperty.LETTER_SPACING, AnimationProperty.LETTER_SPACING)]
    [InlineData("fill-blur", EffectScriptProperty.FILL_BLUR, AnimationProperty.FILL_BLUR)]
    [InlineData("stroke-blur", EffectScriptProperty.STROKE_BLUR, AnimationProperty.STROKE_BLUR)]
    public void MetadataAndCompilerRequireASubtitleStyleEvenForExplicitValues(string name,
        EffectScriptProperty scriptProperty, AnimationProperty animationProperty)
    {
        Assert.True(EffectScriptPropertyMetadata.TryGetProperty(name, out var parsedProperty));
        Assert.Equal(scriptProperty, parsedProperty);
        Assert.Equal(animationProperty, EffectScriptPropertyMetadata.GetAnimationProperty(parsedProperty));
        var script = EffectScriptRegressionParser.Parse(Source(name, "0"));
        var subtitle = new ProjectLayer { SubtitleId = Guid.NewGuid() };
        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, subtitle));
        Assert.Single(EffectScriptCompiler.Compile(script, subtitle, new()));
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1) };
        Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script, shape, new()));
    }

    [Theory]
    [InlineData("letter-spacing", "4097")]
    [InlineData("letter-spacing", "offset(-4097)")]
    [InlineData("fill-blur", "-1")]
    [InlineData("fill-blur", "513")]
    [InlineData("stroke-blur", "-1")]
    [InlineData("stroke-blur", "513")]
    public void ResolvedValuesOutsideNativeRangesAreRejected(string property, string value)
    {
        var script = EffectScriptRegressionParser.Parse(Source(property, value));
        var error = Assert.Throws<EffectScriptException>(() => EffectScriptCompiler.Compile(script,
            new() { SubtitleId = Guid.NewGuid() }, new()));
        Assert.Equal(4, error.Line);
    }

    private static string Source(string property, string value) => $"""
        effect "appearance-check" version 1
        short-clip compress
        segment stay flex 1
            at 0 {property} {value}
            at 1 {property} {value}
        end
        """;
}
