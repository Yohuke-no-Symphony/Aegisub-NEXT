using AegiNext.Testing.Media;
using System.Security.Cryptography;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Export;
using AegiNext.Media.Preview;
using AegiNext.Media.Probing;

namespace AegiNext.Export.Tests;

/// <summary>常见源通过实际 Auto 后端与独立 worker 完成高精度压制，透明源仍可叠加前景。</summary>
[Collection(nameof(NativeExportTestGroup))]
public sealed class VideoCompatibilityExportTests
{
    /// <summary>核验真实输入、全部帧时间、Auto/CPU 源采样保真、输出格式与 Alpha 黑底和前景合成。</summary>
    [ExportTheory]
    [InlineData("prores422-hq", "prores", "yuv422p10le", VideoCodec.H264)]
    [InlineData("prores4444-alpha", "prores", "yuva444p12le", VideoCodec.H264)]
    [InlineData("prores4444-xq-alpha", "prores", "yuva444p12le", VideoCodec.Hevc)]
    [InlineData("mpeg4-avi", "mpeg4", "yuv420p", VideoCodec.H264)]
    [InlineData("av1-ten-bit", "av1", "yuv420p10le", VideoCodec.H264)]
    [InlineData("av1-film-grain-ten-bit", "av1", "yuv420p10le", VideoCodec.H264)]
    public async Task WorkerExportsAllFramesWithSourceTimingAndMatchingSoftwareAndAutoPixels(
        string name, string codecName, string sourceFormat, VideoCodec outputCodec)
    {
        using var fixture = await VideoCompatibilityFixture.CreateAsync(name, codecName, sourceFormat,
            codecName == "prores" ? 256 : VideoCompatibilityFixture.WIDTH);
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(fixture.MediaPath));
        var directory = Directory.CreateTempSubdirectory("aeginext-compatibility-export-").FullName;
        var alpha = sourceFormat.StartsWith("yuva", StringComparison.Ordinal);
        var resources = Resources();
        var referencePlanes = new List<byte[][]>();
        try
        {
            var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: fixture.MediaPath);
            var project = new ProjectDocument
            {
                Width = fixture.Width, Height = fixture.Height,
                Assets = [asset], Media = new(asset.Id, fixture.VideoStreamIndex, null, MediaTime.Zero),
                Layers = alpha ? [new()
                {
                    Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 12, 12),
                    Transform = new(X: 8, Y: 8), Fill = SceneColor.White, End = new(1)
                }] : []
            };
            foreach (var mode in new[] { VideoDecodeMode.Software, VideoDecodeMode.Auto })
            {
                var output = Path.Combine(directory, mode + ".mkv");
                var request = new VideoExportRequest(project, directory, output)
                {
                    Codec = outputCodec, DecodeMode = mode, EncodingMode = VideoEncodingMode.SOFTWARE,
                    AudioMode = AudioExportMode.None, Crf = 0, Preset = "ultrafast",
                    FfmpegPath = Environment.GetEnvironmentVariable("AEGINEXT_FFMPEG_PATH"),
                    WorkerPath = Environment.GetEnvironmentVariable("AEGINEXT_EXPORT_WORKER_PATH")
                };
                var result = await new VideoExporter().ExportAsync(request).WaitAsync(TimeSpan.FromSeconds(60));
                Assert.Equal((ulong)VideoCompatibilityFixture.FRAME_COUNT, result.Frames);
                Assert.Equal(outputCodec == VideoCodec.H264 ? "libx264" : "libx265", result.Encoder);
                Assert.Equal(mode, result.Decoder!.RequestedMode);
                var requiredHardware = Environment.GetEnvironmentVariable("AEGINEXT_REQUIRED_HARDWARE_CODECS")?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(codecName, StringComparer.Ordinal) == true;
                if (mode == VideoDecodeMode.Auto && requiredHardware)
                {
                    Assert.True(result.Decoder.HardwareConfirmed,
                        $"{codecName}: worker must confirm hardware decoding on this validation host; fallback={result.Decoder.FallbackReason}");
                }
                if (result.Decoder.ActiveBackend == VideoDecoderBackend.Software)
                {
                    Assert.False(result.Decoder.HardwareConfirmed);
                    if (mode == VideoDecodeMode.Auto)
                    {
                        Assert.NotEmpty(result.Decoder.FallbackReason);
                    }
                    else
                    {
                        Assert.Empty(result.Decoder.FallbackReason);
                    }
                }
                else
                {
                    Assert.Equal(VideoDecodeMode.Auto, mode);
                    Assert.Contains(result.Decoder.ActiveBackend, new[] { VideoDecoderBackend.VideoToolbox, VideoDecoderBackend.D3D11VA, VideoDecoderBackend.Vulkan });
                    Assert.True(result.Decoder.HardwareConfirmed);
                    Assert.Empty(result.Decoder.FallbackReason);
                }
                var probe = new FfprobeMediaProbe(new(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!));
                var report = await probe.ProbeAsync(output);
                var video = Assert.Single(report.Asset.Streams.Where(stream => stream.Video is not null));
                Assert.Equal(outputCodec == VideoCodec.H264 ? "h264" : "hevc", video.CodecName);
                Assert.Equal(outputCodec == VideoCodec.H264 ? "yuv420p" : "yuv420p10le", video.Video!.PixelFormat);
                using var source = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, options: new() { Mode = VideoDecodeMode.Software });
                using var encoded = FfmpegVideoDecoder.Open(output, video.Index, options: new() { Mode = VideoDecodeMode.Software });
                using var converter = new SdrVideoConverter(new(fixture.Width, fixture.Height));
                for (var index = 0; index < VideoCompatibilityFixture.FRAME_COUNT; index++)
                {
                    using var raw = Assert.IsType<DecodedVideoFrame>(source.ReadFrame());
                    using var frame = Assert.IsType<DecodedVideoFrame>(encoded.ReadFrame());
                    var sourceTime = raw.Info.DisplayTiming!.Timestamp.ToMediaTime();
                    var outputTime = frame.Info.DisplayTiming!.Timestamp.ToMediaTime();
                    var difference = outputTime - sourceTime;
                    Assert.True(difference >= new MediaTime(-1, 1000) && difference <= new MediaTime(1, 1000),
                        $"Frame {index}: export time {outputTime} differs from source time {sourceTime}.");
                    if (name == "mpeg4-avi" && index == VideoCompatibilityFixture.FRAME_COUNT - 1)
                    {
                        Assert.Null(raw.Info.PresentationTimestampValue);
                        Assert.Null(raw.Info.BestEffortTimestampValue);
                        Assert.True(raw.Info.DisplayTiming.IsDerived);
                        Assert.Equal(new MediaTime(12, 25), sourceTime);
                    }
                    var planes = Enumerable.Range(0, frame.Info.PlaneCount).Select(frame.CopyPlane).ToArray();
                    if (mode == VideoDecodeMode.Software)
                    {
                        referencePlanes.Add(planes);
                    }
                    else
                    {
                        Assert.Equal(referencePlanes[index].Length, planes.Length);
                        for (var plane = 0; plane < planes.Length; plane++)
                        {
                            if (result.Decoder.HardwareConfirmed)
                            {
                                AssertQuantizedSamples(referencePlanes[index][plane], planes[plane], outputCodec);
                            }
                            else
                            {
                                Assert.Equal(referencePlanes[index][plane], planes[plane]);
                            }
                        }
                    }
                    if (alpha)
                    {
                        Assert.Equal(4, raw.Info.PlaneCount);
                        var pixels = converter.Convert(frame).Pixels.ToArray();
                        var white = (8 * fixture.Width + 8) * 4;
                        for (var channel = 0; channel < 3; channel++)
                        {
                            Assert.InRange(pixels[white + channel], 245, 255);
                            Assert.InRange(pixels[(80 * fixture.Width) * 4 + channel], 0, 12);
                        }
                        var before = Enumerable.Range(0, raw.Info.PlaneCount).SelectMany(raw.CopyPlane).ToArray();
                        _ = converter.Convert(raw);
                        Assert.Equal(before, Enumerable.Range(0, raw.Info.PlaneCount).SelectMany(raw.CopyPlane).ToArray());
                    }
                }
                Assert.Null(source.ReadFrame());
                Assert.Null(encoded.ReadFrame());
                Assert.Empty(Directory.GetDirectories(directory, ".aeginext-export-*"));
            }
            Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(fixture.MediaPath)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Assert.Equal(resources, Resources());
        }
    }

    private static (uint Decoders, uint Frames, uint Converters) Resources()
    {
        return (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount);
    }

    private static void AssertQuantizedSamples(byte[] expected, byte[] actual, VideoCodec codec)
    {
        Assert.Equal(expected.Length, actual.Length);
        var bytesPerSample = codec == VideoCodec.Hevc ? 2 : 1;
        Assert.Equal(0, expected.Length % bytesPerSample);
        var maximum = 0;
        var total = 0L;
        for (var offset = 0; offset < expected.Length; offset += bytesPerSample)
        {
            var expectedSample = bytesPerSample == 1 ? expected[offset] : BitConverter.ToUInt16(expected, offset);
            var actualSample = bytesPerSample == 1 ? actual[offset] : BitConverter.ToUInt16(actual, offset);
            var error = Math.Abs(expectedSample - actualSample);
            maximum = Math.Max(maximum, error);
            total += error;
        }
        var sampleCount = expected.Length / bytesPerSample;
        var mean = total / (double)sampleCount;
        Assert.True(maximum <= (bytesPerSample == 1 ? 2 : 8), $"Hardware-decoded {codec} export maximum sample error {maximum}.");
        Assert.True(mean <= (bytesPerSample == 1 ? 0.25 : 1), $"Hardware-decoded {codec} export mean sample error {mean}.");
    }
}
