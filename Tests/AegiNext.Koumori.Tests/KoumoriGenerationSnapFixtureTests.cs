using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Xunit.Abstractions;

namespace AegiNext.Koumori.Tests;

public sealed class KoumoriGenerationSnapFixtureTests(ITestOutputHelper output)
{
    private static string FixtureDirectory => Environment.GetEnvironmentVariable("KOUMORI_GENERATION_SNAP_FIXTURE")!;
    private static string PackageDirectory => Path.Combine(FixtureDirectory, "package");

    [KoumoriGenerationSnapFixtureFact]
    public async Task BothHeadsAndTaskRoutedResultsMatchPythonAcrossCanonicalEncoderCores()
    {
        using var reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(FixtureDirectory, "context-reference.json")));
        var package = await KoumoriModelPackage.LoadAsync(PackageDirectory, default);
        Assert.Equal(3, package.Manifest.SchemaVersion);
        using var options = KoumoriContextualFixtureTests.CreateOptions();
        using var encoder = new InferenceSession(package.EncoderModel!, options);
        using var generation = new InferenceSession(package.Model, options);
        using var snap = new InferenceSession(package.SnapModel!, options);
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(PackageDirectory);
        var assertions = new KoumoriContextualFixtureTests(output);
        var differs = false;
        foreach (var item in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var samples = KoumoriContextualFixtureTests.ReadPcm(item, FixtureDirectory);
            foreach (var chunk in item.GetProperty("chunks").EnumerateArray())
            {
                assertions.AssertChunk(package, encoder, generation, samples, chunk);
                assertions.AssertChunk(package, encoder, snap, samples, chunk, "snapOutput");
                differs |= !chunk.GetProperty("output").Deserialize<float[]>()!.SequenceEqual(
                    chunk.GetProperty("snapOutput").Deserialize<float[]>()!);
            }

            var source = new KoumoriTestAudioSource(samples, 7000);
            var result = await analyzer.AnalyzeAsync(source);
            Assert.Equal(new(0, samples.Length), result.Range);
            Assert.InRange(source.MaximumRequestedSamples, 1, 480000);
            KoumoriContextualFixtureTests.AssertExpectedPredictions(item, result);
            output.WriteLine($"{item.GetProperty("name").GetString()}: generation segments={result.Segments.Count}, snap boundaries={result.Boundaries.Count}, PCM reads={source.ReadCount}.");
        }
        Assert.True(differs, "The dual-head fixture must exercise two different probability tensors.");
    }

    [KoumoriGenerationSnapFixtureFact]
    public async Task SharedEncoderDualHeadSubrangesKeepWholeMediaSnapCoordinates()
    {
        using var reference = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(FixtureDirectory, "context-reference.json")));
        var item = reference.RootElement.GetProperty("cases").EnumerateArray().Last();
        var samples = KoumoriContextualFixtureTests.ReadPcm(item, FixtureDirectory);
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(PackageDirectory);
        var whole = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples));
        var requested = new KoumoriAnalysisRange(1250 * 160, 1850 * 160);

        var result = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples, 4567), requested);

        Assert.Equal(requested, result.Range);
        Assert.Equal(whole.Boundaries.Where(value => value.Sample >= requested.StartSample && value.Sample <= requested.EndSample),
            result.Boundaries);
    }
}
