using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

internal static class KeyframeCurveEditing
{
    internal static Keyframe Apply(Keyframe frame, int componentMask, KeyframeInterpolation interpolation, double exponent)
    {
        var curves = Enumerable.Range(0, frame.Value.ComponentCount).Select(frame.GetCurve).ToArray();
        for (var index = 0; index < curves.Length; index++)
        {
            if (componentMask != 0 && (componentMask & (1 << index)) == 0)
            {
                continue;
            }
            if (curves[index].Interpolation != interpolation || curves[index].Exponent != exponent)
            {
                curves[index] = new(interpolation) { Exponent = exponent };
            }
        }
        var main = curves[0];
        var components = curves.Skip(1).Select(curve => curve == main ? null : curve).ToImmutableArray();
        return frame with
        {
            Interpolation = main.Interpolation, CurveStart = main.CurveStart, CurveEnd = main.CurveEnd,
            Exponent = main.Exponent, Reverse = main.Reverse,
            ComponentCurves = components.All(curve => curve is null) ? [] : components
        };
    }
}
