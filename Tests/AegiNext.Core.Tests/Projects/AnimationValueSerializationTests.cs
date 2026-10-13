using System.Text.Json;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class AnimationValueSerializationTests
{
    [Fact]
    public void DefaultSerializerWritesTheSameScalarAndVectorShapeWithoutCallerConverters()
    {
        AnimationValue scalar = -0.25;
        AnimationValue vector = new ScenePoint(-25, 2.5);
        AnimationValue vector3 = new SceneVector3(-25, 2.5, 720);
        Assert.Equal("-0.25", JsonSerializer.Serialize(scalar));
        Assert.Equal("{\"x\":-25,\"y\":2.5}", JsonSerializer.Serialize(vector));
        Assert.Equal("{\"x\":-25,\"y\":2.5,\"z\":720}", JsonSerializer.Serialize(vector3));
        Assert.Equal(scalar, JsonSerializer.Deserialize<AnimationValue>(JsonSerializer.Serialize(scalar)));
        Assert.Equal(vector, JsonSerializer.Deserialize<AnimationValue>(JsonSerializer.Serialize(vector)));
        Assert.Equal(vector3, JsonSerializer.Deserialize<AnimationValue>(JsonSerializer.Serialize(vector3)));
    }

    [Fact]
    public void DefaultSerializerPreservesMixedAnimationValuesAndTheSecondaryCurve()
    {
        var keys = new Keyframe[]
        {
            new(new(0), 0.75, KeyframeInterpolation.EASE_IN),
            new(new(0), new ScenePoint(12, -34), KeyframeInterpolation.EASE_OUT)
            {
                CurveStart = 0.1, CurveEnd = 0.9, VectorCurve = new(KeyframeInterpolation.EASE_IN_OUT, 0.2, 0.8)
            }
        };
        var encoded = JsonSerializer.Serialize(keys);
        var restored = Assert.IsType<Keyframe[]>(JsonSerializer.Deserialize<Keyframe[]>(encoded));
        Assert.Equal(keys.Select(frame => frame.Value), restored.Select(frame => frame.Value));
        Assert.Equal(keys.Select(frame => frame.Interpolation), restored.Select(frame => frame.Interpolation));
        Assert.Equal(keys[1].VectorCurve, restored[1].VectorCurve);
        Assert.Equal(keys[1].CurveStart, restored[1].CurveStart);
        Assert.Equal(keys[1].CurveEnd, restored[1].CurveEnd);
    }

    [Fact]
    public void DefaultSerializerRoundTripsCompleteHdrRgbaAndAllComponentCurveOverrides()
    {
        var frame = new Keyframe(new(0), new SceneColor(-0.25, 4.125, 65504, 0.45), KeyframeInterpolation.EASE_IN)
        {
            ComponentCurves = [new(KeyframeInterpolation.EASE_OUT, 0.1, 0.8), null, new(KeyframeInterpolation.HOLD)]
        };
        var encoded = JsonSerializer.Serialize(frame);
        var restored = Assert.IsType<Keyframe>(JsonSerializer.Deserialize<Keyframe>(encoded));
        Assert.Equal(frame.Value, restored.Value);
        Assert.True(frame.ComponentCurves.SequenceEqual(restored.ComponentCurves));
        Assert.DoesNotContain("VectorCurve", encoded, StringComparison.Ordinal);
        Assert.Contains("ComponentCurves", encoded, StringComparison.Ordinal);
        Assert.Equal(AnimationValueKind.COLOR, restored.Value.Kind);
        Assert.Equal(4.125, restored.Value.Color.Green);
        Assert.Equal(frame.GetCurve(2), restored.GetCurve(2));
    }

    [Theory]
    [InlineData("{\"x\":1}")]
    [InlineData("{\"x\":1,\"y\":2,\"z\":3,\"w\":4}")]
    [InlineData("{\"x\":1,\"z\":3}")]
    [InlineData("{\"x\":1,\"y\":2,\"y\":3}")]
    [InlineData("{\"x\":1,\"y\":2,\"z\":\"3\"}")]
    [InlineData("{\"x\":1,\"y\":2,\"z\":1e999}")]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"x\":\"1\",\"y\":2}")]
    [InlineData("{\"red\":1,\"green\":2,\"blue\":3}")]
    [InlineData("{\"red\":1,\"green\":2,\"blue\":3,\"alpha\":1,\"x\":0}")]
    [InlineData("{\"red\":1,\"green\":2,\"blue\":3,\"alpha\":\"1\"}")]
    [InlineData("{\"red\":1e999,\"green\":2,\"blue\":3,\"alpha\":1}")]
    [InlineData("[1,2]")]
    [InlineData("1e999")]
    [InlineData("{\"x\":1,\"y\":1e999}")]
    public void DefaultSerializerRejectsIncompleteOrNonFiniteAnimationValues(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnimationValue>(json));
    }
}
