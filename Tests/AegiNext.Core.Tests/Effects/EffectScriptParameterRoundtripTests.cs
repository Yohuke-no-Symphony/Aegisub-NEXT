using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Effects;

public sealed class EffectScriptParameterRoundtripTests
{
    [Theory]
    [InlineData(3, 10, "0.3s")]
    [InlineData(1, 8, "0.125s")]
    [InlineData(0, 1, "0s")]
    [InlineData(-3, 10, "-0.3s")]
    [InlineData(1, 3, "(1s / 3)")]
    [InlineData(1, 1152921504606846976, "(1s / 1152921504606846976)")]
    public void DurationLiteralIsReadableWhenDecimalCanRepresentItExactly(long numerator, long denominator, string source)
    {
        var value = EffectScriptParameterValue.FromDuration(new(numerator, denominator));
        Assert.Equal(source, value.ToSource());
        Assert.Equal(value, EffectScriptParameterValue.Parse(source));
    }

    [Fact]
    public void RationalDurationRoundTripsWithoutChangingStrictOptionValue()
    {
        var value = EffectScriptParameterValue.FromDuration(new(1, 3));
        Assert.Equal(value, EffectScriptParameterValue.Parse(value.ToSource()));
        var script = EffectScriptParser.Parse("""
            effect "third-duration" version 3
            short-clip compress
            var time = (1s / 3) options ["Third":(1s / 3)]
            """);
        Assert.Equal(value, EffectScriptParameterBinder.Bind(script)["time"]);
    }

    [Fact]
    public void EquivalentNumbersDurationsAndSignedZeroHaveOneCanonicalLiteral()
    {
        Assert.Equal(EffectScriptParameterValue.FromNumber(0.1m).ToSource(), EffectScriptParameterValue.FromNumber(0.10m).ToSource());
        Assert.Equal(EffectScriptParameterValue.Parse("100ms").ToSource(), EffectScriptParameterValue.Parse("0.1s").ToSource());
        Assert.Equal(EffectScriptParameterValue.FromAnimation(AnimationValue.FromVector(new(0, 0))).ToSource(),
            EffectScriptParameterValue.FromAnimation(AnimationValue.FromVector(new(-0.0, 0))).ToSource());
    }

    [Fact]
    public void ScientificVectorAndHdrColorsRoundTripThroughTheEditorLiteral()
    {
        foreach (var value in new[]
        {
            EffectScriptParameterValue.FromAnimation(AnimationValue.FromVector(new(0.00001, -0.00002))),
            EffectScriptParameterValue.FromAnimation(AnimationValue.FromVector(new(1.2345678901234567, -9.876543210987654))),
            EffectScriptParameterValue.FromAnimation(AnimationValue.FromVector3(new(0.00001, 30, 720))),
            EffectScriptParameterValue.FromAnimation(AnimationValue.FromColor(new(2, 0.00001, -0.5, 1)))
        })
        {
            Assert.Equal(value, EffectScriptParameterValue.Parse(value.ToSource()));
        }
    }

    [Theory]
    [InlineData("vec2(1, 2)", "value.z")]
    [InlineData("vec3(1, 2, 3)", "value.a")]
    [InlineData("rgba(1, 1, 1, 1)", "value.z")]
    public void MissingComponentsFailEvenInAnUntakenBranch(string value, string reference)
    {
        var source = $$"""
            effect "invalid-component" version 3
            short-clip compress
            var value = {{value}}
            if false
                scope skipped current
                    segment all flex 1
                        at 0 rotation {{reference}}
                        at 1 rotation 0
                    end
                end
            end
            """;
        Assert.Throws<EffectScriptException>(() => EffectScriptParser.Parse(source));
    }
}
