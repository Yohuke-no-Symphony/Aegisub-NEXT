using AegiNext.Media.Processes;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class NativeContinuousPreviewFixture : IDisposable
{
    private readonly string directory;

    private NativeContinuousPreviewFixture(string directory, string mediaPath)
    {
        this.directory = directory;
        MediaPath = mediaPath;
    }

    internal string MediaPath { get; }

    internal static async Task<NativeContinuousPreviewFixture> CreateAsync(bool withAudio = false)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH");
        Assert.False(string.IsNullOrWhiteSpace(ffmpeg));
        Assert.True(Path.IsPathFullyQualified(ffmpeg));
        Assert.True(File.Exists(ffmpeg));
        var directory = Directory.CreateTempSubdirectory("aeginext-native-continuous-preview-").FullName;
        try
        {
            var path = Path.Combine(directory, withAudio ? "1080p60 explicit BT709 silent audio.mp4" : "1080p60 explicit BT709 silent.mp4");
            var arguments = new List<string>
            {
                "-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=60"
            };
            if (withAudio)
            {
                arguments.AddRange(["-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "6", "-c:a", "aac", "-b:a", "128k"]);
            }
            else
            {
                arguments.Add("-an");
            }
            arguments.AddRange(["-frames:v", "360", "-c:v", "libx264", "-profile:v", "high", "-preset", "ultrafast",
                    "-crf", "18", "-threads", "1", "-g", "120", "-bf", "2", "-pix_fmt", "yuv420p",
                    "-vf", "setsar=1/1,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709",
                    "-color_range", "tv", "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709",
                    "-chroma_sample_location", "left", "-video_track_timescale", "60000", "-y", path]);
            var result = await MediaToolProcess.RunAsync(ffmpeg, arguments,
                TimeSpan.FromSeconds(60), 1024 * 1024, 1024 * 1024, CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardError);
            return new(directory, path);
        }
        catch
        {
            Directory.Delete(directory, true);
            throw;
        }
    }

    public void Dispose()
    {
        Directory.Delete(directory, true);
    }
}
