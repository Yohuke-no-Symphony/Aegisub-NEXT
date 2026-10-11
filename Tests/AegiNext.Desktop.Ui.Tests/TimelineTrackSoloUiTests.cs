using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineTrackSoloUiTests
{
    [AvaloniaFact]
    public async Task HeaderToggleSolosAnotherTrackAndOnlyHidesTimelineRowsWhilePreservingFocusedInvalidDraft()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var current = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var other = context.Session.Editor.AddTrack("Other 中文 ABC");
        var first = context.Session.Editor.AddSubtitle(new(0), new(2), "First", current);
        var second = context.Session.Editor.AddSubtitle(new(3), new(5), "Second", other);
        var scene = new ProjectLayer { Name = "Shape", Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(6), End = new(8), TrackId = current };
        context.Session.Editor.AddLayer(scene);
        context.Session.SelectCue(first);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "FontSizeInput");
        input.BringIntoView();
        context.Window.UpdateLayout();
        var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(textBox.Focus());
        input.RawText = "7e-";
        Dispatcher.UIThread.RunJobs();
        var before = context.Session.DocumentSnapshot;
        var preview = context.Session.PreviewDocument;
        var undo = context.Session.Editor.UndoLabel;
        var dirty = context.Session.HasUnsavedChanges;
        var viewport = context.ViewModel.Timeline.Viewport;
        Assert.NotNull(timeline.GetClipRectangle(first));
        Assert.NotNull(timeline.GetClipRectangle(second));
        Assert.NotNull(timeline.GetClipRectangle(scene.Id));

        ClickSolo(context.Window, timeline, other);

        Assert.Equal(other, context.ViewModel.Timeline.SoloTrackId);
        Assert.Equal(other, timeline.SoloTrackId);
        Assert.Null(timeline.GetTrackHeaderRectangle(current));
        Assert.NotNull(timeline.GetTrackHeaderRectangle(other));
        Assert.Null(timeline.GetClipRectangle(first));
        Assert.NotNull(timeline.GetClipRectangle(second));
        Assert.Null(timeline.GetClipRectangle(scene.Id));
        Assert.Equal(first, context.Session.SelectedCueId);
        Assert.Equal(current, context.Session.CurrentTrackId);
        Assert.True(textBox.IsFocused);
        Assert.Equal("7e-", input.RawText);
        Assert.Equal(2, context.ViewModel.Subtitles.Rows.Length);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.Same(preview, context.Session.PreviewDocument);
        Assert.Equal(undo, context.Session.Editor.UndoLabel);
        Assert.Equal(dirty, context.Session.HasUnsavedChanges);
        Assert.Equal(viewport, context.ViewModel.Timeline.Viewport);
        UiTestCapture.CaptureExportPanel(context.Window, "timeline-track-solo");

        ClickSolo(context.Window, timeline, other);

        Assert.Null(context.ViewModel.Timeline.SoloTrackId);
        Assert.NotNull(timeline.GetClipRectangle(first));
        Assert.NotNull(timeline.GetClipRectangle(second));
        Assert.NotNull(timeline.GetClipRectangle(scene.Id));
        Assert.True(textBox.IsFocused);
        Assert.Equal("7e-", input.RawText);
        Assert.Same(before, context.Session.DocumentSnapshot);
        input.RawText = before.Subtitles.Single(line => line.Id == first).Style.FontSize
            .ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    [AvaloniaFact]
    public async Task SubtitleListTrackSelectorAutomaticallyRestoresAllTimelineRows()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var current = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var solo = context.Session.Editor.AddTrack("Solo");
        var third = context.Session.Editor.AddTrack("Third");
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        ClickSolo(context.Window, timeline, solo);
        Assert.Equal(current, context.Session.CurrentTrackId);
        var selector = UiTestActions.Find<ComboBox>(context.Window, "SubtitleTrackCombo");
        var before = context.Session.DocumentSnapshot;

        selector.SelectedItem = context.ViewModel.Subtitles.Tracks.Single(track => track.Id == third);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(third, context.Session.CurrentTrackId);
        Assert.Null(context.ViewModel.Timeline.SoloTrackId);
        Assert.Null(timeline.SoloTrackId);
        Assert.NotNull(timeline.GetTrackHeaderRectangle(current));
        Assert.NotNull(timeline.GetTrackHeaderRectangle(solo));
        Assert.NotNull(timeline.GetTrackHeaderRectangle(third));
        Assert.Same(before, context.Session.DocumentSnapshot);
        ClickSolo(context.Window, timeline, solo);
        Assert.Equal(third, context.Session.CurrentTrackId);
        context.ViewModel.Subtitles.SelectTrack(third);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(context.ViewModel.Timeline.SoloTrackId);
        Assert.NotNull(timeline.GetTrackHeaderRectangle(current));
    }

    [AvaloniaFact]
    public async Task DeletingSoloTrackRestoresOtherRowsAndDeletingAllTracksClearsTheToggle()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var first = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var solo = context.Session.Editor.AddTrack("Solo");
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        ClickSolo(context.Window, timeline, solo);

        context.Session.Editor.RemoveTrack(solo);
        Dispatcher.UIThread.RunJobs();

        Assert.Null(context.ViewModel.Timeline.SoloTrackId);
        Assert.NotNull(timeline.GetTrackHeaderRectangle(first));
        Assert.Null(timeline.GetTrackSoloToggleRectangle(solo));
        Assert.True(context.Session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.Null(context.ViewModel.Timeline.SoloTrackId);
        Assert.NotNull(timeline.GetTrackHeaderRectangle(solo));
        context.Session.Editor.RemoveTrack(solo);
        ClickSolo(context.Window, timeline, first);
        context.Session.Editor.RemoveTrack(first);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(context.Session.DocumentSnapshot.Tracks);
        Assert.Null(context.ViewModel.Timeline.SoloTrackId);
        Assert.Null(timeline.SoloTrackId);
        Assert.Null(timeline.GetTrackSoloToggleRectangle(first));
    }

    [AvaloniaFact]
    public void StandaloneHeaderClickEmitsStableIdentityAndRestoresCollapsedRowsAndViewport()
    {
        using var environment = new UiTestEnvironment();
        var editor = new ProjectEditor();
        var first = editor.Snapshot.Tracks[0].Id;
        var solo = editor.AddTrack("Solo");
        var cue = editor.AddSubtitle(new(0), new(4), "Animated", solo);
        editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(1), 0.5));
        for (var index = 0; index < 15; index++)
        {
            editor.AddTrack($"Track {index}");
        }
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(editor.Snapshot, null, null);
        timeline.ToggleTrackCollapse(first);
        var animation = new TimelineEffectClipId(cue, new(AnimationProperty.OPACITY));
        timeline.TimelineViewState = timeline.TimelineViewState with { ExpandedAnimationClips = [] };
        var window = new Window { Width = 850, Height = 260, Content = timeline };
        var events = new List<Guid>();
        var unrelated = 0;
        timeline.TrackSoloRequested += (_, args) =>
        {
            events.Add(args.TrackId);
            timeline.SoloTrackId = timeline.SoloTrackId == args.TrackId ? null : args.TrackId;
        };
        timeline.TrackSelected += (_, _) => unrelated++;
        timeline.ClipSelectionChanged += (_, _) => unrelated++;
        timeline.SeekRequested += (_, _) => unrelated++;
        window.Show();
        try
        {
            Prepare(window);
            timeline.SetViewport(new TimelineViewport(1.25, 90, 10), 20);
            var before = timeline.Viewport;
            var toggle = timeline.GetTrackSoloToggleRectangle(solo)!.Value;
            var header = timeline.GetTrackHeaderRectangle(solo)!.Value;
            Assert.True(header.Contains(toggle));
            Assert.Equal(20, toggle.Width);
            Assert.Equal(20, toggle.Height);
            var point = timeline.TranslatePoint(toggle.Center, window)!.Value;
            window.MouseMove(point);
            Assert.Equal(Localization.Get("Workbench.SoloSubtitleTrack"), ToolTip.GetTip(timeline));
            Localization.SetLanguage("zh-CN");
            Assert.Equal(Localization.Get("Workbench.SoloSubtitleTrack"), ToolTip.GetTip(timeline));

            ClickSolo(window, timeline, solo);

            Assert.Equal(solo, timeline.SoloTrackId);
            Assert.Null(timeline.GetTrackHeaderRectangle(first));
            Assert.True(timeline.IsTrackCollapsed(first));
            Assert.True(timeline.IsEffectClipCollapsed(animation));
            Assert.Equal(before.StartSeconds, timeline.Viewport.StartSeconds);
            Assert.Equal(before.PixelsPerSecond, timeline.Viewport.PixelsPerSecond);
            ClickSolo(window, timeline, solo);
            Assert.Null(timeline.SoloTrackId);
            Assert.True(timeline.IsTrackCollapsed(first));
            Assert.True(timeline.IsEffectClipCollapsed(animation));
            Assert.Equal(before, timeline.Viewport);
            Assert.Equal(new[] { solo, solo }, events);
            Assert.Equal(0, unrelated);
            Assert.NotNull(timeline.GetTrackHeaderRectangle(first));
            Assert.NotNull(timeline.GetEffectClipRectangle(animation));
        }
        finally
        {
            window.Close();
        }
    }

    private static void ClickSolo(Window window, SubtitleTimelineControl timeline, Guid trackId)
    {
        Prepare(window);
        var rectangle = timeline.GetTrackSoloToggleRectangle(trackId)!.Value;
        var point = timeline.TranslatePoint(rectangle.Center, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Prepare(window);
    }

    private static void Prepare(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
