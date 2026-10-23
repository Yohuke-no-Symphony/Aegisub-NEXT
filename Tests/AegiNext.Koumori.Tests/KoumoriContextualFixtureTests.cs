using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.ML.OnnxRuntime;
using Xunit.Abstractions;

namespace AegiNext.Koumori.Tests;

public sealed class KoumoriContextualFixtureTests(ITestOutputHelper output)
{
    private static string FixtureDirectory => Environment.GetEnvironmentVariable("KOUMORI_CONTEXTUAL_FIXTURE")!;
    private static string PackageDirectory => Path.Combine(FixtureDirectory, "package");

    [KoumoriContextualFixtureFact]
    public async Task FrontendEncoderHeadAndPublishedBoundariesMatchPythonAcrossCanonicalCores()
    {
        using var reference = ReadReference();
        var package = await KoumoriModelPackage.LoadAsync(PackageDirectory, default);
        using var options = CreateOptions();
        using var encoder = new InferenceSession(package.EncoderModel!, options);
        using var head = new InferenceSession(package.Model, options);
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(PackageDirectory);
        Assert.True(analyzer.IsExperimental);
        foreach (var item in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var samples = ReadPcm(item);
            foreach (var chunk in item.GetProperty("chunks").EnumerateArray())
            {
                AssertChunk(package, encoder, head, samples, chunk);
            }

            var source = new KoumoriTestAudioSource(samples, 7000);
            var result = await analyzer.AnalyzeAsync(source);
            Assert.Equal(new(0, samples.Length), result.Range);
            Assert.InRange(source.MaximumRequestedSamples, 1, 480000);
            AssertExpectedPredictions(item, result);
            output.WriteLine($"{item.GetProperty("name").GetString()}: {result.Segments.Count} segments, {result.Boundaries.Count} boundaries, {source.ReadCount} partial PCM reads.");
        }
    }

