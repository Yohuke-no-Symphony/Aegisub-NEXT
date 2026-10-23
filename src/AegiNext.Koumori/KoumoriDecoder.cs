namespace AegiNext.Koumori;

internal sealed class KoumoriDecoder
{
    private readonly KoumoriDecoderProfile profile;
    private readonly KoumoriSlotDecoder[] slots;
    private readonly long firstFrame;

    public KoumoriDecoder(KoumoriDecoderDefinition definition, int slotCount, long firstFrame, string profileName)
    {
        profile = definition.Profiles.GetValueOrDefault(profileName) ?? definition.Profiles["default"];
        slots = Enumerable.Range(0, slotCount).Select(_ => new KoumoriSlotDecoder(profile)).ToArray();
        this.firstFrame = firstFrame;
    }

    public void Append(ReadOnlySpan<float> probabilities, int frameCount, CancellationToken cancellationToken)
    {
        Append(probabilities, probabilities, frameCount, cancellationToken);
    }

    public void Append(ReadOnlySpan<float> generation, ReadOnlySpan<float> snap, int frameCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        var length = checked(frameCount * slots.Length * 3);
        ValidateProbabilities(generation, length, cancellationToken);
        ValidateProbabilities(snap, length, cancellationToken);
        for (var frame = 0; frame < frameCount; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var slot = 0; slot < slots.Length; slot++)
            {
                var offset = (frame * slots.Length + slot) * 3;
                slots[slot].Append(generation[offset], generation[offset + 1], generation[offset + 2],
                    snap[offset + 1], snap[offset + 2]);
            }
        }
    }

    private static void ValidateProbabilities(ReadOnlySpan<float> probabilities, int length, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (probabilities.Length < length)
        {
            throw new InvalidDataException("Incomplete Koumori output tensor.");
        }

        for (var index = 0; index < length; index++)
        {
            if ((index & 1023) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (!float.IsFinite(probabilities[index]) || probabilities[index] is < 0 or > 1)
            {
                throw new InvalidDataException("Koumori output must contain finite sigmoid probabilities.");
            }
        }
    }

    public KoumoriAnalysisResult Complete(string modelId, KoumoriAnalysisRange range, long sampleCount, CancellationToken cancellationToken)
    {
        var candidates = new List<KoumoriSegment>();
        var boundaries = new List<KoumoriBoundary>();
        for (var slot = 0; slot < slots.Length; slot++)
        {
            candidates.AddRange(slots[slot].Complete(slot, firstFrame, sampleCount, cancellationToken));
            AddBoundaries(slots[slot].SnapStartPeaks.Peaks, slot, KoumoriBoundaryKind.Start);
            AddBoundaries(slots[slot].SnapEndPeaks.Peaks, slot, KoumoriBoundaryKind.End);
        }

        var unique = new List<KoumoriSegment>();
        var recentIndices = new List<int>();
        var tolerance = (long)profile.DedupToleranceFrames * KoumoriAnalyzer.HOP_SAMPLES;
        foreach (var candidate in candidates.OrderBy(value => value.StartSample).ThenBy(value => value.EndSample)
                     .ThenByDescending(value => value.Score).ThenBy(value => value.Slot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recentIndices.RemoveAll(index => candidate.StartSample - unique[index].StartSample > tolerance);
            var recent = recentIndices.FindLastIndex(index => candidate.Slot == unique[index].Slot
                && Math.Abs(candidate.EndSample - unique[index].EndSample) <= tolerance);
            if (recent < 0)
            {
                recentIndices.Add(unique.Count);
                unique.Add(candidate);
            }
            else if (candidate.Score > unique[recentIndices[recent]].Score)
            {
                unique[recentIndices[recent]] = candidate;
            }
        }

        var laneEnds = new List<long>();
        var segments = new List<KoumoriSegment>();
        foreach (var candidate in unique.OrderBy(value => value.StartSample).ThenBy(value => value.EndSample))
        {
            var start = Math.Max(range.StartSample, candidate.StartSample);
            var end = Math.Min(range.EndSample, candidate.EndSample);
            if (end - start < (long)profile.MinDurationFrames * KoumoriAnalyzer.HOP_SAMPLES)
            {
                continue;
            }

            var lane = laneEnds.FindIndex(value => value <= start);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(end);
            }
            else
            {
                laneEnds[lane] = end;
            }

            segments.Add(candidate with { StartSample = start, EndSample = end, Lane = lane });
        }

        return new(modelId, range, segments.AsReadOnly(), boundaries.OrderBy(value => value.Sample).ThenBy(value => value.Kind)
            .ThenByDescending(value => value.Score).ToList().AsReadOnly());

        void AddBoundaries(List<KoumoriFramePeak> peaks, int slot, KoumoriBoundaryKind kind)
        {
            foreach (var peak in peaks)
            {
                var sample = checked((firstFrame + peak.Frame) * KoumoriAnalyzer.HOP_SAMPLES);
                if (sample >= range.StartSample && sample <= range.EndSample)
                {
                    boundaries.Add(new(sample, kind, peak.Score, slot));
                }
            }
        }
    }
}
