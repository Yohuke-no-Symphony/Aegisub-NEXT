namespace AegiNext.Koumori.Tests;

public sealed class KoumoriGenerationSnapRuntimeTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SeparateHeadsKeepLegacyGenerationExactAndPublishOnlySnapBoundaries(int schemaVersion)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        using var parent = new KoumoriGenerationSnapTestPackage();
        if (schemaVersion == 1)
        {
            parent.UseLegacyGenerationOnly();
        }
        else
        {
            parent.UseGenerationOnly();
        }
        await using var dual = await KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath);
        await using var original = await KoumoriAnalyzer.LoadAsync(parent.DirectoryPath);
        var samples = new float[20000];
        var source = new KoumoriTestAudioSource(samples, 777);

        var result = await dual.AnalyzeAsync(source);
        var baseline = await original.AnalyzeAsync(new KoumoriTestAudioSource(samples));

        Assert.True(dual.IsExperimental);
        Assert.Equal(baseline.Segments, result.Segments);
        Assert.Equal(new KoumoriSegment(1600, 6400, 0.8f, 0), Assert.Single(result.Segments));
        Assert.Collection(result.Boundaries,
            boundary => Assert.Equal(new KoumoriBoundary(2240, KoumoriBoundaryKind.Start, 0.9f, 0), boundary),
            boundary => Assert.Equal(new KoumoriBoundary(7200, KoumoriBoundaryKind.End, 0.85f, 0), boundary));
        Assert.NotEqual(baseline.Boundaries.ToArray(), result.Boundaries.ToArray());
        Assert.InRange(source.MaximumRequestedSamples, 1, 480000);
        Assert.True(source.ReadCount > 1);
    }

    [Fact]
    public async Task WholeSubrangeAndEarlyEofKeepTheSameEncoderCoreOrigin()
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath);
        var samples = new float[200000];
        var whole = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples));
        var range = new KoumoriAnalysisRange(1036 * 160, 1070 * 160);

        var ranged = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples, 4567), range);
        var eof = await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples, 4567, 640000));

        Assert.Equal(whole.Boundaries.Where(value => value.Sample >= range.StartSample && value.Sample <= range.EndSample), ranged.Boundaries);
        var segment = Assert.Single(ranged.Segments);
        Assert.Equal((range.StartSample, 1064 * 160L), (segment.StartSample, segment.EndSample));
        Assert.Equal(whole.Segments, eof.Segments);
        Assert.Equal(whole.Boundaries, eof.Boundaries);
        Assert.Equal(new(0, samples.Length), eof.Range);
        var emptySource = new KoumoriTestAudioSource(samples);
        var empty = await analyzer.AnalyzeAsync(emptySource, new(16000, 16000));
        Assert.Empty(empty.Segments);
        Assert.Empty(empty.Boundaries);
        Assert.Equal(0, emptySource.ReadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonFiniteEitherHeadOutsideTheReadRangeIsRejectedBeforePublishingAnyResult(bool snap)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        fixture.SetPayload(snap ? "snapModel" : "model", snap ? "snap-model.onnx" : "model.onnx",
            KoumoriGenerationSnapTestPackage.NonFiniteHead);
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => analyzer.AnalyzeAsync(new KoumoriTestAudioSource(new float[20000])));
    }

    [Fact]
    public async Task InvalidSnapMetadataCannotConstructAndARepairedPackageLoadsNormally()
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.DirectoryPath, "snap-model.onnx"));
        fixture.SetPayload("snapModel", "snap-model.onnx", await File.ReadAllBytesAsync(Path.Combine(fixture.DirectoryPath, "encoder.onnx")));

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));

        fixture.SetPayload("snapModel", "snap-model.onnx", bytes);
        await using var repaired = await KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath);
        Assert.Single((await repaired.AnalyzeAsync(new KoumoriTestAudioSource(new float[20000]))).Segments);
    }

    [Theory]
    [InlineData("shape")]
    [InlineData("type")]
    [InlineData("context-type")]
    [InlineData("output-name")]
    public async Task ThirdSessionMustMatchEverySharedTensorContract(string defect)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        fixture.SetPayload("snapModel", "snap-model.onnx", KoumoriGenerationSnapTestPackage.InvalidSnapGraph(defect));

        await Assert.ThrowsAsync<InvalidDataException>(() => KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath));
    }

    [Fact]
    public async Task FinalCoreCancellationReturnsNoResultAndAllThreeSessionsCanBeReused()
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        await using var analyzer = await KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath);
        var samples = new float[20000];
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

        Assert.Single((await analyzer.AnalyzeAsync(new KoumoriTestAudioSource(samples))).Segments);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothDisposalPathsCancelAndDrainAnActiveThreeSessionRequest(bool asynchronous)
    {
        using var fixture = new KoumoriGenerationSnapTestPackage();
        var analyzer = await KoumoriAnalyzer.LoadAsync(fixture.DirectoryPath);
        var source = new KoumoriBlockedAudioSource();
        var pending = analyzer.AnalyzeAsync(source);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (asynchronous)
        {
            await analyzer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            await Task.Run(analyzer.Dispose).WaitAsync(TimeSpan.FromSeconds(10));
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => analyzer.AnalyzeAsync(new KoumoriTestAudioSource(new float[100])));
        analyzer.Dispose();
        await analyzer.DisposeAsync();
    }
}
