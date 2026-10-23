using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Media.Audio;
using AegiNext.Media.Processes;

namespace AegiNext.Media.Tests.Audio;

public sealed class AudioTimestampContractTests
{
    [AudioFact]
    public async Task PartialEncoderPrimingKeepsTheTrimmedFirstSampleAtMediaZero()
    {
        await VerifySequentialContractAsync("priming", false, true);
    }

    [AudioFact]
    public async Task SmallTimestampJitterDoesNotRepeatedlyResetTheResamplingPhase()
    {
        await VerifySequentialContractAsync("jitter", false, false);
    }

    [AudioFact]
    public async Task CorruptPacketsPreserveResamplerStateAcrossTheMissingAudio()
    {
        await VerifySequentialContractAsync("corrupt", true, false);
    }

    [AudioFact]
    public async Task ADelayedNonzeroFirstSampleIncludesLeadingSilenceInTheResamplingFilter()
    {
        await VerifySequentialContractAsync("offset", false, false);
    }

    [AudioFact]
    public async Task LeadingSilenceStartsAtTheNonzeroContainerOrigin()
    {
        await VerifySequentialContractAsync("offset_nonzero", false, false);
    }

    [AudioFact]
    public async Task FiftyMillisecondMediaGapIsPreservedByDefaultAndOnlyModelResamplingClosesIt()
    {
        using var fixture = await FlacLacingFixture.CreateAsync(shiftMilliseconds: 50);
        foreach (var policy in new[] { AudioTimestampPolicy.PRESERVE_DISCONTINUITIES, AudioTimestampPolicy.FFMPEG_ASYNC_ONE })
        {
            using var decoder = FfmpegAudioDecoder.Open(fixture.MediaPath, 0, new(48000, 1), timestampPolicy: policy);
            var previousEnd = 0L;
            var gaps = new List<long>();
            while (decoder.Read() is { } block)
            {
                var start = block.Start.ToTimestamp(new(1, 48000), MediaTimeRounding.TO_EVEN).Value;
                if (start > previousEnd)
                {
                    gaps.Add(start - previousEnd);
                }
                previousEnd = start + block.FrameCount;
            }
            if (policy == AudioTimestampPolicy.PRESERVE_DISCONTINUITIES)
            {
                Assert.InRange(Assert.Single(gaps), 2400 - 48, 2400 + 48);
            }
            else
            {
                Assert.Empty(gaps);
            }
        }
    }

    private static async Task VerifySequentialContractAsync(string variant, bool corrupt, bool trim)
    {
        var executable = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!;
        var probe = Path.Combine(Path.GetDirectoryName(executable)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        var directory = Path.Combine(Path.GetTempPath(), "aeginext-audio-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var hasOffset = variant.StartsWith("offset", StringComparison.Ordinal);
            var extension = hasOffset ? ".mkv" : ".m4a";
            var encoded = Path.Combine(directory, "encoded" + extension);
            var media = Path.Combine(directory, "source" + extension);
            var pcmPath = Path.Combine(directory, "reference.f32");
            var sampleRate = trim ? 48000 : 44100;
            var arguments = new List<string>
            {
                "-v", "error", "-nostdin", "-f", "lavfi", "-i",
                $"sine=frequency=1379:sample_rate={sampleRate}:duration=4"
            };
            if (hasOffset)
            {
                arguments = ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "color=c=black:s=16x16:r=1:d=4",
                    "-f", "lavfi", "-i", "aevalsrc=0.3:s=44100:d=3", "-filter_complex",
                    variant == "offset_nonzero" ? "[0:v]setpts=PTS+2/TB[v];[1:a]asetpts=PTS+2.5/TB[a]" : "[0:v]null[v];[1:a]asetpts=PTS+0.5/TB[a]",
                    "-map", "[v]", "-map", "[a]", "-c:v", "ffv1"];
            }
            else if (variant == "jitter")
            {
                arguments.AddRange(["-af", "asetpts=PTS+floor(N/2048)/44100/TB"]);
            }
            arguments.AddRange(["-c:a", hasOffset ? "pcm_f32le" : "aac", "-b:a", "128k", "-y", encoded]);
            await RunAsync(executable, arguments);
            if (trim)
            {
                await RunAsync(executable, ["-v", "error", "-nostdin", "-ss", "0.001333333333", "-i", encoded, "-c:a", "copy", "-y", media]);
            }
            else
            {
                File.Copy(encoded, media);
            }
            var packetText = await RunAsync(probe, ["-v", "error", "-select_streams", "a:0", "-show_packets", "-show_format", "-of", "json", media]);
            using var packetDocument = JsonDocument.Parse(packetText);
            var packets = packetDocument.RootElement.GetProperty("packets").EnumerateArray().ToArray();
            var originSeconds = double.Parse(packetDocument.RootElement.GetProperty("format").GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);
            if (variant == "offset_nonzero")
            {
                Assert.Equal(2, originSeconds, 6);
            }
            var originSamples = (long)Math.Round(originSeconds * 16000);
            if (trim)
            {
                var skip = packets[0].GetProperty("side_data_list")[0].GetProperty("skip_samples").GetInt32();
                Assert.NotEqual(0, skip % 1024);
            }
            if (corrupt)
            {
                await using var stream = new FileStream(media, FileMode.Open, FileAccess.Write, FileShare.None);
                foreach (var packet in packets.Skip(80).Take(9))
                {
                    stream.Position = long.Parse(packet.GetProperty("pos").GetString()!, CultureInfo.InvariantCulture);
                    var bytes = new byte[int.Parse(packet.GetProperty("size").GetString()!, CultureInfo.InvariantCulture)];
                    await stream.WriteAsync(bytes);
                }
            }
            await RunAsync(executable, ["-v", "error", "-nostdin", "-copyts", "-start_at_zero", "-i", media,
                "-map", "0:a:0", "-af", "aresample=16000:async=1:first_pts=0", "-ac", "1", "-c:a", "pcm_f32le", "-f", "f32le", "-y", pcmPath]);
            var expected = MemoryMarshal.Cast<byte, float>(await File.ReadAllBytesAsync(pcmPath)).ToArray();
            var actual = new float[expected.Length];
            using var decoder = FfmpegAudioDecoder.Open(media, hasOffset ? 1 : 0, new(16000, 1), timestampPolicy: AudioTimestampPolicy.FFMPEG_ASYNC_ONE);
            AudioSampleBlock? block;
            while ((block = decoder.Read()) is not null)
            {
                var start = block.Start.ToTimestamp(new(1, 16000), MediaTimeRounding.TO_EVEN).Value - originSamples;
                var first = Math.Max(0, start);
                var last = Math.Min(expected.Length, start + block.FrameCount);
                if (last > first)
                {
                    block.Samples.Span.Slice(checked((int)(first - start)), checked((int)(last - first))).CopyTo(actual.AsSpan(checked((int)first)));
                }
            }
            var maximum = expected.Zip(actual, (left, right) => Math.Abs(left - right)).Max();
            Assert.True(maximum <= 0.000001f, $"{variant}: maximum sequential PCM difference was {maximum:R}.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        var result = await MediaToolProcess.RunAsync(executable, arguments, TimeSpan.FromSeconds(30), 1024 * 1024, 1024 * 1024, default);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return result.StandardOutput;
    }
}
