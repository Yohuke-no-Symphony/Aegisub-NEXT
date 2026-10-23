namespace AegiNext.Koumori;

internal sealed class KoumoriSlotDecoder
{
    private readonly KoumoriDecoderProfile profile;
    private readonly List<double> coveragePrefix = [0];
    private readonly List<KoumoriFrameRange> ranges = [];
    private readonly List<KoumoriFrameRange> continuationGaps = [];
    private readonly KoumoriPeakDetector internalStartPeaks;
    private readonly KoumoriPeakDetector internalEndPeaks;
    private int? activeStart;

    public KoumoriSlotDecoder(KoumoriDecoderProfile profile)
    {
        this.profile = profile;
        StartPeaks = new(profile.StartThreshold, profile.PeakRadiusFrames);
        EndPeaks = new(profile.EndThreshold, profile.PeakRadiusFrames);
        SnapStartPeaks = new(profile.SnapStartThreshold ?? profile.StartThreshold, profile.PeakRadiusFrames);
        SnapEndPeaks = new(profile.SnapEndThreshold ?? profile.EndThreshold, profile.PeakRadiusFrames);
        internalStartPeaks = profile.InternalStartThreshold is { } start && !start.Equals(profile.StartThreshold)
            ? new(start, profile.PeakRadiusFrames) : StartPeaks;
        internalEndPeaks = profile.InternalEndThreshold is { } end && !end.Equals(profile.EndThreshold)
            ? new(end, profile.PeakRadiusFrames) : EndPeaks;
    }

    public KoumoriPeakDetector StartPeaks { get; }
    public KoumoriPeakDetector EndPeaks { get; }
    public KoumoriPeakDetector SnapStartPeaks { get; }
    public KoumoriPeakDetector SnapEndPeaks { get; }

    public void Append(float coverage, float start, float end)
    {
        Append(coverage, start, end, start, end);
    }

    public void Append(float coverage, float start, float end, float snapStart, float snapEnd)
    {
        var frame = coveragePrefix.Count - 1;
        if (coverage >= profile.CoverageThreshold)
        {
            activeStart ??= frame;
        }
        else if (activeStart is { } first)
        {
            ranges.Add(new(first, frame));
            activeStart = null;
        }

        coveragePrefix.Add(coveragePrefix[^1] + coverage);
        StartPeaks.Append(start);
        EndPeaks.Append(end);
        SnapStartPeaks.Append(snapStart);
        SnapEndPeaks.Append(snapEnd);
        if (!ReferenceEquals(internalStartPeaks, StartPeaks))
        {
            internalStartPeaks.Append(start);
        }
        if (!ReferenceEquals(internalEndPeaks, EndPeaks))
        {
            internalEndPeaks.Append(end);
        }
    }

