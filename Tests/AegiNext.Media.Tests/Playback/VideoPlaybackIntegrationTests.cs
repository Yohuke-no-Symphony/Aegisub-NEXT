using AegiNext.Testing.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;
using AegiNext.Media.Tests.Decoding;

namespace AegiNext.Media.Tests.Playback;

[Collection(nameof(NativeDecoderTestGroup))]
public sealed class VideoPlaybackIntegrationTests
{
    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task RealDecoderSessionSeeksExactPixelsAndTransfersFrameOwnershipPastClose()
    {
        using var fixture = await DecoderFixture.CreateAsync();
        var times = fixture.ExpectedFrames.EnumerateArray()
            .Select(frame => new MediaTimestamp(frame.GetProperty("pts").GetInt64(), fixture.TimeBase).ToMediaTime()).ToArray();
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        await using (var session = await VideoPlaybackSession.OpenAsync(fixture.MediaPath, fixture.VideoStreamIndex))
        {
            using (var first = Assert.IsType<VideoPresentation>(await session.ReadPresentationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))))
            {
                Assert.Equal(times[0], first.PositionedFrame.Time);
                Assert.Equal(VideoPlaybackState.PAUSED, session.Snapshot.State);
                AssertFramePixels(fixture, first.PositionedFrame.Frame, 0);
            }

            var frameIndex = times.Length / 2;
            var target = (times[frameIndex] + times[frameIndex + 1]) / 2;
            var seek = await session.SeekAsync(target).WaitAsync(TimeSpan.FromSeconds(5));
            using (var selected = Assert.IsType<VideoPresentation>(await session.ReadPresentationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))))
            {
                Assert.Equal(times[frameIndex], seek.SelectedTime);
                Assert.Equal(times[frameIndex + 1], seek.NextFrameTime);
                Assert.Equal(seek.Generation, selected.Generation);
                Assert.Equal(times[frameIndex], selected.PositionedFrame.Time);
                Assert.Equal(target, session.Snapshot.Position);
                AssertFramePixels(fixture, selected.PositionedFrame.Frame, frameIndex);

                await session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(VideoPlaybackState.CLOSED, session.Snapshot.State);
                Assert.Null(await session.ReadPresentationAsync());
                Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
                Assert.Equal(initialFrames + 1, FfmpegVideoDecoder.GetLiveFrameCount());
                AssertFramePixels(fixture, selected.PositionedFrame.Frame, frameIndex);
            }

            Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
        }

        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
    }

    private static void AssertFramePixels(DecoderFixture fixture, IVideoFrame frame, int frameIndex)
    {
        var offset = frameIndex * DecoderFixture.WIDTH * DecoderFixture.HEIGHT * 3;
        Assert.Equal("yuv420p10le", frame.Info.PixelFormat);
        Assert.Equal(3, frame.Info.PlaneCount);
        for (var plane = 0; plane < frame.Info.PlaneCount; plane++)
        {
            var pixels = frame.CopyPlane(plane);
            Assert.Equal(fixture.RawFrames.AsSpan(offset, pixels.Length).ToArray(), pixels);
            offset += pixels.Length;
        }
    }
}

