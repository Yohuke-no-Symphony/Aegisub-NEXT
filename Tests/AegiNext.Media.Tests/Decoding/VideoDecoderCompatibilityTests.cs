using AegiNext.Testing.Media;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Media.Tests.Decoding;

/// <summary>验证常见编码通过实际 Auto 后端保留原始帧、定位与预览行为。</summary>
[Collection(nameof(NativeDecoderTestGroup))]
public sealed class VideoDecoderCompatibilityTests
{
    /// <summary>使用实际解码输出格式定义兼容矩阵，ProRes 4444 的编码输入为十位、解码输出为十二位。</summary>
    public static TheoryData<string, string, string> Inputs => new()
    {
        { "h264-ten-bit", "h264", "yuv420p10le" },
        { "h264-rgb", "h264", "gbrp" },
        { "hevc422-ten-bit", "hevc", "yuv422p10le" },
        { "hevc444-ten-bit", "hevc", "yuv444p10le" },
        { "vp8", "vp8", "yuv420p" },
        { "vp9-ten-bit", "vp9", "yuv420p10le" },
        { "av1-ten-bit", "av1", "yuv420p10le" },
        { "av1-film-grain-ten-bit", "av1", "yuv420p10le" },
        { "mpeg2", "mpeg2video", "yuv420p" },
        { "mpeg4", "mpeg4", "yuv420p" },
        { "mjpeg", "mjpeg", "yuvj422p" },
        { "prores422-hq", "prores", "yuv422p10le" },
        { "prores422-proxy", "prores", "yuv422p10le" },
        { "prores422-lt", "prores", "yuv422p10le" },
        { "prores422", "prores", "yuv422p10le" },
        { "prores4444", "prores", "yuv444p12le" },
        { "prores4444-alpha", "prores", "yuva444p12le" },
        { "prores4444-xq", "prores", "yuv444p12le" },
        { "prores4444-xq-alpha", "prores", "yuva444p12le" },
        { "ffv1-sixteen-bit", "ffv1", "yuv444p16le" }
    };

