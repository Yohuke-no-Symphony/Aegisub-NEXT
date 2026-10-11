using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

internal sealed class HdrExportRampFixture : IDisposable
{
    private HdrExportRampFixture(string directory, string mediaPath)
    {
        Directory = directory;
        MediaPath = mediaPath;
    }

    internal string Directory { get; }
    internal string MediaPath { get; }

    internal static async Task<HdrExportRampFixture> CreateAsync(string transfer)
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("aeginext-hdr-ramp-").FullName;
        try
        {
            var data = new byte[64 * 48 * 3 * 3];
            for (var frame = 0; frame < 3; frame++)
            {
                var origin = frame * 64 * 48 * 3;
                for (var y = 0; y < 48; y++)
                {
                    for (var x = 0; x < 64; x++)
                    {
                        var value = transfer == "smpte2084" ? x < 32 ? 723 : 855 : x < 32 ? 940 : 502;
                        BitConverter.TryWriteBytes(data.AsSpan(origin + (y * 64 + x) * 2, 2), (ushort)value);
                    }
                }

                for (var offset = 64 * 48 * 2; offset < 64 * 48 * 3; offset += 2)
                {
                    BitConverter.TryWriteBytes(data.AsSpan(origin + offset, 2), (ushort)512);
                }
            }

            var raw = Path.Combine(directory, "ramp.yuv");
            var media = Path.Combine(directory, "ramp.mkv");
            await File.WriteAllBytesAsync(raw, data);
            var result = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!,
                ["-v", "error", "-f", "rawvideo", "-pixel_format", "yuv420p10le", "-video_size", "64x48", "-framerate", "24", "-i", raw,
                    "-vf", $"setparams=range=limited:color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc",
                    "-c:v", "libx265", "-preset", "ultrafast", "-x265-params", "pools=none:frame-threads=1:lossless=1:repeat-headers=1",
                    "-color_range", "tv", "-color_primaries", "bt2020", "-color_trc", transfer, "-colorspace", "bt2020nc", "-chroma_sample_location", "left", "-y", media],
                TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return new(directory, media);
        }
        catch
        {
            System.IO.Directory.Delete(directory, true);
            throw;
        }
    }

    public void Dispose()
    {
        System.IO.Directory.Delete(Directory, true);
    }
}
