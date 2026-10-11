using AegiNext.Testing.Media;
using System.Runtime.InteropServices;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Media.Tests.Decoding;

[Collection(nameof(NativeDecoderTestGroup))]
public sealed class VideoHardwareDecoderTests
{
    [Fact]
    public void CoreExtensionLayoutsLeaveOriginalFrameAbiUntouched()
    {
        Assert.Equal(16, Marshal.SizeOf<NativeDecoderOptions>());
        Assert.Equal(320, Marshal.SizeOf<NativeDecoderSessionInfo>());
        Assert.Equal(40, Marshal.SizeOf<NativeResolvedColor>());
        Assert.Equal(64, Marshal.OffsetOf<NativeDecoderSessionInfo>("fallbackReason").ToInt32());
        Assert.Equal(600, Marshal.SizeOf<NativeDecodedFrameInfo>());
        Assert.Equal(0, (int)VideoDecodeMode.Auto);
        Assert.Equal(1, (int)VideoDecodeMode.Software);
        Assert.Equal(2, (int)VideoDecodeMode.Hardware);
        Assert.Equal(3, (int)VideoDecoderBackend.Vulkan);
    }

    /// <summary>比较可视样本及帧属性，允许 VT 已裁掉编码 padding 的真实下载布局；1080p 覆盖编码高度 1088。</summary>
    [DecoderTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Category", "DecoderIntegration")]
    public async Task AutoPreservesSamplesColorHdrFactsAndTimestampsThroughEofAndSeek(bool hevcTenBit, bool cropped)
    {
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        await AssertAutoParityAsync(hevcTenBit, cropped);
        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
    }

    [DecoderTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "DecoderIntegration")]
    public async Task SeekSelectionDownloadsOnlyTheChosenFrameAndPreservesTheNextFrame(bool hevcTenBit)
    {
        using var fixture = await HardwareDecoderFixture.CreateAsync(hevcTenBit);
        using var software = FfmpegVideoDecoder.Open(fixture.MediaPath, 0, new VideoDecoderOptions { Mode = VideoDecodeMode.Software });
        using var auto = FfmpegVideoDecoder.Open(fixture.MediaPath, 0);
        for (var index = 0; index < 3; index++)
        {
            software.ReadFrame()!.Dispose();
        }
        using var expected = software.ReadFrame()!;
        using var following = software.ReadFrame()!;
        var target = (expected.Info.PresentationTimestamp!.ToMediaTime() + following.Info.PresentationTimestamp!.ToMediaTime()) / 2;
        using var actual = auto.ReadFrameForSeek(target, default)!;
        AssertFrameFactsEqual(expected, actual);
        AssertSamplesEqual(expected, actual);
        Assert.Equal(1UL, auto.SessionInfo.DeliveredFrames);
        using var next = auto.ReadFrame()!;
        AssertFrameFactsEqual(following, next);
        AssertSamplesEqual(following, next);
        Assert.Equal(2UL, auto.SessionInfo.DeliveredFrames);
        auto.SeekToKeyFrame(MediaTime.Zero);
        software.SeekToKeyFrame(MediaTime.Zero);
        using var restarted = auto.ReadFrame()!;
        using var restartedExpected = software.ReadFrame()!;
        AssertFrameFactsEqual(restartedExpected, restarted);
        AssertSamplesEqual(restartedExpected, restarted);
    }