    /// <summary>逐帧与独立 FFmpeg 参考比较源采样和时间事实，核验 EOF、用户定位、下一帧及确定不支持的 GPU 格式。</summary>
    [DecoderTheory]
    [MemberData(nameof(Inputs))]
    [Trait("Category", "DecoderIntegration")]
    public async Task SoftwareAndAutoPreserveReferenceFramesThroughEofAndSeek(string name, string codec, string pixelFormat)
    {
        using var fixture = await CreateFixtureAsync(name, codec, pixelFormat);
        var resources = NativeResources();
        try
        {
            if (codec == "ffv1")
            {
                Assert.Throws<NotSupportedException>(() =>
                {
                    using var hardware = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex,
                        new VideoDecoderOptions { Mode = VideoDecodeMode.Hardware });
                });
            }
            foreach (var mode in new[] { VideoDecodeMode.Software, VideoDecodeMode.Auto })
            {
                using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = mode });
                AssertSequence(fixture, decoder);
                AssertSession(decoder, mode);
                if (codec == "ffv1")
                {
                    Assert.Equal(VideoDecoderBackend.Software, decoder.SessionInfo.ActiveBackend);
                }
                AssertSeekAndNextFrame(fixture, mode);
            }
        }
        finally
        {
            Assert.Equal(resources, NativeResources());
        }
    }

    /// <summary>常见格式须产生源采样保真的可显示预览，转换不得修改包括真实渐变透明度在内的任何原始平面。</summary>
    [DecoderTheory]
    [MemberData(nameof(Inputs))]
    [Trait("Category", "DecoderIntegration")]
    public async Task SoftwareAndAutoPreviewEveryFrameWithoutChangingSourcePlanes(string name, string codec, string pixelFormat)
    {
        using var fixture = await CreateFixtureAsync(name, codec, pixelFormat);
        var resources = NativeResources();
        try
        {
            using var software = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = VideoDecodeMode.Software });
            using var automatic = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = VideoDecodeMode.Auto });
            using var converter = new SdrVideoConverter(new(64, 64));
            for (var index = 0; index < VideoCompatibilityFixture.FRAME_COUNT; index++)
            {
                using var expected = Assert.IsType<DecodedVideoFrame>(software.ReadFrame());
                using var actual = Assert.IsType<DecodedVideoFrame>(automatic.ReadFrame());
                AssertReferenceFrame(fixture, expected, index);
                AssertReferenceFrame(fixture, actual, index, allowHardwareLayout: true);
                AssertFrameFacts(expected, actual);
                var originalPlanes = Enumerable.Range(0, actual.Info.PlaneCount).Select(actual.CopyPlane).ToArray();
                var expectedPreview = converter.Convert(expected);
                var actualPreview = converter.Convert(actual);
                Assert.Equal(64, actualPreview.Width);
                Assert.Equal(64, actualPreview.Height);
                Assert.Equal(64 * 64 * 4, actualPreview.Pixels.Length);
                if (automatic.SessionInfo.HardwareConfirmed)
                {
                    AssertQuantizedPreview(expectedPreview.Pixels.Span, actualPreview.Pixels.Span);
                }
                else
                {
                    Assert.Equal(expectedPreview.Pixels.ToArray(), actualPreview.Pixels.ToArray());
                }
                AssertReferenceFrame(fixture, expected, index);
                AssertReferenceFrame(fixture, actual, index, allowHardwareLayout: true);
                for (var plane = 0; plane < originalPlanes.Length; plane++)
                {
                    Assert.Equal(originalPlanes[plane], actual.CopyPlane(plane));
                }
            }
            Assert.Null(software.ReadFrame());
            Assert.Null(automatic.ReadFrame());
            AssertSession(software, VideoDecodeMode.Software);
            AssertSession(automatic, VideoDecodeMode.Auto);
        }
        finally
        {
            Assert.Equal(resources, NativeResources());
        }
    }

    /// <summary>AVI 中缺失的原始 PTS 必须保持缺失，同时保留可用 best-effort 时间和全部 B 帧像素。</summary>
    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task Mpeg4AviPreservesMissingRawPtsAndAvailableBestEffortTimestamps()
    {
        using var fixture = await VideoCompatibilityFixture.CreateAsync("mpeg4-avi", "mpeg4", "yuv420p");
        AssertMissingPtsWithAvailableBestEffort(fixture);
        var resources = NativeResources();
        try
        {
            foreach (var mode in new[] { VideoDecodeMode.Software, VideoDecodeMode.Auto })
            {
                using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = mode });
                AssertSequence(fixture, decoder);
                AssertSession(decoder, mode);
            }
        }
        finally
        {
            Assert.Equal(resources, NativeResources());
        }
    }

    /// <summary>用户应能按可用显示时间定位 AVI B 帧，保留选中帧之后的下一帧。</summary>
    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task Mpeg4AviCanSeekAcrossGopsWhenRawPtsIsMissing()
    {
        using var fixture = await VideoCompatibilityFixture.CreateAsync("mpeg4-avi", "mpeg4", "yuv420p");
        AssertMissingPtsWithAvailableBestEffort(fixture);
        var resources = NativeResources();
        try
        {
            foreach (var mode in new[] { VideoDecodeMode.Software, VideoDecodeMode.Auto })
            {
                AssertSeekAndNextFrame(fixture, mode, verifyEof: false);
            }
        }
        finally
        {
            Assert.Equal(resources, NativeResources());
        }
    }

    private static void AssertSequence(VideoCompatibilityFixture fixture, FfmpegVideoDecoder decoder)
    {
        for (var index = 0; index < VideoCompatibilityFixture.FRAME_COUNT; index++)
        {
            using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
            AssertReferenceFrame(fixture, frame, index, decoder.SessionInfo.RequestedMode != VideoDecodeMode.Software);
        }
        Assert.Null(decoder.ReadFrame());
        Assert.Null(decoder.ReadFrame());
        Assert.Equal((ulong)VideoCompatibilityFixture.FRAME_COUNT, decoder.SessionInfo.DeliveredFrames);
    }

    private static void AssertSeekAndNextFrame(VideoCompatibilityFixture fixture, VideoDecodeMode mode, bool verifyEof = true)
    {
        using var navigator = new VideoFrameNavigator(token => FfmpegVideoDecoder.Open(fixture.MediaPath,
            fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = mode }, token), maximumCachedFrames: 0);
        if (verifyEof)
        {
            using var tail = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(fixture.GetFrameTime(11) + new MediaTime(1)));
            Assert.True(tail.ReachedEnd);
            Assert.Null(tail.NextFrameTime);
            AssertReferenceFrame(fixture, tail.Frame, 11, mode != VideoDecodeMode.Software);
            Assert.Null(navigator.ReadFrame());
        }
        var target = (fixture.GetFrameTime(8) + fixture.GetFrameTime(9)) / 2;
        using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(target));
        Assert.Equal(fixture.GetFrameTime(8), selected.Time);
        Assert.Equal(fixture.GetFrameTime(9), selected.NextFrameTime);
        AssertReferenceFrame(fixture, selected.Frame, 8, mode != VideoDecodeMode.Software);
        using var next = Assert.IsType<PositionedVideoFrame>(navigator.ReadFrame());
        Assert.Equal(fixture.GetFrameTime(9), next.Time);
        AssertReferenceFrame(fixture, next.Frame, 9, mode != VideoDecodeMode.Software);
    }

    private static void AssertReferenceFrame(VideoCompatibilityFixture fixture, IVideoFrame frame, int index, bool allowHardwareLayout = false)
    {
        if (!allowHardwareLayout)
        {
            Assert.Equal(fixture.PixelFormat, frame.Info.PixelFormat);
        }
        Assert.Equal(fixture.Width, frame.Info.Width);
        Assert.Equal(fixture.Height, frame.Info.Height);
        Assert.Equal(fixture.TimeBase, frame.Info.StreamTimeBase);
        Assert.False(frame.Info.IsCorrupt);
        Assert.False(frame.Info.IsInterlaced);
        Assert.Equal(0u, frame.Info.DecodeErrorFlags);
        AssertTimestamp(fixture.ExpectedFrames[index], "pts", frame.Info.PresentationTimestamp, fixture.TimeBase);
        AssertTimestamp(fixture.ExpectedFrames[index], "best_effort_timestamp", frame.Info.BestEffortTimestamp, fixture.TimeBase);
        var planeOffset = 0;
        for (var planeIndex = 0; planeIndex < frame.Info.PlaneCount; planeIndex++)
        {
            var plane = frame.GetPlaneInfo(planeIndex);
            Assert.True(plane.RowBytes > 0 && plane.Height > 0);
            Assert.True(Math.Abs((long)plane.SourceStride) >= plane.RowBytes);
            var pixels = frame.CopyPlane(planeIndex);
            Assert.Equal(checked(plane.RowBytes * plane.Height), pixels.Length);
            planeOffset += pixels.Length;
        }
        VideoHardwareFrameAssertions.AssertReferenceSamples(fixture, frame, index, allowProResRounding: allowHardwareLayout);
        if (frame.Info.PixelFormat == fixture.PixelFormat)
        {
            Assert.Equal(fixture.FrameByteCount, planeOffset);
        }
        if (frame.Info.PixelFormat == "yuva444p12le")
        {
            Assert.Equal(4, frame.Info.PlaneCount);
            var alpha = frame.CopyPlane(3);
            var values = Enumerable.Range(0, alpha.Length / 2).Select(sample => BitConverter.ToUInt16(alpha, sample * 2)).ToArray();
            var maximum = values.Max();
            Assert.Equal((ushort)0, values.Min());
            Assert.True(maximum > 0);
            Assert.Contains(values, value => value > 0 && value < maximum);
        }
    }

    private static void AssertTimestamp(JsonElement reference, string name, MediaTimestamp? actual, MediaTimeBase timeBase)
    {
        if (reference.TryGetProperty(name, out var expected))
        {
            Assert.Equal(new MediaTimestamp(expected.GetInt64(), timeBase), actual);
        }
        else
        {
            Assert.Null(actual);
        }
    }

    private static void AssertMissingPtsWithAvailableBestEffort(VideoCompatibilityFixture fixture)
    {
        Assert.Contains(fixture.ExpectedFrames.EnumerateArray(), frame => !frame.TryGetProperty("pts", out _) &&
            frame.TryGetProperty("best_effort_timestamp", out _));
    }

    private static void AssertSession(FfmpegVideoDecoder decoder, VideoDecodeMode mode)
    {
        Assert.Equal(mode, decoder.SessionInfo.RequestedMode);
        if (decoder.SessionInfo.ActiveBackend == VideoDecoderBackend.Software)
        {
            Assert.False(decoder.SessionInfo.HardwareConfirmed);
            if (mode == VideoDecodeMode.Auto)
            {
                Assert.NotEmpty(decoder.SessionInfo.FallbackReason);
            }
            else
            {
                Assert.Empty(decoder.SessionInfo.FallbackReason);
            }
        }
        else
        {
            Assert.Equal(VideoDecodeMode.Auto, mode);
            Assert.Contains(decoder.SessionInfo.ActiveBackend, new[] { VideoDecoderBackend.VideoToolbox, VideoDecoderBackend.D3D11VA, VideoDecoderBackend.Vulkan });
            Assert.True(decoder.SessionInfo.HardwareConfirmed);
            Assert.Empty(decoder.SessionInfo.FallbackReason);
        }
    }

    private static Task<VideoCompatibilityFixture> CreateFixtureAsync(string name, string codec, string pixelFormat)
    {
        return VideoCompatibilityFixture.CreateAsync(name, codec, pixelFormat, codec == "prores" ? 256 : VideoCompatibilityFixture.WIDTH);
    }

    private static void AssertFrameFacts(DecodedVideoFrame expected, DecodedVideoFrame actual)
    {
        Assert.Equal(expected.Info.PresentationTimestamp, actual.Info.PresentationTimestamp);
        Assert.Equal(expected.Info.BestEffortTimestamp, actual.Info.BestEffortTimestamp);
        Assert.Equal(expected.Info.DisplayTiming, actual.Info.DisplayTiming);
        Assert.Equal(expected.Info.DurationTicks, actual.Info.DurationTicks);
        Assert.Equal(expected.Info.RawFrameTimeBase, actual.Info.RawFrameTimeBase);
        Assert.Equal(expected.Info.SampleAspectRatio, actual.Info.SampleAspectRatio);
        Assert.Equal(expected.Info.CropLeft, actual.Info.CropLeft);
        Assert.Equal(expected.Info.CropRight, actual.Info.CropRight);
        Assert.Equal(expected.Info.CropTop, actual.Info.CropTop);
        Assert.Equal(expected.Info.CropBottom, actual.Info.CropBottom);
        Assert.Equal(expected.Info.Color, actual.Info.Color);
        Assert.Equal(expected.Info.AlphaModeCode, actual.Info.AlphaModeCode);
        Assert.Equal(expected.Info.MasteringDisplay, actual.Info.MasteringDisplay);
        Assert.Equal(expected.Info.ContentLight, actual.Info.ContentLight);
        Assert.Equal(expected.Info.SideDataTypes.ToArray(), actual.Info.SideDataTypes.ToArray());
    }

    private static void AssertQuantizedPreview(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        var maximum = 0;
        var total = 0L;
        for (var sample = 0; sample < expected.Length; sample++)
        {
            var error = Math.Abs(expected[sample] - actual[sample]);
            maximum = Math.Max(maximum, error);
            total += error;
            if (sample % 4 == 3)
            {
                Assert.Equal(byte.MaxValue, actual[sample]);
            }
        }
        Assert.True(maximum <= 3, $"Hardware SDR preview maximum byte error {maximum}.");
        Assert.True(total / (double)expected.Length <= 0.3, $"Hardware SDR preview mean byte error {total / (double)expected.Length}.");
    }

    private static (uint Decoders, uint Frames, uint Converters) NativeResources()
    {
        return (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount);
    }
}
