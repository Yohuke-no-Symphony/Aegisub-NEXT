using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

internal sealed class UnmarkedExportFixture : IDisposable
{
    internal const int WIDTH = 1280;
    internal const int HEIGHT = 720;
    internal const int FRAME_COUNT = 8;
    private static readonly string[] sampleColors = ["red", "green", "blue", "yellow", "cyan", "magenta"];
    private readonly string directory;

    private UnmarkedExportFixture(string directory, string mediaPath, string referencePath)
    {
        this.directory = directory;
        MediaPath = mediaPath;
        ReferencePath = referencePath;
    }

    internal string DirectoryPath => directory;
    internal string MediaPath { get; }
    internal string ReferencePath { get; }

    internal static async Task<UnmarkedExportFixture> CreateAsync()
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-unmarked-export-").FullName;
        try
        {
            var source = Path.Combine(directory, "未知色彩 H264 AAC.mp4");
            var reference = Path.Combine(directory, "explicit bt709.mp4");
            var video = "testsrc2=size=1280x720:rate=60000/1001," + string.Join(',',
                sampleColors.Select((color, index) =>
                    FormattableString.Invariant($"drawbox=x={64 + index * 208}:y=64:w=96:h=96:color={color}:t=fill")));
            await RunAsync(["-f", "lavfi", "-i", video,
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

    internal static async Task RunAsync(string[] arguments)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg));
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
