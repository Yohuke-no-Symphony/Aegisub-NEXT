using AegiNext.Media.Processes;
using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;
using AegiNext.Media.Probing;
using AegiNext.Media.Tests.Audio;
using AegiNext.Media.Tests.Decoding;

namespace AegiNext.Media.Tests.Analysis;

[Collection(nameof(NativeDecoderTestGroup))]
public sealed class WaveformFileAnalysisTests
{
    [AudioFact]
    public async Task LocalFileSeekPreservesSignedImpulsesAndProjectOriginsAtExactRangeBoundaries()
    {
        var path = FixturePath("wav");
        try
        {
            var samples = new short[WaveformAnalyzer.SAMPLE_RATE * 3];
            samples[95999] = -32760;
            samples[96063] = 24576;
            samples[96128] = -16384;
            samples[97023] = 8192;
            samples[97024] = 32760;
            WriteWave(path, WaveformAnalyzer.SAMPLE_RATE, samples);
            foreach (var origin in new[] { MediaTime.Zero, new MediaTime(1), new MediaTime(-1) })
            {
                var request = new WaveformAnalysisRequest(new MediaTime(2) - origin, 128, 8);

                var data = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, origin, request);

                var expected = new float[16];
                expected[1] = 0.75F;
                expected[2] = -0.5F;
                expected[15] = 0.25F;
                Assert.Equal(request.Start, data.Start);
                Assert.Equal(request.End, data.End);
                Assert.Equal(expected, data.Peaks.ToArray());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AudioFact]
    public async Task FileResolutionChangesRecoverDetailsWithoutLosingCoarseExtrema()
    {
        var path = FixturePath("wav");
        try
        {
            var samples = new short[WaveformAnalyzer.SAMPLE_RATE * 2];
            samples[65599] = 24576;
            samples[65728] = -16384;
            samples[73727] = 8192;
            WriteWave(path, WaveformAnalyzer.SAMPLE_RATE, samples);
            var start = new MediaTime(65536, WaveformAnalyzer.SAMPLE_RATE);

            var coarse = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, new(start, 4096, 2));
            var fine = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, new(start, 128, 64));

            Assert.Equal(coarse.Start, fine.Start);
            Assert.Equal(coarse.End, fine.End);
            Assert.Equal(new[] { -0.5F, 0.75F, 0F, 0.25F }, coarse.Peaks.ToArray());
            Assert.Equal(0.75F, fine.Peaks.Span[1]);
            Assert.Equal(-0.5F, fine.Peaks.Span[2]);
            Assert.Equal(0.25F, fine.Peaks.Span[127]);
            Assert.Equal(coarse.Peaks.Span[0], fine.Peaks.ToArray().Min());
            Assert.Equal(coarse.Peaks.Span[1], fine.Peaks.ToArray().Max());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AudioFact]
    public async Task LowerRateWaveIsResampledToFixedPcmWithoutChangingStableSignalAmplitudes()
    {
        var path = FixturePath("wav");
        try
        {
            var samples = new short[16000 * 3];
            Array.Fill(samples, (short)16384, 16000, 8000);
            Array.Fill(samples, (short)-24576, 24000, 8000);
            WriteWave(path, 16000, samples);
            var request = new WaveformAnalysisRequest(new(5, 4), 2048, 16);

            var data = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, request);

            Assert.Equal(WaveformAnalyzer.SAMPLE_RATE, data.PcmSampleRate);
            Assert.Equal(request.Start, data.Start);
            Assert.InRange(data.Peaks.Span[1], 0.4999F, 0.5001F);
            Assert.InRange(data.Peaks.Span[20], -0.7501F, -0.7499F);
            Assert.Equal(0, data.Peaks.Span[0]);
            Assert.Equal(0, data.Peaks.Span[21]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AudioFact]
    public async Task EarlyAndAlreadyReachedEofKeepTheRemainingRequestedRangeSilent()
    {
        var path = FixturePath("wav");
        try
        {
            var samples = new short[512];
            samples[256] = 24576;
            samples[511] = -16384;
            WriteWave(path, WaveformAnalyzer.SAMPLE_RATE, samples);
            var request = new WaveformAnalysisRequest(new(256, WaveformAnalyzer.SAMPLE_RATE), 128, 8);

            var shortData = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, request);
            var pastEnd = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, new(new(1), 128, 4));

            var expected = new float[16];
            expected[1] = 0.75F;
            expected[2] = -0.5F;
            Assert.Equal(expected, shortData.Peaks.ToArray());
            Assert.All(pastEnd.Peaks.ToArray(), peak => Assert.Equal(0, peak));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AudioFact]
    public async Task IndependentAnalysisDoesNotSeekOrConsumeTheExistingPlaybackSource()
    {
        var path = FixturePath("wav");
        try
        {
            var samples = new short[WaveformAnalyzer.SAMPLE_RATE * 2];
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = (short)(index % 32767);
            }

            WriteWave(path, WaveformAnalyzer.SAMPLE_RATE, samples);
            using var playback = FfmpegAudioDecoder.Open(path, 0, new(WaveformAnalyzer.SAMPLE_RATE, 2));
            using var reference = FfmpegAudioDecoder.Open(path, 0, new(WaveformAnalyzer.SAMPLE_RATE, 2));
            AssertMatchingBlock(reference.Read(), playback.Read());

            await WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, new(new(1), 128, 8));