    public IEnumerable<KoumoriSegment> Complete(int slot, long firstFrame, long sampleCount, CancellationToken cancellationToken)
    {
        var frameCount = coveragePrefix.Count - 1;
        if (activeStart is { } active)
        {
            ranges.Add(new(active, frameCount));
            activeStart = null;
        }

        StartPeaks.Complete();
        EndPeaks.Complete();
        SnapStartPeaks.Complete();
        SnapEndPeaks.Complete();
        if (!ReferenceEquals(internalStartPeaks, StartPeaks))
        {
            internalStartPeaks.Complete();
        }
        if (!ReferenceEquals(internalEndPeaks, EndPeaks))
        {
            internalEndPeaks.Complete();
        }
        var previousEnd = 0;
        foreach (var range in BridgeContinuations(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = Math.Max(Closest(StartPeaks.Peaks, range.Start), previousEnd);
            var last = Closest(EndPeaks.Peaks, range.End);
            if (last - first < profile.MinDurationFrames)
            {
                continue;
            }

            var segmentStart = first;
            var index = LowerBound(StartPeaks.Peaks, first + profile.MinDurationFrames);
            while (index < StartPeaks.Peaks.Count && StartPeaks.Peaks[index].Frame <= last - profile.MinDurationFrames)
            {
                var startPeak = StartPeaks.Peaks[index++];
                var nextStart = startPeak.Frame;
                var endIndex = LowerBound(EndPeaks.Peaks, nextStart + 1) - 1;
                var segmentEnd = nextStart;
                var paired = false;
                var strongPair = false;
                if (endIndex >= 0)
                {
                    var candidate = EndPeaks.Peaks[endIndex].Frame;
                    if (candidate >= segmentStart + profile.MinDurationFrames && nextStart - candidate <= profile.EdgeRefinementFrames)
                    {
                        segmentEnd = candidate;
                        paired = true;
                        strongPair = startPeak.Score >= (profile.InternalStartThreshold ?? profile.StartThreshold)
                            && EndPeaks.Peaks[endIndex].Score >= (profile.InternalEndThreshold ?? profile.EndThreshold);
                    }
                }

                if (profile.RequirePairedInternalBoundary && !paired || IsContinuationGap(nextStart) && !strongPair)
                {
                    continue;
                }

                if (Create(segmentStart, segmentEnd) is { } segment)
                {
                    yield return segment;
                    segmentStart = nextStart;
                }
            }

            if (Create(segmentStart, last) is { } final)
            {
                yield return final;
            }

            previousEnd = last;
        }

        KoumoriSegment? Create(int start, int end)
        {
            if (end - start < profile.MinDurationFrames)
            {
                return null;
            }

            var startSample = checked((firstFrame + start) * KoumoriAnalyzer.HOP_SAMPLES);
            var endSample = Math.Min(checked((firstFrame + end) * KoumoriAnalyzer.HOP_SAMPLES), sampleCount);
            if (endSample - startSample < (long)profile.MinDurationFrames * KoumoriAnalyzer.HOP_SAMPLES)
            {
                return null;
            }

            var scoreEnd = Math.Min(end, frameCount);
            var confidence = (float)((coveragePrefix[scoreEnd] - coveragePrefix[start]) / (scoreEnd - start));
            return new(startSample, endSample, confidence, slot);
        }
    }

    private IEnumerable<KoumoriFrameRange> BridgeCoverageGaps()
    {
        if (ranges.Count == 0)
        {
            yield break;
        }
        var current = ranges[0];
        for (var index = 1; index < ranges.Count; index++)
        {
            var next = ranges[index];
            var gap = next.Start - current.End;
            var endIndex = LowerBound(EndPeaks.Peaks, current.End - profile.EdgeRefinementFrames);
            var startIndex = LowerBound(StartPeaks.Peaks, next.Start + profile.EdgeRefinementFrames + 1) - 1;
            var protectedGap = endIndex < EndPeaks.Peaks.Count && startIndex >= 0 &&
                EndPeaks.Peaks[endIndex].Frame <= current.End + profile.EdgeRefinementFrames &&
                StartPeaks.Peaks[startIndex].Frame >= next.Start - profile.EdgeRefinementFrames &&
                EndPeaks.Peaks[endIndex].Frame <= StartPeaks.Peaks[startIndex].Frame;
            if (gap > 0 && gap <= profile.MaxCoverageGapFrames && !protectedGap)
            {
                current = new(current.Start, next.End);
            }
            else
            {
                yield return current;
                current = next;
            }
        }
        yield return current;
    }

    private IReadOnlyList<KoumoriFrameRange> BridgeContinuations(CancellationToken cancellationToken)
    {
        continuationGaps.Clear();
        var originals = BridgeCoverageGaps().ToArray();
        if (profile.MaxContinuationGapFrames == 0 || originals.Length < 2)
        {
            return originals;
        }

        var result = new List<KoumoriFrameRange>(originals.Length);
        var current = originals[^1];
        var suffixHasShortJoin = false;
        var suffixLeadingOriginalFrames = current.End - current.Start;
        for (var index = originals.Length - 2; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = originals[index];
            var right = originals[index + 1];
            var gap = current.Start - left.End;
            var shortGap = gap <= profile.MaxContinuationGapFrames;
            var fragmentGap = profile.MaxFragmentContinuationGapFrames > 0
                && gap <= profile.MaxFragmentContinuationGapFrames && suffixHasShortJoin
                && suffixLeadingOriginalFrames <= profile.MaxContinuationFragmentFrames
                && HasPeakAround(internalStartPeaks.Peaks, current.Start)
                && !HasPeakAround(internalEndPeaks.Peaks, left.End);
            var start = ClosestPeak(StartPeaks.Peaks, left.Start);
            var end = ClosestPeak(EndPeaks.Peaks, current.End);
            if (gap > profile.MaxCoverageGapFrames && (shortGap || fragmentGap)
                && left.End - left.Start >= profile.MinDurationFrames && right.End - right.Start >= profile.MinDurationFrames
                && start is { } first && end is { } last && last.Frame - first.Frame >= profile.MinDurationFrames
                && last.Frame - first.Frame <= profile.MaxContinuationSpanFrames
                && !HasStrongInternalPair(left.End, current.Start))
            {
                continuationGaps.Add(new(left.End, current.Start));
                current = new(left.Start, current.End);
                suffixHasShortJoin |= shortGap;
                suffixLeadingOriginalFrames = left.End - left.Start;
            }
            else
            {
                result.Add(current);
                current = left;
                suffixHasShortJoin = false;
                suffixLeadingOriginalFrames = left.End - left.Start;
            }
        }
        result.Add(current);
        result.Reverse();
        continuationGaps.Reverse();
        return result;
    }

    private bool HasStrongInternalPair(int end, int start)
    {
        var ends = internalEndPeaks.Peaks;
        var starts = internalStartPeaks.Peaks;
        var endIndex = LowerBound(ends, end - profile.EdgeRefinementFrames);
        var startIndex = LowerBound(starts, start + profile.EdgeRefinementFrames + 1) - 1;
        return endIndex < ends.Count && startIndex >= 0
            && ends[endIndex].Frame <= end + profile.EdgeRefinementFrames
            && starts[startIndex].Frame >= start - profile.EdgeRefinementFrames
            && ends[endIndex].Frame <= starts[startIndex].Frame;
    }

    private bool HasPeakAround(List<KoumoriFramePeak> peaks, int frame)
    {
        var index = LowerBound(peaks, frame - profile.EdgeRefinementFrames);
        return index < peaks.Count && peaks[index].Frame <= frame + profile.EdgeRefinementFrames;
    }

    private bool IsContinuationGap(int frame)
    {
        var left = 0;
        var right = continuationGaps.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (continuationGaps[middle].Start <= frame + profile.EdgeRefinementFrames)
            {
                left = middle + 1;
            }
            else
            {
                right = middle;
            }
        }
        return left > 0 && frame <= continuationGaps[left - 1].End + profile.EdgeRefinementFrames;
    }

    private int Closest(List<KoumoriFramePeak> peaks, int frame)
    {
        return ClosestPeak(peaks, frame)?.Frame ?? frame;
    }

    private KoumoriFramePeak? ClosestPeak(List<KoumoriFramePeak> peaks, int frame)
    {
        KoumoriFramePeak? best = null;
        var distance = int.MaxValue;
        for (var index = LowerBound(peaks, frame - profile.EdgeRefinementFrames);
             index < peaks.Count && peaks[index].Frame <= frame + profile.EdgeRefinementFrames; index++)
        {
            var candidate = peaks[index];
            var candidateDistance = Math.Abs(candidate.Frame - frame);
            if (candidateDistance < distance || candidateDistance == distance && (best is null || candidate.Score > best.Value.Score))
            {
                best = candidate;
                distance = candidateDistance;
            }
        }

        return best;
    }

    private static int LowerBound(List<KoumoriFramePeak> peaks, int frame)
    {
        var left = 0;
        var right = peaks.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (peaks[middle].Frame < frame)
            {
                left = middle + 1;
            }
            else
            {
                right = middle;
            }
        }

        return left;
    }
}
