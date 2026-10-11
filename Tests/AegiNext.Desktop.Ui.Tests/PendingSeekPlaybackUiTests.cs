using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证真实时间轴指针和播放入口在预览转换未完成时的交接。</summary>
public sealed class PendingSeekPlaybackUiTests
{
    /// <summary>点击或拖动后立即播放，等待精确画面和音频定位完成，并屏蔽旧指针释放。</summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PlayBeforeTimelinePreviewIsReadyWaitsAtTheSelectedTarget(bool pointerHeld, bool withAudio)
    {
        var converter = new CatchupPreviewConverter();
        var audioSource = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = new MainWindowTestContext(
            audioFactory: withAudio
                ? (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(audioSource, output, position))
                : null,
            videoConverterFactory: () => converter);
        await context.OpenMediaAsync();
        var target = new MediaTime(10);
        converter.Block(3);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.Window.UpdateLayout();
        var point = timeline.TranslatePoint(new(timeline.HeaderWidth + timeline.PixelsPerSecond * 10, 8), context.Window)!.Value;
        context.Window.MouseDown(point, MouseButton.Left);
        try
        {
            if (!pointerHeld)
            {
                context.Window.MouseUp(point, MouseButton.Left);
            }
            await DrainAsync(() => converter.Entered.Task.IsCompleted);
            Assert.Equal(target, context.Session.ProjectPosition);
            Assert.NotEqual(target, context.Controller.Snapshot.PresentedAtPosition);

            if (pointerHeld)
            {
                UiTestActions.Press(context.Window, Key.Space);
            }
            else
            {
                UiTestActions.Click(context.Window, "PlayButton");
            }
            var command = Assert.IsType<AsyncRelayCommand>(context.ViewModel.GetCommand(WorkbenchCommand.PLAY_PAUSE));
            var play = command.ExecutionTask;
            Assert.NotNull(play);
            context.Session.Tick();
            Assert.Equal(target, context.Session.ProjectPosition);
            Assert.Equal(target, context.ViewModel.Timeline.Position);
            Assert.True(context.Session.IsTransportPlaybackRequested);
            Assert.False(play.IsCompleted);
            Assert.True(output.Paused);
            Assert.False(context.ViewModel.Timeline.IsSeeking);
            Assert.False(timeline.IsSeeking);
            Assert.False(context.Session.GetPreviewState().IsInteractive);

            converter.Release();
            await play.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(target, context.Controller.Snapshot.PresentedAtPosition);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            if (withAudio)
            {
                Assert.Equal(target, audioSource.LastSeek);
                Assert.False(output.Paused);
                output.Consume(2400);
            }
            context.Window.MouseUp(point, MouseButton.Left);
            context.Clock.Advance(TimeSpan.FromMilliseconds(50));
            context.Session.Tick();
            Assert.Equal(new MediaTime(201, 20), context.Session.ProjectPosition);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            Assert.Null(context.Session.LastError);
            Assert.Null(context.Controller.Snapshot.Error);
            Assert.Null(context.Controller.Snapshot.AudioError);
        }
        finally
        {
            converter.Release();
            context.Window.MouseUp(point, MouseButton.Left);
        }
    }

    private static async Task DrainAsync(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!completed())
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
