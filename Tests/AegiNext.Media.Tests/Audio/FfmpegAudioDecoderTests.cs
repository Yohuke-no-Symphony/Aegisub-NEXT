using AegiNext.Media.Processes;
using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Audio;

public sealed class FfmpegAudioDecoderTests
{
    [AudioFact]
    public void ResamplingAndSeekPreserveSamplesAndCancelPermanentlyTerminatesTheSource()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aeginext-audio-{Guid.NewGuid():N}.wav");
        try
        {
            WriteFixture(path);
            using var decoder = FfmpegAudioDecoder.Open(path, 0, new(16000, 1));
            var count = 0;
            double energy = 0;
            AudioSampleBlock? block;
            while ((block = decoder.Read()) is not null)
            {
                Assert.Equal(new MediaTime(count, 16000), block.Start);
                Assert.InRange(block.FrameCount, 1, 4096);
                count += block.FrameCount;
                foreach (var sample in block.Samples.Span)
                {
                    energy += sample * sample;
                }
            }

            Assert.Equal(16000, count);
            Assert.True(energy / count > 0.01);
            decoder.Seek(new(1, 4));
            Assert.Equal(new MediaTime(1, 4), decoder.Read()!.Start);
            decoder.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => decoder.Read());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AudioFact]
    public async Task CoarseContainerTimestampsPreserveContinuousPcmAndTheAudioOffset()
    {
        var executable = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.True(Path.IsPathFullyQualified(executable));
        var path = Path.Combine(Path.GetTempPath(), $"aeginext-audio-{Guid.NewGuid():N}.mkv");
        try
        {
            var encoded = await MediaToolProcess.RunAsync(executable,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "sine=frequency=1000:sample_rate=44100:duration=1",
                    "-af", "asetpts=PTS+0.5/TB", "-c:a", "pcm_s16le", "-y", path],
                TimeSpan.FromSeconds(20), 1024 * 1024, 1024 * 1024, CancellationToken.None);
            Assert.True(encoded.ExitCode == 0, encoded.StandardError);
            using var decoder = FfmpegAudioDecoder.Open(path, 0, new(16000, 1));
            decoder.Seek(MediaTime.Zero);
            var count = 0;
            AudioSampleBlock? block;
            while ((block = decoder.Read()) is not null)
            {
                Assert.Equal(new MediaTime(8000 + count, 16000), block.Start);
                count += block.FrameCount;
            }

            Assert.Equal(16000, count);
            decoder.Seek(new(3, 4));
            Assert.Equal(new MediaTime(3, 4), decoder.Read()!.Start);
            decoder.Seek(new(3));
            Assert.Null(decoder.Read());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteFixture(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + 88200);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(44100);
        writer.Write(88200);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(88200);
        for (var index = 0; index < 44100; index++)
        {
            writer.Write((short)(12000 * Math.Sin(index * 2 * Math.PI * 440 / 44100)));
        }
    }
}
