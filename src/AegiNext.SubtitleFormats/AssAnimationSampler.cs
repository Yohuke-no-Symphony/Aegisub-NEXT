using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssAnimationSampler
{
    private const int MAX_SAMPLES = 4096;
    private const double MAX_ERROR = 0.5 / 255;
    private static readonly double[] sampleFractions = [0.25, 0.5, 0.75];

    internal static (ImmutableArray<Keyframe> Frames, bool Limited) Sample(AnimationTrack track,
        Func<AnimationValue, double[]> components, MediaTime origin, MediaTime end)
    {
        return Sample(track, components, origin, end, time => SceneEvaluator.EvaluateTrack(track, time));
    }

    internal static (ImmutableArray<Keyframe> Frames, bool Limited) Sample(AnimationTrack track,
        Func<AnimationValue, double[]> components, MediaTime origin, MediaTime end,
        Func<MediaTime, AnimationValue> evaluate, Func<MediaTime, AnimationValue>? beforeDiscontinuity = null,
        IEnumerable<MediaTime>? sourceDiscontinuities = null)
    {
        var duration = Math.Max(1, Milliseconds(end - origin));
        var offsets = new SortedSet<long> { 0, duration };
        var discontinuities = beforeDiscontinuity is null ? [] : track.Transforms
            .Where(operation => operation.Start == operation.End || operation.Acceleration == 0)
            .Select(operation => Milliseconds(operation.Start - origin)).Where(offset => offset > 0 && offset <= duration).ToHashSet();
        var sourceLimits = new Dictionary<long, MediaTime>();
        var quantizedDiscontinuity = false;
        if (beforeDiscontinuity is not null && sourceDiscontinuities is not null)
        {
            foreach (var time in sourceDiscontinuities)
            {
                var offset = (time - origin).ToTimestamp(new(1, 1000), MediaTimeRounding.CEILING).Value;
                if (offset > 0 && offset <= duration)
                {
                    discontinuities.Add(offset);
                    if (!sourceLimits.TryGetValue(offset, out var previous) || time < previous)
                    {
                        sourceLimits[offset] = time;
                    }
                    quantizedDiscontinuity |= time != origin + new MediaTime(offset, 1000);
                }
            }
        }
        AnimationValue LeftLimit(long offset) => beforeDiscontinuity!(sourceLimits.TryGetValue(offset, out var time)
            ? time : origin + new MediaTime(offset, 1000));
        foreach (var time in track.Keyframes.Select(frame => frame.Time)
            .Concat(track.Transforms.SelectMany(operation => new[] { operation.Start, operation.End })))
        {
            var offset = Milliseconds(time - origin);
            if (offset > 0 && offset < duration)
            {
                offsets.Add(offset);
            }
        }
        foreach (var offset in discontinuities)
        {
            offsets.Add(offset);
            offsets.Add(offset - 1);
        }
        var values = new Dictionary<long, AnimationValue>();
        AnimationValue Value(long offset)
        {
            if (!values.TryGetValue(offset, out var value))
            {
                value = evaluate(origin + new MediaTime(offset, 1000));
                values.Add(offset, value);
            }
            return value;
        }
        var queue = new Queue<(long Start, long End)>();
        var overBudget = offsets.Count > MAX_SAMPLES;
        var limited = overBudget || quantizedDiscontinuity;
        if (overBudget)
        {
            var source = offsets.ToArray();
            offsets.Clear();
            for (var index = 0; index < MAX_SAMPLES; index++)
            {
                offsets.Add(source[(int)Math.Round(index * (source.Length - 1d) / (MAX_SAMPLES - 1))]);
            }
        }
        var boundaries = offsets.ToArray();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            queue.Enqueue((boundaries[index], boundaries[index + 1]));
        }
        while (queue.TryDequeue(out var interval))
        {
            var a = components(Value(interval.Start));
            var b = components(discontinuities.Contains(interval.End)
                ? LeftLimit(interval.End) : Value(interval.End));
            var error = false;
            foreach (var fraction in sampleFractions)
            {
                var sampleTime = origin + new MediaTime(checked(interval.Start * 4 +
                    (long)((interval.End - interval.Start) * fraction * 4)), 4000);
                var actual = components(evaluate(sampleTime));
                error |= actual.Where((value, component) => Math.Abs(value - (a[component] + (b[component] - a[component]) * fraction)) > MAX_ERROR).Any();
            }
            if (!error)
            {
                continue;
            }
            if (interval.End - interval.Start <= 1 || offsets.Count >= MAX_SAMPLES)
            {
                limited = true;
                continue;
            }
            var middle = interval.Start + (interval.End - interval.Start) / 2;
            offsets.Add(middle);
            queue.Enqueue((interval.Start, middle));
            queue.Enqueue((middle, interval.End));
        }
        var result = ImmutableArray.CreateBuilder<Keyframe>();
        var finalOffsets = offsets.ToArray();
        for (var index = 0; index < finalOffsets.Length; index++)
        {
            var offset = finalOffsets[index];
            var holds = index + 1 < finalOffsets.Length && discontinuities.Contains(finalOffsets[index + 1]);
            if (holds)
            {
                var a = components(Value(offset));
                var b = components(LeftLimit(finalOffsets[index + 1]));
                limited |= a.Where((value, component) => Math.Abs(value - b[component]) > MAX_ERROR).Any();
            }
            result.Add(new(origin + new MediaTime(offset, 1000), Value(offset), holds ? KeyframeInterpolation.HOLD : KeyframeInterpolation.LINEAR));
        }
        return (result.ToImmutable(), limited);
    }

    private static long Milliseconds(MediaTime time) => time.ToTimestamp(new(1, 1000), MediaTimeRounding.TO_EVEN).Value;
}
