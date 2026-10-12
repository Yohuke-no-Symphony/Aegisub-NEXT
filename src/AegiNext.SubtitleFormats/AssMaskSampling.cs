using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssMaskSampling
{
    internal static ImmutableArray<AssMaskSample> Samples(ProjectDocument document, ProjectLayer layer, SubtitleLine line,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, MediaTime timeOffset = default)
    {
        var tags = AssMaskWriter.WriteTags(layer, AssEventClock.Origin(line.Start, line.Start, layer.AnimationOffset, timeOffset), diagnostics);
        if (tags is not null)
        {
            return [new(line.Start, line.End, layer.AnimationOffset, tags, false)];
        }
        var frameBase = new MediaTimeBase(document.FrameRate.Denominator, document.FrameRate.Numerator);
        var frameDuration = new MediaTime(frameBase.Numerator, frameBase.Denominator);
        var first = line.Start.ToTimestamp(frameBase, MediaTimeRounding.FLOOR).Value;
        var last = line.End.ToTimestamp(frameBase, MediaTimeRounding.CEILING).Value;
        if (last - first > 100000)
        {
            throw new InvalidDataException("Frame-by-frame ASS mask animation expansion exceeds the 100,000-dialogue budget.");
        }
        var buckets = new SortedDictionary<long, AssMaskSample>();
        var quantized = false;
        var geometryQuantized = false;
        var sampleDiagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        for (var frame = first; frame < last; frame++)
        {
            var start = frameDuration * frame;
            start = start < line.Start ? line.Start : start;
            var end = frameDuration * (frame + 1);
            end = end > line.End ? line.End : end;
            var bucket = (start + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.FLOOR).Value;
            var sceneStart = new MediaTime(bucket, 100) - timeOffset;
            quantized |= sceneStart != start || buckets.ContainsKey(bucket);
            var content = start - line.Start + layer.AnimationOffset;
            var evaluated = SceneEvaluator.EvaluateMask(layer, content)!;
            var maskTags = AssMaskWriter.StaticTags(evaluated, line.Id, sampleDiagnostics);
            geometryQuantized |= sampleDiagnostics.Count > 0;
            sampleDiagnostics.Clear();
            buckets[bucket] = new(sceneStart, end, content, maskTags, true);
        }
        var values = buckets.Values.ToArray();
        var result = ImmutableArray.CreateBuilder<AssMaskSample>();
        var finalEnd = new MediaTime((line.End + timeOffset).ToTimestamp(new(1, 100), MediaTimeRounding.CEILING).Value, 100) - timeOffset;
        quantized |= finalEnd != line.End;
        for (var index = 0; index < values.Length; index++)
        {
            var end = index + 1 < values.Length ? values[index + 1].Start : finalEnd;
            if (end > values[index].Start)
            {
                result.Add(values[index] with { End = end, ContentTime = values[index].Start - line.Start + layer.AnimationOffset });
            }
        }
        if (geometryQuantized)
        {
            diagnostics.Add(new("Ass.MaskQuantization", "Expanded vector mask coordinates were quantized to 1/64 of a project pixel.", SubtitleId: line.Id));
        }
        diagnostics.Add(new("Ass.MaskAnimationExpanded", $"Mask animation was expanded at the project frame rate into {result.Count} dialogue entries with static clipping. Reimporting produces independent static clips.", SubtitleId: line.Id));
        if (quantized)
        {
            diagnostics.Add(new("Ass.MaskTimeQuantization", $"Frame-based mask times were quantized to centiseconds; {last - first} frame samples were merged into {result.Count} nonoverlapping time intervals.", SubtitleId: line.Id));
        }
        return result.ToImmutable();
    }
}
