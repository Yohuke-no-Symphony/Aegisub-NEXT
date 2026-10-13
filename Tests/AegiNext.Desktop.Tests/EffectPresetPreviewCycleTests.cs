using System.Threading.Channels;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests;

/// <summary>验证末帧呈现确认、静态队列公平性和循环等待的取消边界。</summary>
public sealed class EffectPresetPreviewCycleTests
{
    /// <summary>末帧未确认时保持当前循环，新增可见卡片的静态海报仍会交付。</summary>
    [Fact]
    public async Task AwaitingClosingFrameConfirmationKeepsTheCycleAndRendersOtherVisiblePosters()
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = CreateScheduler(inbox, out var created);
        var a = EffectPresetPreviewTestSources.Request("a");
        await PrimeVisible(scheduler, inbox.Reader, a);
        scheduler.SetActive(a.PresetId);
        var closing = await Next(inbox.Reader, result => result.CompletesCycle);
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        var animatedCalls = renderer.Calls.Count(call => call.Request.PresetId == a.PresetId);

        Assert.Equal(new MediaTime(1, 12), closing.Position);
        Assert.True(closing.IsAnimated);
        Assert.True(scheduler.IsCurrent(closing));
        Assert.False(scheduler.Completion.IsCompleted);
        var b = EffectPresetPreviewTestSources.Request("b", source: BuiltinEffectScripts.Get("fade-out").Source);
        scheduler.UpdateVisible([a, b]);
        var poster = await Next(inbox.Reader, result => result.Request.PresetId == b.PresetId);
        Assert.False(poster.IsAnimated);
        Assert.False(poster.CompletesCycle);
        Assert.Equal(new MediaTime(1, 20), poster.Position);
        Assert.True(scheduler.IsCurrent(poster));
        await AssertNoResults(inbox.Reader);
        Assert.Equal(animatedCalls, renderer.Calls.Count(call => call.Request.PresetId == a.PresetId));
        Assert.False(scheduler.Completion.IsCompleted);
        Assert.True(scheduler.IsCurrent(closing));
    }

    /// <summary>只有原末帧引用能确认循环，合法确认后的首帧从零开始，旧确认不会完成下一轮。</summary>
    [Fact]
    public async Task AcknowledgingTheExactClosingFrameRestartsAtZeroAndRejectsDuplicateAcknowledgments()
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = CreateScheduler(inbox, out _);
        var request = EffectPresetPreviewTestSources.Request();
        await PrimeVisible(scheduler, inbox.Reader, request);
        scheduler.SetActive(request.PresetId);
        var closing = await Next(inbox.Reader, result => result.CompletesCycle);

        scheduler.CompleteCycle(closing with { });
        await AssertNoResults(inbox.Reader);
        scheduler.CompleteCycle(closing);
        var first = await Next(inbox.Reader, result => result.IsAnimated);
        Assert.Equal(MediaTime.Zero, first.Position);
        Assert.False(first.CompletesCycle);
        var nextClosing = await Next(inbox.Reader, result => result.CompletesCycle);
        Assert.NotSame(closing, nextClosing);
        scheduler.CompleteCycle(closing);
        scheduler.CompleteCycle(closing);
        await AssertNoResults(inbox.Reader);
        Assert.True(scheduler.IsCurrent(nextClosing));
        Assert.False(scheduler.Completion.IsCompleted);

        scheduler.CompleteCycle(nextClosing);
        var restarted = await Next(inbox.Reader, result => result.IsAnimated);
        Assert.Equal(MediaTime.Zero, restarted.Position);
        Assert.False(restarted.CompletesCycle);
    }

    /// <summary>切卡后返回或替换请求引用、源和 DPI 时，先前派发的末帧确认不能解锁当前循环。</summary>
    [Theory]
    [InlineData("reactivated")]
    [InlineData("source")]
    [InlineData("dpi")]
    [InlineData("identity")]
    public async Task OldClosingFrameAcknowledgmentsCannotResumeReactivatedOrReplacedRequests(string replacement)
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = CreateScheduler(inbox, out _);
        var a = EffectPresetPreviewTestSources.Request("a");
        var b = EffectPresetPreviewTestSources.Request("b");
        await PrimeVisible(scheduler, inbox.Reader, a, b);
        scheduler.SetActive(a.PresetId);
        var oldClosing = await Next(inbox.Reader, result => result.CompletesCycle);
        EffectPresetPreviewRequest currentRequest;
        if (replacement == "reactivated")
        {
            scheduler.SetActive(b.PresetId);
            _ = await Next(inbox.Reader, result => result.Request.PresetId == b.PresetId && result.CompletesCycle);
            scheduler.SetActive(a.PresetId);
            currentRequest = a;
        }
        else
        {
            currentRequest = replacement switch
            {
                "identity" => a with { },
                "source" => EffectPresetPreviewTestSources.Request("a", revision: 2, source: BuiltinEffectScripts.Get("fade-out").Source),
                _ => EffectPresetPreviewTestSources.Request("a", scaling: 2)
            };
            scheduler.UpdateVisible([currentRequest, b]);
        }
        Assert.False(scheduler.IsCurrent(oldClosing));
        var currentClosing = await Next(inbox.Reader,
            result => result.Request.PresetId == a.PresetId && result.CompletesCycle && result.Generation != oldClosing.Generation);

        Assert.Same(currentRequest, currentClosing.Request);
        Assert.True(scheduler.IsCurrent(currentClosing));
        scheduler.CompleteCycle(oldClosing);
        scheduler.CompleteCycle(oldClosing with
        {
            Request = currentClosing.Request,
            Generation = currentClosing.Generation
        });
        await AssertNoResults(inbox.Reader);
        Assert.True(scheduler.IsCurrent(currentClosing));
        Assert.False(scheduler.Completion.IsCompleted);
        scheduler.CompleteCycle(currentClosing);
        var first = await Next(inbox.Reader, result => result.IsAnimated && result.Request.PresetId == a.PresetId);
        Assert.Equal(MediaTime.Zero, first.Position);
        Assert.Same(currentRequest, first.Request);
    }

    /// <summary>等待末帧确认时隐藏或关闭不需要 UI 确认，迟到确认不会阻止释放或恢复。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidatingOrDisposingWhileAwaitingConfirmationCompletesWithoutAnAcknowledgment(bool dispose)
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = CreateScheduler(inbox, out var created);
        var request = EffectPresetPreviewTestSources.Request();
        await PrimeVisible(scheduler, inbox.Reader, request);
        scheduler.SetActive(request.PresetId);
        var closing = await Next(inbox.Reader, result => result.CompletesCycle);
        var completion = scheduler.Completion;
        Assert.False(completion.IsCompleted);
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        var calls = renderer.Calls.Count;

        if (dispose)
        {
            await scheduler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(renderer.CreationThread, renderer.DisposalThread);
        }
        else
        {
            scheduler.Invalidate();
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.False(scheduler.IsCurrent(closing));
        scheduler.CompleteCycle(closing);
        await AssertNoResults(inbox.Reader);
        Assert.Equal(calls, renderer.Calls.Count);
        Assert.True(completion.IsCompletedSuccessfully);
        if (!dispose)
        {
            scheduler.UpdateVisible([request]);
            scheduler.SetActive(request.PresetId);
            var first = await Next(inbox.Reader, result => result.IsAnimated);
            Assert.Equal(MediaTime.Zero, first.Position);
            Assert.NotEqual(closing.Generation, first.Generation);
            Assert.True(scheduler.IsCurrent(first));
        }
    }

    /// <summary>结束帧渲染失败时交付具体错误并停止重试，不进入无人能够确认的等待。</summary>
    [Fact]
    public async Task AClosingFrameErrorBecomesIdleWithoutWaitingForConfirmation()
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        var failure = new InvalidDataException("Closing frame failed.");
        await using var scheduler = CreateScheduler(inbox, out var created, (_, position, _) =>
        {
            if (position == new MediaTime(1, 12))
            {
                throw failure;
            }
            return new(1, 1, [0, 0, 0, 255]);
        });
        var request = EffectPresetPreviewTestSources.Request();
        await PrimeVisible(scheduler, inbox.Reader, request);
        scheduler.SetActive(request.PresetId);
        var closing = await Next(inbox.Reader, result => result.CompletesCycle);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var renderer = await created.WaitAsync(TimeSpan.FromSeconds(5));
        var calls = renderer.Calls.Count;

        Assert.Null(closing.Frame);
        Assert.Same(failure, closing.Error);
        Assert.True(scheduler.IsCurrent(closing));
        scheduler.CompleteCycle(closing);
        await AssertNoResults(inbox.Reader);
        Assert.Equal(calls, renderer.Calls.Count);
        Assert.Single(renderer.Calls, call => call.Position == new MediaTime(1, 12));
        scheduler.SetActive(null);
        scheduler.SetActive(request.PresetId);
        var restarted = await Next(inbox.Reader, result => result.IsAnimated);
        Assert.Equal(MediaTime.Zero, restarted.Position);
        Assert.NotEqual(closing.Generation, restarted.Generation);
        Assert.Null(restarted.Error);
    }

    private static EffectPresetPreviewScheduler CreateScheduler(Channel<EffectPresetPreviewResult> inbox,
        out Task<EffectPresetPreviewTestRenderer> rendererTask,
        Func<EffectPresetPreviewRequest, MediaTime, CancellationToken, SdrVideoFrame>? render = null)
    {
        var created = new TaskCompletionSource<EffectPresetPreviewTestRenderer>(TaskCreationOptions.RunContinuationsAsynchronously);
        rendererTask = created.Task;
        return new(result => inbox.Writer.TryWrite(result), new() { Duration = new(1, 10) }, () =>
        {
            var renderer = new EffectPresetPreviewTestRenderer(render);
            created.SetResult(renderer);
            return renderer;
        }, deferLoopUntilPresented: true);
    }

    private static async Task PrimeVisible(EffectPresetPreviewScheduler scheduler,
        ChannelReader<EffectPresetPreviewResult> inbox, params EffectPresetPreviewRequest[] requests)
    {
        scheduler.UpdateVisible(requests);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        while (inbox.TryRead(out _))
        {
        }
    }

    private static async Task AssertNoResults(ChannelReader<EffectPresetPreviewResult> inbox)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        Assert.False(inbox.TryRead(out _));
    }

    private static async Task<EffectPresetPreviewResult> Next(ChannelReader<EffectPresetPreviewResult> inbox,
        Func<EffectPresetPreviewResult, bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var result = await inbox.ReadAsync(cancellation.Token);
            if (predicate(result))
            {
                return result;
            }
        }
    }
}
