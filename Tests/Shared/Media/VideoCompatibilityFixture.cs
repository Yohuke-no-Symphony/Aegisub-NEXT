using AegiNext.Media.Processes;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;

namespace AegiNext.Testing.Media;

internal sealed class VideoCompatibilityFixture : IDisposable
{
    internal const int WIDTH = 128;
    internal const int HEIGHT = 128;
    internal const int FRAME_COUNT = 12;

    private readonly string? directory;

    private VideoCompatibilityFixture(string? directory, string mediaPath, JsonElement reference, byte[] rawFrames)
    {
        this.directory = directory;
        MediaPath = mediaPath;
        var stream = Assert.Single(reference.GetProperty("streams").EnumerateArray());
        VideoStreamIndex = stream.GetProperty("index").GetInt32();
        PixelFormat = stream.GetProperty("pix_fmt").GetString()!;
        Width = stream.GetProperty("width").GetInt32();
        Height = stream.GetProperty("height").GetInt32();
        var ratio = stream.GetProperty("time_base").GetString()!.Split('/');
        TimeBase = new(long.Parse(ratio[0], CultureInfo.InvariantCulture), long.Parse(ratio[1], CultureInfo.InvariantCulture));
        ExpectedFrames = reference.GetProperty("frames").Clone();
        Assert.Equal(FRAME_COUNT, ExpectedFrames.GetArrayLength());
        Assert.True(ExpectedFrames.EnumerateArray().Count(frame => frame.GetProperty("key_frame").GetInt32() != 0) >= 2);
        Assert.NotEmpty(rawFrames);
        Assert.Equal(0, rawFrames.Length % FRAME_COUNT);
        RawFrames = rawFrames;
    }

