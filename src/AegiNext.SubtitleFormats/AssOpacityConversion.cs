using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssOpacityConversion
{
    private const double ALPHA_TOLERANCE = 1e-12;

    internal static AssOpacityEnvelope? FromTrack(AnimationTrack track)
    {
        if (AssMoveConversion.TryConstant(track, out var constant))
        {
            return new(constant.Scalar, []);
        }
        var transitions = new List<AssOpacityTransition>();
        if (track.IsOrdered)
        {
            var value = track.InitialValue!.Value.Scalar;
            MediaTime? previousEnd = null;
            foreach (var operation in track.Transforms)
            {
                if (previousEnd.HasValue && operation.Start < previousEnd.Value)
                {
                    return null;
                }
                previousEnd = operation.End;
                var destination = operation.Value.Scalar;
                if (!value.Equals(destination))
                {
                    if (operation.Curve is { Interpolation: KeyframeInterpolation.HOLD, Reverse: true })
                    {
                        return null;
                    }
                    var start = operation.Curve is { Interpolation: KeyframeInterpolation.HOLD }
                        ? operation.End : operation.Start;
                    var instant = start == operation.End || operation.Curve is null && operation.Acceleration == 0;
                    var linear = operation.Curve is { } curve
                        ? curve.Interpolation == KeyframeInterpolation.LINEAR ||
                            curve.Interpolation == KeyframeInterpolation.POWER && curve.Exponent.Equals(1d)
                        : operation.Acceleration.Equals(1d);
                    if (!Add(transitions, new(start, instant ? start : operation.End, value, destination, !instant && !linear)))
                    {
                        return null;
                    }
                }
                value = destination;
            }
            return new(track.InitialValue!.Value.Scalar, transitions.ToImmutableArray());
        }
        for (var index = 0; index < track.Keyframes.Length - 1; index++)
        {
            var first = track.Keyframes[index];
            var last = track.Keyframes[index + 1];
            if (first.Value == last.Value)
            {
                continue;
            }
            if (first.Reverse && first.Interpolation == KeyframeInterpolation.HOLD)
            {
                return null;
            }
            var instant = first.Interpolation == KeyframeInterpolation.HOLD;
            var linear = first.Interpolation == KeyframeInterpolation.LINEAR ||
                first.Interpolation == KeyframeInterpolation.POWER && first.Exponent.Equals(1d);
            if (!Add(transitions, new(instant ? last.Time : first.Time, last.Time,
                first.Value.Scalar, last.Value.Scalar, !instant && !linear)))
            {
                return null;
            }
        }
        return new(track.Keyframes[0].Value.Scalar, transitions.ToImmutableArray());
    }

    internal static string WriteTags(AssOpacityEnvelope envelope, MediaTime origin, MediaTime end,
        Guid subtitleId, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var clipped = envelope.Clip(origin, end);
        if (clipped.Transitions.IsEmpty && clipped.InitialOpacity.Equals(1d))
        {
            return string.Empty;
        }
        var first = Alpha(clipped.InitialOpacity);
        var middle = first;
        var last = first;
        var time1 = 0L;
        var time2 = 0L;
        var time3 = 0L;
        var time4 = 0L;
        if (!clipped.Transitions.IsEmpty)
        {
            var transition = clipped.Transitions[0];
            middle = Alpha(transition.Last);
            last = middle;
            time1 = Milliseconds(transition.Start);
            time2 = Milliseconds(transition.End);
            time3 = time2;
            time4 = time2;
        }
        if (clipped.Transitions.Length == 2)
        {
            var transition = clipped.Transitions[1];
            last = Alpha(transition.Last);
            time3 = Milliseconds(transition.Start);
            time4 = Milliseconds(transition.End);
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"\\fade({first},{middle},{last},{time1},{time2},{time3},{time4})");

        int Alpha(double opacity)
        {
            var alpha = checked((int)Math.Round((1 - opacity) * 255, MidpointRounding.ToEven));
            if (Math.Abs(opacity - (1 - alpha / 255d)) > ALPHA_TOLERANCE)
            {
                diagnostics.Add(new("Ass.OpacityPrecision", "ASS overall alpha supports only 8-bit precision. Some alpha values were approximated.", SubtitleId: subtitleId));
            }
            return alpha;
        }

        long Milliseconds(MediaTime time)
        {
            var value = time.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value;
            if (new MediaTime(value, 1000) != time)
            {
                diagnostics.Add(new("Ass.FadeTimeQuantization", "ASS fade times were rounded to milliseconds. Very short transitions may become instantaneous.", SubtitleId: subtitleId));
            }
            return value;
        }
    }

    private static bool Add(List<AssOpacityTransition> transitions, AssOpacityTransition transition)
    {
        if (transitions.Count > 0 && CanMerge(transitions[^1], transition))
        {
            var previous = transitions[^1];
            transitions[^1] = previous with { End = transition.End, Last = transition.Last };
            return true;
        }
        if (transitions.Count == 2)
        {
            return false;
        }
        transitions.Add(transition);
        return true;
    }

    private static bool CanMerge(AssOpacityTransition first, AssOpacityTransition second)
    {
        if (first.Approximate || second.Approximate || first.End != second.Start || !first.Last.Equals(second.First) ||
            first.Start == first.End || second.Start == second.End)
        {
            return false;
        }
        var firstDuration = first.End - first.Start;
        var secondDuration = second.End - second.Start;
        var firstSlope = (first.Last - first.First) * firstDuration.Denominator / firstDuration.Numerator;
        var secondSlope = (second.Last - second.First) * secondDuration.Denominator / secondDuration.Numerator;
        return firstSlope.Equals(secondSlope);
    }
}
