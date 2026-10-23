using AegiNext.Core.Timing;
using AegiNext.Desktop.Koumori;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests;

public sealed class KoumoriMediaSourceTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public async Task OriginGapsAndKnownMediaTailPreserveTheSampleGrid(int originSeconds)
    {
        var origin = new MediaTime(originSeconds);
        var format = new AudioSampleFormat(16000, 1);
        var decoder = new KoumoriTestPcmDecoder(
            new(format, origin + new MediaTime(2, 16000), [2, 3, 4, 5, 6, 7]),
            new(format, origin + new MediaTime(10, 16000), [10, 11, 12, 13]));
        using var source = new KoumoriMediaSource(decoder, 40, origin, CachePath());
        var samples = new float[12];
        var count = await source.ReadAsync(4, samples, default);

        Assert.Equal(0, decoder.SeekCount);
        Assert.Equal(12, count);
        Assert.Equal(new float[] { 4, 5, 6, 7, 0, 0, 10, 11, 12, 13, 0, 0 }, samples);
    }

    [Fact]
    public async Task KnownMediaDurationDefinesSilenceBeforeAndAfterAudio()
    {
        var decoder = new KoumoriTestPcmDecoder(new AudioSampleBlock(new(16000, 1), new(20, 16000), [1, 2]));
        using var source = new KoumoriMediaSource(decoder, 40, MediaTime.Zero, CachePath());
        var silence = new float[8];
        Assert.Equal(8, await source.ReadAsync(0, silence, default));
        Assert.All(silence, value => Assert.Equal(0, value));
        Assert.Equal(8, await source.ReadAsync(30, silence, default));
        Assert.All(silence, value => Assert.Equal(0, value));
        Assert.Equal(2, await source.ReadAsync(38, silence, default));
        Assert.Equal(0, await source.ReadAsync(40, silence, default));
    }

    [Fact]
    public async Task CancellationPropagatesAndSourceOwnsDecoderLifetime()
    {
        var decoder = new KoumoriTestPcmDecoder();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new KoumoriMediaSource(decoder, 100, MediaTime.Zero, CachePath());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.ReadAsync(0, new float[10], cancellation.Token));
        source.Dispose();
        Assert.True(decoder.IsDisposed);
    }

    [Fact]
    public async Task BackwardRequestsReuseTheSameDecodedPrefixWithoutSeeking()
    {
        var decoder = new KoumoriTestPcmDecoder(
            new(new(16000, 1), new(-2, 16000), [9, 9, 0, 1, 2, 3]),
            new(new(16000, 1), new(4, 16000), [4, 5, 6, 7]));
        var path = CachePath();
        await using (var source = new KoumoriMediaSource(decoder, 10, MediaTime.Zero, path))
        {
            var tail = new float[4];
            Assert.Equal(4, await source.ReadAsync(6, tail, default));
            Assert.Equal(new float[] { 6, 7, 0, 0 }, tail);
            var reads = decoder.ReadCount;
            var first = new float[6];
            Assert.Equal(6, await source.ReadAsync(0, first, default));
            Assert.Equal(new float[] { 0, 1, 2, 3, 4, 5 }, first);
            Assert.Equal(reads, decoder.ReadCount);
            Assert.Equal(0, decoder.SeekCount);
            Assert.Equal(10, source.CachedSampleCount);
        }
        Assert.False(File.Exists(path));
        Assert.True(decoder.IsDisposed);
    }

    [Fact]
    public async Task RequestCancellationRetainsTheCompletedBlockAndDoesNotCancelTheDecoder()
    {
        using var cancellation = new CancellationTokenSource();
        var decoder = new KoumoriTestPcmDecoder(
            new(new(16000, 1), MediaTime.Zero, [0, 1, 2, 3]),
            new(new(16000, 1), new(4, 16000), [4, 5, 6, 7]))
        {
            OnRead = count =>
            {
                if (count == 1)
                {
                    cancellation.Cancel();
                }
            }
        };
        await using var source = new KoumoriMediaSource(decoder, 8, MediaTime.Zero, CachePath());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.ReadAsync(0, new float[8], cancellation.Token));
        Assert.Equal(4, source.CachedSampleCount);
        Assert.False(source.IsCanceled);
        var samples = new float[8];
        Assert.Equal(8, await source.ReadAsync(0, samples, default));
        Assert.Equal(Enumerable.Range(0, 8).Select(value => (float)value), samples);
        Assert.Equal(2, decoder.ReadCount);
        source.Cancel();
        Assert.True(source.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await source.ReadAsync(0, samples, default));
    }

    private static string CachePath() => Path.Combine(Path.GetTempPath(), "aeginext-koumori-tests", Guid.NewGuid().ToString("N") + ".f32");
}
