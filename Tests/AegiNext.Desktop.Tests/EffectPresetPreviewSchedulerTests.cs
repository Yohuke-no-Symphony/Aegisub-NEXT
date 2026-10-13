using System.Threading.Channels;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;

namespace AegiNext.Desktop.Tests;

/// <summary>验证预览工作者的公平队列、生命周期和双重迟到结果校验。</summary>
public sealed class EffectPresetPreviewSchedulerTests
{
    /// <summary>可见静态卡片使用时长中点，重复可见同步和重新附着复用有界静态缓存。</summary>
    [Fact]
    public async Task StaticCardsUseTheMidpointAndReuseCachedPostersAfterReattachment()
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        var created = new TaskCompletionSource<EffectPresetPreviewTestRenderer>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result), createRenderer: () =>
        {
            var renderer = new EffectPresetPreviewTestRenderer();
            created.SetResult(renderer);
            return renderer;
        });
        var a = EffectPresetPreviewTestSources.Request("a");
        var b = EffectPresetPreviewTestSources.Request("b", source: BuiltinEffectScripts.Get("fade-out").Source);
        scheduler.UpdateVisible([a, b]);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var first = await Next(inbox.Reader, result => result.Request.PresetId == "a");
        var second = await Next(inbox.Reader, result => result.Request.PresetId == "b");
        var renderer = await created.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new MediaTime(3, 2), first.Position);
        Assert.Equal(first.Position, second.Position);
        Assert.False(first.IsAnimated);
        Assert.True(scheduler.IsCurrent(first));
        scheduler.UpdateVisible([a, b]);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, renderer.Calls.Count);
        scheduler.Invalidate();
        Assert.False(scheduler.IsCurrent(first));
        scheduler.UpdateVisible([a, b]);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var reattached = await Next(inbox.Reader, result => result.Request.PresetId == "a");

        Assert.NotEqual(first.Generation, reattached.Generation);
        Assert.False(scheduler.IsCurrent(first));
        Assert.True(scheduler.IsCurrent(reattached));
        Assert.Equal(2, renderer.Calls.Count);
        Assert.Equal(2, scheduler.CacheStatistics.Hits);
    }

    /// <summary>滚出可见区取消当前工作，等待队列仅渲染仍可见的最新请求。</summary>
    [Fact]
    public async Task VisibleReplacementCancelsOldWorkAndKeepsOneOwningThread()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        var created = new TaskCompletionSource<EffectPresetPreviewTestRenderer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result), createRenderer: () =>
        {
            var renderer = new EffectPresetPreviewTestRenderer((request, _, token) =>
            {
                if (request.PresetId == "a")
                {
                    entered.SetResult();
                    Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
                }
                token.ThrowIfCancellationRequested();
                return new(1, 1, [0, 0, 0, 255]);
            });
            created.SetResult(renderer);
            return renderer;
        });
        try
        {
            scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request("a")]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request("b")]);
            scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request("c")]);
            unblock.Set();
            await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            var result = await Next(inbox.Reader, _ => true);
            var renderer = await created.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("c", result.Request.PresetId);
            Assert.Collection(renderer.Calls,
                call => Assert.Equal("a", call.Request.PresetId),
                call => Assert.Equal("c", call.Request.PresetId));
            Assert.Equal(1, renderer.MaximumConcurrency);
            Assert.All(renderer.Calls, call => Assert.Equal(renderer.CreationThread, call.Thread));
            await scheduler.DisposeAsync();
            Assert.Equal(renderer.CreationThread, renderer.DisposalThread);
            Assert.Equal(0, scheduler.CacheStatistics.Bytes);
        }
        finally
        {
            unblock.Set();
            await scheduler.DisposeAsync();
        }
    }

    /// <summary>快速 A、B、A 切换让先前派发的动画永久失效，动画期间海报也不能覆盖帧。</summary>
    [Fact]
    public async Task AnimationGenerationsRejectEarlierDispatchesAfterReturningToTheSameCard()
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result),
            createRenderer: () => new EffectPresetPreviewTestRenderer());
        scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request("a"), EffectPresetPreviewTestSources.Request("b")]);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var poster = await Next(inbox.Reader, result => result.Request.PresetId == "a");
        scheduler.SetActive("a");
        var first = await Next(inbox.Reader, result => result.IsAnimated && result.Request.PresetId == "a");

        Assert.False(scheduler.Completion.IsCompleted);
        Assert.False(scheduler.IsCurrent(poster));
        Assert.True(scheduler.IsCurrent(first));
        scheduler.SetActive("b");
        _ = await Next(inbox.Reader, result => result.IsAnimated && result.Request.PresetId == "b");
        scheduler.SetActive("a");
        var returning = await Next(inbox.Reader, result => result.IsAnimated && result.Request.PresetId == "a" && result.Generation != first.Generation);

        Assert.False(scheduler.IsCurrent(first));
        Assert.True(scheduler.IsCurrent(returning));
        Assert.NotEqual(first.Generation, returning.Generation);
        scheduler.SetActive(null);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var restored = await Next(inbox.Reader, result => !result.IsAnimated && result.Request.PresetId == "a");

        Assert.False(scheduler.IsCurrent(returning));
        Assert.True(scheduler.IsCurrent(restored));
        Assert.Equal(new MediaTime(3, 2), restored.Position);
    }

    /// <summary>源修订或 DPI 替换会拒绝已派发旧帧，并生成新的静态缓存项。</summary>
    [Fact]
    public async Task ChangedSourceAndDpiInvalidatePostedResultsAndTheirStaticCacheIdentity()
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result),
            createRenderer: () => new EffectPresetPreviewTestRenderer());
        scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request()]);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var old = await Next(inbox.Reader, _ => true);
        scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request(revision: 2, scaling: 2,
            source: BuiltinEffectScripts.Get("fade-out").Source)]);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var current = await Next(inbox.Reader, _ => true);

        Assert.False(scheduler.IsCurrent(old));
        Assert.True(scheduler.IsCurrent(current));
        Assert.Equal(2, scheduler.CacheStatistics.Count);
        Assert.Equal(2, scheduler.CacheStatistics.Misses);
    }

    /// <summary>旧工作跨越播放周期才结束时，新目标的实际首帧仍从零开始。</summary>
    [Fact]
    public async Task NewAnimationStartsAtZeroAfterWaitingForASlowCancelledPoster()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result), createRenderer: () =>
            new EffectPresetPreviewTestRenderer((request, _, _) =>
            {
                if (request.PresetId == "old")
                {
                    entered.TrySetResult();
                    Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
                }
                return new(1, 1, [0, 0, 0, 255]);
            }));
        try
        {
            scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request("old"), EffectPresetPreviewTestSources.Request("new")]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scheduler.SetActive("new");
            await Task.Delay(TimeSpan.FromMilliseconds(150));
            unblock.Set();
            var first = await Next(inbox.Reader, result => result.IsAnimated && result.Request.PresetId == "new");

            Assert.Equal(MediaTime.Zero, first.Position);
            scheduler.SetActive(null);
            await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            unblock.Set();
        }
    }

    /// <summary>慢于播放周期的渲染仍轮流处理其它可见卡片，同时跳过过期动画帧。</summary>
    [Fact]
    public async Task SlowAnimationRenderingDoesNotStarveOtherVisiblePosters()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result), createRenderer: () =>
            new EffectPresetPreviewTestRenderer((request, _, token) =>
            {
                if (request.PresetId == "a")
                {
                    entered.TrySetResult();
                    Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
                }
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
                token.ThrowIfCancellationRequested();
                return new(1, 1, [0, 0, 0, 255]);
            }));
        try
        {
            scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request("a"), EffectPresetPreviewTestSources.Request("b"),
                EffectPresetPreviewTestSources.Request("c")]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scheduler.SetActive("a");
            unblock.Set();
            _ = await Next(inbox.Reader, result => !result.IsAnimated && result.Request.PresetId == "b");
            _ = await Next(inbox.Reader, result => !result.IsAnimated && result.Request.PresetId == "c");
            var latest = await Next(inbox.Reader, result => result.IsAnimated && result.Position >= new MediaTime(3, 24));

            Assert.True(scheduler.IsCurrent(latest));
            Assert.True(latest.Position >= new MediaTime(3, 24) && latest.Position < new MediaTime(3));
            scheduler.SetActive(null);
            await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            unblock.Set();
        }
    }

    /// <summary>停止后的海报恢复即使再被切卡取消，也必须最终重排并送达。</summary>
    [Fact]
    public async Task ACancelledPosterRestoreIsRetriedWhileAnotherCardAnimates()
    {
        var restoring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var aPosters = 0;
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result),
            new() { StaticCacheBytes = 0 }, () => new EffectPresetPreviewTestRenderer((request, position, token) =>
            {
                if (request.PresetId == "a" && position == new MediaTime(3, 2) && Interlocked.Increment(ref aPosters) == 2)
                {
                    restoring.SetResult();
                    Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
                }
                token.ThrowIfCancellationRequested();
                return new(1, 1, [0, 0, 0, 255]);
            }));
        try
        {
            scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request("a"), EffectPresetPreviewTestSources.Request("b")]);
            await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            while (inbox.Reader.TryRead(out _))
            {
            }
            scheduler.SetActive("a");
            _ = await Next(inbox.Reader, result => result.IsAnimated && result.Request.PresetId == "a");
            scheduler.SetActive(null);
            await restoring.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scheduler.SetActive("b");
            unblock.Set();
            var restored = await Next(inbox.Reader, result => !result.IsAnimated && result.Request.PresetId == "a");

            Assert.Equal(3, aPosters);
            Assert.True(scheduler.IsCurrent(restored));
            scheduler.SetActive(null);
            await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            unblock.Set();
        }
    }

    /// <summary>临时隐藏或关闭会取消慢工作，即使渲染器忽略取消也不交付迟到帧。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidatingOrClosingRejectsLateFramesAndWaitsForNativeDisposal(bool close)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var results = 0;
        var scheduler = new EffectPresetPreviewScheduler(_ => Interlocked.Increment(ref results), createRenderer: () =>
            new EffectPresetPreviewTestRenderer((_, _, _) =>
            {
                entered.SetResult();
                Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
                return new(1, 1, [0, 0, 0, 255]);
            }));
        try
        {
            scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request()]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var completion = scheduler.Completion;
            Task? disposal = null;
            if (close)
            {
                disposal = scheduler.DisposeAsync().AsTask();
                Assert.False(disposal.IsCompleted);
            }
            else
            {
                scheduler.Invalidate();
            }
            unblock.Set();
            await (disposal ?? completion).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, results);
            Assert.Equal(0, scheduler.CacheStatistics.Count);
        }
        finally
        {
            unblock.Set();
            await scheduler.DisposeAsync();
        }
    }

    /// <summary>动画未写入全帧缓存；停止播放只恢复已有的静态海报。</summary>
    [Fact]
    public async Task AnimationFramesBypassTheStaticCacheAndRestoreThePosterOnStop()
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result),
            createRenderer: () => new EffectPresetPreviewTestRenderer());
        scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request()]);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var poster = await Next(inbox.Reader, _ => true);
        var bytes = scheduler.CacheStatistics.Bytes;
        scheduler.SetActive("card");
        for (var index = 0; index < 4; index++)
        {
            _ = await Next(inbox.Reader, result => result.IsAnimated);
        }

        Assert.Equal(1, scheduler.CacheStatistics.Count);
        Assert.Equal(bytes, scheduler.CacheStatistics.Bytes);
        Assert.Equal(0, scheduler.CacheStatistics.Hits);
        scheduler.SetActive(null);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var restored = await Next(inbox.Reader, result => !result.IsAnimated);
        Assert.Same(poster.Frame, restored.Frame);
        Assert.Equal(1, scheduler.CacheStatistics.Hits);
    }

    /// <summary>真实样例失败作为卡片结果交付，失败动画停止重复渲染并恢复空闲完成态。</summary>
    [Fact]
    public async Task SpecificSampleFailuresAreDeliveredAndFailedAnimationBecomesIdle()
    {
        var inbox = Channel.CreateUnbounded<EffectPresetPreviewResult>();
        await using var scheduler = new EffectPresetPreviewScheduler(result => inbox.Writer.TryWrite(result));
        scheduler.UpdateVisible([EffectPresetPreviewTestSources.Request(source: EffectPresetPreviewTestSources.RANGE)]);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var poster = await Next(inbox.Reader, _ => true);

        Assert.Null(poster.Frame);
        Assert.IsType<EffectScriptException>(poster.Error);
        Assert.Contains("完整字素范围", poster.Error.Message, StringComparison.Ordinal);
        scheduler.SetActive("card");
        var animation = await Next(inbox.Reader, result => result.IsAnimated);
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(scheduler.IsCurrent(animation));
        Assert.False(scheduler.IsCurrent(poster));
        Assert.Equal(poster.Error.Message, animation.Error!.Message);
        Assert.Equal(0, scheduler.CacheStatistics.Count);
        Assert.False(inbox.Reader.TryRead(out _));
    }

    private static async Task<EffectPresetPreviewResult> Next(ChannelReader<EffectPresetPreviewResult> reader,
        Func<EffectPresetPreviewResult, bool> predicate)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var result = await reader.ReadAsync(cancellation.Token);
            if (predicate(result))
            {
                return result;
            }
        }
    }
}