            AssertMatchingBlock(reference.Read(), playback.Read());
            AssertMatchingBlock(reference.Read(), playback.Read());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AudioFact]
    public async Task CanceledAndFailedRequestsReleaseResourcesAndAllowSubsequentAnalysis()
    {
        var path = FixturePath("wav");
        try
        {
            var samples = new short[4096];
            samples[64] = 24576;
            WriteWave(path, WaveformAnalyzer.SAMPLE_RATE, samples);
            var request = new WaveformAnalysisRequest(MediaTime.Zero, 128, 4);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, request, cancellation.Token));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                WaveformAnalyzer.AnalyzeFileAsync(path, 1, MediaTime.Zero, request));
            var first = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, request);
            var second = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, MediaTime.Zero, request);

            Assert.Equal(0.75F, first.Peaks.Span[1]);
            Assert.Equal(first.Peaks.ToArray(), second.Peaks.ToArray());
            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(44 + samples.Length * sizeof(short), exclusive.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AudioFact]
    public async Task NonzeroContainerPtsUsesAbsoluteSeekAndProjectRelativeWaveformRange()
    {
        var input = FixturePath("wav");
        var path = FixturePath("mkv");
        try
        {
            var executable = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
            Assert.False(string.IsNullOrWhiteSpace(executable));
            Assert.True(Path.IsPathFullyQualified(executable));
            var samples = new short[WaveformAnalyzer.SAMPLE_RATE * 2];
            samples[48064] = 24576;
            samples[48192] = -16384;
            WriteWave(input, WaveformAnalyzer.SAMPLE_RATE, samples);
            var encoded = await MediaToolProcess.RunAsync(executable,
                ["-v", "error", "-nostdin", "-i", input, "-af", "asetpts=PTS+0.5/TB", "-c:a", "pcm_s16le", "-y", path],
                TimeSpan.FromSeconds(20), 1024 * 1024, 1024 * 1024, CancellationToken.None);
            Assert.True(encoded.ExitCode == 0, encoded.StandardError);
            var request = new WaveformAnalysisRequest(new(1), 128, 4);

            var data = await WaveformAnalyzer.AnalyzeFileAsync(path, 0, new(1, 2), request);

            Assert.Equal(new MediaTime(1), data.Start);
            Assert.Equal(new[] { 0F, 0.75F, -0.5F, 0F, 0F, 0F, 0F, 0F }, data.Peaks.ToArray());
        }
        finally
        {
            File.Delete(input);
            File.Delete(path);
        }
    }

    private static string FixturePath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"aeginext-waveform-{Guid.NewGuid():N}.{extension}");
    }

    private static void WriteWave(string path, int sampleRate, short[] samples)
    {
        using var writer = new BinaryWriter(File.Create(path));
        var byteCount = samples.Length * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + byteCount);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(byteCount);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }

    private static void AssertMatchingBlock(AudioSampleBlock? expected, AudioSampleBlock? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.Format, actual.Format);
        Assert.Equal(expected.Samples.ToArray(), actual.Samples.ToArray());
    }
}
