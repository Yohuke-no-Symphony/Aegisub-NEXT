using AegiNext.Media.Processes;
using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;

namespace AegiNext.Testing.Media;

internal sealed class DecoderFixture : IDisposable
{
    internal const int WIDTH = 64;
    internal const int HEIGHT = 48;
    internal const int FRAME_COUNT = 12;

    private readonly string directory;

    private DecoderFixture(string directory, string mediaPath, int videoStreamIndex, MediaTimeBase timeBase,
        JsonElement expectedFrames, byte[] rawFrames)
    {
        this.directory = directory;
        MediaPath = mediaPath;
        VideoStreamIndex = videoStreamIndex;
        TimeBase = timeBase;
        ExpectedFrames = expectedFrames;
        RawFrames = rawFrames;
    }

    internal string MediaPath { get; }

    internal int VideoStreamIndex { get; }

    internal MediaTimeBase TimeBase { get; }

    internal JsonElement ExpectedFrames { get; }

    internal byte[] RawFrames { get; }

    internal static async Task<DecoderFixture> CreateAsync(string transfer = "smpte2084", bool variableFrameRate = false,
        int frameCount = FRAME_COUNT, int keyFrameInterval = 48, int startTimeMilliseconds = 0)
    {
        var ffmpeg = GetToolPath("AEGINEXT_FFMPEG_PATH");
        var ffprobe = GetToolPath("AEGINEXT_FFPROBE_PATH");
        var directory = Directory.CreateTempSubdirectory("aeginext-decode-").FullName;
        try
        {
            var mediaPath = Path.Combine(directory, "字幕 空格' $() [测试].mkv");
            var rawPath = Path.Combine(directory, "参考 原始帧.yuv");
            var transferCode = transfer switch
            {
                "smpte2084" => 16,
                "arib-std-b67" => 18,
                _ => 1
            };
            var parameters = FormattableString.Invariant($"pools=none:frame-threads=1:repeat-headers=1:bframes=3:b-adapt=0:scenecut=0:keyint={keyFrameInterval}:min-keyint={keyFrameInterval}:colorprim=9:transfer={transferCode}:colormatrix=9");
            if (transfer == "smpte2084")
            {
                parameters += ":hdr10=1:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400";
            }

            var filter = $"format=yuv420p10le,setparams=range=limited:color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc";
            if (variableFrameRate)
            {
                filter = "setpts=PTS+floor(N/3)/(24000/1001*TB)," + filter;
            }
            if (startTimeMilliseconds != 0)
            {
                filter = FormattableString.Invariant($"setpts=PTS+{startTimeMilliseconds}/(1000*TB),") + filter;
            }

            await RunAsync(ffmpeg,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=24000/1001",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=0.6",
                    "-map", "1:a:0", "-map", "0:v:0", "-frames:v", frameCount.ToString(CultureInfo.InvariantCulture),
                    "-c:a", "pcm_s16le", "-c:v", "libx265", "-preset", "ultrafast", "-threads", "1", "-x265-params", parameters,
                    "-vf", filter, "-fps_mode", "vfr",
                    "-pix_fmt", "yuv420p10le", "-color_primaries", "bt2020", "-color_trc", transfer,
                    "-colorspace", "bt2020nc", "-color_range", "tv", "-y", mediaPath]);

            var frameOutput = await RunAsync(ffprobe,
                ["-v", "error", "-of", "json", "-select_streams", "v:0", "-show_streams", "-show_frames",
                    "-show_entries", "stream=index,time_base:frame=pts,best_effort_timestamp,duration,pict_type,key_frame", "-i", mediaPath]);
            using var reference = JsonDocument.Parse(frameOutput);
            var stream = Assert.Single(reference.RootElement.GetProperty("streams").EnumerateArray());
            var index = stream.GetProperty("index").GetInt32();
            Assert.Equal(1, index);
            var ratio = stream.GetProperty("time_base").GetString()!.Split('/');
            var timeBase = new MediaTimeBase(long.Parse(ratio[0], CultureInfo.InvariantCulture), long.Parse(ratio[1], CultureInfo.InvariantCulture));
            var frames = reference.RootElement.GetProperty("frames").Clone();
            Assert.Equal(frameCount, frames.GetArrayLength());
            Assert.Contains(frames.EnumerateArray(), frame => frame.GetProperty("pict_type").GetString() == "B");

            await RunAsync(ffmpeg,
                ["-v", "error", "-nostdin", "-i", mediaPath, "-map", $"0:{index}", "-fps_mode", "passthrough",
                    "-c:v", "rawvideo", "-pix_fmt", "yuv420p10le", "-threads", "1", "-f", "rawvideo", "-y", rawPath]);
            var rawFrames = await File.ReadAllBytesAsync(rawPath);
            Assert.Equal(frameCount * WIDTH * HEIGHT * 3, rawFrames.Length);
            return new(directory, mediaPath, index, timeBase, frames, rawFrames);
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

    private static string GetToolPath(string name)
    {
        var path = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(path), $"已启用解码测试，必须显式提供 {name}。");
        Assert.True(Path.IsPathFullyQualified(path), $"{name} 必须是完整路径。");
        Assert.True(File.Exists(path), $"{name} 指向的工具不存在：{path}");
        return path;
    }

    private static async Task<string> RunAsync(string executable, string[] arguments)
    {
        var result = await MediaToolProcess.RunAsync(executable, arguments, TimeSpan.FromSeconds(60),
            1024 * 1024, 1024 * 1024, CancellationToken.None);
        Assert.True(result.ExitCode == 0, $"{executable} 退出码 {result.ExitCode}：{result.StandardError}");
        return result.StandardOutput;
    }
}
