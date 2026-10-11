using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

internal sealed class RateControlExportFixture : IDisposable
{
    internal const int WIDTH = 384;
    internal const int HEIGHT = 216;
    internal const int FRAME_RATE = 30;
    internal const int FRAME_COUNT = 120;

    private RateControlExportFixture(string directory, string mediaPath)
    {
        DirectoryPath = directory;
        MediaPath = mediaPath;
    }

    internal string DirectoryPath { get; }
    internal string MediaPath { get; }

    internal static async Task<RateControlExportFixture> CreateAsync()
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-rate-control-").FullName;
        try
        {
            var path = Path.Combine(directory, "rate-control-source.mkv");
            var result = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=384x216:rate=30",
                    "-frames:v", "120", "-an", "-c:v", "ffv1", "-level", "3", "-threads", "1", "-pix_fmt", "yuv420p",
                    "-vf", "setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709",
                    "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv",
                    "-chroma_sample_location", "left", "-y", path],
                TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return new(directory, path);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(DirectoryPath, true);
    }
}