    [KoumoriContextualFixtureFact]
    public async Task SubrangesKeepGlobalEncoderAlignmentAndEmptyRangesDoNotReadAudio()
    {
        using var reference = ReadReference();
        var samples = ReadPcm(reference.RootElement.GetProperty("cases").EnumerateArray().Last());
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(PackageDirectory);
        var whole = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples));
        var requested = new KoumoriAnalysisRange(1250 * 160, 1850 * 160);

        var ranged = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples, 4567), requested);

        Assert.Equal(requested, ranged.Range);
        Assert.Equal(whole.Boundaries.Where(value => value.Sample >= requested.StartSample && value.Sample <= requested.EndSample),
            ranged.Boundaries);
        var emptySource = new KoumoriTestAudioSource(samples);
        var empty = await analyzer.AnalyzeAsync(emptySource, new(16000, 16000));
        Assert.Equal(0, emptySource.ReadCount);
        Assert.Empty(empty.Segments);
        Assert.Empty(empty.Boundaries);
    }

    [KoumoriContextualFixtureFact]
    public async Task EstimatedDurationAndInvalidReadsCannotLeakOutOfMediaOrPoisonTheSession()
    {
        using var reference = ReadReference();
        var samples = ReadPcm(reference.RootElement.GetProperty("cases")[0]);
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(PackageDirectory);

        var result = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples, 333, 640000));

        Assert.Equal(new(0, samples.Length), result.Range);
        Assert.All(result.Boundaries, boundary => Assert.InRange(boundary.Sample, 0, samples.Length));
        foreach (var count in new[] { -1, int.MaxValue })
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => analyzer.AnalyzeAsync(new KoumoriInvalidReadAudioSource(count)));
        }

        var invalidPcm = new float[16000];
        invalidPcm[500] = float.NaN;
        await Assert.ThrowsAsync<InvalidDataException>(() => analyzer.AnalyzeAsync(new KoumoriTestAudioSource(invalidPcm)));
        var retry = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples));
        Assert.Equal(new(0, samples.Length), retry.Range);
    }

    [KoumoriContextualFixtureFact]
    public async Task CancellingTheFinalCoreReturnsNoResultAndTheTwoSessionsRemainReusable()
    {
        using var reference = ReadReference();
        var samples = ReadPcm(reference.RootElement.GetProperty("cases")[0]);
        var analyzer = await KoumoriAnalyzer.LoadAsync(PackageDirectory);
        try
        {
            using var cancellation = new CancellationTokenSource();
            var progress = new KoumoriTestProgress(value =>
            {
                if (value.CompletedFrames > 0)
                {
                    cancellation.Cancel();
                }
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples),
                progress: progress, cancellationToken: cancellation.Token));
            var retry = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples));
            Assert.Equal(new(0, samples.Length), retry.Range);
        }
        finally
        {
            await analyzer.DisposeAsync();
        }

        await Assert.ThrowsAsync<ObjectDisposedException>(() => analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples)));
    }

    [KoumoriContextualFixtureFact]
    public async Task DisposalCancelsAndDrainsTheContextualPcmRead()
    {
        var analyzer = await KoumoriAnalyzer.LoadAsync(PackageDirectory);
        var source = new KoumoriBlockedAudioSource();
        var pending = analyzer.AnalyzeAsync(source);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await analyzer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [KoumoriContextualFixtureFact]
    public async Task NativeHeadAndEncoderMetadataMustMatchTheirSeparateContracts()
    {
        foreach (var headAsEncoder in new[] { true, false })
        {
            using var fixture = new KoumoriV2TestPackage();
            foreach (var source in Directory.EnumerateFiles(PackageDirectory))
            {
                File.Copy(source, Path.Combine(fixture.DirectoryPath, Path.GetFileName(source)), true);
            }

            var path = Path.Combine(fixture.DirectoryPath, "manifest.json");
            var manifest = JsonNode.Parse(await File.ReadAllBytesAsync(path))!;
            var destination = headAsEncoder ? "encoder" : "model";
            var original = headAsEncoder ? "model" : "encoder";
            manifest[destination + "File"] = manifest[original + "File"]!.GetValue<string>();
            manifest[destination + "Sha256"] = manifest[original + "Sha256"]!.GetValue<string>();
            await File.WriteAllTextAsync(path, manifest.ToJsonString());

            await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));
        }
    }

    internal void AssertChunk(KoumoriModelPackage package, InferenceSession encoder, InferenceSession head,
        float[] samples, JsonElement chunk, string outputProperty = "output")
    {
        var definition = package.EncoderFrontend!;
        var center = chunk.GetProperty("centerStartFrame").GetInt64();
        var start = center * 160 - definition.LeftContextSamples;
        var pcm = new float[definition.InputSamples];
        var sourceStart = Math.Max(0, start);
        var destinationStart = checked((int)(sourceStart - start));
        var length = checked((int)Math.Max(0, Math.Min(samples.Length - sourceStart, pcm.Length - destinationStart)));
        samples.AsSpan(checked((int)sourceStart), length).CopyTo(pcm.AsSpan(destinationStart));
        var mel = new KoumoriEncoderFrontend(definition).ExtractInput(pcm, default);
        foreach (var probe in chunk.GetProperty("melProbes").EnumerateArray())
        {
            var value = mel[probe.GetProperty("band").GetInt32() * definition.InputFrames + probe.GetProperty("frame").GetInt32()];
            AssertClose(probe.GetProperty("value").GetSingle(), value, 3e-4f, 2e-4f, "encoder log-mel");
        }

        using var input = OrtValue.CreateTensorValueFromMemory(mel, [1, 80, 3000]);
        using var run = new RunOptions();
        using var encoded = encoder.Run(run, ["mel"], [input], ["hidden"]);
        var hidden = encoded[0].GetTensorDataAsSpan<float>();
        var context = new float[768 * 512];
        for (var frame = 0; frame < 512; frame++)
        {
            for (var channel = 0; channel < 768; channel++)
            {
                context[channel * 512 + frame] = hidden[(definition.ContextStartFrame + frame) * 768 + channel];
            }
        }

        foreach (var probe in chunk.GetProperty("contextProbes").EnumerateArray())
        {
            var value = context[probe.GetProperty("channel").GetInt32() * 512 + probe.GetProperty("frame").GetInt32()];
            AssertClose(probe.GetProperty("value").GetSingle(), value, 3e-4f, 2e-4f, "encoder core context");
        }

        var features = new KoumoriFrontend(package.Frontend).ExtractInput(pcm, start, center,
            (samples.Length + 159L) / 160, default);
        using var featureInput = OrtValue.CreateTensorValueFromMemory(features, [1, 82, 2048]);
        using var contextInput = OrtValue.CreateTensorValueFromMemory(context, [1, 768, 512]);
        using var predicted = head.Run(run, ["features", "context"], [featureInput, contextInput], ["probabilities"]);
        var actual = predicted[0].GetTensorDataAsSpan<float>();
        var expected = chunk.GetProperty(outputProperty).Deserialize<float[]>()!;
        Assert.Equal(expected.Length, actual.Length);
        var maximum = 0f;
        for (var index = 0; index < expected.Length; index++)
        {
            AssertClose(expected[index], actual[index], 1e-5f, 1e-4f, "fusion probability");
            maximum = Math.Max(maximum, Math.Abs(expected[index] - actual[index]));
        }

        output.WriteLine($"core {center}: maximum head probability error {maximum:R}.");
    }

    internal static void AssertExpectedPredictions(JsonElement expected, KoumoriAnalysisResult result)
    {
        var segments = expected.GetProperty("segments");
        Assert.Equal(segments.GetArrayLength(), result.Segments.Count);
        for (var index = 0; index < result.Segments.Count; index++)
        {
            Assert.Equal(segments[index].GetProperty("start").GetDouble(), result.Segments[index].StartSeconds, 8);
            Assert.Equal(segments[index].GetProperty("end").GetDouble(), result.Segments[index].EndSeconds, 8);
        }

        var boundaries = expected.GetProperty("boundaries").EnumerateArray().OrderBy(value => value.GetProperty("timeSeconds").GetDouble())
            .ThenBy(value => value.GetProperty("kind").GetString() == "start" ? KoumoriBoundaryKind.Start : KoumoriBoundaryKind.End)
            .ThenBy(value => value.GetProperty("slot").GetInt32()).ToArray();
        var actualBoundaries = result.Boundaries.OrderBy(value => value.Sample).ThenBy(value => value.Kind).ThenBy(value => value.Slot).ToArray();
        Assert.Equal(boundaries.Length, actualBoundaries.Length);
        for (var index = 0; index < actualBoundaries.Length; index++)
        {
            var boundary = boundaries[index];
            var actual = actualBoundaries[index];
            Assert.Equal(boundary.GetProperty("timeSeconds").GetDouble(), actual.Seconds, 8);
            Assert.Equal(boundary.GetProperty("kind").GetString() == "start" ? KoumoriBoundaryKind.Start : KoumoriBoundaryKind.End, actual.Kind);
            Assert.Equal(boundary.GetProperty("slot").GetInt32(), actual.Slot);
            AssertClose(boundary.GetProperty("confidence").GetSingle(), actual.Score, 2e-4f, 0, "boundary confidence");
        }
    }

    private static void AssertClose(float expected, float actual, float absoluteTolerance, float relativeTolerance, string label)
    {
        Assert.True(float.IsFinite(actual) && Math.Abs(expected - actual) <= absoluteTolerance + relativeTolerance * Math.Abs(expected),
            $"{label}: expected {expected:R}, actual {actual:R}.");
    }

    internal static SessionOptions CreateOptions()
    {
        var options = new SessionOptions { IntraOpNumThreads = 4, InterOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL };
        options.AddSessionConfigEntry("optimization.enable_gelu_approximation", "0");
        options.AddSessionConfigEntry("mlas.enable_gemm_fastmath_arm64_bfloat16", "0");
        return options;
    }

    private static JsonDocument ReadReference()
    {
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixtureDirectory, "context-reference.json")));
    }

    internal static float[] ReadPcm(JsonElement item, string? directory = null)
    {
        using var stream = File.OpenRead(Path.Combine(directory ?? FixtureDirectory, item.GetProperty("pcmFile").GetString()!));
        using var reader = new BinaryReader(stream);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        _ = reader.ReadUInt32();
        Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        var validFormat = false;
        while (stream.Position + 8 <= stream.Length)
        {
            var kind = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            var next = stream.Position + size + (size & 1);
            if (kind == "fmt ")
            {
                Assert.Equal(3, reader.ReadUInt16());
                Assert.Equal(1, reader.ReadUInt16());
                Assert.Equal(16000u, reader.ReadUInt32());
                _ = reader.ReadUInt32();
                Assert.Equal(4, reader.ReadUInt16());
                Assert.Equal(32, reader.ReadUInt16());
                validFormat = true;
            }
            else if (kind == "data")
            {
                Assert.True(validFormat);
                var bytes = reader.ReadBytes(checked((int)size));
                var count = item.GetProperty("pcmSampleCount").GetInt32();
                Assert.Equal(count * sizeof(float), bytes.Length);
                var samples = new float[count];
                Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
                return samples;
            }

            stream.Position = next;
        }

        throw new InvalidDataException("The contextual fixture WAV has no floating-point PCM data.");
    }
}
