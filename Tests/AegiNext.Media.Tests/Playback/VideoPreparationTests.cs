using AegiNext.Core.Timing;
using AegiNext.Media.Playback;
using AegiNext.Media.Preview;
using AegiNext.Media.Tests.Decoding;

namespace AegiNext.Media.Tests.Playback;

public sealed class VideoPreparationTests
{
    [Fact]
    public async Task AudioOnlyPlaceholderFramesOpenWithPreparationEnabledAndHoldNoPixelBudget()
    {
        await using var session = new VideoPlaybackSession(_ => new AudioOnlyFrameSource(1920, 1080, MediaTime.Zero, new(10, 1)));
        session.ConfigurePreparation(new(pendingCapacity: 1));
        await session.OpenAsync();

        using (var opened = await session.ReadPreparationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.NotNull(opened);
            Assert.Equal(0, session.Snapshot.PreparationPendingBytes);
        }
        await session.CloseAsync();
    }

    [Fact]
    public async Task PreparationRetainsAnEarlierUsefulFutureFrameWithoutReadingToTheFullLookahead()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new ClockAdvancingPreparationSource(clock, TimeSpan.FromMilliseconds(4),
            Enumerable.Range(0, 200).Select(index => index * 16L).ToArray());
        await using var session = new VideoPlaybackSession(_ => source, clock);
        session.ConfigurePreparation(new(pendingCapacity: 1));
        session.SetPreparationLead(new(60, 1000), new(4, 1000));
        await session.OpenAsync();
        using (var opened = await session.ReadPreparationAsync())
        {
        }
        await session.PlayAsync();
        using (var prepared = await session.ReadPreparationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Empty(source.SeekTargets);
            Assert.Equal(2, source.ReadCount);
            Assert.Equal(new MediaTime(16, 1000), prepared!.PositionedFrame.Time);
            Assert.Equal(new MediaTime(32, 1000), prepared.PositionedFrame.NextFrameTime);
            Assert.True(session.Snapshot.Position + new MediaTime(4, 1000) < prepared.PositionedFrame.NextFrameTime);
            Assert.True(prepared.PositionedFrame.Time < session.Snapshot.Position + session.PreparationLead);
        }
        await session.CloseAsync();
        Assert.All(source.Inner.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public async Task AQuickDecoderKeepsReadingFutureCandidatesWithoutSeeking()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new ClockAdvancingPreparationSource(clock, TimeSpan.FromMilliseconds(4),
            Enumerable.Range(0, 200).Select(index => index * 16L).ToArray());
        await using var session = new VideoPlaybackSession(_ => source, clock);
        session.ConfigurePreparation(new(pendingCapacity: 1));
        session.SetPreparationLead(new(60, 1000));
        await session.OpenAsync();
        using (var opened = await session.ReadPreparationAsync())
        {
        }
        await session.PlayAsync();
        using (var prepared = await session.ReadPreparationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Empty(source.SeekTargets);
            Assert.Equal(4, source.ReadCount);
            Assert.Equal(new MediaTime(48, 1000), prepared!.PositionedFrame.Time);
            Assert.Equal(new MediaTime(64, 1000), prepared.PositionedFrame.NextFrameTime);
            Assert.True(session.Snapshot.Position < prepared.PositionedFrame.Time);
        }
        await session.CloseAsync();
        Assert.All(source.Inner.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public async Task ADecoderSlowerThanTheFrameRatePublishesABoundedPreparedIntervalInsteadOfChasingTheClock()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new ClockAdvancingPreparationSource(clock, TimeSpan.FromMilliseconds(32),
            Enumerable.Range(0, 200).Select(index => index * 16L).ToArray());
        await using var session = new VideoPlaybackSession(_ => source, clock);
        session.ConfigurePreparation(new(pendingCapacity: 1));
        session.SetPreparationLead(new(60, 1000));
        await session.OpenAsync();
        using (var opened = await session.ReadPreparationAsync())
        {
        }

        await session.PlayAsync();
        using (var prepared = await session.ReadPreparationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.Equal(VideoPlaybackState.PLAYING, session.Snapshot.State);
            Assert.InRange(source.ReadCount, 2, 3);
            var target = Assert.Single(source.SeekTargets);
            Assert.True(target >= session.Snapshot.Position);
            Assert.True(target <= session.Snapshot.Position + session.MaximumPreparationAhead);
            Assert.True(prepared!.PositionedFrame.Time <= target);
            Assert.True(target < prepared.PositionedFrame.NextFrameTime);
            Assert.True(session.Snapshot.Position < prepared.PositionedFrame.NextFrameTime);
            Assert.InRange(session.Snapshot.PreparationPendingCount, 0, 1);
        }
        await session.CloseAsync();
        Assert.All(source.Inner.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public async Task FuturePreparationPreservesTheClockAndDoesNotEndAtPrefetchedEof()
    {
        var source = new FakeVideoFrameSource(100, 133);
        var clock = new ManualPlaybackTimeProvider();
        await using var session = new VideoPlaybackSession(_ => source, clock);
        session.ConfigurePreparation(new());
        session.SetPreparationLead(new(50, 1000));
        await session.OpenAsync();
        using (var opened = await session.ReadPreparationAsync())
        {
            Assert.Equal(new MediaTime(100, 1000), opened!.PositionedFrame.Time);
        }

        await session.PlayAsync();
        using var future = await session.ReadPreparationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new MediaTime(133, 1000), future!.PositionedFrame.Time);
        Assert.Equal(VideoPlaybackState.PLAYING, session.Snapshot.State);
        Assert.Equal(new MediaTime(100, 1000), session.Snapshot.Position);
        Assert.Equal(new MediaTime(100, 1000), session.Snapshot.DisplayTime);

        await EventuallyAsync(() => clock.ActiveTimerCount != 0);
        clock.Advance(TimeSpan.FromMilliseconds(33));
        await EventuallyAsync(() => session.Snapshot.State == VideoPlaybackState.ENDED);
        Assert.Equal(new MediaTime(133, 1000), session.Snapshot.Position);
        Assert.Equal(new MediaTime(133, 1000), session.Snapshot.DisplayTime);
    }

    [Fact]
    public async Task OutstandingNativeLeasesApplyBackpressureUntilTheyAreDisposed()
    {
        var source = new FakeVideoFrameSource(0, 33, 66, 99);
        var clock = new ManualPlaybackTimeProvider();
        await using var session = new VideoPlaybackSession(_ => source, clock);
        session.ConfigurePreparation(new(pendingCapacity: 1, maximumPendingBytes: 1));
        session.SetPreparationLead(new(250, 1000));
        await session.OpenAsync();
        var first = await session.ReadPreparationAsync();
        try
        {
            await session.PlayAsync();
            await Task.Yield();
            Assert.Single(source.IssuedFrames);
            Assert.Equal(1, session.Snapshot.PreparationPendingCount);
            Assert.Equal(1, session.Snapshot.PreparationPendingBytes);
        }
        finally
        {
            first!.Dispose();
        }

        using var next = await session.ReadPreparationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new MediaTime(99, 1000), next!.PositionedFrame.Time);
        Assert.Equal(VideoPlaybackState.PLAYING, session.Snapshot.State);
        await session.CloseAsync();
        Assert.All(source.IssuedFrames.Where(frame => !ReferenceEquals(frame, next.PositionedFrame.Frame)),
            frame => Assert.Equal(1, frame.DisposeCount));
    }

    [Fact]
    public async Task SeekingDiscardsFutureMetadataAndRestoresTheExactPausedInterval()
    {
        var source = new FakeVideoFrameSource(-100, -67, -31, 20, 120);
        var clock = new ManualPlaybackTimeProvider();
        await using var session = new VideoPlaybackSession(_ => source, clock);
        session.ConfigurePreparation(new());
        session.SetPreparationLead(new(200, 1000));
        await session.OpenAsync();
        using (var opened = await session.ReadPreparationAsync())
        {
        }
        await session.PlayAsync();
        using var old = await session.ReadPreparationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var seek = await session.SeekAsync(new(-50, 1000));
        using var selected = await session.ReadPreparationAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(VideoPlaybackState.PAUSED, session.Snapshot.State);
        Assert.Equal(new MediaTime(-50, 1000), session.Snapshot.Position);
        Assert.Equal(new MediaTime(-67, 1000), selected!.PositionedFrame.Time);
        Assert.Equal(new MediaTime(-31, 1000), selected.PositionedFrame.NextFrameTime);
        Assert.Equal(seek.Generation, selected.Generation);
        Assert.NotEqual(old!.Generation, selected.Generation);
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }
}
