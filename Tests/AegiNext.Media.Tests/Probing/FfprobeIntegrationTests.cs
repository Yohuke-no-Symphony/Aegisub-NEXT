using AegiNext.Media.Processes;
using System.Text.Json;
using AegiNext.Media.Probing;
using Xunit.Abstractions;

namespace AegiNext.Media.Tests.Probing;

public sealed class FfprobeIntegrationTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions reportOptions = new() { WriteIndented = true };

    [MediaToolsTheory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    [InlineData("bt709")]
    [Trait("Category", "MediaIntegration")]
    public async Task ProbesRealTenBitMediaAndRejectsCorruptedInput(string transfer)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!;
        var ffprobe = Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!;
        Assert.True(Path.IsPathFullyQualified(ffmpeg));
        var directory = Directory.CreateTempSubdirectory("aeginext-media-").FullName;
        try
        {
            var filePath = Path.Combine(directory, "字幕 空格' $() [测试].mkv");
            var transferCode = transfer switch
            {
                "smpte2084" => 16,
                "arib-std-b67" => 18,
                _ => 1
            };
            var parameters = FormattableString.Invariant($"pools=none:frame-threads=1:repeat-headers=1:colorprim=9:transfer={transferCode}:colormatrix=9");
            if (transfer == "smpte2084")
            {
                parameters += ":hdr10=1:master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400";
            }

            var encoded = await MediaToolProcess.RunAsync(ffmpeg,
                ["-v", "error", "-nostdin", "-f", "lavfi", "-i", "testsrc2=size=64x48:rate=24000/1001",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "0.3", "-map", "0:v:0", "-map", "1:a:0",
                    "-c:v", "libx265", "-preset", "ultrafast", "-threads", "1", "-x265-params", parameters,
                    "-vf", $"format=yuv420p10le,setparams=range=limited:color_primaries=bt2020:color_trc={transfer}:colorspace=bt2020nc",
                    "-pix_fmt", "yuv420p10le", "-color_primaries", "bt2020", "-color_trc", transfer, "-colorspace", "bt2020nc", "-color_range", "tv",
                    "-c:a", "pcm_s16le", "-metadata:s:a:0", "language=jpn", "-y", filePath],
                TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
            Assert.True(encoded.ExitCode == 0, encoded.StandardError);

            var probe = new FfprobeMediaProbe(new(ffprobe));
            var report = await probe.ProbeAsync(filePath);
            output.WriteLine(JsonSerializer.Serialize(report, reportOptions));
            var videoStream = Assert.Single(report.Asset.Streams, stream => stream.CodecType == "video");
            var video = Assert.IsType<AegiNext.Core.Media.MediaVideoInfo>(videoStream.Video);
            Assert.Equal(transfer, video.Color.Transfer);
            Assert.Equal(transfer == "smpte2084", video.Color.IsPq);
            Assert.Equal(transfer == "arib-std-b67", video.Color.IsHlg);
            Assert.Equal("yuv420p10le", video.PixelFormat);
            Assert.Equal(64, video.Width);
            Assert.NotNull(videoStream.Timing.TimeBase);
            Assert.NotNull(videoStream.Timing.StartPts);
            Assert.Equal("jpn", Assert.Single(report.Asset.Streams, stream => stream.CodecType == "audio").Tags["language"]);
            Assert.Equal(Path.GetFullPath(filePath), report.SourcePath);
            Assert.Equal(64, report.Tool.Sha256.Length);

            var corruptPath = Path.Combine(directory, "损坏.bin");
            await File.WriteAllTextAsync(corruptPath, "not a media container");
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => probe.ProbeAsync(corruptPath));
            Assert.Contains("exited with code", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
