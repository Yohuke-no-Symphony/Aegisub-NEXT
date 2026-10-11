using AegiNext.Media.Processes;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;
using Xunit.Abstractions;

namespace AegiNext.Media.Tests.Probing;

public sealed class FfprobeVideoTimingIntegrationTests(ITestOutputHelper output)
{
    [MediaToolsTheory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 5)]
    [Trait("Category", "MediaIntegration")]
    public async Task ScansRealCfrAndVfrDisplayPtsIncludingBFramesAndNonzeroOrigin(bool variable, int originSeconds)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!;
        var ffprobe = Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!;
        Assert.True(Path.IsPathFullyQualified(ffmpeg));
        Assert.True(Path.IsPathFullyQualified(ffprobe));
        var directory = Directory.CreateTempSubdirectory("aeginext-video-timing-").FullName;
        try
        {
            var source = Path.Combine(directory, "帧时间 空格' $() [VFR].mkv");
            var filter = variable ? "select='eq(n,0)+eq(n,1)+eq(n,3)+eq(n,6)+eq(n,9)'" : "null";
            filter += FormattableString.Invariant($",setpts=PTS+{originSeconds}/TB");
            var encoded = await MediaToolProcess.RunAsync(ffmpeg,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=10:duration=1",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=8000:duration=1",
                    "-map", "1:a", "-map", "0:v", "-vf", filter, "-fps_mode", "vfr",
                    "-c:a", "pcm_s16le", "-c:v", "libx264", "-preset", "veryfast", "-threads", "1",
                    "-g", "3", "-keyint_min", "3", "-sc_threshold", "0", "-bf", "2", "-x264-params", "b-adapt=0", "-y", source],
                TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
            Assert.True(encoded.ExitCode == 0, encoded.StandardError);

            var probe = new FfprobeVideoTimingProbe(new(ffprobe, TimeSpan.FromMinutes(1)));
            var index = await probe.ProbeAsync(source, 1, new(originSeconds));
            int[] expectedTicks = variable ? [0, 1, 3, 6, 9] : Enumerable.Range(0, 10).ToArray();

            Assert.Equal(expectedTicks.Select(tick => new MediaTime(tick, 10)), index.FrameTimes);
            Assert.Equal<int>(variable ? [0, 3, 4] : [0, 3, 6, 9], index.Keyframes);
            Assert.Equal(0, index.FrameAtTime(new(1, 10), end: true));
            output.WriteLine($"{(variable ? "VFR" : "CFR")}; origin={originSeconds}; frames={index.FrameTimes.Length}; PTS={string.Join(", ", index.FrameTimes)}; keys={string.Join(", ", index.Keyframes)}");

            var corrupt = Path.Combine(directory, "损坏.bin");
            await File.WriteAllTextAsync(corrupt, "invalid media");
            await Assert.ThrowsAsync<InvalidDataException>(() => probe.ProbeAsync(corrupt, 1, default));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
