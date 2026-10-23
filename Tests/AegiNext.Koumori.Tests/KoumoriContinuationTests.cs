using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegiNext.Koumori.Tests;

public sealed class KoumoriContinuationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(900)]
    public void WeakSentencePausesJoinAcrossStreamingBlocksWithoutInventingSnapEdges(int offset)
    {
        var values = Sentence(offset + 620, offset: offset);
        var result = Decode(values, Definition(), offset: 0, blockFrames: 1024);

        var segment = Assert.Single(result.Segments);
        Assert.Equal(((offset + 10L) * 160, (offset + 590L) * 160), (segment.StartSample, segment.EndSample));
        Assert.Equal(0.9f * 500 / 580, segment.Score, 6);
        Assert.Equal(3, result.Boundaries.Count(value => value.Kind == KoumoriBoundaryKind.Start));
        Assert.Equal(3, result.Boundaries.Count(value => value.Kind == KoumoriBoundaryKind.End));
    }

    [Fact]
    public void SuffixFirstJoiningRecoversAMiddleRunWithoutItsOwnEndAnchor()
    {
        var values = Sentence(620);
        values[360 * 3 + 2] = 0;

        var segment = Assert.Single(Decode(values, Definition()).Segments);

        Assert.Equal((1600L, 94400L), (segment.StartSample, segment.EndSample));
    }

    [Fact]
    public void JoinedPauseSuppressesAWeakPairWhoseStartIsAfterTheCoverageGap()
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 200, 0.95f, 0);
        Run(values, 1, 0, 230, 590, 0, 0.95f);
        values[219 * 3 + 2] = 0.55f;
        values[236 * 3 + 1] = 0.55f;

        var segment = Assert.Single(Decode(values, Definition()).Segments);

        Assert.Equal((1600L, 94400L), (segment.StartSample, segment.EndSample));
    }

    [Theory]
    [InlineData(0.95f, 0.95f, 2)]
    [InlineData(0.95f, 0.55f, 1)]
    [InlineData(0.55f, 0.95f, 1)]
    [InlineData(0.55f, 0.55f, 1)]
    public void OnlyAnOrderedStrongInternalPairProtectsARealPause(float endScore, float startScore, int count)
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 200, 0.95f, endScore);
        Run(values, 1, 0, 215, 590, startScore, 0.95f);

        var result = Decode(values, Definition());

        Assert.Equal(count, result.Segments.Count);
        Assert.Equal(1600, result.Segments[0].StartSample);
        Assert.Equal(94400, result.Segments[^1].EndSample);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContinuationRequiresBothOuterAnchors(bool missingStart)
    {
        var values = Sentence(620);
        values[(missingStart ? 10 : 590) * 3 + (missingStart ? 1 : 2)] = 0;

        Assert.Equal(2, Decode(values, Definition()).Segments.Count);
    }

    [Fact]
    public void GapAndAnchoredSpanLimitsPreventUnboundedJoining()
    {
        var values = Sentence(620);
        Assert.Equal(3, Decode(values, Definition(maximumGap: 20)).Segments.Count);
        var limited = Decode(values, Definition(maximumSpan: 400));
        Assert.Collection(limited.Segments,
            segment => Assert.Equal((1600L, 32000L), (segment.StartSample, segment.EndSample)),
            segment => Assert.Equal((36800L, 94400L), (segment.StartSample, segment.EndSample)));

        values[10 * 3 + 1] = 0;
        values[590 * 3 + 2] = 0;
        values[1] = 0.95f;
        values[599 * 3 + 2] = 0.95f;
        Assert.Equal(2, Decode(values, Definition(maximumSpan: 580)).Segments.Count);
    }

    [Fact]
    public void AnOriginallySubminimumRunCannotBecomeAContinuationBridge()
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 200, 0.95f, 0.55f);
        Run(values, 1, 0, 230, 240, 0.55f, 0.55f);
        Run(values, 1, 0, 270, 590, 0.55f, 0.95f);

        var result = Decode(values, Definition());

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(32000, result.Segments[0].EndSample);
        Assert.Equal(43200, result.Segments[1].StartSample);
    }

    [Fact]
    public void RealShortSubtitlesAndOtherSlotsRemainIndependent()
    {
        var values = Sentence(760, slots: 2);
        Run(values, 2, 0, 700, 715, 0.95f, 0.95f);
        Run(values, 2, 1, 140, 155, 0.95f, 0.95f);

        var result = Decode(values, Definition(), slots: 2);

        Assert.Equal(3, result.Segments.Count);
        Assert.Contains(result.Segments, segment => segment.Slot == 0 && segment.StartSample == 112000 && segment.EndSample == 114400);
        Assert.Contains(result.Segments, segment => segment.Slot == 1 && segment.StartSample == 22400 && segment.EndSample == 24800);
        Assert.Contains(result.Segments, segment => segment.Slot == 0 && segment.StartSample == 1600 && segment.EndSample == 94400);
    }

    [Theory]
    [InlineData(580, 0)]
    [InlineData(575, 1)]
    public void JoinedRangesStillEnforceMinimumDurationAfterRequestedRangeClipping(int firstFrame, int count)
    {
        var result = Decode(Sentence(620), Definition(), range: new(firstFrame * 160, 590 * 160));

        Assert.Equal(count, result.Segments.Count);
        Assert.All(result.Segments, value => Assert.True(value.EndSample - value.StartSample >= 15 * 160));
    }

    [Fact]
    public void MissingV2FieldsAndNullInternalThresholdsKeepTheirExplicitFallbacks()
    {
        var values = Sentence(620);
        var legacy = Definition(algorithm: "coverage-boundary-slots-v1", includeContinuation: false);
        var disabled = Definition(includeContinuation: false);
        var fallback = Definition(internalStart: null, internalEnd: null);

        Assert.Equal(3, Decode(values, legacy).Segments.Count);
        Assert.Equal(3, Decode(values, disabled).Segments.Count);
        Assert.Equal(3, Decode(values, fallback).Segments.Count);
    }

    [Fact]
    public void ProtectedMicroGapsKeepTheirOriginalBoundarySemantics()
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 200, 0.95f, 0.55f);
        Run(values, 1, 0, 203, 590, 0.55f, 0.95f);

        Assert.Equal(2, Decode(values, Definition()).Segments.Count);
    }

    [Fact]
    public void InternalVetoFindsPeaksBelowTheGenerationThreshold()
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 200, 0.95f, 0.45f);
        Run(values, 1, 0, 215, 590, 0.45f, 0.95f);

        Assert.Equal(2, Decode(values, Definition(internalStart: 0.4f, internalEnd: 0.4f)).Segments.Count);
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(20, 2)]
    public void ALongGapRequiresAnAlreadyJoinedShortSuffixWithAShortOriginalLeader(int maximumFragment, int count)
    {
        var values = FragmentedSentence();

        var result = Decode(values, Definition(maximumGap: 20, internalStart: 0.55f, internalEnd: 0.55f,
            maximumFragmentGap: 60, maximumFragment: maximumFragment));

        Assert.Equal(count, result.Segments.Count);
        Assert.Equal(1600, result.Segments[0].StartSample);
        Assert.Equal(94400, result.Segments[^1].EndSample);
    }

    [Fact]
    public void AnIsolatedShortSubtitleDoesNotEnableTheLongGapAllowance()
    {
        var values = new float[320 * 3];
        Run(values, 1, 0, 10, 200, 0.95f, 0.52f);
        Run(values, 1, 0, 257, 287, 0.69f, 0.95f);

        Assert.Equal(2, Decode(values, FragmentDefinition()).Segments.Count);
    }

    [Theory]
    [InlineData(0.52f, 0.54f, 2)]
    [InlineData(0.52f, 0.69f, 1)]
    public void LongGapRequiresReliableRecoveryInsteadOfTwoWeakSides(float endScore, float startScore, int count)
    {
        var values = FragmentedSentence();
        values[200 * 3 + 2] = endScore;
        values[257 * 3 + 1] = startScore;

        Assert.Equal(count, Decode(values, FragmentDefinition()).Segments.Count);
    }

    [Fact]
    public void LongGapRejectsAReliableEndEvenWhenItsStartAndEndPeaksAreReversed()
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 200, 0.95f, 0);
        Run(values, 1, 0, 221, 251, 0, 0.54f);
        Run(values, 1, 0, 264, 590, 0.54f, 0.95f);
        values[220 * 3 + 2] = 0.95f;
        values[201 * 3 + 1] = 0.69f;

        Assert.Equal(2, Decode(values, FragmentDefinition()).Segments.Count);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    public void StrongRealPairsStillProtectBothShortAndLongFragmentGaps(bool shortGap, int count)
    {
        var values = FragmentedSentence();
        values[(shortGap ? 287 : 200) * 3 + 2] = 0.95f;
        values[(shortGap ? 300 : 257) * 3 + 1] = 0.95f;

        Assert.Equal(count, Decode(values, FragmentDefinition()).Segments.Count);
    }

    [Fact]
    public void FlushingASuffixResetsItsPriorShortJoinEvidence()
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 100, 0.95f, 0.52f);
        Run(values, 1, 0, 157, 187, 0.69f, 0.54f);
        Run(values, 1, 0, 250, 300, 0.54f, 0.54f);
        Run(values, 1, 0, 310, 590, 0.54f, 0.95f);

        Assert.Equal(3, Decode(values, FragmentDefinition()).Segments.Count);
    }

    [Fact]
    public void ConsecutiveLongJoinsUseTheOriginalLeadingRunLength()
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 100, 0.95f, 0.52f);
        Run(values, 1, 0, 157, 187, 0.69f, 0.52f);
        Run(values, 1, 0, 244, 274, 0.69f, 0.54f);
        Run(values, 1, 0, 287, 590, 0.54f, 0.95f);

        Assert.Single(Decode(values, FragmentDefinition()).Segments);
    }

    [KoumoriFixtureFact]
    public async Task V2PackagesLoadAndV1CannotSilentlyEnableContinuation()
    {
        await WithPackageAsync(Definition(), async directory =>
        {
            var package = await KoumoriModelPackage.LoadAsync(directory, default);
            Assert.Equal("coverage-boundary-slots-v2", package.Decoder.Algorithm);
        });
        await WithPackageAsync(Definition(algorithm: "coverage-boundary-slots-v1"), async directory =>
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(directory, default));
        });
        await WithPackageAsync(Definition(algorithm: "coverage-boundary-slots-v1", includeContinuation: false), async directory =>
        {
            await KoumoriModelPackage.LoadAsync(directory, default);
        });
    }

    [KoumoriFixtureFact]
    public async Task InvalidContinuationFieldsAreRejectedByThePackageLoader()
    {
        foreach (var (field, value) in new (string Field, JsonNode Value)[]
                 {
                     ("maxContinuationGapFrames", JsonValue.Create(-1)),
                     ("maxContinuationGapFrames", JsonValue.Create(1001)),
                     ("maxContinuationSpanFrames", JsonValue.Create(-1)),
                     ("maxContinuationSpanFrames", JsonValue.Create(10001)),
                     ("maxContinuationSpanFrames", JsonValue.Create(14)),
                     ("maxContinuationSpanFrames", JsonValue.Create(60)),
                     ("internalStartThreshold", JsonValue.Create(-0.01)),
                     ("internalEndThreshold", JsonValue.Create(1.01)),
                     ("internalStartThreshold", JsonValue.Create("NaN")),
                     ("internalEndThreshold", JsonValue.Create("Infinity"))
                 })
        {
            var definition = DefinitionJson();
            definition["profiles"]!["default"]![field] = value;
            await WithPackageJsonAsync(definition, async directory =>
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(directory, default));
            });
        }
    }

    [KoumoriFixtureFact]
    public async Task InvalidFragmentPolicyCombinationsAreRejectedByThePackageLoader()
    {
        foreach (var (field, value) in new (string Field, int Value)[]
                 {
                     ("maxFragmentContinuationGapFrames", -1),
                     ("maxFragmentContinuationGapFrames", 1001),
                     ("maxContinuationFragmentFrames", -1),
                     ("maxContinuationFragmentFrames", 10001),
                     ("maxFragmentContinuationGapFrames", 0),
                     ("maxContinuationFragmentFrames", 0),
                     ("maxContinuationGapFrames", 0),
                     ("maxFragmentContinuationGapFrames", 20),
                     ("maxContinuationFragmentFrames", 14),
                     ("maxContinuationFragmentFrames", 2102),
                     ("maxContinuationSpanFrames", 60)
                 })
        {
            var definition = DefinitionJson(maximumGap: 20, maximumFragmentGap: 60, maximumFragment: 30);
            definition["profiles"]!["default"]![field] = value;
            await WithPackageJsonAsync(definition, async directory =>
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriModelPackage.LoadAsync(directory, default));
            });
        }
    }

    private static KoumoriAnalysisResult Decode(float[] values, KoumoriDecoderDefinition definition, int slots = 1,
        long offset = 0, int blockFrames = 1024, KoumoriAnalysisRange? range = null)
    {
        var frameCount = values.Length / slots / 3;
        var decoder = new KoumoriDecoder(definition, slots, offset, "default");
        for (var begin = 0; begin < frameCount; begin += blockFrames)
        {
            var count = Math.Min(blockFrames, frameCount - begin);
            decoder.Append(values.AsSpan(begin * slots * 3, count * slots * 3), count, default);
        }
        return decoder.Complete("continuation", range ?? new(0, (offset + frameCount) * 160), (offset + frameCount) * 160, default);
    }

    private static float[] Sentence(int frames, int offset = 0, int slots = 1)
    {
        var values = new float[frames * slots * 3];
        Run(values, slots, 0, offset + 10, offset + 200, 0.95f, 0.55f);
        Run(values, slots, 0, offset + 230, offset + 360, 0.55f, 0.55f);
        Run(values, slots, 0, offset + 410, offset + 590, 0.55f, 0.95f);
        return values;
    }

    private static float[] FragmentedSentence()
    {
        var values = new float[620 * 3];
        Run(values, 1, 0, 10, 200, 0.95f, 0.52f);
        Run(values, 1, 0, 257, 287, 0.69f, 0.54f);
        Run(values, 1, 0, 300, 590, 0.54f, 0.95f);
        return values;
    }

    private static KoumoriDecoderDefinition FragmentDefinition() => Definition(maximumGap: 20,
        internalStart: 0.55f, internalEnd: 0.55f, maximumFragmentGap: 60, maximumFragment: 30);

    private static void Run(float[] values, int slots, int slot, int first, int last, float start, float end)
    {
        for (var frame = first; frame < last; frame++)
        {
            values[(frame * slots + slot) * 3] = 0.9f;
        }
        values[(first * slots + slot) * 3 + 1] = start;
        values[(last * slots + slot) * 3 + 2] = end;
    }

    private static KoumoriDecoderDefinition Definition(int maximumGap = 60, int maximumSpan = 2101,
        float? internalStart = 0.65f, float? internalEnd = 0.65f,
        string algorithm = "coverage-boundary-slots-v2", bool includeContinuation = true,
        int maximumFragmentGap = 0, int maximumFragment = 0) =>
        KoumoriModelPackage.Deserialize<KoumoriDecoderDefinition>(System.Text.Encoding.UTF8.GetBytes(
            DefinitionJson(maximumGap, maximumSpan, internalStart, internalEnd, algorithm, includeContinuation,
                maximumFragmentGap, maximumFragment).ToJsonString()));

    private static JsonObject DefinitionJson(int maximumGap = 60, int maximumSpan = 2101,
        float? internalStart = 0.65f, float? internalEnd = 0.65f,
        string algorithm = "coverage-boundary-slots-v2", bool includeContinuation = true,
        int maximumFragmentGap = 0, int maximumFragment = 0)
    {
        var profile = new JsonObject
        {
            ["coverageThreshold"] = 0.5, ["startThreshold"] = 0.5, ["endThreshold"] = 0.5,
            ["snapStartThreshold"] = 0.5, ["snapEndThreshold"] = 0.5,
            ["peakRadiusFrames"] = 3, ["edgeRefinementFrames"] = 20,
            ["minDurationFrames"] = 15, ["dedupToleranceFrames"] = 1,
            ["requirePairedInternalBoundary"] = true, ["maxCoverageGapFrames"] = 5
        };
        if (includeContinuation)
        {
            profile["maxContinuationGapFrames"] = maximumGap;
            profile["maxContinuationSpanFrames"] = maximumSpan;
            profile["maxFragmentContinuationGapFrames"] = maximumFragmentGap;
            profile["maxContinuationFragmentFrames"] = maximumFragment;
            profile["internalStartThreshold"] = internalStart;
            profile["internalEndThreshold"] = internalEnd;
        }
        return new()
        {
            ["schemaVersion"] = 1, ["algorithm"] = algorithm, ["frameSeconds"] = 0.01,
            ["profiles"] = new JsonObject { ["default"] = profile }
        };
    }

    private static Task WithPackageAsync(KoumoriDecoderDefinition definition, Func<string, Task> action) =>
        WithPackageJsonAsync(JsonSerializer.SerializeToNode(definition, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        })!.AsObject(), action);

    private static async Task WithPackageJsonAsync(JsonObject decoder, Func<string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext.Koumori.Continuation.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fixture = Path.Combine(Environment.GetEnvironmentVariable("KOUMORI_PARITY_FIXTURE")!, "package");
            foreach (var source in Directory.EnumerateFiles(fixture))
            {
                File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
            }
            var decoderBytes = System.Text.Encoding.UTF8.GetBytes(decoder.ToJsonString());
            await File.WriteAllBytesAsync(Path.Combine(directory, "decoder.json"), decoderBytes);
            var manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
            manifest["decoderSha256"] = Convert.ToHexStringLower(SHA256.HashData(decoderBytes));
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
            await action(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