    private static async Task AssertAutoParityAsync(bool hevcTenBit, bool cropped)
    {
        using var fixture = await HardwareDecoderFixture.CreateAsync(hevcTenBit, cropped: cropped);
        using var software = FfmpegVideoDecoder.Open(fixture.MediaPath, 0, new VideoDecoderOptions { Mode = VideoDecodeMode.Software });
        using var auto = FfmpegVideoDecoder.Open(fixture.MediaPath, 0, new VideoDecoderOptions { Mode = VideoDecodeMode.Auto });
        var frames = 0;
        while (software.ReadFrame() is { } expected)
        {
            using (expected)
            using (var actual = Assert.IsType<DecodedVideoFrame>(auto.ReadFrame()))
            {
                AssertFrameFactsEqual(expected, actual);
                AssertSamplesEqual(expected, actual);
                if (hevcTenBit)
                {
                    Assert.NotNull(expected.Info.MasteringDisplay);
                    Assert.NotNull(actual.Info.MasteringDisplay);
                    Assert.NotNull(expected.Info.ContentLight);
                    Assert.NotNull(actual.Info.ContentLight);
                }
                frames++;
            }
        }
        Assert.Equal(6, frames);
        Assert.Null(auto.ReadFrame());
        Assert.Null(auto.ReadFrame());
        Assert.Equal(0UL, software.SessionInfo.DownloadNanoseconds);
        Assert.True(software.SessionInfo.DecodeNanoseconds > 0);
        Assert.Equal(6UL, auto.SessionInfo.DeliveredFrames);
        if (auto.SessionInfo.ActiveBackend != VideoDecoderBackend.Software)
        {
            Assert.True(auto.SessionInfo.HardwareConfirmed);
            Assert.True(auto.SessionInfo.DownloadNanoseconds > 0);
            Assert.Empty(auto.SessionInfo.FallbackReason);
        }
        else
        {
            Assert.False(auto.SessionInfo.HardwareConfirmed);
            Assert.NotEmpty(auto.SessionInfo.FallbackReason);
        }
        var generation = auto.SessionInfo.Generation;
        software.SeekToKeyFrame(MediaTime.Zero);
        auto.SeekToKeyFrame(MediaTime.Zero);
        using var restartedExpected = Assert.IsType<DecodedVideoFrame>(software.ReadFrame());
        using var restartedActual = Assert.IsType<DecodedVideoFrame>(auto.ReadFrame());
        AssertFrameFactsEqual(restartedExpected, restartedActual);
        AssertSamplesEqual(restartedExpected, restartedActual);
        Assert.True(auto.SessionInfo.Generation > generation);
        Assert.Equal(7UL, auto.SessionInfo.DeliveredFrames);
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task UnsupportedHardwareCodecFallsBackOnlyInAutoAndExplicitHardwareRejects()
    {
        using var fixture = await SdrPreviewFixture.CreateAsync(false);
        using var auto = FfmpegVideoDecoder.Open(fixture.MediaPath, SdrPreviewFixture.VIDEO_STREAM_INDEX, new VideoDecoderOptions { Mode = VideoDecodeMode.Auto });
        using var frame = Assert.IsType<DecodedVideoFrame>(auto.ReadFrame());
        Assert.Equal(VideoDecoderBackend.Software, auto.SessionInfo.ActiveBackend);
        Assert.NotEmpty(auto.SessionInfo.FallbackReason);
        Assert.Throws<NotSupportedException>(() => FfmpegVideoDecoder.Open(fixture.MediaPath,
            SdrPreviewFixture.VIDEO_STREAM_INDEX, new VideoDecoderOptions { Mode = VideoDecodeMode.Hardware }));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.ThrowsAny<OperationCanceledException>(() => FfmpegVideoDecoder.Open(fixture.MediaPath,
            SdrPreviewFixture.VIDEO_STREAM_INDEX, new VideoDecoderOptions { Mode = VideoDecodeMode.Auto }, cancelled.Token));
    }

    private static void AssertFrameFactsEqual(DecodedVideoFrame expected, DecodedVideoFrame actual)
    {
        Assert.Equal(expected.Info.PresentationTimestamp, actual.Info.PresentationTimestamp);
        Assert.Equal(expected.Info.BestEffortTimestamp, actual.Info.BestEffortTimestamp);
        Assert.Equal(expected.Info.DurationTicks, actual.Info.DurationTicks);
        Assert.Equal(expected.Info.SampleAspectRatio, actual.Info.SampleAspectRatio);
        Assert.Equal(expected.Info.Width - expected.Info.CropLeft - expected.Info.CropRight,
            actual.Info.Width - actual.Info.CropLeft - actual.Info.CropRight);
        Assert.Equal(expected.Info.Height - expected.Info.CropTop - expected.Info.CropBottom,
            actual.Info.Height - actual.Info.CropTop - actual.Info.CropBottom);
        Assert.Equal(expected.Info.ColorRangeCode, actual.Info.ColorRangeCode);
        Assert.Equal(expected.Info.ColorMatrixCode, actual.Info.ColorMatrixCode);
        Assert.Equal(expected.Info.ColorPrimariesCode, actual.Info.ColorPrimariesCode);
        Assert.Equal(expected.Info.ColorTransferCode, actual.Info.ColorTransferCode);
        Assert.Equal(expected.Info.ChromaLocationCode, actual.Info.ChromaLocationCode);
        Assert.Equal(expected.Info.MasteringDisplay, actual.Info.MasteringDisplay);
        Assert.Equal(expected.Info.ContentLight, actual.Info.ContentLight);
        Assert.Equal(expected.Info.ComponentDepths.ToArray(), actual.Info.ComponentDepths.ToArray());
    }

    private static void AssertSamplesEqual(DecodedVideoFrame expected, DecodedVideoFrame actual)
    {
        var tenBit = expected.Info.ComponentDepths[0] == 10;
        var packed = actual.Info.PixelFormat is "nv12" or "p010le";
        if (packed)
        {
            Assert.Equal(tenBit ? "p010le" : "nv12", actual.Info.PixelFormat);
        }
        else
        {
            Assert.Equal(expected.Info.PixelFormat, actual.Info.PixelFormat);
        }
        var bytesPerSample = tenBit ? 2 : 1;
        var visibleWidth = expected.Info.Width - expected.Info.CropLeft - expected.Info.CropRight;
        var visibleHeight = expected.Info.Height - expected.Info.CropTop - expected.Info.CropBottom;
        for (var plane = 0; plane < 3; plane++)
        {
            var original = expected.CopyPlane(plane);
            var result = actual.CopyPlane(packed && plane > 0 ? 1 : plane);
            var expectedPlane = expected.GetPlaneInfo(plane);
            var actualPlane = actual.GetPlaneInfo(packed && plane > 0 ? 1 : plane);
            var subsample = plane == 0 ? 1 : 2;
            var width = visibleWidth / subsample;
            var height = visibleHeight / subsample;
            var expectedLeft = expected.Info.CropLeft / subsample;
            var expectedTop = expected.Info.CropTop / subsample;
            var actualLeft = actual.Info.CropLeft / subsample;
            var actualTop = actual.Info.CropTop / subsample;
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    var expectedOffset = (row + expectedTop) * expectedPlane.RowBytes + (column + expectedLeft) * bytesPerSample;
                    var actualSample = packed && plane > 0 ? (column + actualLeft) * 2 + plane - 1 : column + actualLeft;
                    var actualOffset = (row + actualTop) * actualPlane.RowBytes + actualSample * bytesPerSample;
                    var expectedValue = tenBit ? BitConverter.ToUInt16(original, expectedOffset) : original[expectedOffset];
                    var actualValue = tenBit ? BitConverter.ToUInt16(result, actualOffset) >> (packed ? 6 : 0) : result[actualOffset];
                    Assert.Equal(expectedValue, actualValue);
                }
            }
        }
    }
}
