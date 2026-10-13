using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class KeyframeCurveEditingTests
{
    [Theory]
    [InlineData(AnimationValueKind.VECTOR)]
    [InlineData(AnimationValueKind.VECTOR3)]
    [InlineData(AnimationValueKind.COLOR)]
    public void EditingOneComponentRetainsTheOtherEffectiveCurvesAndSharedValueAndTime(AnimationValueKind kind)
    {
        var frame = Frame(kind);
        var component = frame.Value.ComponentCount - 1;

        var changed = KeyframeCurveEditing.Apply(frame, 1 << component, KeyframeInterpolation.POWER, 7);

        Assert.Equal(frame.Time, changed.Time);
        Assert.Equal(frame.Value, changed.Value);
        Assert.Equal(new(KeyframeInterpolation.POWER) { Exponent = 7 }, changed.GetCurve(component));
        for (var index = 0; index < component; index++)
        {
            Assert.Equal(frame.GetCurve(index), changed.GetCurve(index));
        }
    }

    [Theory]
    [InlineData(AnimationValueKind.VECTOR)]
    [InlineData(AnimationValueKind.VECTOR3)]
    [InlineData(AnimationValueKind.COLOR)]
    public void EditingThePrimaryComponentMaterializesInheritedCurvesForTheOtherAxes(AnimationValueKind kind)
    {
        var frame = Frame(kind) with { ComponentCurves = [] };

        var changed = KeyframeCurveEditing.Apply(frame, 1, KeyframeInterpolation.EASE_IN, 3);

        Assert.Equal(new(KeyframeInterpolation.EASE_IN) { Exponent = 3 }, changed.GetCurve(0));
        for (var index = 1; index < frame.Value.ComponentCount; index++)
        {
            Assert.Equal(frame.GetCurve(index), changed.GetCurve(index));
        }
        Assert.Equal(frame.Value.ComponentCount - 1, changed.ComponentCurves.Length);
    }

    [Fact]
    public void AllComponentsCanShareOneNewCurveWithoutChangingTheKeyframeTimeOrValues()
    {
        var frame = Frame(AnimationValueKind.VECTOR3);

        var changed = KeyframeCurveEditing.Apply(frame, 0, KeyframeInterpolation.POWER, 7);

        Assert.Equal(frame.Time, changed.Time);
        Assert.Equal(frame.Value, changed.Value);
        Assert.Empty(changed.ComponentCurves);
        Assert.All(Enumerable.Range(0, 3), component => Assert.Equal(new(KeyframeInterpolation.POWER) { Exponent = 7 }, changed.GetCurve(component)));
    }

    private static Keyframe Frame(AnimationValueKind kind)
    {
        var value = kind switch
        {
            AnimationValueKind.VECTOR => AnimationValue.FromVector(new(1, 2)),
            AnimationValueKind.VECTOR3 => AnimationValue.FromVector3(new(1, 2, 3)),
            _ => AnimationValue.FromColor(new(1, 2, 3, 0.5f))
        };
        return new(new(1), value, KeyframeInterpolation.POWER)
        {
            Exponent = 2, CurveStart = 0.2, CurveEnd = 0.8, Reverse = true,
            ComponentCurves = Enumerable.Range(1, value.ComponentCount - 1)
                .Select(index => (AnimationCurve?)new AnimationCurve(KeyframeInterpolation.EASE_OUT, 0.1, 0.7) { Exponent = index + 3 })
                .ToImmutableArray()
        };
    }
}
