using AegiNext.Testing.Media;
using System.Diagnostics.CodeAnalysis;
using AegiNext.Core.Media;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;
using AegiNext.Media.Tests.Decoding;

namespace AegiNext.Media.Tests.Preview;

[Collection(nameof(NativeDecoderTestGroup))]
public sealed class SdrVideoConverterTests
{
    [DecoderTheory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    [InlineData("bt709")]
    [Trait("Category", "DecoderIntegration")]
    public async Task ConvertsExplicitHdrAndSdrWithoutChangingNativePlanesOrMetadata(string transfer)
    {
        using var fixture = await DecoderFixture.CreateAsync(transfer);
        var initialConverters = SdrVideoConverter.LiveConverterCount;
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        using (var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex))
        using (var converter = new SdrVideoConverter())
        using (var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame()))
        {
            var info = frame.Info;
            var mastering = info.MasteringDisplay;
            var light = info.ContentLight;
            var planes = Enumerable.Range(0, info.PlaneCount).Select(frame.CopyPlane).ToArray();

            var converted = converter.Convert(frame);

            Assert.Equal(DecoderFixture.WIDTH, converted.Width);
            Assert.Equal(DecoderFixture.HEIGHT, converted.Height);
            AssertOpaqueImage(converted);
            Assert.Same(info, frame.Info);
            Assert.Same(mastering, frame.Info.MasteringDisplay);
            Assert.Same(light, frame.Info.ContentLight);
            Assert.Equal(transfer, frame.Info.Color.Transfer);
            for (var index = 0; index < planes.Length; index++)
            {
                Assert.Equal(planes[index], frame.CopyPlane(index));
            }

            var pixels = converted.Pixels.ToArray();
            using var next = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
            _ = converter.Convert(next);
            Assert.Equal(pixels, converted.Pixels.ToArray());
            Assert.Equal(pixels, converter.Convert(frame).Pixels.ToArray());
        }

        Assert.Equal(initialConverters, SdrVideoConverter.LiveConverterCount);
        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
    }

    [DecoderTheory]
    [InlineData(false, 32)]
    [InlineData(true, 16)]
    [Trait("Category", "DecoderIntegration")]
    public async Task Bt709PreviewFitsBoundsUsingDisplayAspectRatherThanCodedPixels(bool anamorphic, int expectedHeight)
    {
        using var fixture = await SdrPreviewFixture.CreateAsync(anamorphic);
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, SdrPreviewFixture.VIDEO_STREAM_INDEX);
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        using var converter = new SdrVideoConverter(new(48, 32));
        Assert.Equal(1, frame.Info.ColorPrimariesCode);
        Assert.Equal(1, frame.Info.ColorTransferCode);
        Assert.Equal(1, frame.Info.ColorMatrixCode);
        Assert.Equal(1, frame.Info.ColorRangeCode);
        Assert.Equal(new MediaRatio(anamorphic ? 2 : 1, 1), frame.Info.SampleAspectRatio);

        var converted = converter.Convert(frame);

        Assert.Equal(48, converted.Width);
        Assert.Equal(expectedHeight, converted.Height);
        AssertOpaqueImage(converted);
        Assert.Equal(SdrPreviewFixture.WIDTH, frame.Info.Width);
        Assert.Equal(SdrPreviewFixture.HEIGHT, frame.Info.Height);
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    [SuppressMessage("ReSharper", "DisposeOnUsingVariable", Justification = "Repeated disposal and conversion after owner disposal are the behavior under test; using declarations guarantee cleanup on failure.")]
    public async Task ConversionOwnsNoSourceFrameAndRemainsIndependentOfTheDecoderLifetime()
    {
        using var fixture = await DecoderFixture.CreateAsync();
        var initialConverters = SdrVideoConverter.LiveConverterCount;
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex);
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        using var converter = new SdrVideoConverter();
        Assert.Equal(initialConverters + 1, SdrVideoConverter.LiveConverterCount);
        decoder.Dispose();
        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());

        var converted = converter.Convert(frame);

        AssertOpaqueImage(converted);
        Assert.Equal(initialFrames + 1, FfmpegVideoDecoder.GetLiveFrameCount());
        converter.Dispose();
        converter.Dispose();
        Assert.Equal(initialConverters, SdrVideoConverter.LiveConverterCount);
        Assert.Throws<ObjectDisposedException>(() => converter.Convert(frame));
        Assert.NotEmpty(frame.CopyPlane(0));
        frame.Dispose();
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
        AssertOpaqueImage(converted);
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task PreCancellationDoesNotPoisonConverterOrTakeSourceOwnership()
    {
        using var fixture = await DecoderFixture.CreateAsync();
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex);
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        using var converter = new SdrVideoConverter();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        var original = frame.CopyPlane(0);

        var error = Assert.ThrowsAny<OperationCanceledException>(() => converter.Convert(frame, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
        Assert.Equal(original, frame.CopyPlane(0));
        AssertOpaqueImage(converter.Convert(frame));
        Assert.Throws<ArgumentNullException>(() => converter.Convert(null!));
    }

    internal static void AssertOpaqueImage(SdrVideoFrame frame)
    {
        var pixels = frame.Pixels.ToArray();
        Assert.Equal(frame.Width * frame.Height * 4, pixels.Length);
        Assert.NotEmpty(pixels);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            Assert.Equal(byte.MaxValue, pixels[index]);
        }

        Assert.Contains(pixels.Where((_, index) => index % 4 != 3), value => value != 0);
        Assert.Contains(pixels.Where((_, index) => index % 4 != 3), value => value != byte.MaxValue);
    }
}
