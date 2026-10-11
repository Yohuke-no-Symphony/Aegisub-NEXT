using AegiNext.Testing.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;
using AegiNext.Media.Preview;
using AegiNext.Media.Tests.Decoding;
using AegiNext.Media.Tests.Playback;

namespace AegiNext.Media.Tests.Preview;

[Collection(nameof(NativeDecoderTestGroup))]
public sealed class SdrPreviewPlaybackTests
{
    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public async Task RealPlaybackConvertsFirstPlayingAndSeekedFramesAndClosesWithConsumerOwnership()
    {
        using var fixture = await DecoderFixture.CreateAsync(variableFrameRate: true);
        var times = fixture.ExpectedFrames.EnumerateArray()
            .Select(frame => new MediaTimestamp(frame.GetProperty("pts").GetInt64(), fixture.TimeBase).ToMediaTime()).ToArray();
        var initialConverters = SdrVideoConverter.LiveConverterCount;
        var initialDecoders = FfmpegVideoDecoder.GetLiveDecoderCount();
        var initialFrames = FfmpegVideoDecoder.GetLiveFrameCount();
        var clock = new ManualPlaybackTimeProvider();
        using (var converter = new SdrVideoConverter())
        await using (var session = await VideoPlaybackSession.OpenAsync(fixture.MediaPath, fixture.VideoStreamIndex, clock))
        {
            using (var first = Assert.IsType<VideoPresentation>(await session.ReadPresentationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))))
            {
                Assert.Equal(times[0], first.PositionedFrame.Time);
                SdrVideoConverterTests.AssertOpaqueImage(converter.Convert(first.PositionedFrame.Frame));
            }

            await session.PlayAsync();
            await WaitForClockAsync(clock);
            clock.Advance((times[1] - times[0]).ToTimeSpan(MediaTimeRounding.CEILING));
            using (var playing = Assert.IsType<VideoPresentation>(await session.ReadPresentationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))))
            {
                Assert.Equal(times[1], playing.PositionedFrame.Time);
                SdrVideoConverterTests.AssertOpaqueImage(converter.Convert(playing.PositionedFrame.Frame));
            }

            var index = times.Length / 2;
            var result = await session.SeekAsync((times[index] + times[index + 1]) / 2).WaitAsync(TimeSpan.FromSeconds(5));
            using (var selected = Assert.IsType<VideoPresentation>(await session.ReadPresentationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))))
            {
                Assert.Equal(result.Generation, selected.Generation);
                Assert.Equal(times[index], selected.PositionedFrame.Time);
                var beforeClose = converter.Convert(selected.PositionedFrame.Frame);
                await session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));

                Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
                Assert.Equal(initialFrames + 1, FfmpegVideoDecoder.GetLiveFrameCount());
                Assert.NotEmpty(selected.PositionedFrame.Frame.CopyPlane(0));
                var afterClose = converter.Convert(selected.PositionedFrame.Frame);
                Assert.Equal(beforeClose.Pixels.ToArray(), afterClose.Pixels.ToArray());
            }

            Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
        }

        Assert.Equal(initialConverters, SdrVideoConverter.LiveConverterCount);
        Assert.Equal(initialDecoders, FfmpegVideoDecoder.GetLiveDecoderCount());
        Assert.Equal(initialFrames, FfmpegVideoDecoder.GetLiveFrameCount());
    }

    private static async Task WaitForClockAsync(ManualPlaybackTimeProvider clock)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (clock.ActiveTimerCount == 0)
        {
            await Task.Delay(1, timeout.Token);
        }
    }
}
