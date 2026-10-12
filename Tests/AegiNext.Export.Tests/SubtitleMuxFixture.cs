using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Media.Probing;
using AegiNext.Media.Processes;

namespace AegiNext.Export.Tests;

internal sealed class SubtitleMuxFixture : IDisposable
{
    private readonly ExportTestDirectory directory = new();

    private SubtitleMuxFixture()
    {
    }

    internal string DirectoryPath => directory.Path;
    internal string SourcePath { get; private set; } = string.Empty;
    internal MediaProbeReport Source { get; private set; } = null!;
    internal string Ffmpeg { get; } = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!;
    internal string Ffprobe { get; } = Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!;

    internal static async Task<SubtitleMuxFixture> CreateAsync(string extension, bool chapters = false,
        bool originalSubtitle = true, bool nonzeroOrigin = true)
    {
        var fixture = new SubtitleMuxFixture();
        try
        {
            fixture.SourcePath = Path.Combine(fixture.DirectoryPath, "source" + extension);
            var oldSubtitle = Path.Combine(fixture.DirectoryPath, "original.srt");
            await File.WriteAllTextAsync(oldSubtitle, "1\n00:00:03,300 --> 00:00:03,600\n原字幕 Original\n\n");
            var metadata = Path.Combine(fixture.DirectoryPath, "source.ffmeta");
            await File.WriteAllTextAsync(metadata, ";FFMETADATA1\ntitle=Original title\n" +
                (chapters ? "[CHAPTER]\nTIMEBASE=1/1000\nSTART=3100\nEND=3800\ntitle=Chapter One\n" : string.Empty));
            var arguments = new List<string>
                { "-nostdin", "-v", "error", "-copyts", "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=25:duration=1",
                    "-f", "lavfi", "-i", "testsrc2=size=32x32:rate=25:duration=1",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
                    "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=2",
                    "-i", oldSubtitle, "-f", "ffmetadata", "-i", metadata,
                    "-map", "0:0", "-map", "1:0", "-map", "2:0", "-map", "3:0",
                    "-vf:0", nonzeroOrigin ? "setpts=PTS+3.1/TB" : "null", "-vf:1", nonzeroOrigin ? "setpts=PTS+3.1/TB" : "null",
                    "-af", nonzeroOrigin ? "asetpts=PTS+3/TB" : "anull",
                    "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac", "-c:s", extension == ".mp4" ? "mov_text" : "srt",
                    "-color_range", "tv", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
                    "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=jpn",
                    "-disposition:v:0", "default", "-disposition:v:1", "0", "-disposition:a:0", "default", "-disposition:a:1", "0",
                    "-map_metadata", "5", "-map_chapters", "5",
                    "-avoid_negative_ts", "disabled", "-n", fixture.SourcePath };
            if (originalSubtitle)
            {
                arguments.InsertRange(arguments.IndexOf("-vf:0"), ["-map", "4:0"]);
                arguments.InsertRange(arguments.IndexOf("-map_metadata"), ["-metadata:s:s:0", "language=chi", "-disposition:s:0", "default+forced"]);
            }

            var result = await MediaToolProcess.RunAsync(fixture.Ffmpeg, arguments,
                TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            fixture.Source = await new FfprobeMediaProbe(new(fixture.Ffprobe)).ProbeAsync(fixture.SourcePath);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    internal SubtitleMuxRequest Request(string extension)
    {
        var video = Source.Asset.Streams.First(stream => stream.CodecType == "video");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: SourcePath);
        var line = new SubtitleLine { Start = new(1, 5), End = new(3, 5), Text = "新增中文😀\nSecond line" };
        var project = new ProjectDocument
        {
            Width = 65, Height = 49, Assets = [asset],
            Media = new(asset.Id, video.Index, null, video.Timing.StartTimestamp!.ToMediaTime()),
            Subtitles = [line], Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        return new(project, DirectoryPath, Path.Combine(DirectoryPath, "output" + extension))
        {
            FfmpegPath = Ffmpeg, WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH")
        };
    }

    internal static async Task<SubtitleMuxFixture> CreateVideoOnlyAsync(string extension, string? range)
    {
        var fixture = new SubtitleMuxFixture();
        try
        {
            fixture.SourcePath = Path.Combine(fixture.DirectoryPath, "source" + extension);
            var arguments = new List<string>
            {
                "-nostdin", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=25:duration=1",
                "-map", "0:0", "-c:v", "libx264", "-preset", "ultrafast", "-disposition:v:0", "default"
            };
            if (range is not null)
            {
                arguments.AddRange(["-color_range", range]);
            }

            arguments.AddRange(["-n", fixture.SourcePath]);
            var result = await MediaToolProcess.RunAsync(fixture.Ffmpeg, arguments,
                TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            fixture.Source = await new FfprobeMediaProbe(new(fixture.Ffprobe)).ProbeAsync(fixture.SourcePath);
            return fixture;
        }
        catch
        {
            fixture.Dispose();
            throw;
        }
    }

    internal async Task<string[]> PacketEvidenceAsync(string path, int streamIndex)
    {
        var result = await MediaToolProcess.RunAsync(Ffprobe,
            ["-v", "error", "-select_streams", streamIndex.ToString(CultureInfo.InvariantCulture), "-show_packets", "-show_data_hash", "sha256",
                "-show_entries", "packet=pts_time,dts_time,duration_time,data_hash:packet_side_data=skip_samples,discard_padding", "-of", "compact", path],
            TimeSpan.FromSeconds(30), 1024 * 1024, 65536, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    internal async Task<string> SubtitleTextAsync(string path, int streamIndex)
    {
        var result = await MediaToolProcess.RunAsync(Ffmpeg,
            ["-nostdin", "-v", "error", "-copyts", "-i", path, "-map", $"0:{streamIndex.ToString(CultureInfo.InvariantCulture)}",
                "-c:s", "srt", "-f", "srt", "pipe:1"],
            TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
        return result.StandardOutput;
    }

    /// <inheritdoc />
    public void Dispose() => directory.Dispose();
}
