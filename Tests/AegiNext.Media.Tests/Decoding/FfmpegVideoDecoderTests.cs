using AegiNext.Testing.Media;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

[Collection(nameof(NativeDecoderTestGroup))]
public sealed class FfmpegVideoDecoderTests
{
    private static readonly int[] expectedComponentDepths = [10, 10, 10];

    [DecoderTheory]
    [InlineData("smpte2084")]
    [InlineData("arib-std-b67")]
    [InlineData("bt709")]
    [Trait("Category", "DecoderIntegration")]
    public async Task DrainsEveryBFrameWithExactTimestampsAndUnmodifiedTenBitPlanes(string transfer)
    {
        using var fixture = await DecoderFixture.CreateAsync(transfer);
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        var masteringObserved = false;
        var contentLightObserved = false;
        using (var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = VideoDecodeMode.Software }))
        {
            Assert.Equal(initialDecoders + 1, FfmpegVideoDecoder.GetLiveDecoderCount());
            var frameIndex = 0;
            while (decoder.ReadFrame() is { } frame)
            {
                using (frame)
                {
                    Assert.InRange(frameIndex, 0, DecoderFixture.FRAME_COUNT - 1);
                    var info = frame.Info;
                    Assert.Equal(DecoderFixture.WIDTH, info.Width);
                    Assert.Equal(DecoderFixture.HEIGHT, info.Height);
                    Assert.Equal(3, info.PlaneCount);
                    Assert.Equal("yuv420p10le", info.PixelFormat);
                    Assert.Equal(expectedComponentDepths, info.ComponentDepths);
                    Assert.Equal(fixture.TimeBase, info.TimeBase);
                    Assert.Equal(fixture.TimeBase, info.StreamTimeBase);
                    Assert.Equal(transfer, info.Color.Transfer);
                    Assert.Equal("bt2020", info.Color.Primaries);
                    Assert.Equal("bt2020nc", info.Color.Matrix);
                    Assert.Equal("tv", info.Color.Range);
                    Assert.False(info.IsCorrupt);
                    Assert.Equal(0u, info.DecodeErrorFlags);
                    var expected = fixture.ExpectedFrames[frameIndex];
                    AssertTimestamp(expected, "pts", info.PresentationTimestamp, fixture.TimeBase);
                    AssertTimestamp(expected, "best_effort_timestamp", info.BestEffortTimestamp, fixture.TimeBase);
                    var duration = GetOptionalInt64(expected, "duration");
                    Assert.Equal(duration is > 0 ? duration : null, info.DurationTicks);
                    AssertPlanesEqual(fixture, frame, frameIndex);

                    if (info.MasteringDisplay is { } mastering)
                    {
                        Assert.Equal(new MediaRatio(1000, 1), mastering.MaxLuminance);
                        Assert.Equal(new MediaRatio(1, 10000), mastering.MinLuminance);
                        masteringObserved = true;
                    }

                    if (info.ContentLight is { } light)
                    {
                        Assert.Equal(1000u, light.MaxContentLightLevel);
                        Assert.Equal(400u, light.MaxFrameAverageLightLevel);
                        contentLightObserved = true;
                    }
                }

                Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
                frameIndex++;
            }

            Assert.Equal(DecoderFixture.FRAME_COUNT, frameIndex);
            Assert.Null(decoder.ReadFrame());
            Assert.Null(decoder.ReadFrame());
        }

        Assert.Equal(transfer == "smpte2084", masteringObserved);
        Assert.Equal(transfer == "smpte2084", contentLightObserved);
        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task PreservesDeliberatelyVariableFrameTimestampsWithoutFrameRateInference()
    {
        using var fixture = await DecoderFixture.CreateAsync(variableFrameRate: true);
        var expectedTimestamps = fixture.ExpectedFrames.EnumerateArray().Select(frame => frame.GetProperty("pts").GetInt64()).ToArray();
        var gaps = expectedTimestamps.Zip(expectedTimestamps.Skip(1), (first, second) => second - first).ToArray();
        Assert.All(gaps, gap => Assert.True(gap > 0));
        Assert.True(gaps.Distinct().Count() >= 2);
        Assert.True(gaps.Max() > gaps.Min() * 3 / 2, "VFR fixture 必须包含超过时间基舍入误差的真实间距变化。");
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = VideoDecodeMode.Software });
        for (var index = 0; index < DecoderFixture.FRAME_COUNT; index++)
        {
            using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
            AssertTimestamp(fixture.ExpectedFrames[index], "pts", frame.Info.PresentationTimestamp, fixture.TimeBase);
            AssertTimestamp(fixture.ExpectedFrames[index], "best_effort_timestamp", frame.Info.BestEffortTimestamp, fixture.TimeBase);
            AssertPlanesEqual(fixture, frame, index);
        }

        Assert.Null(decoder.ReadFrame());
        Assert.Null(decoder.ReadFrame());
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    [SuppressMessage("ReSharper", "DisposeOnUsingVariable", Justification = "Explicit repeated disposal verifies ownership and idempotence; using declarations also clean up when an assertion fails.")]
    public async Task ReturnedFrameSurvivesLaterReadsAndDecoderDisposal()
    {
        using var fixture = await DecoderFixture.CreateAsync();
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = VideoDecodeMode.Software });
        using var first = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        var snapshot = first.Info;
        var original = first.CopyPlane(0);
        using (var second = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame()))
        {
            Assert.NotEqual(first.Info.PresentationTimestamp, second.Info.PresentationTimestamp);
            Assert.Equal(original, first.CopyPlane(0));
        }

        decoder.Dispose();
        decoder.Dispose();
        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
        Assert.Equal(initialFrames + 1, FfmpegVideoDecoder.GetLiveFrameCount());
        Assert.Same(snapshot, first.Info);
        AssertPlanesEqual(fixture, first, 0);
        Array.Fill(original, (byte)0);
        AssertPlanesEqual(fixture, first, 0);
        first.Dispose();
        first.Dispose();
        Assert.Same(snapshot, first.Info);
        Assert.Throws<ObjectDisposedException>(() => first.CopyPlane(0));
        Assert.Throws<ObjectDisposedException>(() => decoder.ReadFrame());
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task CallerPreCancellationLeavesAnOpenDecoderUsableButExplicitCancelIsSticky()
    {
        using var fixture = await DecoderFixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var openError = Assert.ThrowsAny<OperationCanceledException>(() =>
            FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, cancellation.Token));
        Assert.Equal(cancellation.Token, openError.CancellationToken);
        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());

        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = VideoDecodeMode.Software });
        var readError = Assert.ThrowsAny<OperationCanceledException>(() => decoder.ReadFrame(cancellation.Token));
        Assert.Equal(cancellation.Token, readError.CancellationToken);
        using var first = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
        AssertPlanesEqual(fixture, first, 0);
        await Task.Run(decoder.Cancel);
        Assert.ThrowsAny<OperationCanceledException>(() => decoder.ReadFrame());
        Assert.ThrowsAny<OperationCanceledException>(() => decoder.ReadFrame());
        AssertPlanesEqual(fixture, first, 0);
    }

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task InvalidInputsAndPlaneIndicesFailWithoutLeakingHandles()
    {
        using var fixture = await DecoderFixture.CreateAsync();
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        Assert.Throws<ArgumentOutOfRangeException>(() => FfmpegVideoDecoder.Open(fixture.MediaPath, -1));
        Assert.ThrowsAny<ArgumentException>(() => FfmpegVideoDecoder.Open(fixture.MediaPath, 0));
        Assert.ThrowsAny<ArgumentException>(() => FfmpegVideoDecoder.Open(fixture.MediaPath, 999));
        Assert.Throws<FileNotFoundException>(() => FfmpegVideoDecoder.Open(fixture.MediaPath + ".missing", fixture.VideoStreamIndex));
        var corruptPath = fixture.MediaPath + ".corrupt";
        await File.WriteAllTextAsync(corruptPath, "not a media container");
        Assert.Throws<InvalidDataException>(() => FfmpegVideoDecoder.Open(corruptPath, 0));
        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());

        using (var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = VideoDecodeMode.Software }))
        using (var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame()))
        {
            Assert.ThrowsAny<ArgumentException>(() => frame.GetPlaneInfo(-1));
            Assert.ThrowsAny<ArgumentException>(() => frame.GetPlaneInfo(frame.Info.PlaneCount));
            Assert.ThrowsAny<ArgumentException>(() => frame.CopyPlane(-1));
            Assert.ThrowsAny<ArgumentException>(() => frame.CopyPlane(frame.Info.PlaneCount));
            AssertPlanesEqual(fixture, frame, 0);
        }

        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
    }

    private static void AssertPlanesEqual(DecoderFixture fixture, DecodedVideoFrame frame, int frameIndex)
    {
        var offset = frameIndex * DecoderFixture.WIDTH * DecoderFixture.HEIGHT * 3;
        for (var plane = 0; plane < 3; plane++)
        {
            var info = frame.GetPlaneInfo(plane);
            var width = plane == 0 ? DecoderFixture.WIDTH : DecoderFixture.WIDTH / 2;
            var height = plane == 0 ? DecoderFixture.HEIGHT : DecoderFixture.HEIGHT / 2;
            Assert.Equal(plane, info.Index);
            Assert.Equal(width * 2, info.RowBytes);
            Assert.Equal(height, info.Height);
            Assert.True(Math.Abs((long)info.SourceStride) >= info.RowBytes);
            Assert.Equal(width * height * 2, info.ByteCount);
            var pixels = frame.CopyPlane(plane);
            Assert.Equal(info.ByteCount, pixels.Length);
            Assert.Equal(fixture.RawFrames.AsSpan(offset, pixels.Length).ToArray(), pixels);
            offset += pixels.Length;
        }
    }

    private static void AssertTimestamp(JsonElement frame, string property, MediaTimestamp? actual, MediaTimeBase timeBase)
    {
        var expected = GetOptionalInt64(frame, property);
        if (expected is { } value)
        {
            Assert.NotNull(actual);
            Assert.Equal(value, actual.Value);
            Assert.Equal(timeBase, actual.TimeBase);
        }
        else
        {
            Assert.Null(actual);
        }
    }

    private static long? GetOptionalInt64(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
    }
}
