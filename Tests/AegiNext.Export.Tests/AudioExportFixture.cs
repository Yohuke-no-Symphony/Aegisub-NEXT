using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;
using AegiNext.Media.Processes;

namespace AegiNext.Export.Tests;

internal sealed class AudioExportFixture : IDisposable
{
    private readonly ExportTestDirectory directory = new();

    internal string DirectoryPath => directory.Path;
    internal string SourcePath { get; private set; } = string.Empty;
    internal MediaProbeReport Source { get; private set; } = null!;
    internal string Ffmpeg { get; } = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!;
    internal string Ffprobe { get; } = Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!;

    internal static async Task<AudioExportFixture> CreateAsync(bool video, bool multichannel = false)
    {
        var fixture = new AudioExportFixture();
        try
        {
            fixture.SourcePath = Path.Combine(fixture.DirectoryPath, video ? "source.mkv" : "source.mka");
            var arguments = new List<string> { "-nostdin", "-v", "error", "-copyts" };
            if (video)
            {
                arguments.AddRange(["-f", "lavfi", "-i", "testsrc2=size=64x48:rate=25:duration=0.25"]);
            }

            arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.65",
                "-f", "lavfi", "-i", multichannel
                    ? "aevalsrc=sin(2*PI*880*t)|sin(2*PI*440*t)|0|0|0|0:s=48000:d=1.25:c=5.1"
                    : "sine=frequency=880:sample_rate=48000:duration=1.25"]);
            if (video)
            {
                arguments.AddRange(["-map", "0:0", "-map", "1:0", "-map", "2:0", "-c:v", "libx264",
                    "-preset", "ultrafast", "-vf", "setpts=PTS+3/TB"]);
            }
            else
            {
                arguments.AddRange(["-map", "0:0", "-map", "1:0"]);
            }

            arguments.AddRange(["-af", "asetpts=PTS+3/TB", "-c:a", "pcm_s24le", "-n", fixture.SourcePath]);
            var output = await MediaToolProcess.RunAsync(fixture.Ffmpeg, arguments,
                TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
            Assert.True(output.ExitCode == 0, output.StandardError);
            fixture.Source = await new FfprobeMediaProbe(new(fixture.Ffprobe)).ProbeAsync(fixture.SourcePath);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    internal AudioExportRequest Request(AudioFileFormat format, bool selectLast = true)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: SourcePath);
        var project = new ProjectDocument
        {
            Assets = [asset],
            Media = new(asset.Id, Source.Asset.Streams.FirstOrDefault(stream => stream.CodecType == "video")?.Index,
                selectLast ? Source.Asset.Streams.Last(stream => stream.CodecType == "audio").Index : null, new MediaTime(3))
        };
        return new(project, DirectoryPath, Path.Combine(DirectoryPath, "output" + AudioExportCapabilities.GetExtension(format)))
        {
            Format = format,
            FfmpegPath = Ffmpeg,
            WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH")
        };
    }

    internal async Task<string> DecodedHashAsync(string path, int streamIndex)
    {
        var output = await MediaToolProcess.RunAsync(Ffmpeg,
            ["-nostdin", "-v", "error", "-i", path, "-map", $"0:{streamIndex.ToString(CultureInfo.InvariantCulture)}",
                "-c:a", "pcm_s32le", "-f", "hash", "-hash", "sha256", "pipe:1"],
            TimeSpan.FromSeconds(30), 4096, 65536, CancellationToken.None);
        Assert.True(output.ExitCode == 0, output.StandardError);
        return output.StandardOutput.Trim();
    }

    /// <inheritdoc />
    public void Dispose() => directory.Dispose();
}
