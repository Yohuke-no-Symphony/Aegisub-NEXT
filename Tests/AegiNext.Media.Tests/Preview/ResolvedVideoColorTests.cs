using AegiNext.Testing.Media;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;
using AegiNext.Media.Tests.Decoding;

namespace AegiNext.Media.Tests.Preview;

[Collection(nameof(NativeDecoderTestGroup))]
public sealed class ResolvedVideoColorTests
{
    [DecoderTheory]
    [InlineData("smpte2084", 16)]
    [InlineData("arib-std-b67", 18)]
    [InlineData("bt709", 1)]
    [Trait("Category", "DecoderIntegration")]
    public async Task PreservesExplicitColorAndRawHdrFactsInSharedNativeResolution(string transfer, int transferCode)
    {
        using var fixture = await DecoderFixture.CreateAsync(transfer);
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = VideoDecodeMode.Software });
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        var raw = frame.Info;
        var planes = Enumerable.Range(0, raw.PlaneCount).Select(frame.CopyPlane).ToArray();

        var color = ResolvedVideoColor.Resolve(frame);

        Assert.Equal(1, color.Range);
        Assert.Equal(9, color.Matrix);
        Assert.Equal(9, color.Primaries);
        Assert.Equal(transferCode, color.Transfer);
        Assert.Equal(1, color.ChromaLocation);
        Assert.Equal(0u, color.InferredFields);
        Assert.Same(raw, frame.Info);
        Assert.Same(raw.MasteringDisplay, frame.Info.MasteringDisplay);
        Assert.Same(raw.ContentLight, frame.Info.ContentLight);
        for (var index = 0; index < planes.Length; index++)
        {
            Assert.Equal(planes[index], frame.CopyPlane(index));
        }
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task UntaggedH264UsesSharedSdrPolicyWithoutMutatingRawFacts()
    {
        using var fixture = await HardwareDecoderFixture.CreateAsync(hevcTenBit: false, tagged: false);
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, 0, new VideoDecoderOptions { Mode = VideoDecodeMode.Software });
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        var raw = frame.Info;
        var original = frame.CopyPlane(0);
        Assert.Equal(2, raw.ColorMatrixCode);
        Assert.Equal(2, raw.ColorPrimariesCode);
        Assert.Equal(2, raw.ColorTransferCode);

        var color = ResolvedVideoColor.Resolve(frame);
        using var converter = new SdrVideoConverter(new(160, 90));
        var converted = converter.Convert(frame);

        Assert.Equal(1, color.Range);
        Assert.Equal(1, color.Matrix);
        Assert.Equal(1, color.Primaries);
        Assert.Equal(1, color.Transfer);
        Assert.Equal(14u, color.InferredFields & 14u);
        Assert.Same(raw, frame.Info);
        Assert.Equal(original, frame.CopyPlane(0));
        Assert.Equal(160, converted.Width);
        Assert.Equal(90, converted.Height);
    }

    [DecoderTheory]
    [InlineData(VideoDecodeMode.Software)]
    [InlineData(VideoDecodeMode.Auto)]
    [Trait("Category", "DecoderIntegration")]
    public async Task RealH264FilmGrainRemainsExplicitlyUnsupportedBeforeAnySdrDefault(VideoDecodeMode mode)
    {
        using var fixture = await FilmGrainDecoderFixture.CreateAsync();
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, 0, new VideoDecoderOptions { Mode = mode });
        using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        Assert.Contains(frame.Info.SideDataTypes, name => name.Contains("Film grain", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, frame.Info.ColorMatrixCode);
        Assert.Equal(2, frame.Info.ColorPrimariesCode);
        Assert.Equal(2, frame.Info.ColorTransferCode);
        var raw = frame.Info;
        var plane = frame.CopyPlane(0);

        Assert.Throws<NotSupportedException>(() => ResolvedVideoColor.Resolve(frame));
        using var converter = new SdrVideoConverter(new(160, 90));
        Assert.Throws<NotSupportedException>(() => converter.Convert(frame));
        Assert.Same(raw, frame.Info);
        Assert.Equal(plane, frame.CopyPlane(0));
    }

    [Fact]
    public void RejectsFramesThatCannotKeepTheNativeReferenceAndStreamEvidence()
    {
        using var frame = new FakeVideoFrame(0, 1);
        Assert.Throws<NotSupportedException>(() => ResolvedVideoColor.Resolve(frame));
        Assert.Throws<ArgumentNullException>(() => ResolvedVideoColor.Resolve(null!));
    }
}
