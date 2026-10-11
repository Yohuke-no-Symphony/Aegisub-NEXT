using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Decoding;

internal sealed class FilmGrainDecoderFixture : IDisposable
{
    private static readonly byte[] filmGrainSei = [0, 0, 0, 1, 6, 19, 6, 0, 0xA0, 0, 0, 0xFF, 0x50, 0x80];
    private readonly string directory;

    private FilmGrainDecoderFixture(string directory, string path)
    {
        this.directory = directory;
        MediaPath = path;
    }

    internal string MediaPath { get; }

    internal static async Task<FilmGrainDecoderFixture> CreateAsync()
    {
        using var original = await HardwareDecoderFixture.CreateAsync(hevcTenBit: false, tagged: false);
        var directory = Directory.CreateTempSubdirectory("aeginext-film-grain-").FullName;
        try
        {
            var path = Path.Combine(directory, "grain.h264");
            var result = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!,
                ["-v", "error", "-nostdin", "-i", original.MediaPath, "-c:v", "copy", "-an", "-bsf:v", "h264_mp4toannexb", "-f", "h264", "-y", path],
                TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
            Assert.Equal(0, result.ExitCode);
            var source = await File.ReadAllBytesAsync(path);
            var insertion = FirstIdrPosition(source);
            Assert.True(insertion >= 0, "H.264 fixture must contain an IDR frame.");
            var output = new byte[source.Length + filmGrainSei.Length];
            source.AsSpan(0, insertion).CopyTo(output);
            filmGrainSei.CopyTo(output, insertion);
            source.AsSpan(insertion).CopyTo(output.AsSpan(insertion + filmGrainSei.Length));
            await File.WriteAllBytesAsync(path, output);
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

    private static int FirstIdrPosition(byte[] source)
    {
        for (var index = 0; index < source.Length - 4; index++)
        {
            if (source[index] != 0 || source[index + 1] != 0)
            {
                continue;
            }
            var prefix = source[index + 2] == 1 ? 3 : source[index + 2] == 0 && source[index + 3] == 1 ? 4 : 0;
            if (prefix != 0 && (source[index + prefix] & 31) == 5)
            {
                return index;
            }
        }
        return -1;
    }
}
