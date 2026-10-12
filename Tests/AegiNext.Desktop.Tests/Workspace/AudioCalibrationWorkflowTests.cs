using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;
using AegiNext.SubtitleFormats;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AudioCalibrationWorkflowTests
{
    [Fact]
    public async Task CalibratingPlayingAudioPausesSeeksAndResumesTheExactPositionWithoutChangingSubtitleUndoOrExport()
    {
        var line = new SubtitleLine { Start = new(101, 100), End = new(203, 100), Text = "TEST 中文 123" };
        var document = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        var source = new CalibrationAudioSource();
        var output = new CalibrationAudioOutput("speaker");
        await using var context = new WorkspaceSessionTestContext(document, controllerFactory: update => Controller(update,
            (_, _, target, _) => Task.FromResult(new AudioPlaybackSession(source, output, target))));
        await context.InitializeAsync();
        context.Editor.Apply("Draft name", value => value with { Name = "Unsaved project" });
        var before = context.Editor.Snapshot;
        var undoLabel = context.Editor.UndoLabel;
        var srt = SubtitleTextFormat.WriteSrt(before.Subtitles);
        var ass = AssSubtitleFormat.Write(before).Text;
        await context.Session.Controller.OpenAsync("audio-calibration.mkv");
        var target = new MediaTime(400001, 120000);
        await context.Session.Controller.SeekAsync(target);
        await context.Session.Controller.PlayAsync();
        output.Operations.Clear();
        var seeks = source.SeekTargets.Count;

        await context.Session.SetAudioCalibrationAsync(new("speaker", "test-system", 48000, 2, 85));

        Assert.Null(context.Session.LastError);
        Assert.Equal(VideoPlaybackState.PLAYING, context.Session.Controller.Snapshot.State);
        Assert.Equal(target, context.Session.Controller.Snapshot.Position);
        Assert.Equal(target, source.SeekTargets.Last());
        Assert.True(source.SeekTargets.Count > seeks);
        Assert.False(output.Paused);
        var operations = output.Operations.ToArray();
        Assert.Equal("Pause", operations[0]);
        Assert.True(Array.IndexOf(operations, "Clear") < Array.LastIndexOf(operations, "Play"));
        Assert.Same(before, context.Editor.Snapshot);
        Assert.Equal(undoLabel, context.Editor.UndoLabel);
        Assert.Equal(srt, SubtitleTextFormat.WriteSrt(context.Editor.Snapshot.Subtitles));
        Assert.Equal(ass, AssSubtitleFormat.Write(context.Editor.Snapshot).Text);
        Assert.Equal((line.Start, line.End), (context.Editor.Snapshot.Subtitles[0].Start, context.Editor.Snapshot.Subtitles[0].End));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
    }

    [Fact]
    public async Task LostDefaultRouteRebuildsAtTheFrozenPositionAndUsesOnlyTheReplacementDeviceProfile()
    {
        var firstSource = new CalibrationAudioSource();
        var nextSource = new CalibrationAudioSource();
        var firstOutput = new CalibrationAudioOutput("speaker");
        var nextOutput = new CalibrationAudioOutput("headphones");
        var entered = new TaskCompletionSource<MediaTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => Controller(update,
            async (_, _, target, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return new(firstSource, firstOutput, target);
                }
                entered.TrySetResult(target);
                await release.Task.WaitAsync(token);
                return new(nextSource, nextOutput, target);
            }));
        try
        {
            await context.InitializeAsync();
            context.Session.UpdatePreferences(value => value with
            {
                AudioCalibrations = [new("speaker", "test-system", 48000, 2, 80), new("headphones", "test-system", 48000, 2, 160)]
            });
            await context.Session.AudioCalibrationCompletion;
            await context.Session.Controller.OpenAsync("route-loss.mkv");
            var target = new MediaTime(7, 3);
            await context.Session.Controller.SeekAsync(target);
            await context.Session.Controller.PlayAsync();
            firstOutput.Consume(4800);
            var frozen = context.Session.Controller.Snapshot.Position;
            Assert.Equal(target + new MediaTime(1, 50), frozen);
            var before = context.Editor.Snapshot;
            firstOutput.Quality = AudioClockQuality.UNAVAILABLE;
            context.Session.Tick();
            Assert.Equal(frozen, await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(context.Session.IsSwitchingAudioDevice);
            Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.TIMING_ENTER));
            Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.TIMING_EXIT));
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.TIMING_ENTER);
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.TIMING_EXIT);
            Assert.Same(before, context.Editor.Snapshot);
            Assert.Equal(1, firstSource.DisposeCount);
            Assert.Equal(1, firstOutput.DisposeCount);
            release.TrySetResult();
            await context.Session.AudioCalibrationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, calls);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Session.Controller.Snapshot.State);
            Assert.Equal(frozen, context.Session.Controller.Snapshot.Position);
            Assert.Equal(frozen, nextSource.SeekTargets.Last());
            nextOutput.Consume(9600);
            Assert.Equal(frozen + new MediaTime(1, 25), context.Session.Controller.Snapshot.Position);
            Assert.Equal("headphones", context.Session.AudioClock!.DeviceId);
            Assert.False(context.Session.IsSwitchingAudioDevice);
            Assert.Same(before, context.Editor.Snapshot);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingCancelsAnAwaitingRouteFactoryAndDisposesAnyLateReplacement(bool lateResult)
    {
        var firstSource = new CalibrationAudioSource();
        var nextSource = new CalibrationAudioSource();
        var firstOutput = new CalibrationAudioOutput("speaker");
        var nextOutput = new CalibrationAudioOutput("headphones");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => Controller(update,
            async (_, _, target, token) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return new(firstSource, firstOutput, target);
                }
                if (lateResult)
                {
                    using var registration = token.Register(() => cancelled.TrySetResult());
                    var replacement = new AudioPlaybackSession(nextSource, nextOutput, target);
                    entered.TrySetResult();
                    await release.Task;
                    return replacement;
                }
                entered.TrySetResult();
                try
                {
                    await release.Task.WaitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    cancelled.TrySetResult();
                    throw;
                }
                return new(nextSource, nextOutput, target);
            }));
        Task? close = null;
        try
        {
            await context.InitializeAsync();
            await context.Session.Controller.OpenAsync("close-route-rebuild.mkv");
            await context.Session.Controller.PlayAsync();
            firstOutput.Quality = AudioClockQuality.UNAVAILABLE;
            context.Session.Tick();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            close = context.Session.DisposeAsync().AsTask();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.TrySetResult();
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(context.Session.IsClosing);
            Assert.Equal(1, firstSource.DisposeCount);
            Assert.Equal(1, firstOutput.DisposeCount);
            Assert.Equal(lateResult ? 1 : 0, nextSource.DisposeCount);
            Assert.Equal(lateResult ? 1 : 0, nextOutput.DisposeCount);
            Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.TIMING_ENTER));
            Assert.False(context.Session.CanExecuteCommand(WorkbenchCommand.TIMING_EXIT));
        }
        finally
        {
            release.TrySetResult();
            if (close is not null)
            {
                await close.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private static VideoPreviewController Controller(Action<VideoPreviewUpdate> update,
        Func<string, int, MediaTime, CancellationToken, Task<AudioPlaybackSession>> audioFactory)
    {
        return new((_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 1000, 2000, 5000, 10000, 16000), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update, audioFactory);
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
