using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineContextEditingUiTests
{
    [AvaloniaFact]
    public async Task RightClickingASelectedClipKeepsTheWholeSelectionAndDeleteIsOneTransaction()
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Second" };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Name = "Selected shape", Start = new(5), End = new(6),
            Shape = new(ShapeKind.RECTANGLE, 40, 20)
        };
        var original = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second), shape] };
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, CommandModifier());
        Select(context, timeline, shape.Id, CommandModifier());
        var selection = new[] { first.Id, second.Id, shape.Id };
        var menu = OpenMenu(context, timeline, timeline.GetClipRectangle(first.Id)!.Value.Center);

        Assert.True(menu.IsOpen);
        Assert.Equal(selection.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(timeline.HasActiveDrag);
        await ExecuteAsync(menu, "DeleteTimelineClipsMenuItem");
        menu.Close();
        Flush(context.Window);

        Assert.Empty(context.Session.DocumentSnapshot.Subtitles);
        Assert.Empty(context.Session.DocumentSnapshot.Layers);
        Assert.Empty(context.ViewModel.Timeline.SelectedLayerIds);
        Assert.Null(context.Session.SelectedKeyTime);
        Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, context.Session.CurrentTrackId);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task RightClickingAnUnselectedClipReplacesTheClipSelectionBeforeOpeningItsMenu()
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Second" };
        var original = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        var menu = OpenMenu(context, timeline, timeline.GetClipRectangle(second.Id)!.Value.Center);
        try
        {
            Assert.True(menu.IsOpen);
            Assert.Equal(second.Id, Assert.Single(context.ViewModel.Timeline.SelectedLayerIds));
            Assert.Equal(second.Id, context.Session.SelectedLayerId);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            menu.Close();
        }
    }

    [AvaloniaFact]
    public async Task BlankTrackContextCreatesTwoSecondsAtItsCapturedTimeAndTrackWithoutFollowingPlayback()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var destinationTrack = new ProjectTrack { Name = "Destination" };
        var original = context.Session.DocumentSnapshot with { Tracks = [ProjectTrack.Default, destinationTrack] };
        context.Session.Editor.Reset(original);
        await context.Controller.SeekAsync(new(1));
        var timeline = Prepare(context);
        var header = timeline.GetTrackHeaderRectangle(destinationTrack.Id)!.Value;
        var click = new Point(timeline.HeaderWidth + 6 * timeline.PixelsPerSecond, header.Center.Y);
        var menu = OpenMenu(context, timeline, click);
        Assert.True(menu.IsOpen);
        await ExecuteAsync(menu, "CreateTimelineSubtitleMenuItem");
        menu.Close();
        Flush(context.Window);

        var created = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(new MediaTime(6), created.Start);
        Assert.Equal(new MediaTime(8), created.End);
        Assert.Equal(destinationTrack.Id, context.Session.ClipIndex.GetSubtitleTrackId(created.Id));
        Assert.Equal(string.Empty, created.Text);
        Assert.Equal(created.Id, context.Session.SelectedCueId);
        Assert.Equal(created.Id, Assert.Single(context.Session.DocumentSnapshot.Layers).SubtitleId);
        var createdDocument = context.Session.DocumentSnapshot;
        Assert.Null(context.ViewModel.Timeline.TimingPreview);
        Assert.False(context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));

        await context.Controller.PlayAsync();
        context.Clock.Advance(TimeSpan.FromSeconds(4));
        context.Session.Tick();
        Assert.Equal(new MediaTime(5), context.ViewModel.Timeline.Position);
        Assert.Null(context.ViewModel.Timeline.TimingPreview);
        Assert.Same(createdDocument, context.Session.DocumentSnapshot);
        context.Clock.Advance(TimeSpan.FromSeconds(2));
        context.Session.Tick();
        Assert.Equal(new MediaTime(7), context.ViewModel.Timeline.Position);
        Assert.Equal(new MediaTime(8), Assert.Single(context.Session.PreviewDocument.Subtitles).End);
        Assert.Equal(new MediaTime(8), Assert.Single(context.Session.PreviewDocument.Layers).End);
        context.Clock.Advance(TimeSpan.FromSeconds(2));
        context.Session.Tick();
        Assert.Equal(new MediaTime(9), context.ViewModel.Timeline.Position);
        Assert.Null(context.ViewModel.Timeline.TimingPreview);
        Assert.Same(createdDocument, context.Session.DocumentSnapshot);
        await context.Controller.PauseAsync();
        await context.Controller.SeekAsync(new(12));
        Assert.Same(createdDocument, context.Session.DocumentSnapshot);
        Assert.True(timeline.Focus());
        Assert.False(context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        UiTestActions.Press(context.Window, Key.Z, CommandModifier());
        Flush(context.Window);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task ContextCopyFreezesTheContentAndContextPasteKeepsSpacingAtTheCapturedTime()
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "Copied first" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Copied second" };
        var original = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, CommandModifier());
        var menu = OpenMenu(context, timeline, timeline.GetClipRectangle(second.Id)!.Value.Center);
        await ExecuteAsync(menu, "CopyTimelineClipsMenuItem");
        menu.Close();
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);

        context.Session.Editor.UpdateSubtitle(first.Id, cue => cue with { Text = "Changed after copy" });
        var beforePaste = context.Session.DocumentSnapshot;
        context.Session.Editor.Reset(beforePaste);
        Flush(context.Window);
        var row = timeline.GetTrackHeaderRectangle(ProjectTrack.DEFAULT_TRACK_ID)!.Value;
        menu = OpenMenu(context, timeline, new(timeline.HeaderWidth + 6 * timeline.PixelsPerSecond, row.Bottom - 14));
        Assert.Equal(new[] { first.Id, second.Id }.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        await ExecuteAsync(menu, "PasteTimelineClipsMenuItem");
        menu.Close();
        Flush(context.Window);

        var copies = context.Session.DocumentSnapshot.Subtitles.Where(cue => cue.Id != first.Id && cue.Id != second.Id)
            .OrderBy(cue => cue.Start).ToArray();
        Assert.Equal(2, copies.Length);
        Assert.Equal("Copied first", copies[0].Text);
        Assert.Equal("Copied second", copies[1].Text);
        Assert.Equal(new MediaTime(6), copies[0].Start);
        Assert.Equal(new MediaTime(8), copies[1].Start);
        Assert.Equal(new MediaTime(1), copies[0].End - copies[0].Start);
        Assert.Equal(new MediaTime(1), copies[1].End - copies[1].Start);
        var copyIds = copies.Select(cue => cue.Id).ToHashSet();
        Assert.Equal(context.Session.DocumentSnapshot.Layers.Where(layer => layer.SubtitleId is { } id && copyIds.Contains(id))
            .Select(layer => layer.Id).Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(copies[1].Id, context.Session.SelectedCueId);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(beforePaste, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task PasteIntoAnOccupiedTrackIsRejectedAtomicallyWithoutLosingTheSelection()
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Second" };
        var original = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, CommandModifier());
        var menu = OpenMenu(context, timeline, timeline.GetClipRectangle(second.Id)!.Value.Center);
        await ExecuteAsync(menu, "CopyTimelineClipsMenuItem");
        menu.Close();
        var row = timeline.GetTrackHeaderRectangle(ProjectTrack.DEFAULT_TRACK_ID)!.Value;
        menu = OpenMenu(context, timeline, new(timeline.HeaderWidth + 2.5 * timeline.PixelsPerSecond, row.Bottom - 14));
        await ExecuteAsync(menu, "PasteTimelineClipsMenuItem");
        menu.Close();
        Flush(context.Window);

        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.NotNull(context.Session.LastError);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
    }

    [AvaloniaFact]
    public async Task TimelineCopyPasteAndDeleteKeysUseTheWholeSelectionAndPointer()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Second" };
        var original = context.Session.DocumentSnapshot with { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, CommandModifier());
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.C, CommandModifier());
        Flush(context.Window);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        await context.Controller.SeekAsync(new(10));
        Flush(context.Window);
        var track = timeline.GetTrackHeaderRectangle(ProjectTrack.DEFAULT_TRACK_ID)!.Value;
        context.Window.MouseMove(timeline.TranslatePoint(new(timeline.HeaderWidth + 6 * timeline.PixelsPerSecond,
            track.Bottom - 14), context.Window)!.Value);
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.V, CommandModifier());
        Flush(context.Window);

        var pasted = context.Session.DocumentSnapshot;
        var copies = pasted.Subtitles.Where(cue => cue.Id != first.Id && cue.Id != second.Id).OrderBy(cue => cue.Start).ToArray();
        Assert.Equal(2, copies.Length);
        Assert.Equal(new MediaTime(6), copies[0].Start);
        Assert.Equal(new MediaTime(8), copies[1].Start);
        var copyIds = copies.Select(cue => cue.Id).ToHashSet();
        Assert.Equal(pasted.Layers.Where(layer => layer.SubtitleId is { } id && copyIds.Contains(id))
            .Select(layer => layer.Id).Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        UiTestActions.Press(context.Window, Key.Delete);
        Flush(context.Window);

        Assert.Equal(new[] { first.Id, second.Id }.Order(), context.Session.DocumentSnapshot.Subtitles.Select(cue => cue.Id).Order());
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(pasted, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task SubtitleTextInputKeepsCopyPasteAndDeleteLocal()
    {
        await using var context = new MainWindowTestContext();
        var cue = new SubtitleLine { Start = new(1), End = new(2), Text = "Editable text" };
        var original = new ProjectDocument { Subtitles = [cue], Layers = [Layer(cue)] };
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        Select(context, timeline, cue.Id);
        context.Window.Layouts.Activate("subtitles");
        Flush(context.Window);
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var input = list.GetVisualDescendants().OfType<TextBox>().Single(control =>
            control.DataContext is SubtitleRow row && row.Id == cue.Id && Grid.GetColumn(control) == 4);
        Assert.True(input.Focus());
        input.SelectAll();
        var clipboard = context.Window.Clipboard!;
        await clipboard.SetTextAsync("Text only");
        try
        {
            UiTestActions.Press(context.Window, Key.C, CommandModifier());
            UiTestActions.Press(context.Window, Key.V, CommandModifier());
            UiTestActions.Press(context.Window, Key.Delete);
            Flush(context.Window);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.Equal(cue.Id, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Id);
            Assert.Equal(cue.Id, Assert.Single(context.Session.DocumentSnapshot.Layers).Id);
            Assert.True(input.IsFocused);
        }
        finally
        {
            context.ViewModel.Subtitles.Rows.Single(row => row.Id == cue.Id).Accept(cue);
        }
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
        var point = timeline.TranslatePoint(timeline.GetClipRectangle(id)!.Value.Center, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left, modifiers);
        context.Window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(context.Window);
    }

    private static ContextMenu OpenMenu(MainWindowTestContext context, SubtitleTimelineControl timeline, Point local)
    {
        var point = timeline.TranslatePoint(local, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Right);
        context.Window.MouseUp(point, MouseButton.Right);
        Flush(context.Window);
        return Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>()).ClipMenu;
    }

    private static async Task ExecuteAsync(ContextMenu menu, string name)
    {
        var item = TimelineTrackTestActions.Item(menu, name);
        Assert.True(item.Command!.CanExecute(null));
        if (item.Command is IAsyncRelayCommand command)
        {
            await command.ExecuteAsync(null);
        }
        else
        {
            item.Command.Execute(null);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static RawInputModifiers CommandModifier() => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
