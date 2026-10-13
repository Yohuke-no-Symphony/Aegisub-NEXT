using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal sealed record AssNumericAnimation(AnimationProperty Property, int Component, double Initial,
    ImmutableArray<AssNumericOperation> Operations, bool Approximate)
{
    internal bool HasNegative => Initial < 0 || Operations.Any(operation => operation.Value < 0);

    internal static AssNumericAnimation FromTrack(AnimationTrack track, int component = 0)
    {
        var initial = track.IsOrdered ? track.InitialValue!.Value.GetComponent(component) : track.Keyframes[0].Value.GetComponent(component);
        var operations = ImmutableArray.CreateBuilder<AssNumericOperation>();
        var approximate = false;
        if (track.IsOrdered)
        {
            foreach (var operation in track.Transforms.Where(operation => operation.ComponentMask == 0 ||
                (operation.ComponentMask & (1 << component)) != 0))
            {
                var exponent = operation.Acceleration;
                var start = operation.Start;
                if (operation.Curve is { } curve)
                {
                    approximate |= !AssCurveCompatibility.TryGetAcceleration(curve, out exponent);
                    if (curve.Interpolation == KeyframeInterpolation.HOLD && !curve.Reverse)
                    {
                        start = operation.End;
                    }
                }
                operations.Add(new(start, operation.End, operation.Value.GetComponent(component), exponent));
            }
        }
        else
        {
            for (var index = 0; index < track.Keyframes.Length - 1; index++)
            {
                var first = track.Keyframes[index];
                var next = track.Keyframes[index + 1];
                var value = next.Value.GetComponent(component);
                if (first.Value.GetComponent(component).Equals(value))
                {
                    continue;
                }
                var curve = first.GetCurve(component);
                var reversedNonlinear = AssCurveCompatibility.IsReversedNonlinear(curve);
                if (curve.Interpolation == KeyframeInterpolation.HOLD && !reversedNonlinear)
                {
                    operations.Add(new(next.Time, next.Time, value, 1));
                    continue;
                }
                approximate |= !AssCurveCompatibility.TryGetAcceleration(curve, out var exponent);
                operations.Add(new(first.Time, next.Time, value, exponent));
            }
        }
        return new(track.Property, component, initial, operations.ToImmutable(), approximate);
    }

    internal string Write(string tag, double factor, MediaTime origin, Guid id,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var initial = Initial;
        var pending = new List<AssNumericOperation>();
        foreach (var operation in Operations)
        {
            if (operation.End <= origin)
            {
                initial = operation.Value;
                pending.Clear();
            }
            else
            {
                pending.Add(operation);
            }
        }
        var initialValue = Scale(initial, factor);
        AssExportPrecision.AddNumbers(id, diagnostics, initialValue);
        if (tag == "\\blur")
        {
            AssExportPrecision.AddBlurRange(initialValue, id, diagnostics);
        }
        var result = new StringBuilder(tag).Append(AssFormatValues.Number(initialValue));
        foreach (var operation in pending)
        {
            var start = Milliseconds(operation.Start - origin);
            var end = Math.Max(1, Milliseconds(operation.End - origin));
            if (operation.Start == operation.End)
            {
                start = end;
            }
            else if (start >= end)
            {
                start = end - 1;
            }
            if (new MediaTime(start, 1000) != operation.Start - origin || new MediaTime(end, 1000) != operation.End - origin)
            {
                diagnostics.Add(new("Ass.TransformTimeQuantization", "ASS numeric transform times were rounded to milliseconds. Unfinished transforms retain at least 1 millisecond after the event start.", SubtitleId: id));
            }
            var value = Scale(operation.Value, factor);
            AssExportPrecision.AddNumbers(id, diagnostics, value, operation.Exponent);
            if (tag == "\\blur")
            {
                AssExportPrecision.AddBlurRange(value, id, diagnostics);
            }
            result.Append("\\t(").Append(start.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(end.ToString(CultureInfo.InvariantCulture)).Append(',').Append(AssFormatValues.Number(operation.Exponent))
                .Append(',').Append(tag).Append(AssFormatValues.Number(value)).Append(')');
        }
        return result.ToString();
    }

    private static long Milliseconds(MediaTime time)
    {
        return time.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value;
    }

    private static double Scale(double value, double factor)
    {
        return value == 0 || factor == 0 ? 0 : value * factor;
    }
}
