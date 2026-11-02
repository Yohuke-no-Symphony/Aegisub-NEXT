using AegiNext.Core.Timing;
using AegiNext.Media.Preview;

namespace AegiNext.Media.Tests.Preview;

public sealed class AudioOnlyFrameSourceTests
{
    [Fact]
    public void FramesFollowAFixedGridFromTheStartAndStopAtTheDuration()
    {
        using var source = new AudioOnlyFrameSource(64, 36, new MediaTime(1, 10), new MediaTime(1, 10));

        var first = source.ReadFrame()!;
        var second = source.ReadFrame()!;
        var third = source.ReadFrame()!;

        Assert.Equal(new MediaTime(1, 10), first.Time);
        Assert.Equal(new MediaTime(1, 10) + new MediaTime(1, AudioOnlyFrameSource.FRAMES_PER_SECOND), second.Time);
        Assert.Equal(first.Time, first.Frame.Info.PresentationTimestamp!.ToMediaTime());
        Assert.Equal(64, first.Frame.Info.Width);
        Assert.False(second.ReachedEnd);
        Assert.True(third.ReachedEnd);
        Assert.Null(third.NextFrameTime);
        Assert.Null(source.ReadFrame());
    }

    [Fact]
    public void SeekSelectsTheFrameCoveringTheTargetAndClampsToTheRange()
    {
        using var source = new AudioOnlyFrameSource(64, 36, MediaTime.Zero, new MediaTime(10));

        var middle = source.SeekFrame(new MediaTime(1234, 1000))!;
        Assert.Equal(new MediaTime(30, AudioOnlyFrameSource.FRAMES_PER_SECOND), middle.Time);
        Assert.Equal(middle.Time + new MediaTime(1, AudioOnlyFrameSource.FRAMES_PER_SECOND), source.ReadFrame()!.Time);

        var before = source.SeekFrame(new MediaTime(-1))!;
        Assert.True(before.IsBeforeFirst);
        Assert.Equal(MediaTime.Zero, before.Time);

        var after = source.SeekFrame(new MediaTime(60))!;
        Assert.True(after.ReachedEnd);
    }

    [Fact]
    public void UnknownDurationKeepsProducingFramesAndCancelStopsTheSource()
    {
        var source = new AudioOnlyFrameSource(16, 9, MediaTime.Zero, null);
        Assert.False(source.SeekFrame(new MediaTime(3600))!.ReachedEnd);
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.ReadFrame());
        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.ReadFrame());
    }

    [Fact]
    public void ConverterRendersOpaqueBlackFittedToThePreviewBoundsAndDelegatesRealFrames()
    {
        using var source = new AudioOnlyFrameSource(1920, 1080, MediaTime.Zero, new MediaTime(1));
        var inner = new CountingConverter();
        using var converter = new AudioOnlyPreviewConverter(inner, new SdrPreviewOptions(640, 640));

        var image = converter.Convert(source.ReadFrame()!.Frame);

        Assert.Equal(640, image.Width);
        Assert.Equal(360, image.Height);
        var pixels = image.Pixels.Span;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            Assert.Equal(0, pixels[index]);
            Assert.Equal(byte.MaxValue, pixels[index + 3]);
        }
        Assert.Equal(0, inner.Calls);
    }

    private sealed class CountingConverter : IVideoPreviewConverter
    {
        internal int Calls { get; private set; }

        public SdrVideoFrame Convert(AegiNext.Media.Decoding.IVideoFrame frame, CancellationToken cancellationToken = default)
        {
            Calls++;
            return new(1, 1, [0, 0, 0, 255]);
        }

        public void Dispose()
        {
        }
    }
}
