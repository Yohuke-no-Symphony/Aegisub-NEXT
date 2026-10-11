using AegiNext.Testing.Media;
using AegiNext.Media.Processes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Media.Preview;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

[Collection(nameof(NativeExportTestGroup))]
public sealed class VideoExportColorIntegrationTests
{
    [ExportTheory]
    [InlineData(VideoDecodeMode.Auto)]
    [InlineData(VideoDecodeMode.Hardware)]
    public async Task UnsupportedHardwareCodecFallsBackOnlyWhenAutomatic(VideoDecodeMode mode)
    {
        using var fixture = await SdrPreviewFixture.CreateAsync(false);
        var directory = Path.GetDirectoryName(fixture.MediaPath)!;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: fixture.MediaPath);
        var project = new ProjectDocument
        {
            Width = 96, Height = 64, Assets = [asset], Media = new(asset.Id, 1, null, MediaTime.Zero)
        };
        var output = Path.Combine(directory, "fallback.mp4");
        var request = new VideoExportRequest(project, directory, output)
        {
            DecodeMode = mode, Codec = VideoCodec.H264, AudioMode = AudioExportMode.None,
            Quality = 0, Preset = "ultrafast", FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"),
            WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH")
        };
        if (mode == VideoDecodeMode.Hardware)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new VideoExporter().ExportAsync(request));
            Assert.Contains("hardware", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(output));
        }
        else
        {
            var result = await new VideoExporter().ExportAsync(request);
            Assert.Equal(VideoDecoderBackend.Software, result.Decoder!.ActiveBackend);
            Assert.False(result.Decoder.HardwareConfirmed);
            Assert.False(string.IsNullOrWhiteSpace(result.Decoder.FallbackReason));
        }
        Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MidstreamEquivalentTagsAreAcceptedAndRealColorChangesNeverCommit(bool changesColor)
    {
        using var fixture = await MidstreamExportFixture.CreateAsync(changesColor);
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: fixture.MediaPath);
        var project = new ProjectDocument
        {
            Width = 1280, Height = 720, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        };
        var output = Path.Combine(fixture.DirectoryPath, "output.mp4");
        var request = new VideoExportRequest(project, fixture.DirectoryPath, output)
        {
            DecodeMode = VideoDecodeMode.Software, Codec = VideoCodec.H264, AudioMode = AudioExportMode.None,
            Quality = 0, Preset = "ultrafast", FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"),
            WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH")
        };
        if (changesColor)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new VideoExporter().ExportAsync(request));
            Assert.Contains("Midstream", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(output));
        }
        else
        {
            var result = await new VideoExporter().ExportAsync(request);
            Assert.Equal(6UL, result.Frames);
            Assert.Equal(1, result.OutputColor!.Matrix);
            Assert.NotEqual(0U, result.OutputColor.InferredFields);
        }
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(VideoDecodeMode.Software)]
    [InlineData(VideoDecodeMode.Auto)]
    public async Task UnmarkedH264ExportsWithEffectiveColorAndCopiesOriginalAacPackets(VideoDecodeMode decodeMode)
    {
        using var fixture = await UnmarkedExportFixture.CreateAsync();
        var sourceHash = await VideoHashAsync(fixture.MediaPath);
        var unknown = await FirstInfoAsync(fixture.MediaPath);
        Assert.Equal(0, unknown.ColorRangeCode);
        Assert.Equal(2, unknown.ColorMatrixCode);
        Assert.Equal(2, unknown.ColorPrimariesCode);
        Assert.Equal(2, unknown.ColorTransferCode);
        var output = Path.Combine(fixture.DirectoryPath, "output.mp4");
        var result = await new VideoExporter().ExportAsync(CreateRequest(fixture, fixture.MediaPath, output, decodeMode));
        Assert.Equal((ulong)UnmarkedExportFixture.FRAME_COUNT, result.Frames);
        Assert.NotNull(result.Decoder);
        Assert.Equal(decodeMode, result.Decoder.RequestedMode);
        Assert.Equal(result.Frames, result.Decoder.DeliveredFrames);
        Assert.NotNull(result.OutputColor);
        Assert.Equal(1, result.OutputColor.Range);
        Assert.Equal(1, result.OutputColor.Matrix);
        Assert.Equal(1, result.OutputColor.Primaries);
        Assert.Equal(1, result.OutputColor.Transfer);
        Assert.NotEqual(0U, result.OutputColor.InferredFields);
        if (decodeMode == VideoDecodeMode.Software)
        {
            Assert.Equal(VideoDecoderBackend.Software, result.Decoder.ActiveBackend);
        }

        var probe = new FfprobeMediaProbe(new(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!));
        var report = await probe.ProbeAsync(output);
        var video = report.Asset.Streams.Single(stream => stream.CodecType == "video");
        Assert.Equal("h264", video.CodecName);
        Assert.Equal("bt709", video.Video!.Color.Matrix);
        Assert.Equal("bt709", video.Video.Color.Transfer);
        Assert.Equal("bt709", video.Video.Color.Primaries);
        Assert.Equal("tv", video.Video.Color.Range);
        var audio = report.Asset.Streams.Single(stream => stream.CodecType == "audio");
        Assert.Equal("aac", audio.CodecName);
        Assert.Equal(await AudioPacketHashAsync(fixture.MediaPath), await AudioPacketHashAsync(output));
        Assert.Equal(sourceHash, await VideoHashAsync(fixture.MediaPath));
        var after = await FirstInfoAsync(fixture.MediaPath);
        Assert.Equal(unknown.Color, after.Color);
        Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
    }

    [ExportTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SoftwareAndConfirmedHardwareDecodeProduceTheSameExport(bool overlay)
    {
        using var fixture = await UnmarkedExportFixture.CreateAsync();
        var softwareOutput = Path.Combine(fixture.DirectoryPath, "software.mp4");
        var hardwareOutput = Path.Combine(fixture.DirectoryPath, "hardware.mp4");
        var software = await new VideoExporter().ExportAsync(CreateRequest(fixture, fixture.MediaPath,
            softwareOutput, VideoDecodeMode.Software, overlay));
        VideoExportResult hardware;
        try
        {
            hardware = await new VideoExporter().ExportAsync(CreateRequest(fixture, fixture.MediaPath,
                hardwareOutput, VideoDecodeMode.Hardware, overlay));
        }
        catch (InvalidOperationException error)
        {
            Assert.Contains("hardware", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(hardwareOutput));
            Assert.Empty(Directory.GetDirectories(fixture.DirectoryPath, ".aeginext-export-*"));
            return;
        }

        Assert.True(hardware.Decoder!.HardwareConfirmed);
        Assert.NotEqual(VideoDecoderBackend.Software, hardware.Decoder.ActiveBackend);
        Assert.Equal(software.Frames, hardware.Frames);
        Assert.Equal(software.OutputColor, hardware.OutputColor);
        Assert.Equal(await VideoHashAsync(softwareOutput), await VideoHashAsync(hardwareOutput));
        Assert.Equal(await AudioPacketHashAsync(softwareOutput), await AudioPacketHashAsync(hardwareOutput));
    }

    [ExportTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InferredAndExplicitInputsProduceTheSameColorManagedExport(bool overlay)
    {
        using var fixture = await UnmarkedExportFixture.CreateAsync();
        var unmarkedOutput = Path.Combine(fixture.DirectoryPath, "inferred.mp4");
        var explicitOutput = Path.Combine(fixture.DirectoryPath, "explicit.mp4");
        var first = CreateRequest(fixture, fixture.MediaPath, unmarkedOutput, VideoDecodeMode.Software, overlay);
        var second = CreateRequest(fixture, fixture.ReferencePath, explicitOutput, VideoDecodeMode.Software, overlay);
        await new VideoExporter().ExportAsync(first);
        await new VideoExporter().ExportAsync(second);
        Assert.Equal(await VideoHashAsync(unmarkedOutput), await VideoHashAsync(explicitOutput));
        using var source = FfmpegVideoDecoder.Open(fixture.MediaPath, 0, options: new() { Mode = VideoDecodeMode.Software });
        using var decoded = FfmpegVideoDecoder.Open(unmarkedOutput, 0, options: new() { Mode = VideoDecodeMode.Software });
        using var converter = new SdrVideoConverter();
        var frames = 0;
        while (source.ReadFrame() is { } original)
        {
            using (original)
            using (var encoded = Assert.IsType<DecodedVideoFrame>(decoded.ReadFrame()))
            {
                Assert.Equal(original.Info.PresentationTimestamp!.ToMediaTime(), encoded.Info.PresentationTimestamp!.ToMediaTime());
                if (!overlay)
                {
                    var before = converter.Convert(original);
                    var after = converter.Convert(encoded);
                    Assert.Equal(before.Width, after.Width);
                    Assert.Equal(before.Height, after.Height);
                    var sourcePixels = before.Pixels.ToArray();
                    var outputPixels = after.Pixels.ToArray();
                    foreach (var x in new[] { 112, 320, 528, 736, 944, 1152 })
                    {
                        var offset = (112 * before.Width + x) * 4;
                        for (var channel = 0; channel < 4; channel++)
                        {
                            Assert.True(Math.Abs(sourcePixels[offset + channel] - outputPixels[offset + channel]) <= 3,
                                $"Frame {frames}, pixel ({x}, 112), channel {channel}: source {sourcePixels[offset + channel]}, output {outputPixels[offset + channel]}");
                        }
                    }
                }
            }
            frames++;
        }
        Assert.Equal(UnmarkedExportFixture.FRAME_COUNT, frames);
        Assert.Null(decoded.ReadFrame());
    }

    private static VideoExportRequest CreateRequest(UnmarkedExportFixture fixture, string source, string output,
        VideoDecodeMode mode, bool overlay = false)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: source);
        var project = new ProjectDocument
        {
            Width = UnmarkedExportFixture.WIDTH,
            Height = UnmarkedExportFixture.HEIGHT,
            Assets = [asset],
            Media = new(asset.Id, 0, 1, MediaTime.Zero),
            Layers = overlay ? [new()
            {
                Kind = LayerKind.SHAPE,
                Shape = new(ShapeKind.RECTANGLE, 16, 16),
                Transform = new(X: 32, Y: 32),
                Fill = new(1, 1, 1, 0.5),
                End = new(1)
            }] : []
        };
        return new(project, fixture.DirectoryPath, output)
        {
            Codec = VideoCodec.H264,
            DecodeMode = mode,
            Quality = 0,
            Preset = "ultrafast",
            AudioMode = AudioExportMode.Copy,
            FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"),
            WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH")
        };
    }

    private static Task<VideoFrameInfo> FirstInfoAsync(string source)
    {
        using var decoder = FfmpegVideoDecoder.Open(source, 0);
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        return Task.FromResult(frame.Info);
    }

    private static Task<string> VideoHashAsync(string source)
    {
        return HashAsync(source, "0:v:0", ["-c:v", "rawvideo", "-pix_fmt", "yuv420p"]);
    }

    private static Task<string> AudioPacketHashAsync(string source)
    {
        return HashAsync(source, "0:a:0", ["-c:a", "copy"]);
    }

    private static async Task<string> HashAsync(string source, string stream, string[] codec)
    {
        var result = await MediaToolProcess.RunAsync(Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH")!,
            ["-v", "error", "-i", source, "-map", stream, .. codec, "-f", "hash", "-hash", "sha256", "-"],
            TimeSpan.FromSeconds(30), 65536, 65536, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        return result.StandardOutput;
    }
}
