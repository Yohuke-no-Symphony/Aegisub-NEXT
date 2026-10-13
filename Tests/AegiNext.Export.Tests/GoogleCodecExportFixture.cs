using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Processes;

namespace AegiNext.Export.Tests;

internal sealed class GoogleCodecExportFixture : IDisposable
{
    internal const int WIDTH = 192;
    internal const int HEIGHT = 128;
    internal const int FRAME_RATE = 12;
    internal const int FRAME_COUNT = 24;
    private const string MASTER_DISPLAY = "G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,50)";

    private GoogleCodecExportFixture(string directoryPath, string mediaPath)
    {
        DirectoryPath = directoryPath;
        MediaPath = mediaPath;
    }

    internal string DirectoryPath { get; }
    internal string MediaPath { get; }

    internal static async Task<GoogleCodecExportFixture> CreateAsync(string? transfer = null)
    {
        var directory = Directory.CreateTempSubdirectory("aeginext-google-codec-").FullName;
        try
        {
            var path = Path.Combine(directory, "source.mkv");
            var arguments = new List<string>
            {
                "-v", "error", "-nostdin", "-f", "lavfi", "-i", $"color=black:size={WIDTH}x{HEIGHT}:rate={FRAME_RATE}:duration=2",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=2",
                "-map", "0:v:0", "-map", "1:a:0", "-frames:v", FRAME_COUNT.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-c:a", "aac", "-b:a", "128k", "-threads", "1"
            };
            if (transfer is null)
            {
                arguments.AddRange(["-c:v", "ffv1", "-level", "3", "-pix_fmt", "yuv420p", "-color_range", "tv",
                    "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709"]);
            }
            else
            {
                arguments.AddRange(["-vf", $"format=yuv420p10le,setparams=range=limited:color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc",
                    "-c:v", "libx265", "-preset", "ultrafast", "-pix_fmt", "yuv420p10le",
                    "-x265-params", $"pools=none:frame-threads=1:repeat-headers=1:master-display={MASTER_DISPLAY}:max-cll=1000,400",
                    "-color_range", "tv", "-color_primaries", "bt2020", "-color_trc", transfer, "-colorspace", "bt2020nc"]);
            }
            arguments.AddRange(["-chroma_sample_location", "left", "-avoid_negative_ts", "disabled", "-y", path]);
            await RunFfmpegAsync(arguments);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"), Path.Combine(directory, "subtitle.ttf"));
            return new(directory, path);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }

    internal VideoExportRequest CreateRequest(VideoCodec codec, string extension, AudioExportMode audioMode)
    {
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: MediaPath);
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "subtitle.ttf");
        var subtitle = new SubtitleLine
        {
            Text = "GOOGLE",
            Start = MediaTime.Zero,
            End = new(2),
            Style = new()
            {
                FontFamily = "Noto Sans", FontAssetId = font.Id, FontSize = 24, Alignment = TextAlignment.MIDDLE_CENTER,
                Scaling = new() { ReferenceWidth = WIDTH, ReferenceHeight = HEIGHT },
                Margins = new(4, 4, 4), Fill = SceneColor.White, StrokeWidth = 0, ShadowColor = SceneColor.Transparent,
                ShadowBlur = 0
            }
        };
        var project = new ProjectDocument
        {
            Width = WIDTH,
            Height = HEIGHT,
            Assets = [media, font],
            Media = new(media.Id, 0, 1, MediaTime.Zero),
            Subtitles = [subtitle],
            Layers = [new()
            {
                Id = subtitle.Id, Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id,
                Start = subtitle.Start, End = subtitle.End
            }]
        };
        return new VideoExportRequest(project, DirectoryPath, Path.Combine(DirectoryPath, "output" + extension))
        {
            WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH"),
            FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"),
            DecodeMode = VideoDecodeMode.Software
        }.WithSettings(VideoExportCapabilities.CreateDefaultSettings(codec) with
        {
            Preset = "ultrafast", AudioMode = audioMode, AudioBitrate = 96000, VideoBitrate = 400000
        });
    }

    internal async Task<string> PrepareAudioAsync(AudioExportMode mode)
    {
        var path = Path.Combine(DirectoryPath, mode == AudioExportMode.Opus ? "prepared.mka" : "prepared.m4a");
        await RunFfmpegAsync(["-nostdin", "-v", "error", "-copyts", "-i", MediaPath, "-map", "0:1", "-vn",
            "-c:a", mode == AudioExportMode.Opus ? "libopus" : "aac", "-b:a", "96000", "-map_metadata", "0",
            "-map_chapters", "-1", "-avoid_negative_ts", "disabled", "-n", path]);
        return path;
    }

    internal static Task<JsonElement> ProbeVideoAsync(string path) => ProbeAsync(path,
        ["-select_streams", "v:0", "-show_streams", "-show_frames", "-show_format"]);

    internal static Task<JsonElement> ProbeAudioAsync(string path) => ProbeAsync(path,
        ["-select_streams", "a:0", "-show_streams", "-show_packets", "-show_data_hash", "sha256"]);

    private static async Task<JsonElement> ProbeAsync(string path, string[] arguments)
    {
        var result = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!,
            ["-v", "error", .. arguments, "-of", "json", path], TimeSpan.FromSeconds(30), 4 * 1024 * 1024, 65536, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        return document.RootElement.Clone();
    }

    private static async Task RunFfmpegAsync(IReadOnlyList<string> arguments)
    {
        var result = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!, arguments,
            TimeSpan.FromSeconds(60), 65536, 65536, CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }

    internal void AssertCleanOutput(bool exists)
    {
        Assert.Equal(exists, Directory.EnumerateFiles(DirectoryPath, "output.*").Any());
        Assert.Empty(Directory.EnumerateDirectories(DirectoryPath, ".aeginext-export-*"));
        Assert.Empty(Directory.EnumerateFiles(DirectoryPath, "audio-compatibility*", SearchOption.AllDirectories));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Directory.Delete(DirectoryPath, true);
    }
}
