namespace AegiNext.Koumori.Tests;

public sealed class KoumoriEncoderFrontendTests
{
    [Fact]
    public void SilenceUsesWhisperLogFloorWithoutAnAcousticPowerScale()
    {
        var definition = KoumoriV2TestPackage.CreateEncoderDefinition();
        var frontend = new KoumoriEncoderFrontend(definition);

        var result = frontend.ExtractInput(new float[480000], default);

        Assert.Equal(80 * 3000, result.Length);
        Assert.All(result, value => Assert.Equal(-1.5f, value));
    }

    [Fact]
    public void PeriodicHannAndFourHundredPointFftPreserveTheUnscaledDcAndSideBand()
    {
        var definition = KoumoriV2TestPackage.CreateEncoderDefinition();
        var frontend = new KoumoriEncoderFrontend(definition);

        var result = frontend.ExtractInput(Enumerable.Repeat(0.25f, 480000).ToArray(), default);

        var maximum = (float)Math.Log10(50 * 50);
        foreach (var frame in new[] { 0, 1, 1500, 2999 })
        {
            Assert.Equal((maximum + 4) / 4, result[frame], 5);
            Assert.Equal(((float)Math.Log10(25 * 25) + 4) / 4, result[3000 + frame], 5);
            Assert.Equal((maximum - 8 + 4) / 4, result[79 * 3000 + frame], 5);
        }
    }

    [Fact]
    public void ReflectPaddingAppliesToTheChunkAndTheDynamicFloorUsesTheWholeMelTensor()
    {
        var definition = KoumoriV2TestPackage.CreateEncoderDefinition();
        var frontend = new KoumoriEncoderFrontend(definition);
        var pcm = new float[480000];
        pcm[1] = 1;

        var result = frontend.ExtractInput(pcm, default);

        var amplitude = definition.HannWindow[199] + definition.HannWindow[201];
        var maximum = Math.Log10(amplitude * amplitude);
        foreach (var band in new[] { 0, 1, 17, 79 })
        {
            var power = amplitude * amplitude * Math.Pow(Math.Cos(2 * Math.PI * band / 400), 2);
            Assert.Equal((Math.Max(Math.Log10(power), maximum - 8) + 4) / 4, result[band * 3000], 5);
            Assert.Equal((maximum - 8 + 4) / 4, result[band * 3000 + 1000], 5);
        }
    }

    [Fact]
    public void InvalidPcmAndCancellationFailBeforeProducingFeatures()
    {
        var frontend = new KoumoriEncoderFrontend(KoumoriV2TestPackage.CreateEncoderDefinition());
        var pcm = new float[480000];
        pcm[200] = float.NaN;
        Assert.Throws<InvalidDataException>(() => frontend.ExtractInput(pcm, default));
        Assert.Throws<ArgumentException>(() => frontend.ExtractInput(new float[16000], default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => frontend.ExtractInput(new float[480000], cancellation.Token));
    }
}
