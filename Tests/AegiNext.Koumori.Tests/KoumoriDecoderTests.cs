namespace AegiNext.Koumori.Tests;

public sealed class KoumoriDecoderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesCoincidentAndPartialOverlapsAcrossSlots(bool requirePairedInternalBoundary)
    {
        var values = new float[60 * 3 * 3];
        SetSegment(values, 3, 0, 10, 40, 0.8f);
        SetSegment(values, 3, 1, 10, 40, 0.9f);
        SetSegment(values, 3, 2, 20, 50, 0.85f);
        var decoder = new KoumoriDecoder(Definition(requirePairedInternalBoundary: requirePairedInternalBoundary), 3, 0, "default");
        decoder.Append(values.AsSpan(0, 31 * 9), 31, default);
        decoder.Append(values.AsSpan(31 * 9), 29, default);
        var result = decoder.Complete("test", new(0, 60 * 160), 60 * 160, default);

        Assert.Collection(result.Segments,
            first =>
            {
                Assert.Equal((1600L, 6400L, 1, 0), (first.StartSample, first.EndSample, first.Slot, first.Lane));
                Assert.Equal(0.9f, first.Score, 5);
            },
            second => Assert.Equal((1600L, 6400L, 0, 1), (second.StartSample, second.EndSample, second.Slot, second.Lane)),
            third => Assert.Equal((3200L, 8000L, 2, 2), (third.StartSample, third.EndSample, third.Slot, third.Lane)));
        Assert.Contains(result.Boundaries, value => value.Sample == 3200 && value.Kind == KoumoriBoundaryKind.Start);
    }

    [Fact]
    public void SplitsContinuousCoverageAtSubtitleStartPeaks()
    {
        var values = new float[60 * 3];
        SetSegment(values, 1, 0, 10, 40, 0.9f);
        values[20 * 3 + 1] = 0.9f;
        values[18 * 3 + 2] = 0.9f;
        var decoder = new KoumoriDecoder(Definition(), 1, 1024, "default");
        decoder.Append(values, 60, default);
        var result = decoder.Complete("test", new(0, 1200 * 160), 1200 * 160, default);

        Assert.Collection(result.Segments,
            value => Assert.Equal((1034L * 160, 1042L * 160), (value.StartSample, value.EndSample)),
            value => Assert.Equal((1044L * 160, 1064L * 160), (value.StartSample, value.EndSample)));
    }

    [Fact]
    public void ClipsTheLastFrameToActualSampleDuration()
    {
        var values = new float[3 * 3];
        SetSegment(values, 1, 0, 0, 3, 0.9f);
        var decoder = new KoumoriDecoder(Definition(), 1, 0, "speech");
        decoder.Append(values, 3, default);
        var result = decoder.Complete("test", new(0, 321), 321, default);
        Assert.Equal(321, Assert.Single(result.Segments).EndSample);
    }

    [Fact]
    public void EqualPeakPlateausProduceOnlyTheirFirstFrame()
    {
        var detector = new KoumoriPeakDetector(0.5f, 3);
        foreach (var value in new[] { 0.1f, 0.9f, 0.9f, 0.9f, 0.9f, 0.1f })
        {
            detector.Append(value);
        }
        detector.Complete();
        Assert.Equal(1, Assert.Single(detector.Peaks).Frame);
    }

    [Fact]
    public void RejectsInvalidModelProbabilities()
    {
        var decoder = new KoumoriDecoder(Definition(), 1, 0, "default");
        Assert.Throws<InvalidDataException>(() => decoder.Append([float.NaN, 0, 0], 1, default));
    }

    [Fact]
    public void SnapThresholdCanRejectAnEdgeWithoutChangingSubtitleSegmentation()
    {
        var values = new float[60 * 3];
        SetSegment(values, 1, 0, 10, 40, 0.8f);
        var decoder = new KoumoriDecoder(Definition(0.95f), 1, 0, "default");
        decoder.Append(values, 60, default);
        var result = decoder.Complete("test", new(0, 9600), 9600, default);
        Assert.Single(result.Segments);
        Assert.DoesNotContain(result.Boundaries, value => value.Kind == KoumoriBoundaryKind.Start);
        Assert.Contains(result.Boundaries, value => value.Kind == KoumoriBoundaryKind.End);
    }

    [Fact]
    public void FragmentedLongRecordingRetainsEverySlotAndBothBoundaryKinds()
    {
        const int FRAME_COUNT = 120000;
        const int SLOT_COUNT = 3;
        var values = new float[FRAME_COUNT * SLOT_COUNT * 3];
        for (var frame = 0; frame < FRAME_COUNT; frame += 12)
        {
            for (var slot = 0; slot < SLOT_COUNT; slot++)
            {
                SetSegment(values, SLOT_COUNT, slot, frame + 2, frame + 8, 0.8f);
            }
        }

        var decoder = new KoumoriDecoder(Definition(), SLOT_COUNT, 0, "default");
        decoder.Append(values, FRAME_COUNT, default);
        var result = decoder.Complete("long-recording", new(0, FRAME_COUNT * 160), FRAME_COUNT * 160, default);

        Assert.Equal(30000, result.Segments.Count);
        Assert.Equal(60000, result.Boundaries.Count);
        Assert.Equal(320, result.Segments[0].StartSample);
        Assert.Equal((FRAME_COUNT - 4L) * 160, result.Segments[^1].EndSample);
        Assert.Equal(new[] { 0, 1, 2 }, result.Segments.Select(segment => segment.Lane).Distinct().Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PairedInternalBoundariesSuppressUnpairedStartsAndPreserveTouchingSubtitles(bool hasEndPeak)
    {
        var values = new float[60 * 3];
        SetSegment(values, 1, 0, 10, 40, 0.8f);
        values[20 * 3 + 1] = 0.9f;
        if (hasEndPeak)
        {
            values[20 * 3 + 2] = 0.9f;
        }
        var decoder = new KoumoriDecoder(Definition(requirePairedInternalBoundary: true), 1, 0, "default");
        decoder.Append(values, 60, default);
        var result = decoder.Complete("paired", new(0, 9600), 9600, default);

        if (hasEndPeak)
        {
            Assert.Collection(result.Segments,
                segment => Assert.Equal((1600L, 3200L), (segment.StartSample, segment.EndSample)),
                segment => Assert.Equal((3200L, 6400L), (segment.StartSample, segment.EndSample)));
        }
        else
        {
            var segment = Assert.Single(result.Segments);
            Assert.Equal((1600L, 6400L), (segment.StartSample, segment.EndSample));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TooCloseStartPeaksDoNotDiscardCoverage(bool requirePairedInternalBoundary)
    {
        var values = new float[60 * 3];
        SetSegment(values, 1, 0, 10, 50, 0.9f);
        values[20 * 3 + 1] = 0.9f;
        values[25 * 3 + 1] = 0.9f;
        if (requirePairedInternalBoundary)
        {
            values[20 * 3 + 2] = 0.9f;
        }

        var decoder = new KoumoriDecoder(Definition(requirePairedInternalBoundary: requirePairedInternalBoundary, minDurationFrames: 10), 1, 0, "default");
        decoder.Append(values, 60, default);
        var result = decoder.Complete("test", new(0, 9600), 9600, default);

        Assert.Collection(result.Segments,
            segment => Assert.Equal((1600L, 3200L), (segment.StartSample, segment.EndSample)),
            segment => Assert.Equal((3200L, 8000L), (segment.StartSample, segment.EndSample)));
    }

    [Theory]
    [InlineData(8976, 0)]
    [InlineData(9119, 0)]
    [InlineData(9120, 1)]
    [InlineData(9280, 1)]
    public void MinimumDurationAppliesAfterMediaEndClipping(long sampleCount, int expectedCount)
    {
        var values = new float[57 * 3];
        SetSegment(values, 1, 0, 47, 57, 0.9f);
        var decoder = new KoumoriDecoder(Definition(minDurationFrames: 10), 1, 0, "default");
        decoder.Append(values, 57, default);
        var result = decoder.Complete("test", new(0, sampleCount), sampleCount, default);

        Assert.Equal(expectedCount, result.Segments.Count);
        Assert.All(result.Segments, segment => Assert.Equal((7520L, 9120L), (segment.StartSample, segment.EndSample)));
    }

    [Theory]
    [InlineData(0, 8960, 0)]
    [InlineData(9600, 24000, 0)]
    [InlineData(8000, 9600, 1)]
    [InlineData(0, 10400, 1)]
    public void MinimumDurationAppliesAfterAnalysisRangeClipping(long rangeStart, long rangeEnd, int expectedCount)
    {
        var values = new float[150 * 3];
        SetSegment(values, 1, 0, 50, 65, 0.9f);
        var decoder = new KoumoriDecoder(Definition(minDurationFrames: 10), 1, 0, "default");
        decoder.Append(values, 150, default);
        var result = decoder.Complete("test", new(rangeStart, rangeEnd), 24000, default);

        Assert.Equal(expectedCount, result.Segments.Count);
        Assert.All(result.Segments, segment => Assert.True(segment.EndSample - segment.StartSample >= 1600));
    }

    internal static KoumoriDecoderDefinition Definition(float? snapStartThreshold = null, bool requirePairedInternalBoundary = false, int minDurationFrames = 1)
    {
        return new()
        {
            SchemaVersion = 1,
            Algorithm = "coverage-boundary-slots-v1",
            FrameSeconds = 0.01,
            Profiles = new()
            {
                ["default"] = new()
                {
                    CoverageThreshold = 0.5f,
                    StartThreshold = 0.5f,
                    EndThreshold = 0.5f,
                    SnapStartThreshold = snapStartThreshold,
                    RequirePairedInternalBoundary = requirePairedInternalBoundary,
                    PeakRadiusFrames = 3,
                    EdgeRefinementFrames = 20,
                    MinDurationFrames = minDurationFrames,
                    DedupToleranceFrames = 1
                }
            }
        };
    }

    private static void SetSegment(float[] values, int slots, int slot, int first, int last, float score)
    {
        for (var frame = first; frame < last; frame++)
        {
            values[(frame * slots + slot) * 3] = score;
        }
        values[(first * slots + slot) * 3 + 1] = score;
        if ((last * slots + slot) * 3 + 2 < values.Length)
        {
            values[(last * slots + slot) * 3 + 2] = score;
        }
    }
}
