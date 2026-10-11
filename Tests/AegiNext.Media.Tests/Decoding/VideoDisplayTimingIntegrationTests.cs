using AegiNext.Testing.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

[Collection(nameof(NativeDecoderTestGroup))]
public sealed class VideoDisplayTimingIntegrationTests
{
    [DecoderTheory]
    [InlineData(VideoDecodeMode.Software, 0)]
    [InlineData(VideoDecodeMode.Auto, 0)]
    [InlineData(VideoDecodeMode.Auto, 3)]
    [Trait("Category", "DecoderIntegration")]
    public async Task MissingAviTailKeepsRawFactsAndUsesDurationAcrossSeekEofAndContinuation(VideoDecodeMode mode, int cachedFrames)
    {
        using var fixture = await VideoCompatibilityFixture.CreateAsync("mpeg4-avi", "mpeg4", "yuv420p");
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        var tailIndex = VideoCompatibilityFixture.FRAME_COUNT - 1;
        var tailReference = fixture.ExpectedFrames[tailIndex];
        Assert.False(tailReference.TryGetProperty("pts", out _));
        Assert.False(tailReference.TryGetProperty("best_effort_timestamp", out _));
        var times = Enumerable.Range(0, tailIndex).Select(fixture.GetFrameTime).ToList();
        var previousDuration = fixture.ExpectedFrames[tailIndex - 1].GetProperty("duration").GetInt64();
        times.Add(times[^1] + new MediaTimestamp(previousDuration, fixture.TimeBase).ToMediaTime());
        try
        {
            using (var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex, new VideoDecoderOptions { Mode = mode }))
            {
                for (var index = 0; index < times.Count; index++)
                {
                    using var frame = Assert.IsType<DecodedVideoFrame>(decoder.ReadFrame());
                    Assert.Equal(times[index], frame.Info.DisplayTiming!.Timestamp.ToMediaTime());
                    if (index == tailIndex)
                    {
                        Assert.Null(frame.Info.PresentationTimestampValue);
                        Assert.Null(frame.Info.BestEffortTimestampValue);
                        Assert.True(frame.Info.DisplayTiming.IsDerived);
                        Assert.True(frame.Info.DisplayTiming.Evidence.HasFlag(VideoDisplayTimingEvidence.PreviousFrameDuration));
                        AssertPixels(fixture, frame, index);
                    }
                }
                Assert.Null(decoder.ReadFrame());
                Assert.Null(decoder.ReadFrame());
            }
            using var navigator = new VideoFrameNavigator(token => FfmpegVideoDecoder.Open(fixture.MediaPath, fixture.VideoStreamIndex,
                new VideoDecoderOptions { Mode = mode }, token), cachedFrames);
            using (var tail = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(times[^1] + new MediaTime(1))))
            {
                Assert.Equal(times[^1], tail.Time);
                Assert.True(tail.ReachedEnd);
                Assert.Null(tail.NextFrameTime);
                AssertPixels(fixture, tail.Frame, tailIndex);
            }
            Assert.Null(navigator.ReadFrame());
            foreach (var index in new[] { 3, 10, 0, 8, 11, 2 })
            {
                using var selected = Assert.IsType<PositionedVideoFrame>(navigator.SeekFrame(times[index]));
                Assert.Equal(times[index], selected.Time);
                AssertPixels(fixture, selected.Frame, index);
                using var next = navigator.ReadFrame();
                if (index == tailIndex)
                {
                    Assert.Null(next);
                }
                else
                {
                    Assert.NotNull(next);
                    Assert.Equal(times[index + 1], next.Time);
                    AssertPixels(fixture, next.Frame, index + 1);
                }
            }
        }
        finally
        {
            Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
            Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
        }
    }

    private static void AssertPixels(VideoCompatibilityFixture fixture, IVideoFrame frame, int index)
    {
        var offset = index * fixture.FrameByteCount;
        for (var plane = 0; plane < frame.Info.PlaneCount; plane++)
        {
            var pixels = frame.CopyPlane(plane);
            Assert.Equal(fixture.RawFrames.AsSpan(offset, pixels.Length).ToArray(), pixels);
            offset += pixels.Length;
        }
    }
}
