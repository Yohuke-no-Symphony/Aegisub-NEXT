using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Xunit.Abstractions;

namespace AegiNext.Koumori.Tests;

public sealed class KoumoriFixtureTests(ITestOutputHelper output)
{
    private static string FixtureDirectory => Environment.GetEnvironmentVariable("KOUMORI_PARITY_FIXTURE")!;

    [KoumoriFixtureFact]
    public void FrontendMatchesPython()
    {
        var definition = KoumoriModelPackage.Deserialize<KoumoriFrontendDefinition>(File.ReadAllBytes(Path.Combine(FixtureDirectory, "package", "frontend.json")));
        using var reference = ReadJson("reference_features.json");
        var pcm = reference.RootElement.GetProperty("pcm").Deserialize<float[]>()!;
        var expected = reference.RootElement.GetProperty("features").Deserialize<float[][]>()!;
        var frontend = new KoumoriFrontend(definition);
        var features = frontend.ExtractInput(pcm, 0, 0, expected.Length, default);
        var expectedInput = new float[82 * 2048];
        for (var frame = 0; frame < expected.Length; frame++)
        {
            for (var feature = 0; feature < 82; feature++)
            {
                expectedInput[feature * 2048 + 512 + frame] = expected[frame][feature];
            }
        }

        AssertClose(expectedInput, features, 2e-4f, 2e-5f, "frontend");
    }

    [KoumoriFixtureFact]
    public async Task NativeInferenceMatchesPython()
    {
        var package = await KoumoriModelPackage.LoadAsync(Path.Combine(FixtureDirectory, "package"), default);
        using var reference = ReadJson("reference_features.json");
        var pcm = reference.RootElement.GetProperty("pcm").Deserialize<float[]>()!;
        var frontend = new KoumoriFrontend(package.Frontend);
        var features = frontend.ExtractInput(pcm, 0, 0, (pcm.Length + 159) / 160, default);
        AssertNativeOutput(package, features, "reference_model.json");
    }

    [KoumoriFixtureFact]
    public async Task GlobalFrameGridAndFluxRemainContinuousInTheSecondWindow()
    {
        var package = await KoumoriModelPackage.LoadAsync(Path.Combine(FixtureDirectory, "package"), default);
        using var reference = ReadJson("reference_features.json");
        using var continuity = ReadJson("reference_continuity.json");
        var shortPcm = reference.RootElement.GetProperty("pcm").Deserialize<float[]>()!;
        var pcm = Enumerable.Range(0, 8).SelectMany(_ => shortPcm).ToArray();
        var totalFrames = (pcm.Length + 159) / 160;
        var frontend = new KoumoriFrontend(package.Frontend);
        var firstReadSample = (512 - 1) * 160 - 200;
        var features = frontend.ExtractInput(pcm.AsSpan(firstReadSample), firstReadSample, 1024, totalFrames, default);
        var indices = continuity.RootElement.GetProperty("probeFrameIndices").Deserialize<int[]>()!;
        var expected = continuity.RootElement.GetProperty("features").Deserialize<float[][]>()!;
        for (var index = 0; index < indices.Length; index++)
        {
            var frame = indices[index] - 512;
            if (frame is < 0 or >= 2048)
            {
                continue;
            }

            var actual = Enumerable.Range(0, 82).Select(feature => features[feature * 2048 + frame]).ToArray();
            AssertClose(expected[index], actual, 2e-4f, 2e-5f, $"global frame {indices[index]}");
        }
        AssertNativeOutput(package, features, "reference_continuity.json");
    }

    [KoumoriFixtureFact]
    public async Task SlotDecoderMatchesPythonTargets()
    {
        var package = await KoumoriModelPackage.LoadAsync(Path.Combine(FixtureDirectory, "package"), default);
        using var reference = ReadJson("reference_decode.json");
        var probabilities = reference.RootElement.GetProperty("probabilities").Deserialize<float[][][]>()!;
        var values = probabilities.SelectMany(frame => frame.SelectMany(slot => slot)).ToArray();
        var sampleCount = (long)Math.Round(reference.RootElement.GetProperty("durationSeconds").GetDouble() * 16000);
        var decoder = new KoumoriDecoder(package.Decoder, package.Manifest.Slots, 0, "default");
        decoder.Append(values, probabilities.Length, default);
        var result = decoder.Complete(package.Manifest.ModelId, new(0, sampleCount), sampleCount, default);
        var expected = reference.RootElement.GetProperty("segments");
        Assert.Equal(expected.GetArrayLength(), result.Segments.Count);
        for (var index = 0; index < result.Segments.Count; index++)
        {
            var actual = result.Segments[index];
            Assert.Equal(expected[index].GetProperty("start").GetDouble(), actual.StartSeconds, 8);
            Assert.Equal(expected[index].GetProperty("end").GetDouble(), actual.EndSeconds, 8);
            Assert.Equal(expected[index].GetProperty("confidence").GetSingle(), actual.Score, 5);
            Assert.Equal(expected[index].GetProperty("slot").GetInt32(), actual.Slot);
            Assert.Equal(expected[index].GetProperty("lane").GetInt32(), actual.Lane);
        }
    }

