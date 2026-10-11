namespace AegiNext.Export.Tests;

internal sealed class MidstreamExportFixture : IDisposable
{
    private MidstreamExportFixture(string directory, string mediaPath)
    {
        DirectoryPath = directory;
        MediaPath = mediaPath;
    }

    internal string DirectoryPath { get; }
    internal string MediaPath { get; }

    internal static async Task<MidstreamExportFixture> CreateAsync(bool changesColor)
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-midstream-export-").FullName;
        try
        {
            var raw = Path.Combine(directory, "unknown.ts");
            var tagged = Path.Combine(directory, "same.ts");
            var changed = Path.Combine(directory, "changed.ts");
            var output = Path.Combine(directory, "midstream.ts");
            await UnmarkedExportFixture.RunAsync(["-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30",
                "-frames:v", "3", "-c:v", "libx264", "-preset", "medium", "-threads", "1",
                "-pix_fmt", "yuv420p", "-f", "mpegts", "-y", raw]);
            await TagAsync(raw, tagged, 1, 1);
            if (changesColor)
            {
                await TagAsync(raw, changed, 9, 9);
            }
            var first = changesColor ? tagged : raw;
            var second = changesColor ? changed : tagged;
            var listing = Path.Combine(directory, "concat.txt");
            await File.WriteAllTextAsync(listing,
                $"file '{Escape(first)}'\nfile '{Escape(second)}'\n");
            await UnmarkedExportFixture.RunAsync(["-f", "concat", "-safe", "0", "-i", listing,
                "-c", "copy", "-f", "mpegts", "-y", output]);
            return new(directory, output);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }

    private static Task TagAsync(string input, string output, int primaries, int matrix)
    {
        return UnmarkedExportFixture.RunAsync(["-i", input, "-c", "copy", "-bsf:v",
            FormattableString.Invariant($"h264_metadata=colour_primaries={primaries}:transfer_characteristics=1:matrix_coefficients={matrix}:video_full_range_flag=0"),
            "-f", "mpegts", "-y", output]);
    }

    private static string Escape(string path)
    {
        return path.Replace('\\', '/').Replace("'", "'\\''", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(DirectoryPath, true);
    }
}
