namespace AegiNext.Koumori.Tests;

public sealed class KoumoriGenerationSnapDecoderTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(1024)]
    public void GenerationAndInternalContinuationIgnoreEverySnapChannelAcrossBlocks(int blockFrames)
    {
        var generation = new float[1300 * 2 * 3];
        SetRun(generation, 2, 0, 900, 1000, 0.9f);
        SetRun(generation, 2, 0, 1030, 1200, 0.9f);
        generation[(1000 * 2) * 3 + 2] = 0.55f;
        generation[(1030 * 2) * 3 + 1] = 0.55f;
        SetRun(generation, 2, 1, 950, 1100, 0.8f);
        var snap = new float[generation.Length];
        SetRun(snap, 2, 0, 100, 200, 0.99f);
        snap[(1000 * 2) * 3 + 2] = 0.99f;
        snap[(1030 * 2) * 3 + 1] = 0.99f;
        SetRun(snap, 2, 1, 1050, 1250, 0.95f);
        var definition = Definition();

        var original = Decode(generation, generation, definition, blockFrames);
        var combined = Decode(generation, snap, definition, blockFrames);
        var snapOnly = Decode(snap, snap, definition, blockFrames);

        Assert.Equal(original.Segments, combined.Segments);
        Assert.Equal(snapOnly.Boundaries, combined.Boundaries);
        Assert.Contains(combined.Segments, value => value.Slot == 0 && value.StartSample == 900 * 160 && value.EndSample == 1200 * 160);
        Assert.Contains(combined.Segments, value => value.Slot == 1 && value.Lane == 1);
        Assert.DoesNotContain(combined.Segments, value => value.StartSample == 100 * 160);
        Assert.NotEqual(original.Boundaries.ToArray(), combined.Boundaries.ToArray());
    }

    [Fact]
    public void LegacyAppendRemainsIdenticalToTwoEqualHeads()
    {
        var values = new float[1300 * 2 * 3];
        SetRun(values, 2, 0, 1000, 1100, 0.9f);
        SetRun(values, 2, 1, 1010, 1200, 0.8f);
        var legacy = new KoumoriDecoder(Definition(), 2, 1024, "default");
        legacy.Append(values, 1300, default);

        var result = legacy.Complete("dual", new(1024 * 160, 2324 * 160), 2324 * 160, default);
        var dual = Decode(values, values, Definition(), 1024, 1024);

        Assert.Equal(result.Segments, dual.Segments);
        Assert.Equal(result.Boundaries, dual.Boundaries);
    }

    [Theory]
    [InlineData(false, float.NaN)]
    [InlineData(true, float.NaN)]
    [InlineData(true, float.PositiveInfinity)]
    [InlineData(true, -0.01f)]
    [InlineData(true, 1.01f)]
    public void BothHeadsAreFullyValidatedBeforeAnyFrameIsAdded(bool invalidSnap, float value)
    {
        var generation = new float[100 * 2 * 3];
        SetRun(generation, 2, 0, 10, 50, 0.9f);
        var snap = generation.ToArray();
        (invalidSnap ? snap : generation)[^1] = value;
        var decoder = new KoumoriDecoder(Definition(), 2, 0, "default");

        Assert.Throws<InvalidDataException>(() => decoder.Append(generation, snap, 100, default));

        (invalidSnap ? snap : generation)[^1] = 0;
        decoder.Append(generation, snap, 100, default);
        var actual = decoder.Complete("dual", new(0, 16000), 16000, default);
        var expected = Decode(generation, snap, Definition(), 100);
        Assert.Equal(expected.Segments, actual.Segments);
        Assert.Equal(expected.Boundaries, actual.Boundaries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteHeadCannotAdvanceTheStream(bool incompleteSnap)
    {
        var values = new float[100 * 2 * 3];
        var incomplete = new float[values.Length - 1];
        var decoder = new KoumoriDecoder(Definition(), 2, 0, "default");

        Assert.Throws<InvalidDataException>(() => decoder.Append(incompleteSnap ? values : incomplete,
            incompleteSnap ? incomplete : values, 100, default));
    }

    [Fact]
    public void DualHeadRangeAndMediaClippingRetainOneGlobalFrameOrigin()
    {
        var generation = new float[100 * 2 * 3];
        SetRun(generation, 2, 0, 10, 50, 0.9f);
        var snap = new float[generation.Length];
        SetRun(snap, 2, 1, 20, 60, 0.8f);
        var decoder = new KoumoriDecoder(Definition(), 2, 1024, "default");
        decoder.Append(generation, snap, 100, default);
        var range = new KoumoriAnalysisRange(1040 * 160, 1075 * 160);

        var result = decoder.Complete("dual", range, range.EndSample, default);

        Assert.Equal((range.StartSample, 1074 * 160L), (Assert.Single(result.Segments).StartSample, result.Segments[0].EndSample));
        var boundary = Assert.Single(result.Boundaries);
        Assert.Equal((1044 * 160L, KoumoriBoundaryKind.Start, 1), (boundary.Sample, boundary.Kind, boundary.Slot));
    }

    private static KoumoriAnalysisResult Decode(float[] generation, float[] snap, KoumoriDecoderDefinition definition,
        int blockFrames, long offset = 0)
    {
        var frames = generation.Length / 6;
        var decoder = new KoumoriDecoder(definition, 2, offset, "default");
        for (var begin = 0; begin < frames; begin += blockFrames)
        {
            var count = Math.Min(blockFrames, frames - begin);
            decoder.Append(generation.AsSpan(begin * 6, count * 6), snap.AsSpan(begin * 6, count * 6), count, default);
        }
        return decoder.Complete("dual", new(offset * 160, (offset + frames) * 160), (offset + frames) * 160, default);
    }

    private static KoumoriDecoderDefinition Definition()
    {
        return new()
        {
            SchemaVersion = 1,
            Algorithm = "coverage-boundary-slots-v2",
            FrameSeconds = 0.01,
            Profiles = new()
            {
                ["default"] = new()
                {
                    CoverageThreshold = 0.5f, StartThreshold = 0.5f, EndThreshold = 0.5f,
                    SnapStartThreshold = 0.25f, SnapEndThreshold = 0.25f,
                    InternalStartThreshold = 0.8f, InternalEndThreshold = 0.8f,
                    PeakRadiusFrames = 3, EdgeRefinementFrames = 20, MinDurationFrames = 15,
                    DedupToleranceFrames = 1, RequirePairedInternalBoundary = true, MaxCoverageGapFrames = 5,
                    MaxContinuationGapFrames = 60, MaxContinuationSpanFrames = 2101
                }
            }
        };
    }

    private static void SetRun(float[] values, int slots, int slot, int first, int last, float score)
    {
        for (var frame = first; frame < last; frame++)
        {
            values[(frame * slots + slot) * 3] = score;
        }
        values[(first * slots + slot) * 3 + 1] = score;
        values[(last * slots + slot) * 3 + 2] = score;
    }
}
