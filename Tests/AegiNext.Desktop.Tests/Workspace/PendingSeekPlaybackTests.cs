using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Tests.Controllers;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

/// <summary>验证未完成的定位向播放命令交接时保留目标、播放意图和音频时钟。</summary>
[Collection("Workspace session")]
public sealed class PendingSeekPlaybackTests
{
    /// <summary>解码尚未完成时播放，目标不回跳，完成后音视频从同一位置继续推进。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayDuringPendingSeekKeepsTheTargetAndCanContinuePlaying(bool withAudio)
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new BlockingPreviewSeekSource(new(10, 3000, 4000, 6000, 7000, 10000, 23000));
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var context = CreateContext(clock, source, withAudio ? audioSource : null, output);
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("pending-seek-play.mkv");
        source.BlockNextSeek();
        try
        {
            var seek = session.SeekProjectTimeAsync(new(3));
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var play = session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            session.Tick();

            Assert.Equal(new MediaTime(3), session.ProjectPosition);
            Assert.Equal(new MediaTime(3), session.ViewModel.Timeline.Position);
            Assert.True(session.IsTransportPlaybackRequested);
            Assert.True(output.Paused);

            source.Release();
            await Task.WhenAll(seek, play).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(VideoPlaybackState.PLAYING, session.Controller.Snapshot.State);
            Assert.Equal(new MediaTime(6), session.Controller.Snapshot.Position);
            if (withAudio)
            {
                Assert.Equal(new MediaTime(6), audioSource.LastSeek);
                output.Consume(2400);
            }
            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Equal(new MediaTime(121, 20), session.Controller.Snapshot.Position);

            await session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            Assert.Equal(VideoPlaybackState.PAUSED, session.Controller.Snapshot.State);
            await session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            Assert.Equal(VideoPlaybackState.PLAYING, session.Controller.Snapshot.State);
            if (withAudio)
            {
                output.Consume(2400);
            }
            clock.Advance(TimeSpan.FromMilliseconds(50));
            Assert.Equal(new MediaTime(61, 10), session.Controller.Snapshot.Position);
            Assert.Null(session.LastError);
            Assert.Null(session.Controller.Snapshot.Error);
            Assert.Null(session.Controller.Snapshot.AudioError);
        }
        finally
        {
            source.Release();
        }
    }

    /// <summary>待定位时连续切换播放和暂停，以最后一次意图完成，旧命令不能恢复播放。</summary>
    [Theory]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task RepeatedPlayPauseDuringPendingSeekKeepsTheLatestIntent(bool initiallyPlaying, int toggles)
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new BlockingPreviewSeekSource(new(10, 3000, 4000, 6000, 7000, 23000));
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var context = CreateContext(clock, source, audioSource, output);
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("pending-seek-toggle.mkv");
        if (initiallyPlaying)
        {
            await session.Controller.PlayAsync();
        }
        var playing = initiallyPlaying != (toggles % 2 != 0);
        source.BlockNextSeek();
        try
        {
            var pending = new List<Task> { session.SeekFromUserAsync(new(6)) };
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 0; index < toggles; index++)
            {
                pending.Add(session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE));
            }
            Assert.Equal(new MediaTime(3), session.ProjectPosition);
            Assert.Equal(playing, session.IsTransportPlaybackRequested);
            source.Release();
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(playing ? VideoPlaybackState.PLAYING : VideoPlaybackState.PAUSED,
                session.Controller.Snapshot.State);
            Assert.Equal(new MediaTime(6), session.Controller.Snapshot.Position);
            Assert.Equal(new MediaTime(6), audioSource.LastSeek);
            Assert.Equal(!playing, output.Paused);
            Assert.Null(session.LastError);
        }
        finally
        {
            source.Release();
        }
    }

    /// <summary>等待定位的播放命令可被新的跳转接管，不能迟到恢复旧目标。</summary>
    [Fact]
    public async Task NewSeekSupersedesThePendingPlayAndRetainsItsPlayingIntent()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new BlockingPreviewSeekSource(new(10, 3000, 4000, 6000, 7000, 23000));
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var context = CreateContext(clock, source, audioSource, output);
        await context.InitializeAsync();
        var session = context.Session;
        await session.Controller.OpenAsync("pending-play-new-seek.mkv");
        source.BlockNextSeek();
        try
        {
            var first = session.SeekFromUserAsync(new(6));
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var play = session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            var latest = session.SeekFromUserAsync(new(7));
            source.Release();
            await Task.WhenAll(first, play, latest).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(VideoPlaybackState.PLAYING, session.Controller.Snapshot.State);
            Assert.Equal(new MediaTime(7), session.Controller.Snapshot.Position);
            Assert.Equal(new MediaTime(7), audioSource.LastSeek);
            output.Consume(2400);
            Assert.Equal(new MediaTime(141, 20), session.Controller.Snapshot.Position);
            Assert.Null(session.LastError);
        }
        finally
        {
            source.Release();
        }
    }

    private static WorkspaceSessionTestContext CreateContext(ManualPlaybackTimeProvider clock,
        BlockingPreviewSeekSource source, PreviewAudioSource? audioSource, PreviewAudioOutput output)
    {
        return new(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, new(3), new(20), audioSource is null ? null : 1)),
            (_, _, position) => new(_ => source, clock, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            audioSource is null ? null : (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(audioSource, output, position))));
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
