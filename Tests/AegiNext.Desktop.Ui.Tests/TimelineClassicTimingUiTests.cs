using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClassicTimingUiTests
{
    [AvaloniaFact]
    public async Task ClassicTimingDefaultsOffAndItsBottomLeftTogglePersistsAndIsSharedWithTheFloatingWindow()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var timeline = Prepare(context);
        var panel = Panel(timeline);
        var button = UiTestActions.Find<ToolbarToggleButton>(context.Window, "TimelineClassicTimingButton");
        var processor = UiTestActions.Find<Button>(context.Window, "TimelineTimingPostProcessorButton");
        var step = UiTestActions.Find<ToolbarToggleButton>(context.Window, "TimelineStepButton");
        var collapseAll = UiTestActions.Find<Button>(context.Window, "TimelineCollapseAllTracksButton");
        var scroller = UiTestActions.Find<ScrollViewer>(context.Window, "TimelineToolbarScroller");
        Assert.False(context.Session.Preferences.TimelineClassicTimingEnabled);
        Assert.False(context.ViewModel.Timeline.IsClassicTimingEnabled);
        Assert.False(timeline.IsClassicTimingEnabled);
        Assert.False(button.IsChecked);
        var position = button.TranslatePoint(new(), panel)!.Value;
        var timelinePosition = timeline.TranslatePoint(new(), panel)!.Value;
        Assert.True(position.X + button.Bounds.Width <= timelinePosition.X);
        var processorPosition = processor.TranslatePoint(new(), panel)!.Value;
        Assert.True(position.Y + button.Bounds.Height < processorPosition.Y);
        Assert.InRange(panel.Bounds.Height - processorPosition.Y - processor.Bounds.Height, 0, 12);
        var buttonPoint = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), context.Window)!.Value;
        var buttonHit = context.Window.InputHitTest(buttonPoint);
        Assert.True(ReferenceEquals(buttonHit, button) ||
            buttonHit is Visual hitVisual && hitVisual.GetVisualAncestors().Contains(button));
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height);
        var scrollPoint = scroller.TranslatePoint(new(scroller.Bounds.Width / 2, scroller.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseWheel(scrollPoint, new(0, -10));
        Flush(context.Window);
        Assert.True(scroller.Offset.Y > 0);
        Assert.Equal(position, button.TranslatePoint(new(), panel)!.Value);
        Assert.Equal(processorPosition, processor.TranslatePoint(new(), panel)!.Value);
        var scrollerPosition = scroller.TranslatePoint(new(), panel)!.Value;
        var lastPosition = collapseAll.TranslatePoint(new(), panel)!.Value;
        Assert.True(lastPosition.Y >= scrollerPosition.Y);
        Assert.True(lastPosition.Y + collapseAll.Bounds.Height <= scrollerPosition.Y + scroller.Viewport.Height);
        Assert.True(lastPosition.Y + collapseAll.Bounds.Height < position.Y);
        step.BringIntoView();
        Flush(context.Window);
        var stepPosition = step.TranslatePoint(new(), panel)!.Value;
        Assert.True(stepPosition.Y >= scrollerPosition.Y);
        Assert.True(stepPosition.Y + step.Bounds.Height <= scrollerPosition.Y + scroller.Viewport.Height);
        Assert.True(stepPosition.Y + step.Bounds.Height < position.Y);
        var original = context.Session.DocumentSnapshot;

        UiTestActions.Click(context.Window, "TimelineClassicTimingButton");
        Flush(context.Window);

        Assert.True(context.Session.Preferences.TimelineClassicTimingEnabled);
        Assert.True(context.ViewModel.Timeline.IsClassicTimingEnabled);
        Assert.True(timeline.IsClassicTimingEnabled);
        Assert.True(button.IsChecked);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        await WaitForPersistenceAsync(context);
        context.Window.Layouts.Float("timeline");
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        try
        {
            Flush(floating);
            Assert.Contains(floating, context.WindowRegistry.Windows);
            Assert.Same(timeline, UiTestActions.Find<SubtitleTimelineControl>(floating, "Timeline"));
            Assert.True(UiTestActions.Find<ToolbarToggleButton>(floating, "TimelineClassicTimingButton").IsChecked);
            UiTestActions.Click(floating, "TimelineClassicTimingButton");
            Flush(floating);

            Assert.False(context.Session.Preferences.TimelineClassicTimingEnabled);
            Assert.False(context.ViewModel.Timeline.IsClassicTimingEnabled);
            Assert.False(timeline.IsClassicTimingEnabled);
            await WaitForPersistenceAsync(context);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(floating.IsVisible);
            Assert.DoesNotContain(floating, context.WindowRegistry.Windows);
        }
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, 1137, 1137, 4000)]
    [InlineData(MouseButton.Right, 5137, 2000, 5137)]
    public async Task ClickingAnotherTrackClipChangesOnlyThePrimaryCueAtExactMillisecondsAndUndoIsOneEdit(
        MouseButton button, int clickMs, int startMs, int endMs)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var scene = CreateScene(context);
        context.Session.Editor.Reset(scene.Document);
        var timeline = Prepare(context);
        Select(context, timeline, scene.Other.Id);
        Select(context, timeline, scene.Primary.Id, CommandModifier());
        var selection = context.ViewModel.Timeline.SelectedLayerIds.ToArray();
        Assert.Equal(2, selection.Length);
        Assert.Equal(scene.Primary.Id, context.Session.SelectedCueId);
        UiTestActions.Click(context.Window, "TimelineClassicTimingButton");
        var local = BodyPoint(context.Window, timeline, scene.Other.Id, clickMs);

        Click(context.Window, timeline, local, button);

        var edited = context.Session.DocumentSnapshot;
        var cue = edited.Subtitles.Single(line => line.Id == scene.Primary.Id);
        Assert.Equal(new MediaTime(startMs, 1000), cue.Start);
        Assert.Equal(new MediaTime(endMs, 1000), cue.End);
        Assert.Equal(new ProjectClipIndex(scene.Document).GetSubtitleTrackId(scene.Primary.Id), new ProjectClipIndex(edited).GetSubtitleTrackId(cue.Id));
        Assert.Equal(scene.Other, edited.Subtitles.Single(line => line.Id == scene.Other.Id));
        Assert.Equal(scene.Document.Layers.Single(layer => layer.Id == scene.Other.Id),
            edited.Layers.Single(layer => layer.Id == scene.Other.Id));
        Assert.Equal(cue.Start, edited.Layers.Single(layer => layer.Id == cue.Id).Start);
        Assert.Equal(cue.End, edited.Layers.Single(layer => layer.Id == cue.Id).End);
        Assert.Equal(scene.Primary.Id, context.Session.SelectedCueId);
        Assert.Equal(scene.Primary.Id, context.Session.SelectedLayerId);
        Assert.Equal(new ProjectClipIndex(scene.Document).GetSubtitleTrackId(scene.Primary.Id), context.Session.CurrentTrackId);
        Assert.Equal(selection.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.False(Panel(timeline).ClipMenu.IsOpen);
        Assert.False(timeline.HasActiveDrag);
        Assert.Null(context.Session.LastError);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(scene.Document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, 5000, 4000, 5000)]
    [InlineData(MouseButton.Right, 1000, 1000, 2000)]
    public async Task ClickingAcrossTheOppositeBoundarySwapsStartAndEndAsOneUndoableEdit(
        MouseButton button, int clickMs, int startMs, int endMs)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var scene = CreateScene(context);
        context.Session.Editor.Reset(scene.Document);
        var timeline = Prepare(context);
        Select(context, timeline, scene.Primary.Id);
        UiTestActions.Click(context.Window, "TimelineClassicTimingButton");

        Click(context.Window, timeline, BodyPoint(context.Window, timeline, scene.Primary.Id, clickMs), button);

        var cue = context.Session.DocumentSnapshot.Subtitles.Single(line => line.Id == scene.Primary.Id);
        Assert.Equal(new MediaTime(startMs, 1000), cue.Start);
        Assert.Equal(new MediaTime(endMs, 1000), cue.End);
        Assert.Equal(scene.Primary.Id, context.Session.SelectedCueId);
        Assert.Null(context.Session.LastError);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(scene.Document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, 4000, "en-US")]
    [InlineData(MouseButton.Right, 2000, "zh-CN")]
    public async Task EqualBoundariesAreRejectedWithALocalizedErrorAndNoEdit(MouseButton button, int clickMs, string language)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        Localization.SetLanguage(language);
        var scene = CreateScene(context);
        context.Session.Editor.Reset(scene.Document);
        var timeline = Prepare(context);
        Select(context, timeline, scene.Primary.Id);
        UiTestActions.Click(context.Window, "TimelineClassicTimingButton");

        Click(context.Window, timeline, BodyPoint(context.Window, timeline, scene.Primary.Id, clickMs), button);

        Assert.Equal(Localization.Get("Workbench.TimelineTimingInvalid"), context.Session.LastError?.Message);
        Assert.Same(scene.Document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal(scene.Primary.Id, context.Session.SelectedCueId);
        Assert.False(Panel(timeline).ClipMenu.IsOpen);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left, "en-US")]
    [InlineData(MouseButton.Right, "zh-CN")]
    public async Task ClickingAnotherClipInTheSameTrackRejectsCollisionWithoutChangingSelectionOrDocument(
        MouseButton button, string language)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        Localization.SetLanguage(language);
        var scene = CreateScene(context, true);
        context.Session.Editor.Reset(scene.Document);
        var timeline = Prepare(context);
        Select(context, timeline, scene.Primary.Id);
        UiTestActions.Click(context.Window, "TimelineClassicTimingButton");

        Click(context.Window, timeline, BodyPoint(context.Window, timeline, scene.Other.Id, 5500), button);

        Assert.Equal(Localization.Get("Workbench.TimelineClipCollision"), context.Session.LastError?.Message);
        Assert.Same(scene.Document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal(scene.Primary.Id, context.Session.SelectedCueId);
        Assert.Equal(scene.Primary.Id, Assert.Single(context.ViewModel.Timeline.SelectedLayerIds));
        Assert.False(Panel(timeline).ClipMenu.IsOpen);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    public async Task ClassicBodyClickWithoutASelectedCueDoesNotSelectOrCreateACue(MouseButton button)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var scene = CreateScene(context);
        context.Session.Editor.Reset(scene.Document);
        var timeline = Prepare(context);
        context.Session.SelectTrack(new ProjectClipIndex(scene.Document).GetSubtitleTrackId(scene.Primary.Id));
        Assert.Null(context.Session.SelectedCueId);
        UiTestActions.Click(context.Window, "TimelineClassicTimingButton");

        Click(context.Window, timeline, BodyPoint(context.Window, timeline, scene.Other.Id, 2500), button);

        Assert.Null(context.Session.SelectedCueId);
        Assert.Empty(context.ViewModel.Timeline.SelectedLayerIds);
        Assert.Same(scene.Document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Null(context.Session.LastError);
        Assert.False(Panel(timeline).ClipMenu.IsOpen);
    }

    [AvaloniaFact]
    public async Task NormalModeRightClickStillSelectsTheClickedCueAndOpensItsContextMenu()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var scene = CreateScene(context);
        context.Session.Editor.Reset(scene.Document);
        var timeline = Prepare(context);
        Select(context, timeline, scene.Primary.Id);
        var menu = Panel(timeline).ClipMenu;
        try
        {
            Click(context.Window, timeline, BodyPoint(context.Window, timeline, scene.Other.Id, 2500), MouseButton.Right);

            Assert.True(menu.IsOpen);
            Assert.Equal(scene.Other.Id, context.Session.SelectedCueId);
            Assert.Equal(scene.Other.Id, Assert.Single(context.ViewModel.Timeline.SelectedLayerIds));
            Assert.Same(scene.Document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            menu.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, RawInputModifiers.None, 1040)]
    [InlineData(false, RawInputModifiers.Shift, 1000)]
    [InlineData(true, RawInputModifiers.None, 1000)]
    [InlineData(true, RawInputModifiers.Shift, 1040)]
    [InlineData(true, RawInputModifiers.Alt, 1040)]
    public async Task ShiftTemporarilyReversesSnapAndAltBypassesItWithoutChangingTheSavedToggle(
        bool snap, RawInputModifiers modifiers, int expectedMs)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var scene = CreateScene(context);
        context.Session.Editor.Reset(scene.Document);
        var timeline = Prepare(context);
        Select(context, timeline, scene.Primary.Id);
        context.ViewModel.Timeline.IsSnapEnabled = snap;
        UiTestActions.Click(context.Window, "TimelineClassicTimingButton");

        Click(context.Window, timeline, BodyPoint(context.Window, timeline, scene.Other.Id, 1040), MouseButton.Left, modifiers);

        Assert.Equal(new MediaTime(expectedMs, 1000), context.Session.SelectedCue!.Start);
        Assert.Equal(new MediaTime(4), context.Session.SelectedCue.End);
        Assert.Equal(snap, timeline.IsSnapEnabled);
        Assert.Equal(snap, context.ViewModel.Timeline.IsSnapEnabled);
        Assert.Equal(snap, context.Session.Preferences.TimelineSnapEnabled);
        Assert.Null(context.Session.LastError);
    }

    [AvaloniaFact]
    public async Task ClassicModePreservesRulerSeekingAndHeaderSelectionAndContextMenus()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.OpenMediaAsync();
        var scene = CreateScene(context);
        context.Session.Editor.Reset(scene.Document);
        var timeline = Prepare(context);
        Select(context, timeline, scene.Primary.Id);
        UiTestActions.Click(context.Window, "TimelineClassicTimingButton");
        var requests = 0;
        timeline.ClassicTimingRequested += (_, _) => requests++;

        Click(context.Window, timeline, new(timeline.HeaderWidth + 5 * timeline.PixelsPerSecond, timeline.RulerHeight / 2), MouseButton.Left);
        await EventuallyAsync(() => context.Controller.Snapshot.Position == new MediaTime(5) &&
            context.ViewModel.Timeline.Position == new MediaTime(5));

        Assert.Equal(0, requests);
        Assert.Equal(scene.Primary.Id, context.Session.SelectedCueId);
        Assert.Same(scene.Document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        var header = timeline.GetTrackHeaderRectangle(new ProjectClipIndex(scene.Document).GetSubtitleTrackId(scene.Other.Id))!.Value;
        timeline.SetViewport(timeline.Viewport with
        {
            VerticalOffset = Math.Max(0, timeline.Viewport.VerticalOffset + header.Top - timeline.RulerHeight)
        }, context.ViewModel.Timeline.FullDuration);
        Flush(context.Window);
        header = timeline.GetTrackHeaderRectangle(new ProjectClipIndex(scene.Document).GetSubtitleTrackId(scene.Other.Id))!.Value;
        Click(context.Window, timeline, new(header.Center.X, header.Top + 12), MouseButton.Left);
        Assert.Equal(new ProjectClipIndex(scene.Document).GetSubtitleTrackId(scene.Other.Id), context.Session.CurrentTrackId);
        Assert.Null(context.Session.SelectedCueId);
        var menu = Panel(timeline).TrackMenu;
        try
        {
            header = timeline.GetTrackHeaderRectangle(new ProjectClipIndex(scene.Document).GetSubtitleTrackId(scene.Other.Id))!.Value;
            Click(context.Window, timeline, new(header.Center.X, header.Top + 12), MouseButton.Right);
            Assert.True(menu.IsOpen);
            Assert.False(Panel(timeline).ClipMenu.IsOpen);
            Assert.Equal(0, requests);
            Assert.Same(scene.Document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            menu.Close();
        }
    }

    private static (ProjectDocument Document, SubtitleLine Primary, SubtitleLine Other) CreateScene(
        MainWindowTestContext context, bool sameTrack = false)
    {
        var otherTrack = new ProjectTrack { Name = "Other track 中文 ABC 123" };
        var primary = new SubtitleLine { Start = new(2), End = new(4), Text = "Primary 中文 ABC 123" };
        var other = new SubtitleLine
        {
            Start = new(sameTrack ? 5 : 1), End = new(6), Text = "Other clip"
        };
        var document = context.Session.DocumentSnapshot with
        {
            Tracks = sameTrack ? [ProjectTrack.Default] : [ProjectTrack.Default, otherTrack],
            Subtitles = [primary, other], Layers = [Layer(primary), Layer(other) with { TrackId = sameTrack ? ProjectTrack.DEFAULT_TRACK_ID : otherTrack.Id }]
        };
        return (document, primary, other);
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

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

    private static void Select(MainWindowTestContext context, SubtitleTimelineControl timeline, Guid id,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        EnsureClipVisible(context.Window, timeline, id);
        Click(context.Window, timeline, timeline.GetClipRectangle(id)!.Value.Center, MouseButton.Left, modifiers);
    }

    private static Point BodyPoint(Window window, SubtitleTimelineControl timeline, Guid cueId, int milliseconds)
    {
        EnsureClipVisible(window, timeline, cueId);
        return new(timeline.HeaderWidth + (milliseconds / 1000d - timeline.ViewStart) * timeline.PixelsPerSecond,
            timeline.GetClipRectangle(cueId)!.Value.Center.Y);
    }

    private static void EnsureClipVisible(Window window, SubtitleTimelineControl timeline, Guid id)
    {
        Flush(window);
        var clip = timeline.GetClipRectangle(id)!.Value;
        if (clip.Center.Y <= timeline.RulerHeight || clip.Center.Y >= timeline.Bounds.Height)
        {
            var model = Assert.IsType<TimelinePanelViewModel>(Panel(timeline).DataContext);
            timeline.SetViewport(timeline.Viewport with
            {
                VerticalOffset = Math.Max(0, timeline.Viewport.VerticalOffset + clip.Center.Y - timeline.RulerHeight - 40)
            }, model.FullDuration);
            Flush(window);
        }
    }

    private static void Click(Window window, SubtitleTimelineControl timeline, Point local, MouseButton button,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = timeline.TranslatePoint(local, window)!.Value;
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, button, modifiers);
        window.MouseUp(point, button, modifiers);
        Flush(window);
    }

    private static TimelinePanelView Panel(SubtitleTimelineControl timeline) =>
        Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>());

    private static RawInputModifiers CommandModifier() =>
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static async Task WaitForPersistenceAsync(MainWindowTestContext context)
    {
        using var store = new WorkbenchPreferencesStore(context.Session.PreferencesStore.DirectoryPath);
        var expected = context.Session.Preferences;
        await EventuallyAsync(() => store.Load() == expected);
        Assert.Null(store.LoadError);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Classic timing did not reach the expected state.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
