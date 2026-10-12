using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssSourceAnimationEvaluator
{
    internal const double MAX_BLUR = 100;

    internal static bool NeedsSampling(AssTextAnimationSnapshot snapshot, double minimum = double.NegativeInfinity)
    {
        var lower = Enumerable.Range(0, snapshot.Initial.ComponentCount).Select(snapshot.Initial.GetComponent).ToArray();
        var upper = lower.ToArray();
        foreach (var operation in snapshot.Operations)
        {
            for (var component = 0; component < lower.Length; component++)
            {
                if (!Effective(operation, component))
                {
                    continue;
                }
                var target = operation.Value.GetComponent(component);
                if (operation.Mode == AnimationTransformMode.MULTIPLY_BY)
                {
                    var products = new[] { lower[component], upper[component], lower[component] * target, upper[component] * target };
                    lower[component] = products.Min();
                    upper[component] = products.Max();
                }
                else
                {
                    lower[component] = Math.Min(lower[component], target);
                    upper[component] = Math.Max(upper[component], target);
                }
                if (operation.ClampNonNegative && lower[component] < 0 ||
                    operation.Maximum is { } maximum && upper[component] > maximum ||
                    operation.NonPositiveFallback is not null && lower[component] <= 0 || lower[component] < minimum)
                {
                    return true;
                }
            }
        }
        return false;
    }

    internal static IEnumerable<MediaTime> Discontinuities(AssTextAnimationSnapshot snapshot)
    {
        foreach (var operation in snapshot.Operations)
        {
            if (ResetTime(operation) is { } time)
            {
                yield return time;
            }
        }
    }

    internal static AnimationValue Evaluate(AssTextAnimationSnapshot snapshot, MediaTime time, bool beforeDiscontinuity = false)
    {
        var result = snapshot.Initial;
        var positiveZeroLimits = new bool[result.ComponentCount];
        foreach (var operation in snapshot.Operations)
        {
            var timing = operation.Timing;
            var before = time < timing.Start || beforeDiscontinuity && time == timing.Start &&
                (timing.Start == timing.End || timing.Acceleration == 0);
            var fraction = before ? 0 : time >= timing.End ? 1 :
                Math.Pow(Fraction(time - timing.Start, timing.End - timing.Start), timing.Acceleration);
            var atReset = ResetTime(operation) is { } reset && time == reset;
            for (var component = 0; component < result.ComponentCount; component++)
            {
                if (!Effective(operation, component))
                {
                    continue;
                }
                var initial = result.GetComponent(component);
                var target = operation.Value.GetComponent(component);
                var multiplier = 1 + (target - 1) * fraction;
                var value = operation.Mode == AnimationTransformMode.MULTIPLY_BY
                    ? initial * multiplier : initial + (target - initial) * fraction;
                var positiveZeroLimit = positiveZeroLimits[component] &&
                    (operation.Mode == AnimationTransformMode.MULTIPLY_BY ? multiplier > 0 :
                        fraction == 0 || target == 0 && fraction < 1);
                if (atReset && !before)
                {
                    value = 0;
                    positiveZeroLimit = beforeDiscontinuity;
                }
                if (operation.ClampNonNegative)
                {
                    value = Math.Max(value, 0);
                }
                if (operation.Maximum is { } maximum)
                {
                    value = Math.Min(value, maximum);
                }
                if (operation.NonPositiveFallback is { } fallback && value <= 0 && !positiveZeroLimit)
                {
                    value = fallback;
                }
                positiveZeroLimits[component] = positiveZeroLimit;
                result = result.WithComponent(component, value);
            }
        }
        return result;
    }

    private static bool Effective(AssTextAnimationOperation operation, int component) =>
        operation.ComponentMask == 0 || (operation.ComponentMask & (1 << component)) != 0;

    private static MediaTime? ResetTime(AssTextAnimationOperation operation)
    {
        if (operation.NonPositiveFallback is null || operation.Timing.End <= operation.Timing.Start ||
            operation.Timing.Acceleration <= 0 || operation.Value.Scalar > 0)
        {
            return null;
        }
        var fraction = operation.Mode == AnimationTransformMode.MULTIPLY_BY ? 1 / (1 - operation.Value.Scalar) : 1;
        if (fraction == 1)
        {
            return operation.Timing.End;
        }
        var elapsed = operation.Timing.End - operation.Timing.Start;
        var seconds = (double)elapsed.Numerator / elapsed.Denominator * Math.Pow(fraction, 1 / operation.Timing.Acceleration);
        return operation.Timing.Start + new MediaTime(Math.Max(1, checked((long)Math.Round(seconds * 1_000_000_000))), 1_000_000_000);
    }

    private static double Fraction(MediaTime elapsed, MediaTime duration) => Math.Clamp(
        ((double)elapsed.Numerator / elapsed.Denominator) / ((double)duration.Numerator / duration.Denominator), 0, 1);
}
