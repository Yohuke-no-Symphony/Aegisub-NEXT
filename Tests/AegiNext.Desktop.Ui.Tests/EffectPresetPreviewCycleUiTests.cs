using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证界面积压跨过循环边界时，退出帧不会被下一轮首帧覆盖。</summary>
public sealed class EffectPresetPreviewCycleUiTests
{
    /// <summary>每卡仍只有一个待处理结果，完整退出画面先显示，再从零开始下一轮。</summary>
    [AvaloniaFact]
    public async Task BusyUiPresentsTheClosingFrameBeforeAnyFrameFromTheNextCycle()
    {
        var drains = new ConcurrentQueue<Action>();
        await using var host = await EffectPresetPanelTestHost.CreateAsync(postDrain: drains.Enqueue);
        await host.View.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Drain(drains);
        host.Flush();
        var card = host.Card("fade-out");
        var source = host.Session.DocumentSnapshot;
        host.Window.FocusManager.Focus(null);
        host.Window.MouseMove(host.Center(card));
        host.Flush();
        await host.WaitUntilAsync(() => host.Renderer.Rendered.Any(frame => frame.Request.PresetId == "fade-out" && frame.Position == MediaTime.Zero));
        await Task.Delay(TimeSpan.FromMilliseconds(3300), TestContext.Current.CancellationToken);
        host.Flush();
        Assert.Single(drains);
        Assert.Equal(1, host.View.PendingPreviewCount);
        var previousStarts = host.Renderer.Rendered.Count(frame => frame.Request.PresetId == "fade-out" && frame.Position == MediaTime.Zero);
        Assert.True(drains.TryDequeue(out var presentClosing));
        presentClosing();

        Assert.True(card.IsPreviewAnimating);
        Assert.Equal(new MediaTime(71, 24), card.PresentedPosition);
        await host.WaitUntilAsync(() => host.Renderer.Rendered.Count(frame => frame.Request.PresetId == "fade-out" && frame.Position == MediaTime.Zero) > previousStarts);
        Drain(drains);
        Assert.True(card.PresentedPosition < new MediaTime(3, 10));
        host.Window.MouseMove(new(2, 2));
        host.Flush();
        await host.View.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Drain(drains);
        Assert.Equal(new MediaTime(3, 2), card.PresentedPosition);
        Assert.False(card.IsPreviewAnimating);
        Assert.Same(source, host.Session.DocumentSnapshot);
        Assert.False(host.Session.Editor.CanUndo);
    }

    private static void Drain(ConcurrentQueue<Action> drains)
    {
        while (drains.TryDequeue(out var action))
        {
            action();
        }
    }
}
