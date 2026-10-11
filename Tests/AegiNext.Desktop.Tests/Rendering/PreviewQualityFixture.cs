using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Tests.Rendering;

internal sealed class PreviewQualityFixture : IDisposable
{
    private readonly string directory;

    private PreviewQualityFixture(string directory, string mediaPath)
    {
        this.directory = directory;
        MediaPath = mediaPath;
    }

    internal string MediaPath { get; }
    internal string DirectoryPath => directory;

    internal static async Task<PreviewQualityFixture> CreateAsync(int width, int height)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg));
        Assert.True(File.Exists(ffmpeg));
        var directory = Directory.CreateTempSubdirectory("aeginext-preview-quality-").FullName;
        try
        {
            var mediaPath = Path.Combine(directory, "quality fixture 中文.mkv");
            var result = await MediaToolProcess.RunAsync(ffmpeg,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=25",
                    "-frames:v", "1", "-c:v", "ffv1", "-level", "3", "-threads", "1", "-pix_fmt", "yuv420p",
                    "-vf", "setsar=1/1,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709",
                    "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709",
                    "-color_range", "tv", "-chroma_sample_location", "left", "-y", mediaPath], TimeSpan.FromSeconds(60), 1024 * 1024, 1024 * 1024,
                CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return new(directory, mediaPath);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(directory, true);
}
