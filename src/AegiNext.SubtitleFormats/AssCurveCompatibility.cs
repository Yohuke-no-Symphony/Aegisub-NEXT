using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssCurveCompatibility
{
    internal static bool IsReversedNonlinear(AnimationCurve curve)
    {
        return curve.Reverse && curve.Interpolation != KeyframeInterpolation.LINEAR &&
            !(curve.Interpolation == KeyframeInterpolation.POWER && curve.Exponent.Equals(1d));
    }

    internal static bool HasReversedNonlinearCurve(AnimationTrack track)
    {
        return track.Keyframes.Take(Math.Max(0, track.Keyframes.Length - 1)).Any(frame =>
            Enumerable.Range(0, frame.Value.ComponentCount).Any(component => IsReversedNonlinear(frame.GetCurve(component)))) ||
            track.Transforms.Any(operation => operation.Curve is { } curve && IsReversedNonlinear(curve));
    }

    internal static bool TryGetAcceleration(AnimationCurve curve, out double acceleration)
    {
        acceleration = 1;
        if (IsReversedNonlinear(curve))
        {
            return false;
        }
        if (curve.Interpolation is KeyframeInterpolation.LINEAR or KeyframeInterpolation.HOLD ||
            curve.Interpolation == KeyframeInterpolation.POWER && curve.Exponent.Equals(1d))
        {
            return true;
        }
        if (curve.Interpolation == KeyframeInterpolation.POWER && curve.CurveStart == 0)
        {
            acceleration = curve.Exponent;
            return true;
        }
        if (curve.Interpolation == KeyframeInterpolation.EASE_IN && curve.CurveStart == 0)
        {
            acceleration = 2;
            return true;
        }
        return false;
    }
}
