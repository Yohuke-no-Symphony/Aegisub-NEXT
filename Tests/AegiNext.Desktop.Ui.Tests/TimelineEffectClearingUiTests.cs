using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineEffectClearingUiTests
{
    [AvaloniaFact]
    public async Task ClipMenuClearsMixedSelectionWithOneUndoAndRetainsSelection()
    {
        await using var context = new MainWindowTestContext();
        var first = Line(0);
        var second = Line(3);
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(6), End = new(8),
            Tracks = [new(AnimationProperty.OPACITY, [])
            {
                InitialValue = 1, Transforms = [new(Guid.NewGuid(), new(0), new(2), 0.5)]
            }]
        };
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second), shape] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        var ids = new[] { first.Id, shape.Id };
        context.Session.SelectLayer(first.Id, ids);
        Flush(context.Window);
        var panel = Panel(timeline);
        OpenMenu(context, timeline, timeline.GetClipRectangle(first.Id)!.Value.Center);
        try
        {
            Assert.True(panel.ClipMenu.IsOpen);
            await ExecuteAsync(panel.ClipMenu, "ClearClipAnimationTracksMenuItem");
        }
        finally
        {
            panel.ClipMenu.Close();
        }
        Flush(context.Window);

        var cleared = context.Session.DocumentSnapshot;
        Assert.Empty(cleared.Layers[0].Tracks);
        Assert.Empty(cleared.Layers[2].Tracks);
        Assert.Same(document.Layers[1], cleared.Layers[1]);
        Assert.Equal(first.Id, context.Session.SelectedLayerId);
        Assert.Equal(ids.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Null(context.Session.SelectedKeyTime);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(cleared, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EffectBlockLabelAndBlankClearOnlyTheirCompleteTarget(bool collapsed, bool blank)
    {
        await using var context = new MainWindowTestContext();
        var first = Line(0);
        var second = Line(3);
        var otherTrack = new ProjectTrack { Name = "Other" };
        var other = Line(6);
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, otherTrack], Subtitles = [first, second, other],
            Layers = [NodeLayer(first), NodeLayer(second), NodeLayer(other) with { TrackId = otherTrack.Id }]
        };
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(first.Id, [first.Id]);
        var timeline = Prepare(context);
        var row = new TimelineEffectClipId(first.Id, document.Layers[0].Tracks[0].Target);
        context.ViewModel.Timeline.SetEffectClipCollapsed(new(row, collapsed));
        Flush(context.Window);
        var rectangle = timeline.GetEffectClipRectangle(row)!.Value;
        var panel = Panel(timeline);
        OpenMenu(context, timeline, blank
            ? new(rectangle.Left + 60, rectangle.Bottom - 3)
            : new(rectangle.Left + 28, rectangle.Top + 10));
        try
        {
            Assert.True(panel.AnimationMenu.IsOpen);
            Assert.False(panel.ClipMenu.IsOpen);
            Assert.Equal(first.Id, Assert.Single(context.ViewModel.Timeline.SelectedLayerIds));
            await ExecuteAsync(panel.AnimationMenu, "ClearAnimationPropertyTracksMenuItem");
        }
        finally
        {
            panel.AnimationMenu.Close();
        }
        Flush(context.Window);

        Assert.Equal(document.Layers[0].Tracks[1], Assert.Single(context.Session.DocumentSnapshot.Layers[0].Tracks));
        Assert.Same(document.Layers[1], context.Session.DocumentSnapshot.Layers[1]);
        Assert.Same(document.Layers[2], context.Session.DocumentSnapshot.Layers[2]);
        Assert.Same(document.Layers[0].Mask, context.Session.DocumentSnapshot.Layers[0].Mask);
        Assert.Null(timeline.GetEffectClipRectangle(row));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Flush(context.Window);
        Assert.NotNull(timeline.GetEffectClipRectangle(row));
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task KeyframeAndCurveMenusClearOnlyHitClipPropertyAndFreezeItAcrossSelectionChanges(bool collapsed, bool marker)
    {
        await using var context = new MainWindowTestContext();
        var first = Line(0);
        var second = Line(3);
        var third = Line(6);
        var document = new ProjectDocument
        {
            Subtitles = [first, second, third], Layers = [NodeLayer(first), NodeLayer(second), NodeLayer(third)]
        };
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(first.Id, [first.Id]);
        var timeline = Prepare(context);
        var row = new TimelineEffectClipId(second.Id, document.Layers[1].Tracks[marker ? 1 : 0].Target);
        context.ViewModel.Timeline.SetEffectClipCollapsed(new(row, collapsed));
        Flush(context.Window);
        var point = marker
            ? timeline.KeyframeMarkers.First(value => value.Identity.LayerId == second.Id && value.Identity.OperationId is not null &&
                !value.Identity.IsOperationStart).Position
            : timeline.GetKeyframePoint(second.Id, document.Layers[1].Tracks[0].Target, new(1), new ScenePoint(10, 20))!.Value;
        var panel = Panel(timeline);
        OpenMenu(context, timeline, point);
        try
        {
            Assert.True(panel.AnimationMenu.IsOpen);
            Assert.False(panel.ClipMenu.IsOpen);
            Assert.Equal(first.Id, Assert.Single(context.ViewModel.Timeline.SelectedLayerIds));
            Assert.Equal(Localization.Get("Workbench.ClearEffectClip"),
                TimelineTrackTestActions.Item(panel.AnimationMenu, "ClearAnimationPropertyTracksMenuItem").Header);
            context.Session.SelectLayer(third.Id, [first.Id, third.Id]);
            await ExecuteAsync(panel.AnimationMenu, "ClearAnimationPropertyTracksMenuItem");
        }
        finally
        {
            panel.AnimationMenu.Close();
        }
        Flush(context.Window);

        var cleared = context.Session.DocumentSnapshot;
        Assert.Same(document.Layers[0], cleared.Layers[0]);
        Assert.Equal(document.Layers[1].Tracks[marker ? 0 : 1], Assert.Single(cleared.Layers[1].Tracks));
        Assert.Same(document.Layers[2], cleared.Layers[2]);
        Assert.Same(document.Layers[1].Mask, cleared.Layers[1].Mask);
        Assert.Equal(third.Id, context.Session.SelectedLayerId);
        Assert.Equal(new[] { first.Id, third.Id }.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Null(timeline.GetEffectClipRectangle(row));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(cleared, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task HitClipPropertyMenuRejectsChangedSnapshot()
    {
        await using var context = new MainWindowTestContext();
        var first = Line(0);
        var second = Line(3);
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        var panel = Panel(timeline);
        OpenMenu(context, timeline, timeline.GetKeyframePoint(second.Id, AnimationProperty.OPACITY, new(0), 0.5)!.Value);
        try
        {
            var item = TimelineTrackTestActions.Item(panel.AnimationMenu, "ClearAnimationPropertyTracksMenuItem");
            Assert.True(item.Command!.CanExecute(null));
            context.Session.Editor.UpdateLayer(first.Id, layer => layer with { Name = "Changed" });
            var changed = context.Session.DocumentSnapshot;
            Assert.False(item.Command.CanExecute(null));
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command).ExecuteAsync(null);
            Assert.Same(changed, context.Session.DocumentSnapshot);
            Assert.True(context.Session.Editor.Undo());
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            panel.AnimationMenu.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClassicTimingRightClickOnCurveOrKeyframeOpensClipPropertyMenuWithoutRetiming(bool marker)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var first = Line(0);
        var second = Line(3);
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(first.Id, [first.Id]);
        context.Session.UpdatePreferences(current => current with { TimelineClassicTimingEnabled = true });
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        var timeline = Prepare(context);
        Assert.True(timeline.IsClassicTimingEnabled);
        var input = UiTestActions.Find<ListBox>(context.Window, "SubtitleList").GetVisualDescendants().OfType<TextBox>()
            .First(box => box.DataContext is SubtitleRow row && row.Id == first.Id && Grid.GetColumn(box) == 4);
        Assert.True(input.Focus());
        Assert.Same(input, context.Window.FocusManager!.GetFocusedElement());
        var panel = Panel(timeline);
        var point = timeline.GetKeyframePoint(second.Id, AnimationProperty.OPACITY, new(marker ? 0 : 1), 0.5)!.Value;
        OpenMenu(context, timeline, point);
        try
        {
            Assert.True(panel.AnimationMenu.IsOpen);
            Assert.False(panel.ClipMenu.IsOpen);
            Assert.Equal(Localization.Get("Workbench.ClearEffectClip"),
                TimelineTrackTestActions.Item(panel.AnimationMenu, "ClearAnimationPropertyTracksMenuItem").Header);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.Equal(first.Start, context.Session.DocumentSnapshot.Subtitles[0].Start);
            Assert.Equal(first.End, context.Session.DocumentSnapshot.Subtitles[0].End);
            Assert.False(context.Session.Editor.CanUndo);
            await ExecuteAsync(panel.AnimationMenu, "ClearAnimationPropertyTracksMenuItem");
            Assert.Same(document.Layers[0], context.Session.DocumentSnapshot.Layers[0]);
            Assert.Empty(context.Session.DocumentSnapshot.Layers[1].Tracks);
            Assert.Equal(document.Subtitles, context.Session.DocumentSnapshot.Subtitles);
        }
        finally
        {
            panel.AnimationMenu.Close();
        }
    }

    [AvaloniaFact]
    public async Task StaleMenusCannotClearChangedProjectAndEffectlessClipDisablesClear()
    {
        await using var context = new MainWindowTestContext();
        var line = Line(0);
        var document = new ProjectDocument { Subtitles = [line], Layers = [Layer(line)] };
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(line.Id, [line.Id]);
        var timeline = Prepare(context);
        var panel = Panel(timeline);
        OpenMenu(context, timeline, timeline.GetClipRectangle(line.Id)!.Value.Center);
        var item = TimelineTrackTestActions.Item(panel.ClipMenu, "ClearClipAnimationTracksMenuItem");
        context.Session.Editor.UpdateLayer(line.Id, layer => layer with { Name = "Changed" });
        var changed = context.Session.DocumentSnapshot;
        try
        {
            Assert.False(item.Command!.CanExecute(null));
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command).ExecuteAsync(null);
            Assert.Same(changed, context.Session.DocumentSnapshot);
        }
        finally
        {
            panel.ClipMenu.Close();
        }

        var effectless = changed with { Layers = [changed.Layers[0] with { Tracks = [] }] };
        context.Session.Editor.Reset(effectless);
        Flush(context.Window);
        OpenMenu(context, timeline, timeline.GetClipRectangle(line.Id)!.Value.Center);
        try
        {
            Assert.False(TimelineTrackTestActions.Item(panel.ClipMenu, "ClearClipAnimationTracksMenuItem").Command!.CanExecute(null));
            Assert.Same(effectless, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            panel.ClipMenu.Close();
        }
    }

    private static SubtitleLine Line(int start) => new() { Start = new(start), End = new(start + 2), Text = "Clip" };

    private static ProjectLayer Layer(SubtitleLine line) => new()
    {
        Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
        Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])]
    };

    private static ProjectLayer NodeLayer(SubtitleLine line)
    {
        var first = new MaskNode();
        var second = new MaskNode();
        return Layer(line) with
        {
            Mask = new VectorClipMask { Contours = [new() { Nodes = [first, second] }] },
            Tracks =
            [
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id), [new(new(0), new ScenePoint(10, 20))]),
                new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, second.Id), [])
                {
                    InitialValue = new ScenePoint(0, 0), Transforms = [new(Guid.NewGuid(), new(0), new(2), new ScenePoint(30, 40))]
                }
            ]
        };
    }

    private static SubtitleTimelineControl Prepare(MainWindowTestContext context)
    {
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.IsSnapEnabled = false;
        context.ViewModel.Timeline.IsStepEnabled = false;
        timeline.PixelsPerSecond = 60;
        timeline.ViewStart = 0;
        Flush(context.Window);
        return timeline;
    }

    private static TimelinePanelView Panel(SubtitleTimelineControl timeline) =>
        Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>());

    private static void OpenMenu(MainWindowTestContext context, SubtitleTimelineControl timeline, Point local)
    {
        if (local.Y < timeline.RulerHeight || local.Y >= timeline.Bounds.Height)
        {
            var previousOffset = timeline.Viewport.VerticalOffset;
            context.ViewModel.Timeline.Viewport = timeline.Viewport with
            {
                VerticalOffset = Math.Max(0, previousOffset + local.Y - timeline.RulerHeight - 24)
            };
            Flush(context.Window);
            local = new(local.X, local.Y - timeline.Viewport.VerticalOffset + previousOffset);
        }
        var point = timeline.TranslatePoint(local, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Right);
        context.Window.MouseUp(point, MouseButton.Right);
        Flush(context.Window);
    }

    private static async Task ExecuteAsync(ContextMenu menu, string name)
    {
        var item = TimelineTrackTestActions.Item(menu, name);
        Assert.True(item.Command!.CanExecute(null));
        await Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command).ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
