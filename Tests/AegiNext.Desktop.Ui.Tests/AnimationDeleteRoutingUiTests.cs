using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证真实 Delete 按下、重复和释放的消费，以及属性按钮和本地弹窗的删除边界。</summary>
public sealed class AnimationDeleteRoutingUiTests
{
    /// <summary>时间轴、特效普通属性与蒙版动画按钮只删除选中帧，并消费重复按下和释放。</summary>
    [AvaloniaTheory]
    [InlineData("timeline", false)]
    [InlineData("effects", false)]
    [InlineData("effects", true)]
    public async Task DeleteConsumesBothEventsAndRepeatsWithoutDeletingTheParentSubtitle(string origin, bool maskProperty)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(maskProperty ? AnimationProperty.MASK_NODE_POSITION : AnimationProperty.OPACITY);
        AnimationValue value = 0.25;
        if (maskProperty)
        {
            var node = new MaskNode
            {
                Position = new(10, 20)
            };
            session.Editor.SetClipMask(layerId, new VectorClipMask
            {
                Contours =
                [
                    new()
                    {
                        Nodes = [node]
                    }
                ]
            });
            target = target with { NodeId = node.Id };
            value = node.Position;
        }
        session.Editor.SetKeyframe(layerId, target, new(new(1), value));
        Assert.True(session.SelectKeyframe(new(layerId, target, new(1), new(1))));
        if (origin == "timeline")
        {
            var timeline = await PrepareTimelineAsync(context);
            ClickKeyframe(context, timeline, layerId, value);
            Assert.True(timeline.IsFocused);
        }
        else
        {
            var button = UiTestActions.OpenAnimationOptions(context.Window, target.Property, origin);
            button.Flyout!.Hide();
            Assert.True(button.Focus());
        }
        var source = session.DocumentSnapshot;
        var downHandled = new List<bool>();
        var upHandled = new List<bool>();
        EventHandler<KeyEventArgs> onDown = (_, e) =>
        {
            if (e.Key == Key.Delete)
            {
                downHandled.Add(e.Handled);
            }
        };
        EventHandler<KeyEventArgs> onUp = (_, e) =>
        {
            if (e.Key == Key.Delete)
            {
                upHandled.Add(e.Handled);
            }
        };
        context.Window.AddHandler(InputElement.KeyDownEvent, onDown, RoutingStrategies.Bubble, true);
        context.Window.AddHandler(InputElement.KeyUpEvent, onUp, RoutingStrategies.Bubble, true);
        try
        {
            context.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            await session.WaitForProjectIdleAsync();
            var deleted = session.DocumentSnapshot;
            Assert.NotSame(source, deleted);
            Assert.Empty(Assert.Single(deleted.Layers).Tracks);
            Assert.Single(deleted.Subtitles);
            context.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            context.Window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            await session.WaitForProjectIdleAsync();

            Assert.Equal(2, downHandled.Count);
            Assert.All(downHandled, handled => Assert.True(handled));
            Assert.True(Assert.Single(upHandled));
            Assert.Same(deleted, session.DocumentSnapshot);
            if (origin != "timeline")
            {
                context.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
                context.Window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
                await session.WaitForProjectIdleAsync();
                Assert.Equal(3, downHandled.Count);
                Assert.Equal(2, upHandled.Count);
                Assert.All(downHandled, handled => Assert.True(handled));
                Assert.All(upHandled, handled => Assert.True(handled));
                Assert.Same(deleted, session.DocumentSnapshot);
                Assert.Single(deleted.Subtitles);
            }
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            context.Window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            context.Window.RemoveHandler(InputElement.KeyDownEvent, onDown);
            context.Window.RemoveHandler(InputElement.KeyUpEvent, onUp);
        }
    }

    /// <summary>非文本设置弹窗、展开的插值列表和时间轴菜单保留本地 Delete，关闭后恢复关键帧删除。</summary>
    [AvaloniaTheory]
    [InlineData("options")]
    [InlineData("combo")]
    [InlineData("menu")]
    public async Task NonTextPopupAndMenuKeepDeleteLocalUntilTheyClose(string surface)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var target = new AnimationTrackTarget(AnimationProperty.OPACITY);
        session.Editor.SetKeyframe(layerId, target, new(new(1), 0.25));
        Assert.True(session.SelectKeyframe(new(layerId, target, new(1), new(1))));
        var source = session.DocumentSnapshot;
        ComboBox? combo = null;
        ContextMenu? menu = null;
        Button? button = null;
        try
        {
            TopLevel inputRoot;
            if (surface == "menu")
            {
                var timeline = await PrepareTimelineAsync(context);
                ClickKeyframe(context, timeline, layerId, 0.25);
                var point = timeline.TranslatePoint(timeline.GetKeyframePoint(layerId, new(1), 0.25)!.Value, context.Window)!.Value;
                context.Window.MouseDown(point, MouseButton.Right);
                context.Window.MouseUp(point, MouseButton.Right);
                Flush(context.Window);
                menu = Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>()).AnimationMenu;
                Assert.True(menu.IsOpen);
                var item = menu.Items.OfType<MenuItem>().Single(value => value.Name == "DeleteTimelineKeyframeMenuItem");
                Assert.True(item.Focus());
                inputRoot = TopLevel.GetTopLevel(item)!;
            }
            else
            {
                button = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
                combo = UiTestActions.Find<ComboBox>(context.Window, "InterpolationCombo");
                Assert.True(combo.IsEffectivelyEnabled);
                Assert.True(combo.Focus());
                if (surface == "combo")
                {
                    combo.IsDropDownOpen = true;
                    Flush(context.Window);
                    Assert.True(combo.IsDropDownOpen);
                }
                inputRoot = TopLevel.GetTopLevel(combo)!;
            }

            inputRoot.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            inputRoot.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            inputRoot.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            await session.WaitForProjectIdleAsync();

            Assert.Same(source, session.DocumentSnapshot);
            Assert.Single(Assert.Single(session.SelectedLayer!.Tracks).Keyframes);
            Assert.Single(session.DocumentSnapshot.Subtitles);
            if (combo is not null)
            {
                Assert.Equal((int)KeyframeInterpolation.LINEAR, combo.SelectedIndex);
                combo.IsDropDownOpen = false;
            }
            menu?.Close();
            button?.Flyout?.Hide();
            var settings = UiTestActions.OpenAnimationOptions(context.Window, target.Property);
            settings.Flyout!.Hide();
            Assert.True(settings.Focus());
            context.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            context.Window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.None, null);
            await session.WaitForProjectIdleAsync();

            Assert.Empty(session.SelectedLayer.Tracks);
            Assert.Single(session.DocumentSnapshot.Subtitles);
            Assert.True(session.Editor.Undo());
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            if (combo is not null)
            {
                combo.IsDropDownOpen = false;
            }
            menu?.Close();
            button?.Flyout?.Hide();
        }
    }

    private static async Task<SubtitleTimelineControl> PrepareTimelineAsync(MainWindowTestContext context)
    {
        Assert.True(await context.Window.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.STANDARD));
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Flush(context.Window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Flush(context.Window);
        return timeline;
    }

    private static void ClickKeyframe(MainWindowTestContext context, SubtitleTimelineControl timeline, Guid layerId, AnimationValue value)
    {
        var point = timeline.TranslatePoint(timeline.GetKeyframePoint(layerId, new(1), value)!.Value, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Flush(context.Window);
        Assert.Equal(new MediaTime(1), context.Session.SelectedKeyTime);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
