using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssTextAnimationTrackWriter
{
    internal static string Write(AnimationTrack track, Func<AnimationValue, int, AnimationTransformMode, string> tags,
        Func<AnimationValue, double[]> components, MediaTime origin, MediaTime end, Guid subtitleId,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, bool forceSampling = false)
    {
        var reportedPrecision = new HashSet<string>(StringComparer.Ordinal);
        string OutputTags(AnimationValue value, int mask, AnimationTransformMode mode)
        {
            var output = tags(value, mask, mode);
            if (output.Length > 0 && value.IsColor)
            {
                AssExportPrecision.AddColor(value.Color, subtitleId, diagnostics, mask, reportedPrecision);
            }
            return output;
        }

        if (!forceSampling && Exact(track) && origin == MediaTime.Zero && !track.Transforms.Any(operation => operation.End <= origin))
        {
            var initial = track.IsOrdered ? track.InitialValue!.Value : track.Keyframes[0].Value;
            var result = new StringBuilder(OutputTags(initial, 0, AnimationTransformMode.INTERPOLATE_TO));
            if (track.IsOrdered)
            {
                foreach (var operation in track.Transforms)
                {
                    var acceleration = operation.Acceleration;
                    if (operation.Curve is { } curve)
                    {
                        AssCurveCompatibility.TryGetAcceleration(curve, out acceleration);
                    }
                    var start = operation.Curve is { Interpolation: KeyframeInterpolation.HOLD, Reverse: false }
                        ? operation.End : operation.Start;
                    Append(result, start, operation.End, acceleration,
                        OutputTags(operation.Value, operation.ComponentMask, operation.Mode), subtitleId, diagnostics, reportedPrecision);
                }
            }
            else
            {
                for (var index = 0; index < track.Keyframes.Length - 1; index++)
                {
                    var first = track.Keyframes[index];
                    var next = track.Keyframes[index + 1];
                    for (var component = 0; component < initial.ComponentCount; component++)
                    {
                        if (first.Value.GetComponent(component) == next.Value.GetComponent(component))
                        {
                            continue;
                        }
                        var curve = first.GetCurve(component);
                        AssCurveCompatibility.TryGetAcceleration(curve, out var acceleration);
                        Append(result, curve.Interpolation == KeyframeInterpolation.HOLD ? next.Time : first.Time,
                            next.Time, acceleration,
                            OutputTags(next.Value, 1 << component, AnimationTransformMode.INTERPOLATE_TO), subtitleId, diagnostics, reportedPrecision);
                    }
                }
            }
            return result.ToString();
        }
        return Sample(track, OutputTags, components, origin, end, subtitleId, diagnostics, reportedPrecision);
    }

    private static bool Exact(AnimationTrack track)
    {
        if (track.Property is AnimationProperty.FILL or AnimationProperty.STROKE or AnimationProperty.SHADOW_COLOR)
        {
            return track.ColorSpace == AnimationColorSpace.SRGB && track.IsOrdered &&
                track.Transforms.All(operation => operation.Mode == AnimationTransformMode.INTERPOLATE_TO &&
                    operation.ComponentMask is 0 or 7 or 8 or 15 &&
                    (operation.Curve is null || AssCurveCompatibility.TryGetAcceleration(operation.Curve, out _)));
        }
        if (track.IsOrdered)
        {
            return track.Transforms.All(operation => (operation.Mode == AnimationTransformMode.INTERPOLATE_TO ||
                track.Property == AnimationProperty.FONT_SIZE) &&
                (operation.Curve is null || AssCurveCompatibility.TryGetAcceleration(operation.Curve, out _)));
        }
        return track.Keyframes.All(frame => Enumerable.Range(0, frame.Value.ComponentCount).All(component =>
        {
            var curve = frame.GetCurve(component);
            return AssCurveCompatibility.TryGetAcceleration(curve, out _);
        }));
    }

    private static string Sample(AnimationTrack track, Func<AnimationValue, int, AnimationTransformMode, string> tags,
        Func<AnimationValue, double[]> components, MediaTime origin, MediaTime end, Guid subtitleId,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, ISet<string> reportedPrecision)
    {
        var sampled = AssAnimationSampler.Sample(track, components, origin, end);
        var limited = sampled.Limited;
        diagnostics.Add(new(limited ? "Ass.AnimationSamplingLimit" : "Ass.AnimationSampling",
            limited ? "Animation sampling reached the 1-millisecond or 4096-point-per-track limit. Errors in some intervals may exceed 1/255; the sampled result was preserved." :
                "Native interpolation was sampled as ASS linear transforms. Errors in checked output components do not exceed 1/255, with millisecond time precision.", SubtitleId: subtitleId));
        var result = new StringBuilder(tags(sampled.Frames[0].Value, 0, AnimationTransformMode.INTERPOLATE_TO));
        for (var index = 0; index < sampled.Frames.Length - 1; index++)
        {
            Append(result, sampled.Frames[index].Time - origin, sampled.Frames[index + 1].Time - origin, 1,
                tags(sampled.Frames[index + 1].Value, 0, AnimationTransformMode.INTERPOLATE_TO), subtitleId, diagnostics, reportedPrecision);
        }
        return result.ToString();
    }

    private static void Append(StringBuilder result, MediaTime startTime, MediaTime endTime, double acceleration,
        string tags, Guid subtitleId, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, ISet<string> reportedPrecision)
    {
        if (tags.Length == 0)
        {
            return;
        }
        var start = Milliseconds(startTime);
        var end = Milliseconds(endTime);
        if (startTime != endTime && end <= start)
        {
            end = checked(start + 1);
        }
        if (end == 0)
        {
            end = 1;
            start = Math.Min(start, 1);
        }
        if (new MediaTime(start, 1000) != startTime || new MediaTime(end, 1000) != endTime)
        {
            diagnostics.Add(new("Ass.TransformTimeQuantization", "ASS transform times were quantized to milliseconds; positive durations retain at least 1 millisecond.", SubtitleId: subtitleId));
        }
        AssExportPrecision.AddNumbers(subtitleId, diagnostics, reportedPrecision, acceleration);
        result.Append("\\t(").Append(start.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(end.ToString(CultureInfo.InvariantCulture)).Append(',').Append(AssFormatValues.Number(acceleration))
            .Append(',').Append(tags).Append(')');
    }

    private static long Milliseconds(MediaTime time) => time.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value;
}
