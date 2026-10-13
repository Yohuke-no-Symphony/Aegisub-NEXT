using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.EffectPresets;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证真实工作台窗口中的预设卡片导航保留本地键盘所有权。</summary>
public sealed class EffectPresetsKeyboardRoutingUiTests
{
    /// <summary>停靠和浮动预设面板的方向键与空格不会触发主播放控制，也不会被全局按键释放状态消费。</summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresetCardNavigationStaysLocalInRegisteredDockedAndFloatingWindows(bool floating)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.Session.EffectScripts.Completion;
        await context.OpenMediaAsync();
        context.Window.Layouts.Activate(WorkbenchPanelIds.EFFECT_PRESETS);
        if (floating)
        {
            context.Window.Layouts.Float(WorkbenchPanelIds.EFFECT_PRESETS);
        }
        Flush(context.Window);

        var view = Assert.IsType<EffectPresetsPanelView>(context.Window.Panels[WorkbenchPanelIds.EFFECT_PRESETS]);
        var window = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(view));
        Assert.Equal(floating, !ReferenceEquals(context.Window, window));
        Assert.Contains(window, context.WindowRegistry.Windows);
        if (floating)
        {
            window.Width = 680;
            window.Height = 640;
        }
        Flush(window);

        var model = context.ViewModel.EffectPresets;
        var list = view.FindControl<ListBox>("EffectPresetList")!;
        var cards = view.GetVisualDescendants().OfType<EffectPresetCardView>().ToArray();
        var firstCard = Assert.Single(cards, card => ReferenceEquals(card.ViewModel, model.Items[0]));
        var firstItem = firstCard.GetVisualAncestors().OfType<ListBoxItem>().First();
        list.SelectedIndex = 0;
        firstItem.BringIntoView();
        Flush(window);
        Assert.True(firstItem.Focus(NavigationMethod.Tab));
        Assert.Same(firstItem, window.FocusManager!.GetFocusedElement());
        var firstRowTop = firstCard.TranslatePoint(default, view)!.Value.Y;
        var columns = cards.Count(card => Math.Abs(card.TranslatePoint(default, view)!.Value.Y - firstRowTop) < 0.001);
        Assert.InRange(columns, 1, model.Items.Length - 2);
        var source = context.Session.DocumentSnapshot;
        var position = context.Controller.Snapshot.Position;
        var projectPosition = context.Session.ProjectPosition;
        Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.True(context.Window.GetCommand(WorkbenchCommand.PLAY_PAUSE).CanExecute(null));
        Assert.True(context.Window.GetCommand(WorkbenchCommand.SEEK_FORWARD).CanExecute(null));

        var down = new List<(Key Key, bool Handled)>();
        var releaseAtWindow = new List<(Key Key, bool Handled)>();
        void RecordDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is Key.Right or Key.Left or Key.Down or Key.Up or Key.Space)
            {
                down.Add((e.Key, e.Handled));
            }
        }
        void RecordRelease(object? sender, KeyEventArgs e)
        {
            if (e.Key is Key.Right or Key.Left or Key.Down or Key.Up or Key.Space)
            {
                releaseAtWindow.Add((e.Key, e.Handled));
            }
        }
        window.AddHandler(InputElement.KeyDownEvent, RecordDown, RoutingStrategies.Bubble, handledEventsToo: true);
        window.AddHandler(InputElement.KeyUpEvent, RecordRelease, RoutingStrategies.Tunnel, handledEventsToo: true);
        try
        {
            UiTestActions.Press(window, Key.Right);
            Flush(window);
            Assert.Same(model.Items[1], model.SelectedPreset);
            Assert.Equal(model.Items[1].Id, view.ActivePreviewId);

            UiTestActions.Press(window, Key.Down);
            Flush(window);
            Assert.Same(model.Items[columns + 1], model.SelectedPreset);
            Assert.Equal(model.SelectedPreset!.Id, view.ActivePreviewId);

            UiTestActions.Press(window, Key.Up);
            Flush(window);
            Assert.Same(model.Items[1], model.SelectedPreset);

            UiTestActions.Press(window, Key.Space);
            if (context.Window.GetCommand(WorkbenchCommand.PLAY_PAUSE) is IAsyncRelayCommand { ExecutionTask: { } playback })
            {
                await playback;
            }
            await context.Session.WaitForProjectIdleAsync();
            Flush(window);
            Assert.Same(model.Items[1], model.SelectedPreset);

            UiTestActions.Press(window, Key.Left);
            await context.Session.WaitForProjectIdleAsync();
            Flush(window);
            Assert.Same(model.Items[0], model.SelectedPreset);

            var keys = new[] { Key.Right, Key.Down, Key.Up, Key.Space, Key.Left };
            Assert.Equal(keys, down.Select(eventState => eventState.Key));
            Assert.All(down, eventState => Assert.True(eventState.Handled));
            Assert.Equal(keys, releaseAtWindow.Select(eventState => eventState.Key));
            Assert.All(releaseAtWindow, eventState => Assert.False(eventState.Handled));
            Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
            Assert.Equal(position, context.Controller.Snapshot.Position);
            Assert.Equal(projectPosition, context.Session.ProjectPosition);
            Assert.Same(source, context.Session.DocumentSnapshot);
        }
        finally
        {
            window.RemoveHandler(InputElement.KeyDownEvent, RecordDown);
            window.RemoveHandler(InputElement.KeyUpEvent, RecordRelease);
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
