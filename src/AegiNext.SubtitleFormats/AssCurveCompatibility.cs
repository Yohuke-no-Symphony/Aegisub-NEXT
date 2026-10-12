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
            Enumerable.Range(0, frame.Value.ComponentCount).Any(component => IsReversedNonlinear(frame.GetCurve(component))));
    }
}
