using AegiNext.Media.Processes;
using System.Globalization;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Audio;

internal sealed class AacFullRangeFixture : IDisposable
{
    private const int SAMPLE_RATE = 44100;
    private readonly string directory;

    private AacFullRangeFixture(string directory, int seconds, bool shortPacketDurations, int sampleRate)
    {
        this.directory = directory;
        MediaPath = Path.Combine(directory, "full-range.mp4");
        Duration = new MediaTime(seconds) - (shortPacketDurations ? new MediaTime(24, sampleRate) : MediaTime.Zero);
    }

    internal string MediaPath { get; }
    internal MediaTime Duration { get; }

    internal static async Task<AacFullRangeFixture> CreateAsync(int seconds = 300, bool shortPacketDurations = false,
        int sampleRate = SAMPLE_RATE)
    {
        var executable = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(executable));
        Assert.True(Path.IsPathFullyQualified(executable));
        var directory = Path.Combine(Path.GetTempPath(), $"aeginext-aac-full-range-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var fixture = new AacFullRangeFixture(directory, seconds, shortPacketDurations, sampleRate);
        try
        {
            var encodedPath = shortPacketDurations ? Path.Combine(directory, "source.mp4") : fixture.MediaPath;
            var encoded = await MediaToolProcess.RunAsync(executable,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i",
                    $"sine=frequency=1000:sample_rate={sampleRate}:duration={seconds.ToString(CultureInfo.InvariantCulture)}",
                    "-c:a", "aac", "-b:a", "96k", "-y", encodedPath],
                TimeSpan.FromSeconds(30), 1024 * 1024, 1024 * 1024, CancellationToken.None);
            Assert.True(encoded.ExitCode == 0, encoded.StandardError);
            if (shortPacketDurations)
            {
                var remuxed = await MediaToolProcess.RunAsync(executable,
                    ["-v", "error", "-nostdin", "-i", encodedPath, "-c:a", "copy", "-bsf:a",
                        "setts=pts='PTS-8*gte(N,3)-16*gte(N,4)':dts='DTS-8*gte(N,3)-16*gte(N,4)'", "-y", fixture.MediaPath],
                    TimeSpan.FromSeconds(30), 1024 * 1024, 1024 * 1024, CancellationToken.None);
                Assert.True(remuxed.ExitCode == 0, remuxed.StandardError);
            }
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(directory, true);
    }
}
