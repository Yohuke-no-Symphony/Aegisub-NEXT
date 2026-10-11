using AegiNext.Media.Processes;
using System.Globalization;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

/// <summary>真实 CPU 码控的原生回执、解码时间轴、码率测量和体积影响。</summary>
[Collection(nameof(NativeExportTestGroup))]
public sealed class VideoRateControlExportTests
{
    /// <summary>每种 CPU 编码与码控组合都必须确认实际配置，保持完整帧数、PTS 和有效成片。</summary>
    [ExportTheory]
    [InlineData(VideoCodec.H264, VideoRateControlMode.CRF)]
    [InlineData(VideoCodec.H264, VideoRateControlMode.VBR)]
    [InlineData(VideoCodec.H264, VideoRateControlMode.CBR)]
    [InlineData(VideoCodec.Hevc, VideoRateControlMode.CRF)]
    [InlineData(VideoCodec.Hevc, VideoRateControlMode.VBR)]
    [InlineData(VideoCodec.Hevc, VideoRateControlMode.CBR)]
    public async Task WorkerConfirmsSoftwareRateControlAndDecodableTimeline(VideoCodec codec, VideoRateControlMode mode)
    {
        using var fixture = await RateControlExportFixture.CreateAsync();
        var request = Request(fixture, codec, mode, "confirmed.mp4") with { VideoBitrate = 600999 };

        var result = await new VideoExporter().ExportAsync(request);

        Assert.Equal((ulong)RateControlExportFixture.FRAME_COUNT, result.Frames);
        Assert.Equal(codec == VideoCodec.H264 ? "libx264" : "libx265", result.Encoder);
        Assert.Equal(new VideoRateControlInfo(mode, mode == VideoRateControlMode.CRF ? 0 : 600000,
            mode == VideoRateControlMode.CRF ? 20 : 0), result.RateControl);
        var probe = new FfprobeMediaProbe(new(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!));
        var report = await probe.ProbeAsync(result.OutputPath);
        Assert.Equal(codec == VideoCodec.H264 ? "h264" : "hevc", report.Asset.Streams.Single(stream => stream.CodecType == "video").CodecName);
        var duration = Assert.IsType<MediaTime>(report.Asset.ReportedDuration);
        var seconds = duration.ToTimeSpan(MediaTimeRounding.FLOOR).TotalSeconds;
        Assert.InRange(seconds, 3.95, 4.05);
        using var decoder = FfmpegVideoDecoder.Open(result.OutputPath, 0);
        for (var index = 0; index < RateControlExportFixture.FRAME_COUNT; index++)
        {
            using var frame = decoder.ReadFrame();
            Assert.NotNull(frame);
            var actual = frame.Info.PresentationTimestamp!.ToMediaTime().ToTimeSpan(MediaTimeRounding.FLOOR).TotalSeconds;
            Assert.InRange(Math.Abs(actual - (double)index / RateControlExportFixture.FRAME_RATE), 0, 0.001);
        }
        Assert.Null(decoder.ReadFrame());
        var measurements = await MeasureVideoPacketsAsync(result.OutputPath);
        Assert.True(measurements.TotalBits > 0);
        Assert.Equal(4, measurements.WindowBits.Length);
        Assert.All(measurements.WindowBits, bits => Assert.True(bits > 0));
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    /// <summary>VBR 的目标变化必须在实际编码视频载荷中体现，而不仅改变 managed 回执。</summary>
    [ExportTheory]
    [InlineData(VideoCodec.H264)]
    [InlineData(VideoCodec.Hevc)]
    public async Task ChangingVbrTargetChangesEncodedVideoSize(VideoCodec codec)
    {
        using var fixture = await RateControlExportFixture.CreateAsync();
        var low = Request(fixture, codec, VideoRateControlMode.VBR, "low.mp4") with { VideoBitrate = 100000 };
        var high = low with { OutputPath = Path.Combine(fixture.DirectoryPath, "high.mp4"), VideoBitrate = 1200000 };
        var exporter = new VideoExporter();

        var lowResult = await exporter.ExportAsync(low);
        var highResult = await exporter.ExportAsync(high);

        Assert.Equal(new VideoRateControlInfo(VideoRateControlMode.VBR, 100000, 0), lowResult.RateControl);
        Assert.Equal(new VideoRateControlInfo(VideoRateControlMode.VBR, 1200000, 0), highResult.RateControl);
        var lowMeasurements = await MeasureVideoPacketsAsync(lowResult.OutputPath);
        var highMeasurements = await MeasureVideoPacketsAsync(highResult.OutputPath);
        Assert.True(highMeasurements.TotalBits > lowMeasurements.TotalBits * 2,
            $"Higher VBR target must change real video payload: {lowMeasurements.TotalBits} -> {highMeasurements.TotalBits} bits.");
        Assert.Equal(lowResult.Frames, highResult.Frames);
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    private static VideoExportRequest Request(RateControlExportFixture fixture, VideoCodec codec,
        VideoRateControlMode mode, string name)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: fixture.MediaPath);
        var subtitle = new SubtitleLine
        {
            Text = "AegiNext",
            End = new(4),
            Style = new() { FontFamily = "Arial", FontSize = 20, Margins = new(8, 8, 8), StrokeWidth = 0, ShadowBlur = 0 }
        };
        var project = new ProjectDocument
        {
            Width = RateControlExportFixture.WIDTH,
            Height = RateControlExportFixture.HEIGHT,
            Assets = [asset],
            Media = new(asset.Id, 0, null, MediaTime.Zero),
            Subtitles = [subtitle],
            Layers = [new() { Id = subtitle.Id, Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, Start = subtitle.Start, End = subtitle.End }]
        };
        return new(project, fixture.DirectoryPath, Path.Combine(fixture.DirectoryPath, name))
        {
            Codec = codec,
            RateControlMode = mode,
            Crf = 20,
            Preset = "fast",
            DecodeMode = VideoDecodeMode.Software,
            AudioMode = AudioExportMode.None,
            WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH"),
            FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")
        };
    }

    private static async Task<(long TotalBits, long[] WindowBits)> MeasureVideoPacketsAsync(string path)
    {
        var process = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!,
            ["-v", "error", "-select_streams", "v:0", "-show_packets", "-show_entries", "packet=pts_time,size", "-of", "json", path],
            TimeSpan.FromSeconds(15), 1024 * 1024, 65536, CancellationToken.None);
        Assert.Equal(0, process.ExitCode);
        using var document = JsonDocument.Parse(process.StandardOutput);
        var total = 0L;
        var windows = new long[4];
        foreach (var packet in document.RootElement.GetProperty("packets").EnumerateArray())
        {
            var bits = long.Parse(packet.GetProperty("size").GetString()!, CultureInfo.InvariantCulture) * 8;
            var seconds = double.Parse(packet.GetProperty("pts_time").GetString()!, CultureInfo.InvariantCulture);
            total += bits;
            var index = Math.Clamp((int)Math.Floor(seconds), 0, windows.Length - 1);
            windows[index] += bits;
        }

        return (total, windows);
    }
}
