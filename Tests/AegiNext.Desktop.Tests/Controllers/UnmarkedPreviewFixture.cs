using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class UnmarkedPreviewFixture : IDisposable
{
    private readonly string directory;

    private UnmarkedPreviewFixture(string directory, string mediaPath, string referencePath)
    {
        this.directory = directory;
        MediaPath = mediaPath;
        ReferencePath = referencePath;
    }

    internal string MediaPath { get; }
    internal string ReferencePath { get; }

    internal static async Task<UnmarkedPreviewFixture> CreateAsync()
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-unmarked-preview-").FullName;
        try
        {
            var source = Path.Combine(directory, "H264 High 720p AAC 未标记.mp4");
            var reference = Path.Combine(directory, "explicit BT709.mp4");
            await RunAsync(["-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=60000/1001",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=0.15",
                "-map", "0:v:0", "-map", "1:a:0", "-frames:v", "8", "-c:v", "libx264",
                "-profile:v", "high", "-preset", "medium", "-threads", "1", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-ac", "2", "-y", source]);
            await RunAsync(["-i", source, "-map", "0", "-c", "copy", "-bsf:v",
                "h264_metadata=video_full_range_flag=0:colour_primaries=1:transfer_characteristics=1:matrix_coefficients=1",
                "-y", reference]);
            return new(directory, source, reference);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }

    private static async Task RunAsync(string[] arguments)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg));
        Assert.True(Path.IsPathFullyQualified(ffmpeg));
        Assert.True(File.Exists(ffmpeg));
        var result = await MediaToolProcess.RunAsync(ffmpeg, ["-v", "error", "-nostdin", .. arguments],
            TimeSpan.FromSeconds(60), 1024 * 1024, 1024 * 1024, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(directory, true);
    }
}
