using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Testing.Media;

internal sealed class SdrPreviewFixture : IDisposable
{
    internal const int WIDTH = 96;
    internal const int HEIGHT = 64;
    internal const int VIDEO_STREAM_INDEX = 1;

    private readonly string directory;

    private SdrPreviewFixture(string directory, string mediaPath)
    {
        this.directory = directory;
        MediaPath = mediaPath;
    }

    internal string MediaPath { get; }

    internal static async Task<SdrPreviewFixture> CreateAsync(bool anamorphic)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg), "已启用预览测试，必须显式提供 AEGINEXT_FFMPEG_PATH。");
        Assert.True(Path.IsPathFullyQualified(ffmpeg));
        Assert.True(File.Exists(ffmpeg));
        var directory = Directory.CreateTempSubdirectory("aeginext-sdr-preview-").FullName;
        try
        {
            var mediaPath = Path.Combine(directory, "BT709 空格' $() [预览].mkv");
            var aspect = anamorphic ? "2/1" : "1/1";
            var result = await MediaToolProcess.RunAsync(ffmpeg,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=96x64:rate=25",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.12",
                    "-map", "1:a:0", "-map", "0:v:0", "-frames:v", "3", "-c:a", "pcm_s16le",
                    "-c:v", "ffv1", "-level", "3", "-threads", "1", "-pix_fmt", "yuv420p",
                    "-vf", $"setsar={aspect},setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709",
                    "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709",
                    "-color_range", "tv", "-chroma_sample_location", "left", "-y", mediaPath],
                TimeSpan.FromSeconds(60), 1024 * 1024, 1024 * 1024, CancellationToken.None);
            Assert.True(result.ExitCode == 0, $"预览素材生成失败：{result.StandardError}");
            return new(directory, mediaPath);
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }
}
