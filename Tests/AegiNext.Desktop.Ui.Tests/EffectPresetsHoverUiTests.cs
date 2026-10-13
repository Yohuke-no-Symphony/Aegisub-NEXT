using System.Collections.Concurrent;
using System.Diagnostics;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证预设卡片提示只展示实际错误，单次鼠标进入后预览持续完成退场及循环。</summary>
public sealed class EffectPresetsHoverUiTests
{
    /// <summary>健康卡片没有提示内容，真实鼠标驻留和显式提示打开均不能产生空 Tooltip。</summary>
    [AvaloniaFact]
    public async Task HealthyCardHasNoTooltipAndTheServiceCannotOpenAnEmptyPopup()
    {
        await using var host = await EffectPresetPanelTestHost.CreateAsync();
        var card = host.Card("fade-in-out");
        await host.WaitUntilAsync(() => card.PresentedPosition is not null);
        var tooltipOwner = Assert.IsType<Grid>(card.Content);
        Assert.False(card.ViewModel.HasPreviewError);
        Assert.Null(ToolTip.GetTip(tooltipOwner));

        host.Window.FocusManager.Focus(null);
        Assert.Null(host.Window.FocusManager.GetFocusedElement());
        ToolTip.SetShowDelay(tooltipOwner, 20);
        host.Window.MouseMove(host.Center(card));
        host.Flush();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        host.Flush();
        Assert.False(ToolTip.GetIsOpen(tooltipOwner));

        try
        {
            ToolTip.SetIsOpen(tooltipOwner, true);
            host.Flush();
            Assert.False(ToolTip.GetIsOpen(tooltipOwner));
            Assert.Null(ToolTip.GetTip(tooltipOwner));
        }
        finally
        {
            ToolTip.SetIsOpen(tooltipOwner, false);
        }
    }

    /// <summary>预览错误显示具体提示，鼠标仍驻留时成功结果会关闭已打开的错误 Tooltip。</summary>
    [AvaloniaFact]
    public async Task FailedCardShowsItsActualErrorAndSuccessfulResultClosesTheOpenTooltip()
    {
        var failures = new ConcurrentDictionary<string, Exception>(StringComparer.Ordinal)
        {
            ["fade-out"] = new InvalidDataException("Missing mask node.")
        };
        var renderer = new EffectPresetPanelTestRenderer(request => failures.GetValueOrDefault(request.PresetId));
        await using var host = await EffectPresetPanelTestHost.CreateAsync(renderer);
        var card = host.Card("fade-out");
        await host.WaitUntilAsync(() => card.ViewModel.HasPreviewError);
        var tooltipOwner = Assert.IsType<Grid>(card.Content);
        var tip = Assert.IsType<string>(ToolTip.GetTip(tooltipOwner));
        Assert.Contains("Missing mask node.", tip);
        Assert.Equal(card.ViewModel.PreviewStatus, tip);
        ToolTip.SetShowDelay(tooltipOwner, 20);
        host.Window.FocusManager.Focus(null);
        host.Window.MouseMove(host.Center(card));
        host.Flush();
        try
        {
            await host.WaitUntilAsync(() => ToolTip.GetIsOpen(tooltipOwner));
            Assert.True(ToolTip.GetIsOpen(tooltipOwner));
            Assert.Contains("Missing mask node.", Assert.IsType<string>(ToolTip.GetTip(tooltipOwner)));
            var request = Assert.IsType<EffectPresetPreviewRequest>(card.Request);
            var position = new MediaTime(3, 2);
            Assert.True(failures.TryRemove("fade-out", out _));
            var frame = renderer.Render(request, position, TestContext.Current.CancellationToken);
            Assert.True(ToolTip.GetIsOpen(tooltipOwner));
            card.Present(new(request, 0, position, true, frame, null));
            host.Flush();

            Assert.False(card.ViewModel.HasPreviewError);
            Assert.Null(ToolTip.GetTip(tooltipOwner));
            Assert.False(ToolTip.GetIsOpen(tooltipOwner));
            Assert.True(card.IsPreviewAnimating);
            Assert.Same(request, card.Request);
            Assert.Same(card, host.Card("fade-out"));
        }
        finally
        {
            ToolTip.SetIsOpen(tooltipOwner, false);
        }
    }