    [KoumoriFixtureFact]
    public async Task AnalyzerUsesBoundedPartialReadsAndHonorsCancellationAndDisposal()
    {
        using var reference = ReadJson("reference_features.json");
        var original = reference.RootElement.GetProperty("pcm").Deserialize<float[]>()!;
        var samples = Enumerable.Range(0, 8).SelectMany(_ => original).ToArray();
        var source = new KoumoriTestAudioSource(samples, 7000);
        var analyzer = await KoumoriAnalyzer.LoadAsync(Path.Combine(FixtureDirectory, "package"));
        var result = await analyzer.AnalyzeAsync(source);
        Assert.Equal(new(0, samples.Length), result.Range);
        Assert.True(source.ReadCount > 3);
        Assert.InRange(source.MaximumRequestedSamples, 1, 328080);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzer.AnalyzeAsync(source, cancellationToken: cancelled.Token));
        await analyzer.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => analyzer.AnalyzeAsync(source));
    }

    [KoumoriFixtureFact]
    public void PairedBoundaryAndMicroCoverageDecodingMatchesPython()
    {
        using var paired = ReadJson("reference_decode_paired.json");
        AssertDecodedFixture(paired.RootElement);
        using var micro = ReadJson("reference_decode_microcoverage.json");
        foreach (var item in micro.RootElement.GetProperty("cases").EnumerateArray())
        {
            AssertDecodedFixture(item);
        }
    }

    [KoumoriFixtureFact]
    public void DecoderRegressionCasesMatchPython()
    {
        using var regression = ReadJson("reference_decode_regressions.json");
        foreach (var item in regression.RootElement.GetProperty("cases").EnumerateArray())
        {
            AssertDecodedFixture(item);
        }
    }

    [KoumoriFixtureFact]
    public void ContinuationCasesMatchPythonAcrossChunkedGlobalFrameOffsets()
    {
        using var reference = ReadJson("reference_decode_continuation.json");
        foreach (var item in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            AssertDecodedFixture(item);
            AssertDecodedFixture(item, 31, 900);
            AssertDecodedFixture(item, 31, 1024);
        }
    }

    [KoumoriTrainedParityFact]
    public async Task TrainedPackageMatchesPythonOnRealValidationPcmAndAlignedRanges()
    {
        var directory = Environment.GetEnvironmentVariable("KOUMORI_TRAINED_PARITY")!;
        File.Delete(Path.Combine(directory, "native-parity-summary.json"));
        using var reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, "reference.json")));
        var root = reference.RootElement;
        Assert.Equal("validation", root.GetProperty("split").GetString());
        Assert.False(root.GetProperty("testDataRead").GetBoolean());
        Assert.Equal(KoumoriAnalyzer.SAMPLE_RATE, root.GetProperty("sampleRate").GetInt32());
        var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, root.GetProperty("pcmFile").GetString()!));
        Assert.Equal(root.GetProperty("sampleCount").GetInt32() * sizeof(float), bytes.Length);
        Assert.Equal(root.GetProperty("pcmSha256").GetString(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        var samples = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
        var packageDirectory = root.GetProperty("packageDirectory").GetString()!;
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(packageDirectory, "manifest.json")));
        var schemaVersion = manifest.RootElement.GetProperty("schemaVersion").GetInt32();
        var maximumReadSamples = schemaVersion switch
        {
            1 => 328080,
            2 or 3 => 480000,
            _ => throw new InvalidDataException("Unsupported trained parity package schema.")
        };
        Assert.Equal(root.GetProperty("modelSha256").GetString(), manifest.RootElement.GetProperty("modelSha256").GetString());
        Assert.Equal(root.GetProperty("decoderSha256").GetString(), manifest.RootElement.GetProperty("decoderSha256").GetString());
        Assert.True(manifest.RootElement.GetProperty("training").GetProperty("trained").GetBoolean());
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(packageDirectory);
        Assert.Equal(root.GetProperty("modelId").GetString(), analyzer.ModelId);
        var actualCases = new List<object>();
        var maximumSegmentScoreError = 0f;
        var maximumBoundaryScoreError = 0f;
        foreach (var item in root.GetProperty("cases").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            var range = new KoumoriAnalysisRange(item.GetProperty("startSample").GetInt64(), item.GetProperty("endSample").GetInt64());
            var source = new KoumoriTestAudioSource(samples, 7000);
            var result = await analyzer.AnalyzeAsync(source, range);
            var boundaries = result.Boundaries.OrderBy(value => value.Sample).ThenBy(value => value.Kind).ThenBy(value => value.Slot).ToArray();
            actualCases.Add(new
            {
                name,
                startSample = result.Range.StartSample,
                endSample = result.Range.EndSample,
                source.ReadCount,
                source.MaximumRequestedSamples,
                segments = result.Segments.Select(value => new { value.StartSample, value.EndSample, value.Slot, value.Lane, value.Score }),
                boundaries = boundaries.Select(value => new { value.Sample, kind = (int)value.Kind, value.Slot, value.Score })
            });
            await File.WriteAllTextAsync(Path.Combine(directory, "native-result.json"), JsonSerializer.Serialize(new
            {
                analyzer.ModelId,
                cases = actualCases
            }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            Assert.Equal(range, result.Range);
            Assert.True(source.ReadCount > 3);
            Assert.InRange(source.MaximumRequestedSamples, 1, maximumReadSamples);
            var expectedSegments = item.GetProperty("segments");
            Assert.Equal(expectedSegments.GetArrayLength(), result.Segments.Count);
            for (var index = 0; index < result.Segments.Count; index++)
            {
                var expected = expectedSegments[index];
                var actual = result.Segments[index];
                Assert.Equal(expected.GetProperty("startSample").GetInt64(), actual.StartSample);
                Assert.Equal(expected.GetProperty("endSample").GetInt64(), actual.EndSample);
                Assert.Equal(expected.GetProperty("slot").GetInt32(), actual.Slot);
                Assert.Equal(expected.GetProperty("lane").GetInt32(), actual.Lane);
                var error = Math.Abs(expected.GetProperty("score").GetSingle() - actual.Score);
                maximumSegmentScoreError = Math.Max(maximumSegmentScoreError, error);
                Assert.InRange(error, 0, 2e-4f);
            }

            var expectedBoundaries = item.GetProperty("boundaries");
            Assert.Equal(expectedBoundaries.GetArrayLength(), boundaries.Length);
            for (var index = 0; index < boundaries.Length; index++)
            {
                var expected = expectedBoundaries[index];
                var actual = boundaries[index];
                Assert.Equal(expected.GetProperty("sample").GetInt64(), actual.Sample);
                Assert.Equal(expected.GetProperty("kind").GetInt32(), (int)actual.Kind);
                Assert.Equal(expected.GetProperty("slot").GetInt32(), actual.Slot);
                var error = Math.Abs(expected.GetProperty("score").GetSingle() - actual.Score);
                maximumBoundaryScoreError = Math.Max(maximumBoundaryScoreError, error);
                Assert.InRange(error, 0, 2e-4f);
            }

            output.WriteLine($"{name}: {result.Segments.Count} segments, {boundaries.Length} boundaries, {source.ReadCount} bounded PCM reads.");
        }

        output.WriteLine($"Maximum score error: segment {maximumSegmentScoreError:R}, boundary {maximumBoundaryScoreError:R}.");
        await File.WriteAllTextAsync(Path.Combine(directory, "native-parity-summary.json"), JsonSerializer.Serialize(new
        {
            analyzer.ModelId,
            schemaVersion,
            maximumReadSamples,
            modelSha256 = root.GetProperty("modelSha256").GetString(),
            decoderSha256 = root.GetProperty("decoderSha256").GetString(),
            caseCount = actualCases.Count,
            maximumSegmentScoreError,
            maximumBoundaryScoreError,
            passed = true
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static void AssertDecodedFixture(JsonElement reference, int blockFrames = 1024, int firstFrame = 0)
    {
        var definition = KoumoriModelPackage.Deserialize<KoumoriDecoderDefinition>(System.Text.Encoding.UTF8.GetBytes(reference.GetProperty("decoder").GetRawText()));
        var probabilities = reference.GetProperty("probabilities").Deserialize<float[][][]>()!;
        var sampleCount = (long)Math.Round(reference.GetProperty("durationSeconds").GetDouble() * 16000);
        var decoder = new KoumoriDecoder(definition, probabilities[0].Length, firstFrame, "default");
        for (var begin = 0; begin < probabilities.Length; begin += blockFrames)
        {
            var count = Math.Min(blockFrames, probabilities.Length - begin);
            decoder.Append(probabilities.Skip(begin).Take(count).SelectMany(frame => frame.SelectMany(slot => slot)).ToArray(), count, default);
        }
        var offsetSamples = firstFrame * 160L;
        var offsetSeconds = firstFrame * 0.01;
        var result = decoder.Complete("fixture", new(offsetSamples, offsetSamples + sampleCount), offsetSamples + sampleCount, default);
        var expected = reference.GetProperty("segments");
        Assert.Equal(expected.GetArrayLength(), result.Segments.Count);
        for (var index = 0; index < result.Segments.Count; index++)
        {
            var actual = result.Segments[index];
            Assert.Equal(expected[index].GetProperty("start").GetDouble() + offsetSeconds, actual.StartSeconds, 8);
            Assert.Equal(expected[index].GetProperty("end").GetDouble() + offsetSeconds, actual.EndSeconds, 8);
            Assert.Equal(expected[index].GetProperty("confidence").GetSingle(), actual.Score, 5);
            Assert.Equal(expected[index].GetProperty("slot").GetInt32(), actual.Slot);
            Assert.Equal(expected[index].GetProperty("lane").GetInt32(), actual.Lane);
        }
    }

    [KoumoriFixtureFact]
    public async Task CancelledAnalysisReturnsNoPartialResultAndTheSessionCanBeReused()
    {
        using var reference = ReadJson("reference_features.json");
        var original = reference.RootElement.GetProperty("pcm").Deserialize<float[]>()!;
        var source = new KoumoriTestAudioSource(Enumerable.Range(0, 8).SelectMany(_ => original).ToArray());
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(Path.Combine(FixtureDirectory, "package"));
        using var cancellation = new CancellationTokenSource();
        var progress = new KoumoriTestProgress(value =>
        {
            if (value.CompletedFrames > 0)
            {
                cancellation.Cancel();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzer.AnalyzeAsync(source, progress: progress, cancellationToken: cancellation.Token));
        var completed = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(original));
        Assert.Equal(original.Length, completed.Range.EndSample);
    }

    [KoumoriFixtureFact]
    public async Task DisposalCancelsAndDrainsAnActiveSourceRead()
    {
        var analyzer = await KoumoriAnalyzer.LoadAsync(Path.Combine(FixtureDirectory, "package"));
        var source = new KoumoriBlockedAudioSource();
        var analysis = analyzer.AnalyzeAsync(source);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await analyzer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis);
    }

    [KoumoriFixtureFact]
    public async Task TamperedPackagesAreRejectedBeforeNativeLoading()
    {
        var directory = Path.Combine(FixtureDirectory, ".test-artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var source in Directory.EnumerateFiles(Path.Combine(FixtureDirectory, "package")))
            {
                File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
            }
            await File.AppendAllTextAsync(Path.Combine(directory, "model.onnx"), "changed");
            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriAnalyzer.LoadAsync(directory));
            Assert.Contains("digest mismatch", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [KoumoriFixtureFact]
    public async Task EarlyEofShortensAnEstimatedMediaDuration()
    {
        using var reference = ReadJson("reference_features.json");
        var pcm = reference.RootElement.GetProperty("pcm").Deserialize<float[]>()!;
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(Path.Combine(FixtureDirectory, "package"));
        var result = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(pcm, declaredSampleCount: 40 * 16000));
        Assert.Equal(pcm.Length, result.Range.EndSample);
        Assert.All(result.Segments, segment => Assert.True(segment.EndSample <= pcm.Length));
        Assert.True(analyzer.IsExperimental);
    }

    [Fact]
    public async Task MissingModelPackageFailsExplicitly()
    {
        await Assert.ThrowsAnyAsync<IOException>(() => KoumoriAnalyzer.LoadAsync(Path.Combine(AppContext.BaseDirectory, "missing-koumori-model")));
    }

    private void AssertNativeOutput(KoumoriModelPackage package, float[] features, string filename)
    {
        using var reference = ReadJson(filename);
        var expected = reference.RootElement.GetProperty("output").Deserialize<float[]>()!;
        using var session = new InferenceSession(package.Model);
        using var input = OrtValue.CreateTensorValueFromMemory(features, [1, 82, 2048]);
        using var run = new RunOptions();
        using var values = session.Run(run, ["features"], [input], ["probabilities"]);
        AssertClose(expected, values[0].GetTensorDataAsSpan<float>(), 1e-5f, 1e-4f, $"ONNX {filename}");
    }

    private void AssertClose(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual, float absoluteTolerance, float relativeTolerance, string label)
    {
        Assert.Equal(expected.Length, actual.Length);
        var maximum = 0f;
        for (var index = 0; index < expected.Length; index++)
        {
            var error = Math.Abs(expected[index] - actual[index]);
            maximum = Math.Max(maximum, error);
            Assert.True(error <= absoluteTolerance + relativeTolerance * Math.Abs(expected[index]),
                $"{label}[{index}]: expected {expected[index]:R}, actual {actual[index]:R}, absolute error {error:R}.");
        }
        output.WriteLine($"{label}: maximum absolute error {maximum:R}.");
    }

    private static JsonDocument ReadJson(string filename)
    {
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, filename)));
    }
}
