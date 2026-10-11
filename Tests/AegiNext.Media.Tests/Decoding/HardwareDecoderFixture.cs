using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Decoding;

internal sealed class HardwareDecoderFixture : IDisposable
{
    private readonly string directory;

    private HardwareDecoderFixture(string directory, string mediaPath)
    {
        this.directory = directory;
        MediaPath = mediaPath;
    }

    internal string MediaPath { get; }

    internal static async Task<HardwareDecoderFixture> CreateAsync(bool hevcTenBit, bool tagged = true, bool cropped = false)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg));
        var directory = Directory.CreateTempSubdirectory("aeginext-hardware-decode-").FullName;
        try
        {
            var path = Path.Combine(directory, "media.mkv");
            var arguments = new List<string>
            {
                "-v", "error", "-nostdin", "-f", "lavfi", "-i", cropped ? "testsrc2=size=1920x1080:rate=60000/1001" : "testsrc2=size=1280x720:rate=60000/1001",
                "-frames:v", "6", "-c:v", hevcTenBit ? "libx265" : "libx264", "-threads", "1",
                "-pix_fmt", hevcTenBit ? "yuv420p10le" : "yuv420p", "-preset", "ultrafast"
            };
            if (hevcTenBit)
            {
                arguments.AddRange(["-x265-params", tagged ? "pools=none:frame-threads=1:log-level=error:hdr10=1:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400" : "pools=none:frame-threads=1:log-level=error"]);
            }
            if (tagged)
            {
                arguments.AddRange(["-color_range", "tv", "-colorspace", hevcTenBit ? "bt2020nc" : "bt709",
                    "-color_primaries", hevcTenBit ? "bt2020" : "bt709", "-color_trc", hevcTenBit ? "smpte2084" : "bt709",
                    "-chroma_sample_location", "left"]);
            }
            arguments.AddRange(["-y", path]);
            var result = await MediaToolProcess.RunAsync(ffmpeg!, arguments, TimeSpan.FromSeconds(60),
                1024 * 1024, 1024 * 1024, CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return new(directory, path);
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        Directory.Delete(directory, recursive: true);
    }
}