    internal string MediaPath { get; }
    internal int VideoStreamIndex { get; }
    internal string PixelFormat { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal MediaTimeBase TimeBase { get; }
    internal JsonElement ExpectedFrames { get; }
    internal byte[] RawFrames { get; }
    internal int FrameByteCount => RawFrames.Length / FRAME_COUNT;

    internal MediaTime GetFrameTime(int index)
    {
        var frame = ExpectedFrames[index];
        var timestamp = frame.TryGetProperty("pts", out var pts) ? pts.GetInt64() : frame.GetProperty("best_effort_timestamp").GetInt64();
        return new MediaTimestamp(timestamp, TimeBase).ToMediaTime();
    }

    internal static async Task<VideoCompatibilityFixture> CreateAsync(string name, string codec, string pixelFormat, int size = WIDTH)
    {
        var ffprobe = GetToolPath("AEGINEXT_FFPROBE_PATH");
        if (name is "vp8" or "vp9-ten-bit" or "av1-ten-bit" or "av1-film-grain-ten-bit")
        {
            Assert.Equal(WIDTH, size);
            var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "VideoCompatibility");
            var path = Path.Combine(root, name + (codec == "av1" ? ".mkv" : ".webm"));
            Assert.True(File.Exists(path), $"Missing generated compatibility fixture: {path}");
            using var reference = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, name + ".reference.json")));
            var generation = reference.RootElement.GetProperty("generation");
            Assert.Equal(generation.GetProperty("media_sha256").GetString(), Hash(await File.ReadAllBytesAsync(path)));
            await using var source = File.OpenRead(Path.Combine(root, name + ".raw.gz"));
            await using var compressed = new GZipStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream();
            await compressed.CopyToAsync(output);
            var raw = output.ToArray();
            Assert.Equal(generation.GetProperty("raw_sha256").GetString(), Hash(raw));
            using var actual = JsonDocument.Parse(await RunAsync(ffprobe, ProbeArguments(path, includeFrames: false)));
            AssertStream(actual.RootElement, codec, pixelFormat);
            AssertStream(reference.RootElement, codec, pixelFormat);
            return new(null, path, reference.RootElement, raw);
        }

        var ffmpeg = GetToolPath("AEGINEXT_FFMPEG_PATH");
        var directory = Directory.CreateTempSubdirectory("aeginext-video-compatibility-").FullName;
        try
        {
            var extension = name switch
            {
                "mpeg2" => "ts",
                "mpeg4" => "mp4",
                "mpeg4-avi" or "mjpeg" => "avi",
                _ when name.StartsWith("prores", StringComparison.Ordinal) => "mov",
                _ => "mkv"
            };
            var path = Path.Combine(directory, $"{name} 字幕 空格' $() [测试].{extension}");
            await RunAsync(ffmpeg, EncodeArguments(name, pixelFormat, path, size));
            using var reference = JsonDocument.Parse(await RunAsync(ffprobe, ProbeArguments(path, includeFrames: true)));
            AssertStream(reference.RootElement, codec, pixelFormat, size);
            var rawPath = Path.Combine(directory, "reference.raw");
            await RunAsync(ffmpeg,
                ["-v", "error", "-nostdin", "-hwaccel", "none", "-i", path, "-map", "0:v:0", "-fps_mode", "passthrough",
                    "-c:v", "rawvideo", "-pix_fmt", pixelFormat, "-threads", "1", "-f", "rawvideo", "-y", rawPath]);
            return new(directory, path, reference.RootElement, await File.ReadAllBytesAsync(rawPath));
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
        if (directory is not null)
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string[] EncodeArguments(string name, string pixelFormat, string path, int size)
    {
        var encoder = name switch
        {
            "h264-ten-bit" => "libx264",
            "h264-rgb" => "libx264rgb",
            "hevc422-ten-bit" or "hevc444-ten-bit" => "libx265",
            "mpeg2" => "mpeg2video",
            "mpeg4" or "mpeg4-avi" => "mpeg4",
            "mjpeg" => "mjpeg",
            _ when name.StartsWith("prores", StringComparison.Ordinal) => "prores_ks",
            "ffv1-sixteen-bit" => "ffv1",
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown compatibility fixture.")
        };
        var sourceFormat = name switch
        {
            "h264-rgb" => "rgb24",
            "prores4444" or "prores4444-xq" => "yuv444p10le",
            "prores4444-alpha" or "prores4444-xq-alpha" => "yuva444p10le",
            _ => pixelFormat
        };
        var rgb = name == "h264-rgb";
        var fullRange = rgb || name == "mjpeg";
        var range = fullRange ? "full" : "limited";
        var matrix = rgb ? "gbr" : "bt709";
        var transfer = rgb ? "iec61966-2-1" : "bt709";
        var filter = $"format={sourceFormat},setsar=1/1,setparams=range={range}:color_primaries=bt709:color_trc={transfer}:colorspace={matrix}";
        if (name.EndsWith("-alpha", StringComparison.Ordinal))
        {
            filter = "format=yuva444p10le,geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='1023*X/(W-1)'," +
                "setsar=1/1,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709";
        }

        var arguments = new List<string>
        {
            "-v", "error", "-nostdin", "-f", "lavfi", "-i", $"testsrc2=size={size}x{size}:rate=25"
        };
        if (name == "hevc444-ten-bit")
        {
            arguments.AddRange(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.48",
                "-map", "1:a:0", "-map", "0:v:0", "-c:a", "flac"]);
        }
        else
        {
            arguments.Add("-an");
        }
        arguments.AddRange(["-frames:v", FRAME_COUNT.ToString(CultureInfo.InvariantCulture), "-vf", filter,
            "-c:v", encoder, "-pix_fmt", sourceFormat, "-threads", "1"]);
        arguments.AddRange(encoder switch
        {
            "libx264" or "libx264rgb" => ["-preset", "ultrafast", "-g", "4", "-bf", "2", "-x264-params", "keyint=4:min-keyint=4:scenecut=0"],
            "libx265" => ["-preset", "ultrafast", "-x265-params", "pools=none:frame-threads=1:log-level=error:keyint=4:min-keyint=4:scenecut=0:bframes=2:b-adapt=0"],
            "mpeg2video" or "mpeg4" => ["-g", "4", "-bf", "2", "-q:v", "3"],
            "mjpeg" => ["-q:v", "3"],
            "prores_ks" => ["-profile:v", name switch
                {
                    "prores422-proxy" => "0",
                    "prores422-lt" => "1",
                    "prores422" => "2",
                    "prores422-hq" => "3",
                    "prores4444-xq" or "prores4444-xq-alpha" => "5",
                    _ => "4"
                }, "-alpha_bits", name.EndsWith("-alpha", StringComparison.Ordinal) ? "16" : "0"],
            "ffv1" => ["-level", "3", "-g", "4"],
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        });
        arguments.AddRange(["-color_range", fullRange ? "pc" : "tv", "-color_primaries", "bt709", "-color_trc", transfer,
            "-colorspace", rgb ? "rgb" : matrix, "-chroma_sample_location", "left", "-y", path]);
        return [.. arguments];
    }

    private static string[] ProbeArguments(string path, bool includeFrames)
    {
        var arguments = new List<string> { "-v", "error", "-of", "json", "-select_streams", "v:0", "-show_streams" };
        if (includeFrames)
        {
            arguments.Add("-show_frames");
        }
        arguments.AddRange(["-show_entries", "stream=index,codec_name,profile,pix_fmt,width,height,time_base:frame=pts,best_effort_timestamp,duration,pict_type,key_frame", "-i", path]);
        return [.. arguments];
    }

    private static void AssertStream(JsonElement reference, string codec, string pixelFormat, int size = WIDTH)
    {
        var stream = Assert.Single(reference.GetProperty("streams").EnumerateArray());
        Assert.Equal(codec, stream.GetProperty("codec_name").GetString());
        Assert.Equal(pixelFormat, stream.GetProperty("pix_fmt").GetString());
        Assert.Equal(size, stream.GetProperty("width").GetInt32());
        Assert.Equal(size, stream.GetProperty("height").GetInt32());
    }

    private static string GetToolPath(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(path), $"Compatibility tests require {name}.");
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.True(File.Exists(path));
        return path;
    }

    private static string Hash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static async Task<string> RunAsync(string executable, string[] arguments)
    {
        var result = await MediaToolProcess.RunAsync(executable, arguments, TimeSpan.FromSeconds(60),
            1024 * 1024, 1024 * 1024, CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"{executable} exited with {result.ExitCode}: {result.StandardError}");
        return result.StandardOutput;
    }
}