    /// <summary>不点击或反复移动鼠标，预设在同一请求下播放至退场并完成三秒循环，海报帧不覆盖活动预览。</summary>
    [AvaloniaTheory]
    [InlineData("fade-in-out")]
    [InlineData("fade-out")]
    [InlineData("pop-out")]
    public async Task OnePointerMoveWithoutFocusPlaysThroughTheOutroAndLoopsWithoutPosterReplacement(string presetId)
    {
        await using var host = await EffectPresetPanelTestHost.CreateAsync();
        var source = host.Session.DocumentSnapshot;
        var playhead = host.Session.ViewModel.Preview.Position;
        var card = host.Card(presetId);
        card.BringIntoView();
        host.Flush();
        host.Window.FocusManager.Focus(null);
        Assert.Null(host.Window.FocusManager.GetFocusedElement());
        var renderedBefore = host.Renderer.Rendered.Count(frame => frame.Request.PresetId == presetId);
        var elapsed = Stopwatch.StartNew();
        host.Window.MouseMove(host.Center(card));
        host.Flush();
        await host.WaitUntilAsync(() => card.IsPreviewAnimating);
        var request = card.Request;
        Assert.NotNull(request);
        var revision = request.Revision;
        var previous = card.PresentedPosition!.Value;
        var observedOutro = false;
        var observedLoop = false;
        var samples = new List<MediaTime>();
        while (!observedLoop && elapsed.Elapsed < TimeSpan.FromSeconds(6))
        {
            host.Flush();
            Assert.Same(card, host.Card(presetId));
            Assert.Same(request, card.Request);
            Assert.Equal(revision, card.Request!.Revision);
            Assert.Equal(presetId, host.View.ActivePreviewId);
            Assert.True(card.IsPreviewAnimating);
            Assert.Null(host.Window.FocusManager.GetFocusedElement());
            Assert.False(ToolTip.GetIsOpen(Assert.IsType<Grid>(card.Content)));
            var position = card.PresentedPosition!.Value;
            samples.Add(position);
            if (position < previous)
            {
                Assert.True(observedOutro, $"The hover restarted before its outro: {string.Join(", ", samples)}");
                Assert.True(previous > new MediaTime(11, 4));
                Assert.True(position < new MediaTime(3, 10));
                observedLoop = true;
            }
            observedOutro |= position > new MediaTime(11, 4);
            previous = position;
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.True(observedOutro, $"The hover never reached its outro: {string.Join(", ", samples)}");
        Assert.True(observedLoop, $"The hover never completed a loop: {string.Join(", ", samples)}");
        Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(3));
        var rendered = host.Renderer.Rendered.Where(frame => frame.Request.PresetId == presetId).Skip(renderedBefore).ToArray();
        var start = Array.FindIndex(rendered, frame => frame.Position == MediaTime.Zero);
        Assert.True(start >= 0);
        var animation = rendered.Skip(start).ToArray();
        Assert.All(animation, frame => Assert.Same(request, frame.Request));
        var outro = Array.FindIndex(animation, frame => frame.Position > new MediaTime(11, 4));
        Assert.True(outro >= 0);
        Assert.Contains(animation.Skip(outro + 1), frame => frame.Position < new MediaTime(3, 10));

        host.Window.MouseMove(new(2, 2));
        host.Flush();
        await host.View.PreviewCompletion.WaitAsync(TestContext.Current.CancellationToken);
        host.Flush();
        Assert.Null(host.View.ActivePreviewId);
        Assert.False(card.IsPreviewAnimating);
        Assert.Equal(new MediaTime(3, 2), card.PresentedPosition);
        var stoppedRenderCount = host.Renderer.Rendered.Count;
        await Task.Delay(100, TestContext.Current.CancellationToken);
        host.Flush();
        Assert.Equal(stoppedRenderCount, host.Renderer.Rendered.Count);
        Assert.Same(source, host.Session.DocumentSnapshot);
        Assert.Equal(playhead, host.Session.ViewModel.Preview.Position);
        Assert.False(host.Session.Editor.CanUndo);
    }
}
