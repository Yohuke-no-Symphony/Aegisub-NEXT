using System.Threading.Channels;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests;

/// <summary>验证独立脚本预览的防抖、单帧交付、播放和专用线程生命周期。</summary>
public sealed class EffectScriptPreviewControllerTests
{
    /// <summary>每次实际输入变化重新等待 200ms，过期草稿不编译。</summary>
    [Fact]
    public async Task DebounceCompilesOnlyTheLatestDraftAfterItsOwnTwoHundredMilliseconds()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        await using var controller = CreateController(inbox, clock, out var created);
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        var original = Request();
        controller.Submit(original, autoPlay: false);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Empty(renderer.Calls);

        var replacement = Request(original.DraftId, original.Input with { Options = original.Input.Options with { Text = "Latest" } });
        controller.Submit(replacement, autoPlay: false);
        var completion = controller.Completion;
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.Empty(renderer.Calls);
        Assert.False(completion.IsCompleted);
        Assert.True(controller.Snapshot.IsBusy);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await completion.WaitAsync(TimeSpan.FromSeconds(5));

        var delivered = await NextFrame(inbox.Reader);
        Assert.Same(replacement, delivered.Snapshot.Request);
        Assert.Same(replacement, Assert.Single(renderer.Calls).Request);
        Assert.False(delivered.Snapshot.IsBusy);
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    /// <summary>连续 seek 取消正在渲染的旧目标，并只保留最后一个位置。</summary>
    [Fact]
    public async Task SeekKeepsOnlyTheLatestPositionAndNeverDeliversTheCancelledFrame()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var renders = 0;
        await using var controller = CreateController(inbox, clock, out var created, (_, _, token) =>
        {
            if (Interlocked.Increment(ref renders) == 1)
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                entered.TrySetResult();
                release.Wait(CancellationToken.None);
            }
            return new(1, 1, [0, 0, 0, 255]);
        });
        try
        {
            controller.Submit(Request(), autoPlay: false, debounce: false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.Seek(new(3, 4));
            controller.Seek(new(1, 4));
            controller.Seek(new(1, 2));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.Set();
            await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            var delivered = await NextFrame(inbox.Reader);
            var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new MediaTime(1, 2), delivered.Snapshot.Position);
            Assert.True(controller.IsCurrent(delivered));
            Assert.Equal(new[] { MediaTime.Zero, new MediaTime(1, 2) }, renderer.Calls.Select(call => call.Position));
            Assert.Equal(1, renderer.MaximumConcurrency);
            Assert.False(inbox.Reader.TryRead(out _));
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>新草稿取消编译中的旧请求，等待槽只保留最新完整样例。</summary>
    [Fact]
    public async Task DraftReplacementCancelsOldWorkAndOnlyTheLatestFullInputCanBePresented()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var original = Request();
        await using var controller = CreateController(inbox, clock, out var created, (request, _, token) =>
        {
            if (ReferenceEquals(request, original))
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                entered.TrySetResult();
                release.Wait(CancellationToken.None);
            }
            return new(1, 1, [0, 0, 0, 255]);
        });
        try
        {
            controller.Submit(original, debounce: false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var middle = Request(original.DraftId, original.Input with { Source = BuiltinEffectScripts.Get("fade-out").Source });
            controller.Submit(middle, debounce: false);
            var latest = Request(original.DraftId, middle.Input with
            {
                Options = middle.Input.Options with { Text = "Newest", Duration = new(2) }
            });
            controller.Submit(latest, autoPlay: false, debounce: false);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            release.Set();
            await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            var delivered = await NextFrame(inbox.Reader);
            var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(latest, delivered.Snapshot.Request);
            Assert.Equal(MediaTime.Zero, delivered.Snapshot.Position);
            Assert.False(delivered.Snapshot.IsPlaying);
            Assert.True(controller.IsCurrent(delivered));
            Assert.Equal(new[] { original, latest }, renderer.Calls.Select(call => call.Request));
            Assert.DoesNotContain(renderer.Calls, call => ReferenceEquals(call.Request, middle));
            Assert.Equal(1, renderer.MaximumConcurrency);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>初帧和末帧均需实际呈现，末帧确认后下一轮从零开始。</summary>
    [Fact]
    public async Task PlaybackWaitsForTheExactPresentedFrameBeforeAdvancingOrLooping()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        await using var controller = CreateController(inbox, clock, out var created);
        controller.Submit(Request(), debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var first = await NextFrame(inbox.Reader);
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Single(renderer.Calls);
        Assert.Equal(0, clock.ActiveTimerCount);
        Assert.Equal(MediaTime.Zero, first.Snapshot.Position);

        controller.AcknowledgePresentation(first with { });
        Assert.Equal(0, clock.ActiveTimerCount);
        controller.AcknowledgePresentation(first);
        clock.Advance(TimeSpan.FromSeconds(2));
        var closing = await NextFrame(inbox.Reader);
        Assert.Equal(new MediaTime(23, 24), closing.Snapshot.Position);
        Assert.True(closing.CompletesCycle);
        Assert.True(controller.IsCurrent(closing));
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(2, renderer.Calls.Count);
        controller.AcknowledgePresentation(first);
        controller.AcknowledgePresentation(closing with { });
        Assert.Equal(0, clock.ActiveTimerCount);

        controller.AcknowledgePresentation(closing);
        clock.Advance(new MediaTime(1, 24).ToTimeSpan(MediaTimeRounding.CEILING));
        var restarted = await NextFrame(inbox.Reader);
        Assert.Equal(MediaTime.Zero, restarted.Snapshot.Position);
        Assert.False(restarted.CompletesCycle);
        Assert.True(restarted.Snapshot.IsPlaying);
        Assert.Equal(3, renderer.Calls.Count);
        controller.AcknowledgePresentation(closing);
        Assert.Equal(0, clock.ActiveTimerCount);
        Assert.True(controller.IsCurrent(restarted));
    }

    /// <summary>禁用循环会在末个可见帧停止，再次播放从零开始。</summary>
    [Fact]
    public async Task NonLoopingPlaybackStopsAtTheLastVisibleFrameAndPlayRestartsAtZero()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        await using var controller = CreateController(inbox, clock, out var created);
        controller.Loop = false;
        controller.Submit(Request(), debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        controller.AcknowledgePresentation(await NextFrame(inbox.Reader));
        clock.Advance(TimeSpan.FromSeconds(2));
        var closing = await NextFrame(inbox.Reader);
        Assert.True(closing.CompletesCycle);
        Assert.Equal(new MediaTime(23, 24), closing.Snapshot.Position);
        Assert.False(closing.Snapshot.IsPlaying);
        controller.AcknowledgePresentation(closing);
        Assert.Equal(0, clock.ActiveTimerCount);
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(2, renderer.Calls.Count);

        controller.Play();
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var restarted = await NextFrame(inbox.Reader);
        Assert.Equal(MediaTime.Zero, restarted.Snapshot.Position);
        Assert.True(restarted.Snapshot.IsPlaying);
        Assert.False(restarted.Snapshot.Loop);
    }

    /// <summary>状态更新撤销尚未呈现的帧并交付新静态帧，宿主无需伪造确认。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PauseAndLoopChangesReplaceUnpresentedFramesWithoutWaitingForTheirAcknowledgment(bool changeLoop)
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        await using var controller = CreateController(inbox, clock, out var created);
        controller.Submit(Request(), debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var old = await NextFrame(inbox.Reader);
        if (changeLoop)
        {
            controller.Loop = false;
        }
        else
        {
            controller.Pause();
        }
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var current = await NextFrame(inbox.Reader);
        Assert.False(controller.IsCurrent(old));
        Assert.True(controller.IsCurrent(current));
        Assert.Equal(MediaTime.Zero, current.Snapshot.Position);
        Assert.Equal(changeLoop, current.Snapshot.IsPlaying);
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, renderer.Calls.Count);
        controller.AcknowledgePresentation(old);
        Assert.True(controller.IsCurrent(current));
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    /// <summary>切页暂停并保留已呈现位置，返回只呈现静态帧。</summary>
    [Fact]
    public async Task HidingRetainsThePresentedPositionAndShowingRendersItWhilePaused()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        await using var controller = CreateController(inbox, clock, out var created);
        controller.Submit(Request(), autoPlay: false, debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        controller.AcknowledgePresentation(await NextFrame(inbox.Reader));
        controller.Seek(new(3, 4));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        controller.AcknowledgePresentation(await NextFrame(inbox.Reader));
        controller.Play();
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var playing = await NextFrame(inbox.Reader);
        controller.AcknowledgePresentation(playing);
        controller.SetVisible(false);
        var hidden = controller.Snapshot;
        Assert.False(hidden.IsPlaying);
        Assert.False(hidden.IsBusy);
        Assert.False(hidden.IsVisible);
        Assert.Equal(new MediaTime(3, 4), hidden.Position);
        Assert.False(controller.IsCurrent(playing));
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        var count = renderer.Calls.Count;
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(count, renderer.Calls.Count);
        Assert.Equal(0, clock.ActiveTimerCount);

        controller.SetVisible(true);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var resumed = await NextFrame(inbox.Reader);
        Assert.Equal(new MediaTime(3, 4), resumed.Snapshot.Position);
        Assert.False(resumed.Snapshot.IsPlaying);
        Assert.True(resumed.Snapshot.IsVisible);
        Assert.True(controller.IsCurrent(resumed));
    }

    /// <summary>scrub 期间 seek 使用帧网格，取消回到起点并恢复原播放状态。</summary>
    [Fact]
    public async Task ScrubbingQuantizesAndClampsPositionsAndCancelRestoresThePresentedOrigin()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        await using var controller = CreateController(inbox, clock, out _);
        controller.Submit(Request(), debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        controller.AcknowledgePresentation(await NextFrame(inbox.Reader));
        controller.Seek(new(1, 2));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        controller.AcknowledgePresentation(await NextFrame(inbox.Reader));
        controller.BeginScrub();
        Assert.True(controller.Snapshot.IsScrubbing);
        Assert.False(controller.Snapshot.IsPlaying);
        controller.Seek(new(51, 100));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var quantized = await NextFrame(inbox.Reader);
        Assert.Equal(new MediaTime(1, 2), quantized.Snapshot.Position);
        controller.AcknowledgePresentation(quantized);
        controller.Seek(new(3));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var end = await NextFrame(inbox.Reader);
        Assert.Equal(new MediaTime(23, 24), end.Snapshot.Position);
        controller.AcknowledgePresentation(end);
        controller.CancelScrub();
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var restored = await NextFrame(inbox.Reader);
        Assert.Equal(new MediaTime(1, 2), restored.Snapshot.Position);
        Assert.True(restored.Snapshot.IsPlaying);
        Assert.False(restored.Snapshot.IsScrubbing);
        controller.AcknowledgePresentation(restored);

        controller.BeginScrub();
        controller.Seek(new(-1));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var start = await NextFrame(inbox.Reader);
        Assert.Equal(MediaTime.Zero, start.Snapshot.Position);
        controller.AcknowledgePresentation(start);
        controller.EndScrub();
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var committed = await NextFrame(inbox.Reader);
        Assert.Equal(MediaTime.Zero, committed.Snapshot.Position);
        Assert.True(committed.Snapshot.IsPlaying);
        Assert.False(committed.Snapshot.IsScrubbing);
    }

    /// <summary>仅输出 DPI 变化保持暂停位置，等价元数据保留当前规范请求和帧。</summary>
    [Fact]
    public async Task PresentationChangesPreservePositionWhileEquivalentMetadataPreservesTheCanonicalRequest()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        await using var controller = CreateController(inbox, clock, out var created);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Original", new());
        var original = Request(input: new(BuiltinEffectScripts.Get("fade-in").Source, new(), preset));
        controller.Submit(original, autoPlay: false, debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        controller.AcknowledgePresentation(await NextFrame(inbox.Reader));
        controller.Seek(new(1, 2));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var paused = await NextFrame(inbox.Reader);
        controller.AcknowledgePresentation(paused);
        var before = controller.Snapshot;
        var renamed = new EffectScriptPreviewRequest(original.DraftId, 42, original.Input with
        {
            StylePreset = preset with { Id = Guid.NewGuid(), Name = "Renamed", TimingPostProcessor = new() { LeadInMilliseconds = 600 } }
        });
        Assert.False(controller.Submit(renamed));
        Assert.Same(original, controller.Snapshot.Request);
        Assert.Equal(before.Generation, controller.Snapshot.Generation);
        Assert.Equal(new MediaTime(1, 2), controller.Snapshot.Position);
        Assert.False(controller.Snapshot.IsBusy);
        Assert.False(controller.Snapshot.IsPlaying);

        var dpi = new EffectScriptPreviewRequest(original.DraftId, 43, renamed.Input, 2);
        Assert.True(controller.Submit(dpi));
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var resized = await NextFrame(inbox.Reader);
        Assert.Equal(new MediaTime(1, 2), resized.Snapshot.Position);
        Assert.False(resized.Snapshot.IsPlaying);
        Assert.Same(dpi, resized.Snapshot.Request);
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, renderer.Calls.Count);
    }

    /// <summary>错误只交付给本地预览，停止播放且修复输入后恢复渲染。</summary>
    [Fact]
    public async Task RenderingFailureClearsTheFrameStopsPlaybackAndAChangedDraftCanRecover()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        var failure = new InvalidDataException("Preview failed.");
        await using var controller = CreateController(inbox, clock, out var created, (request, _, _) =>
        {
            if (request.Input.Options.Text == "Broken")
            {
                throw failure;
            }
            return new(1, 1, [0, 0, 0, 255]);
        });
        var original = Request(input: new(BuiltinEffectScripts.Get("fade-in").Source, new() { Text = "Broken" }));
        controller.Submit(original, debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var failed = await Next(inbox.Reader, update => update.Snapshot.Error is not null);
        Assert.Same(failure, failed.Snapshot.Error);
        Assert.True(failed.ClearFrame);
        Assert.Null(failed.Frame);
        Assert.False(failed.Snapshot.IsPlaying);
        Assert.False(failed.Snapshot.IsBusy);
        Assert.True(controller.IsCurrent(failed));
        Assert.True(controller.Completion.IsCompletedSuccessfully);
        clock.Advance(TimeSpan.FromSeconds(10));
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(renderer.Calls);
        Assert.Equal(0, clock.ActiveTimerCount);

        var repaired = Request(original.DraftId, original.Input with { Options = original.Input.Options with { Text = "Repaired" } });
        controller.Submit(repaired, debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var delivered = await NextFrame(inbox.Reader);
        Assert.Null(delivered.Snapshot.Error);
        Assert.True(delivered.Snapshot.IsPlaying);
        Assert.Same(repaired, delivered.Snapshot.Request);
        Assert.Equal(2, renderer.Calls.Count);
        Assert.Single(renderer.Calls.Select(call => call.Thread).Distinct());
    }

    /// <summary>关闭立即撤销交付，异步等待不响应取消的工作结束，并在创建线程只释放一次。</summary>
    [Fact]
    public async Task DisposeAsyncWaitsForNativeWorkAndDisposesOnItsDedicatedCreatingThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        await using var controller = CreateController(inbox, clock, out var created, (_, _, _) =>
        {
            entered.TrySetResult();
            release.Wait(CancellationToken.None);
            return new(1, 1, [0, 0, 0, 255]);
        });
        try
        {
            controller.Submit(Request(), debounce: false);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disposal = controller.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            Assert.False(controller.Snapshot.IsVisible);
            Assert.False(controller.Snapshot.IsPlaying);
            Assert.False(controller.Submit(Request(), debounce: false));
            release.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            await controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(callerThread, renderer.CreationThread);
            Assert.Equal(renderer.CreationThread, Assert.Single(renderer.Calls).Thread);
            Assert.Equal(renderer.CreationThread, renderer.DisposalThread);
            Assert.Equal(1, renderer.DisposalCount);
            Assert.Equal(1, renderer.MaximumConcurrency);
            Assert.Equal(0, clock.ActiveTimerCount);
            Assert.DoesNotContain(ReadAll(inbox.Reader), update => update.Frame is not null);
        }
        finally
        {
            release.Set();
        }
    }

    /// <summary>失效无需确认待呈现帧，撤销计时器和请求后仍可接收新草稿。</summary>
    [Fact]
    public async Task InvalidatingAnUnpresentedFrameClearsItsRequestAndAllowsANewDraft()
    {
        var clock = new ManualPlaybackTimeProvider();
        var inbox = Channel.CreateUnbounded<EffectScriptPreviewUpdate>();
        await using var controller = CreateController(inbox, clock, out var created);
        controller.Submit(Request(), debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var old = await NextFrame(inbox.Reader);
        controller.Invalidate();
        Assert.Null(controller.Snapshot.Request);
        Assert.False(controller.Snapshot.IsPlaying);
        Assert.False(controller.Snapshot.IsBusy);
        Assert.False(controller.IsCurrent(old));
        controller.AcknowledgePresentation(old);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, clock.ActiveTimerCount);
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(renderer.Calls);

        var replacement = Request();
        controller.Submit(replacement, autoPlay: false, debounce: false);
        await controller.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var delivered = await NextFrame(inbox.Reader);
        Assert.Same(replacement, delivered.Snapshot.Request);
        Assert.Equal(MediaTime.Zero, delivered.Snapshot.Position);
        Assert.True(controller.IsCurrent(delivered));
    }

    private static EffectScriptPreviewController CreateController(Channel<EffectScriptPreviewUpdate> inbox,
        TimeProvider clock, out Task<EffectScriptPreviewTestRenderer> rendererTask,
        Func<EffectScriptPreviewRequest, MediaTime, CancellationToken, SdrVideoFrame>? render = null)
    {
        var created = new TaskCompletionSource<EffectScriptPreviewTestRenderer>(TaskCreationOptions.RunContinuationsAsynchronously);
        rendererTask = created.Task;
        return new(update => inbox.Writer.TryWrite(update), () =>
        {
            var renderer = new EffectScriptPreviewTestRenderer(render);
            created.SetResult(renderer);
            return renderer;
        }, clock);
    }

    private static EffectScriptPreviewRequest Request(Guid? draftId = null, EffectScriptPreviewInput? input = null)
    {
        return new(draftId ?? Guid.NewGuid(), 1, input ?? new(BuiltinEffectScripts.Get("fade-in").Source, new()));
    }

    private static Task<EffectScriptPreviewUpdate> NextFrame(ChannelReader<EffectScriptPreviewUpdate> inbox)
    {
        return Next(inbox, update => update.Frame is not null);
    }

    private static async Task<EffectScriptPreviewUpdate> Next(ChannelReader<EffectScriptPreviewUpdate> inbox,
        Func<EffectScriptPreviewUpdate, bool> predicate)
    {
        while (true)
        {
            var update = await inbox.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            if (predicate(update))
            {
                return update;
            }
        }
    }

    private static IEnumerable<EffectScriptPreviewUpdate> ReadAll(ChannelReader<EffectScriptPreviewUpdate> inbox)
    {
        while (inbox.TryRead(out var update))
        {
            yield return update;
        }
    }
}
