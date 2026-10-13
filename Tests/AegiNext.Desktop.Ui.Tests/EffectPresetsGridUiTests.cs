using System.Collections.Concurrent;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.EffectPresets;
using AegiNext.Desktop.Rendering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectPresetsGridUiTests
{
    [AvaloniaFact]
    public async Task ContinuousHoverSurvivesLayoutRefreshesThroughTheOutroAndLoopsWithoutPosterReplacement()
    {
        await using var host = await EffectPresetPanelTestHost.CreateAsync();
        var source = host.Session.DocumentSnapshot;
        var playhead = host.Session.ViewModel.Preview.Position;
        var card = host.Card("fade-in-out");
        host.Window.MouseMove(host.Center(card));
        host.Flush();
        await host.WaitUntilAsync(() => card.IsPreviewAnimating);
        var request = card.Request;
        var observedOutro = false;
        var observedLoop = false;
        var samples = new List<MediaTime>();
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (!observedLoop && DateTime.UtcNow < deadline)
        {
            host.Window.Width = host.Window.Width == 680 ? 681 : 680;
            host.View.InvalidateMeasure();
            host.Flush();
            Assert.Same(card, host.Card("fade-in-out"));
            Assert.Same(request, card.Request);
            Assert.Equal("fade-in-out", host.View.ActivePreviewId);
            Assert.True(card.IsPreviewAnimating);
            var position = Assert.IsType<MediaTime>(card.PresentedPosition);
            samples.Add(position);
            observedOutro |= position > new MediaTime(3, 4);
            observedLoop |= observedOutro && position < new MediaTime(3, 10);
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.True(observedOutro, $"The hover never reached the outro: {string.Join(", ", samples)}");
        Assert.True(observedLoop, $"The hover never completed a loop: {string.Join(", ", samples)}");
        Assert.Same(source, host.Session.DocumentSnapshot);
        Assert.Equal(playhead, host.Session.ViewModel.Preview.Position);
        Assert.False(host.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task CatalogBrowsesWithoutSelectionAndGridAdaptsToNarrowPanelsAndBothThemes()
    {
        await using var host = await EffectPresetPanelTestHost.CreateAsync();
        var source = host.Session.DocumentSnapshot;
        Assert.Null(host.Session.SelectedLayer);
        Assert.Equal(BuiltinEffectScripts.Templates.Length, host.ViewModel.Items.Length);
        Assert.False(host.ViewModel.ApplyCommand.CanExecute(null));
        Assert.Equal(Localization.Get("Workbench.EffectPresetNoSelection"), host.ViewModel.ApplyHint);
        Assert.True(host.ViewModel.ShowApplyHint);
        await host.WaitUntilAsync(() => host.Card("fade-in-out").PresentedPosition is not null);
        var cards = host.View.GetVisualDescendants().OfType<EffectPresetCardView>().ToArray();
        var first = cards[0];
        var second = cards[1];
        var firstPoint = first.TranslatePoint(default, host.View)!.Value;
        var secondPoint = second.TranslatePoint(default, host.View)!.Value;
        Assert.Equal(firstPoint.Y, secondPoint.Y);
        Assert.True(secondPoint.X > firstPoint.X);
        Assert.True(first.GetVisualAncestors().OfType<ListBoxItem>().First().Bounds.Width >= 176);
        var preview = first.GetVisualDescendants().OfType<Viewbox>().Single();
        Assert.Equal(16d / 9, preview.Bounds.Width / preview.Bounds.Height, 8);

        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            host.Window.RequestedThemeVariant = theme;
            host.Window.Width = 260;
            host.Flush();
            firstPoint = first.TranslatePoint(default, host.View)!.Value;
            secondPoint = second.TranslatePoint(default, host.View)!.Value;
            Assert.Equal(firstPoint.X, secondPoint.X);
            Assert.True(secondPoint.Y > firstPoint.Y);
            var scroller = host.List.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroller.Extent.Width <= scroller.Viewport.Width + 0.001);
        }
        Assert.Same(source, host.Session.DocumentSnapshot);
        Assert.False(host.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task KeyboardNavigationOverridesHoverAndLeavingFocusRestoresTheStaticPoster()
    {
        await using var host = await EffectPresetPanelTestHost.CreateAsync();
        var source = host.Session.DocumentSnapshot;
        var position = host.Session.ViewModel.Preview.Position;
        var first = host.ViewModel.Items[0];
        var second = host.ViewModel.Items[1];
        host.Window.MouseMove(host.Center(host.Card(first.Id)));
        host.Flush();
        Assert.Equal(first.Id, host.View.ActivePreviewId);
        await host.WaitUntilAsync(() => host.Card(first.Id).IsPreviewAnimating);
        var container = host.Card(first.Id).GetVisualAncestors().OfType<ListBoxItem>().First();
        Assert.True(container.Focus(NavigationMethod.Tab));
        UiTestActions.Press(host.Window, Key.Right);
        host.Flush();

        Assert.Same(second, host.ViewModel.SelectedPreset);
        Assert.Equal(second.Id, host.View.ActivePreviewId);
        await host.WaitUntilAsync(() => host.Card(second.Id).IsPreviewAnimating);
        Assert.Contains(host.Renderer.Rendered, frame => frame.Request.PresetId == second.Id && frame.Position == MediaTime.Zero);
        var rowTop = host.Card(first.Id).TranslatePoint(default, host.View)!.Value.Y;
        var columns = host.View.GetVisualDescendants().OfType<EffectPresetCardView>()
            .Count(card => Math.Abs(card.TranslatePoint(default, host.View)!.Value.Y - rowTop) < 0.001);
        UiTestActions.Press(host.Window, Key.Down);
        host.Flush();
        Assert.Same(host.ViewModel.Items[columns + 1], host.ViewModel.SelectedPreset);
        Assert.Equal(host.ViewModel.SelectedPreset!.Id, host.View.ActivePreviewId);
        UiTestActions.Press(host.Window, Key.Up);
        host.Flush();
        Assert.Same(second, host.ViewModel.SelectedPreset);
        Assert.Equal(second.Id, host.View.ActivePreviewId);
        host.Window.MouseMove(new(2, 2));
        host.Flush();
        Assert.Equal(second.Id, host.View.ActivePreviewId);
        Assert.True(host.View.FindControl<Button>("ManageEffectScriptsButton")!.Focus());
        host.Flush();
        Assert.Null(host.View.ActivePreviewId);
        await host.WaitUntilAsync(() => !host.Card(second.Id).IsPreviewAnimating &&
            host.Card(second.Id).PresentedPosition == new MediaTime(1, 2));
        Assert.Same(source, host.Session.DocumentSnapshot);
        Assert.Equal(position, host.Session.ViewModel.Preview.Position);
        Assert.False(host.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task SingleClickOnlySelectsAndDoubleClickAppliesExactlyOneUndo()
    {
        await using var host = await EffectPresetPanelTestHost.CreateAsync();
        var id = host.Session.Editor.AddSubtitle(new(0), new(4), "YOSYMPH666");
        host.Session.SelectCue(id);
        var source = host.Session.DocumentSnapshot;
        host.Session.Editor.Reset(source);
        host.Session.SelectCue(id);
        host.Flush();
        var point = host.Center(host.Card("fade-in-out"));
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseUp(point, MouseButton.Left);
        host.Flush();
        Assert.Equal("fade-in-out", host.ViewModel.SelectedPreset!.Id);
        Assert.Same(source, host.Session.DocumentSnapshot);
        Assert.False(host.Session.Editor.CanUndo);
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseUp(point, MouseButton.Left);
        host.Flush();
        Assert.NotNull(host.ViewModel.ApplyCommand.ExecutionTask);
        await host.ViewModel.ApplyCommand.ExecutionTask!;
        Assert.Null(host.Session.LastError);
        Assert.Single(host.Session.SelectedLayer!.Tracks);
        Assert.True(host.Session.Editor.Undo());
        Assert.Same(source, host.Session.DocumentSnapshot);
        Assert.False(host.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task BusyUiCoalescesFramesAndRejectsPendingResultsAcrossHideScrollAndReattachment()
    {
        var drains = new ConcurrentQueue<Action>();
        await using var host = await EffectPresetPanelTestHost.CreateAsync(postDrain: drains.Enqueue);
        await host.View.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Drain(drains);
        host.Flush();
        var first = host.ViewModel.Items[0].Id;
        var firstCard = host.Card(first);
        host.Window.MouseMove(host.Center(firstCard));
        host.Flush();
        var initialFrames = host.Renderer.Rendered.Count;
        await host.WaitUntilAsync(() => host.Renderer.Rendered.Count >= initialFrames + 5);
        Assert.Single(drains);
        Assert.Equal(1, host.View.PendingPreviewCount);
        Drain(drains);
        Assert.True(firstCard.IsPreviewAnimating);
        Assert.True(firstCard.PresentedPosition > MediaTime.Zero);

        var second = host.ViewModel.Items[1].Id;
        var secondCard = host.Card(second);
        Assert.True(secondCard.GetVisualAncestors().OfType<ListBoxItem>().First().Focus());
        host.Flush();
        Assert.Equal(second, host.View.ActivePreviewId);
        await host.WaitUntilAsync(() => host.Renderer.Rendered.Any(frame => frame.Request.PresetId == second && frame.Position == MediaTime.Zero));
        var previousStarts = host.Renderer.Rendered.Count(frame => frame.Request.PresetId == first && frame.Position == MediaTime.Zero);
        Assert.True(firstCard.GetVisualAncestors().OfType<ListBoxItem>().First().Focus());
        host.Flush();
        Assert.Equal(first, host.View.ActivePreviewId);
        await host.WaitUntilAsync(() => host.Renderer.Rendered.Count(frame => frame.Request.PresetId == first && frame.Position == MediaTime.Zero) > previousStarts);
        Assert.Single(drains);
        Drain(drains);
        Assert.True(firstCard.IsPreviewAnimating);
        Assert.False(secondCard.IsPreviewAnimating);

        host.View.IsVisible = false;
        host.Flush();
        Assert.Null(host.View.ActivePreviewId);
        Assert.Equal(0, host.View.PendingPreviewCount);
        Drain(drains);
        Assert.Null(firstCard.PresentedPosition);
        Assert.Null(firstCard.GetVisualDescendants().OfType<VideoFramePresenter>().Single().Source);
        host.Window.MouseMove(new(2, 2));
        host.Window.FocusManager!.Focus(null);
        host.Window.Content = null;
        host.Window.Content = host.View;
        host.View.IsVisible = true;
        host.Flush();
        firstCard = host.Card(first);
        await host.View.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Drain(drains);
        Assert.True(firstCard.PresentedPosition == new MediaTime(1, 2),
            $"Position={firstCard.PresentedPosition}; Active={host.View.ActivePreviewId}; Pending={host.View.PendingPreviewCount}; " +
            $"Request={firstCard.Request?.PresetId}; Attached={firstCard.IsAttachedToVisualTree()}; Rendered={host.Renderer.Rendered.Count}");
        Assert.False(firstCard.IsPreviewAnimating);
        Assert.NotNull(firstCard.GetVisualDescendants().OfType<VideoFramePresenter>().Single().Source);

        host.Window.Height = 260;
        host.Flush();
        host.Window.MouseMove(host.Center(firstCard));
        host.Flush();
        Assert.Equal(first, host.View.ActivePreviewId);
        var scroller = host.List.GetVisualDescendants().OfType<ScrollViewer>().First();
        scroller.Offset = new(0, scroller.Extent.Height);
        host.Flush();
        Assert.NotEqual(first, host.View.ActivePreviewId);
        Drain(drains);
        Assert.Null(firstCard.PresentedPosition);
        Assert.False(firstCard.IsPreviewAnimating);
        Assert.Null(firstCard.GetVisualDescendants().OfType<VideoFramePresenter>().Single().Source);
    }

    [AvaloniaFact]
    public async Task PreviewFailuresKeepCardsAndLanguageRefreshPreservesSourceRequestsAndSelection()
    {
        var renderer = new EffectPresetPanelTestRenderer(request => request.PresetId switch
        {
            "pop-out" => new EffectPresetPreviewRequirementException(EffectPresetPreviewRequirement.MOTION_PATH, 1, 1),
            "fade-out" => new InvalidDataException("Missing mask node."),
            _ => null
        });
        await using var host = await EffectPresetPanelTestHost.CreateAsync(renderer);
        await host.WaitUntilAsync(() => host.Card("pop-out").ViewModel.HasPreviewError && host.Card("fade-out").ViewModel.HasPreviewError);
        var selected = host.ViewModel.Items.Single(item => item.Id == "pop-out");
        host.ViewModel.SelectedPreset = selected;
        var source = host.Session.DocumentSnapshot;
        var request = host.Card(selected.Id).Request;
        Assert.Equal(Localization.Get("Workbench.EffectPresetRequiresPath"), selected.PreviewStatus);
        Assert.Contains("Missing mask node.", host.Card("fade-out").ViewModel.PreviewStatus);

        Localization.SetLanguage("zh-CN");
        host.Flush();
        Assert.Same(selected, host.ViewModel.SelectedPreset);
        Assert.Same(request, host.Card(selected.Id).Request);
        Assert.Equal(Localization.Get("Workbench.PopOut"), selected.Name);
        Assert.Equal(Localization.Get("Workbench.EffectPresetRequiresPath"), selected.PreviewStatus);
        Assert.Same(source, host.Session.DocumentSnapshot);
        Assert.False(host.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task EditingAPersonalScriptReplacesPendingRequestsAndKeepsItsStableSelection()
    {
        var drains = new ConcurrentQueue<Action>();
        await using var host = await EffectPresetPanelTestHost.CreateAsync(postDrain: drains.Enqueue);
        host.Window.Height = 740;
        var preset = new EffectScriptPreset(Guid.NewGuid(), "个人预设 Personal", BuiltinEffectScripts.Get("fade-in-out").Source
            .Replace("effect \"fade-in-out\"", "effect \"personal-preview\"", StringComparison.Ordinal));
        await host.Session.EffectScripts.UpsertAsync(preset);
        var id = preset.Id.ToString("D");
        host.ViewModel.SelectedPreset = host.ViewModel.Items.Single(item => item.Id == id);
        host.Flush();
        var previous = host.Card(id);
        var request = previous.Request;
        Assert.NotNull(request);
        await host.View.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(host.View.PendingPreviewCount > 0);
        var snapshot = host.Session.DocumentSnapshot;

        var source = BuiltinEffectScripts.Get("pop-in").Source
            .Replace("effect \"pop-in\"", "effect \"personal-preview\"", StringComparison.Ordinal);
        await host.Session.EffectScripts.UpsertAsync(preset with { Source = source });
        host.Flush();
        var replacement = host.Card(id);
        Assert.NotSame(previous, replacement);
        Assert.Null(previous.GetVisualDescendants().OfType<VideoFramePresenter>().Single().Source);
        Assert.Equal(id, host.ViewModel.SelectedPreset!.Id);
        Assert.Equal(preset.Name, host.ViewModel.SelectedPreset.Name);
        Assert.Equal(source, replacement.Request!.Source);
        Assert.True(replacement.Request.Revision > request!.Revision);
        await host.View.PreviewCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Single(drains);
        Drain(drains);
        Assert.Equal(new MediaTime(1, 2), replacement.PresentedPosition);
        Assert.NotNull(replacement.GetVisualDescendants().OfType<VideoFramePresenter>().Single().Source);
        Assert.Same(snapshot, host.Session.DocumentSnapshot);
        Assert.False(host.Session.Editor.CanUndo);
    }

    private static void Drain(ConcurrentQueue<Action> drains)
    {
        while (drains.TryDequeue(out var drain))
        {
            drain();
        }
    }
}
