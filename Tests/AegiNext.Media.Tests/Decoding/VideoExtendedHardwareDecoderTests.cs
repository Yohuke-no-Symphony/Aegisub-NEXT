using AegiNext.Testing.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Media.Tests.Decoding;

/// <summary>验证扩展硬件格式的实际加速、精度、色度、透明度与定位，不把 VideoToolbox 软件会话当成硬件。</summary>
[Collection(nameof(NativeDecoderTestGroup))]
public sealed class VideoExtendedHardwareDecoderTests
{
    /// <summary>不同容器中的高位深编码及 ProRes profile 必须按实际设备能力协商。</summary>
    public static TheoryData<string, string, string> Inputs => new()
    {
        { "hevc422-ten-bit", "hevc", "yuv422p10le" },
        { "hevc444-ten-bit", "hevc", "yuv444p10le" },
        { "vp9-ten-bit", "vp9", "yuv420p10le" },
        { "av1-ten-bit", "av1", "yuv420p10le" },
        { "av1-film-grain-ten-bit", "av1", "yuv420p10le" },
        { "prores422-proxy", "prores", "yuv422p10le" },
        { "prores422-lt", "prores", "yuv422p10le" },
        { "prores422", "prores", "yuv422p10le" },
        { "prores422-hq", "prores", "yuv422p10le" },
        { "prores4444", "prores", "yuv444p12le" },
        { "prores4444-alpha", "prores", "yuva444p12le" },
        { "prores4444-xq", "prores", "yuv444p12le" },
        { "prores4444-xq-alpha", "prores", "yuva444p12le" }
    };

    /// <summary>支持时严格模式必须确认真实硬件并保留源样本；设备验收开关禁止意外回退掩盖硬件回归。</summary>
    [DecoderTheory]
    [MemberData(nameof(Inputs))]
    [Trait("Category", "DecoderIntegration")]
    public async Task HardwarePreservesSourceSamplesPreviewEofAndSeek(string name, string codec, string pixelFormat)
    {
        using var fixture = await VideoCompatibilityFixture.CreateAsync(name, codec, pixelFormat,
            size: codec == "prores" ? 256 : VideoCompatibilityFixture.WIDTH);
        var resources = (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount);
        try
        {
            using var automatic = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex);
            using var first = Assert.IsType<DecodedVideoFrame>(automatic.ReadFrame());
            var session = automatic.SessionInfo;
            var requiredCodecs = (Environment.GetEnvironmentVariable("AEGINEXT_REQUIRED_HARDWARE_CODECS") ?? "").Split(',');
            if (requiredCodecs.Contains(codec, StringComparer.Ordinal))
            {
                Assert.True(session.HardwareConfirmed, $"{name}: {session.FallbackReason}");
            }
            if (session.ActiveBackend == VideoDecoderBackend.Software)
            {
                Assert.False(session.HardwareConfirmed);
                Assert.NotEmpty(session.FallbackReason);
                var failure = Record.Exception(() =>
                {
                    using var unsupported = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex,
                        new VideoDecoderOptions { Mode = VideoDecodeMode.Hardware });
                    using var frame = unsupported.ReadFrame();
                });
                Assert.True(failure is NotSupportedException or InvalidDataException, failure?.ToString());
                return;
            }

            using var hardware = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex,
                new VideoDecoderOptions { Mode = VideoDecodeMode.Hardware });
            using var converter = new SdrVideoConverter(new(64, 64));
            for (var index = 0; index < VideoCompatibilityFixture.FRAME_COUNT; index++)
            {
                using var frame = Assert.IsType<DecodedVideoFrame>(hardware.ReadFrame());
                AssertFrame(fixture, frame, index);
                var before = Enumerable.Range(0, frame.Info.PlaneCount).SelectMany(frame.CopyPlane).ToArray();
                var preview = converter.Convert(frame);
                Assert.Equal(64 * 64 * 4, preview.Pixels.Length);
                Assert.Equal(before, Enumerable.Range(0, frame.Info.PlaneCount).SelectMany(frame.CopyPlane).ToArray());
            }
            Assert.Null(hardware.ReadFrame());
            Assert.Null(hardware.ReadFrame());
            Assert.True(hardware.SessionInfo.HardwareConfirmed);
            Assert.Equal(session.ActiveBackend, hardware.SessionInfo.ActiveBackend);
            Assert.Empty(hardware.SessionInfo.FallbackReason);
            Assert.True(hardware.SessionInfo.DownloadNanoseconds > 0);
            Assert.Equal(12UL, hardware.SessionInfo.DeliveredFrames);
            var generation = hardware.SessionInfo.Generation;
            hardware.SeekToKeyFrame(fixture.GetFrameTime(8));
            using var selected = Assert.IsType<DecodedVideoFrame>(hardware.ReadFrameForSeek(
                (fixture.GetFrameTime(8) + fixture.GetFrameTime(9)) / 2, default));
            AssertFrame(fixture, selected, 8);
            using var next = Assert.IsType<DecodedVideoFrame>(hardware.ReadFrame());
            AssertFrame(fixture, next, 9);
            Assert.Equal(14UL, hardware.SessionInfo.DeliveredFrames);
            Assert.True(hardware.SessionInfo.Generation > generation);
            hardware.SeekToKeyFrame(MediaTime.Zero);
            using var restarted = Assert.IsType<DecodedVideoFrame>(hardware.ReadFrame());
            AssertFrame(fixture, restarted, 0);
        }
        finally
        {
            Assert.Equal(resources, (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount));
        }
    }

    private static void AssertFrame(VideoCompatibilityFixture fixture, DecodedVideoFrame frame, int index)
    {
        Assert.Equal(fixture.Width, frame.Info.Width);
        Assert.Equal(fixture.Height, frame.Info.Height);
        Assert.Equal(fixture.GetFrameTime(index), frame.Info.DisplayTiming!.Timestamp.ToMediaTime());
        Assert.False(frame.Info.IsCorrupt);
        Assert.Equal(0u, frame.Info.DecodeErrorFlags);
        Assert.DoesNotContain("Film grain parameters", frame.Info.SideDataTypes);
        VideoHardwareFrameAssertions.AssertReferenceSamples(fixture, frame, index, allowProResRounding: true);
    }
}
