using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineTrackCollapseUiTests
{
    [AvaloniaFact]
    public void HeaderExpanderRequestsStableIdentityAndWaitsForTheStateConsumer()
    {
        var editor = new ProjectEditor();
        var track = editor.Snapshot.Tracks[0].Id;
        var cue = editor.AddSubtitle(new(0), new(4), "Animated", track);
        editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(1), 0.5));
        var row = new TimelineEffectClipId(cue, new(AnimationProperty.OPACITY));
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(editor.Snapshot, null, null);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [] };
        TimelineTrackCollapseEventArgs? request = null;
        timeline.TrackCollapseRequested += (_, e) => request = e;
        var selections = 0;
        timeline.TrackSelected += (_, _) => selections++;
        timeline.ClipSelectionChanged += (_, _) => selections++;
        timeline.SeekRequested += (_, _) => selections++;
        var window = new Window { Width = 800, Height = 350, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            var expanded = timeline.GetEffectClipRectangle(row);

            ClickExpander(window, timeline, track);

            Assert.NotNull(request);
            Assert.Equal(track, request.Id);
            Assert.True(request.IsCollapsed);
            Assert.False(timeline.IsTrackCollapsed(track));
            Assert.Equal(expanded, timeline.GetEffectClipRectangle(row));
            Assert.Equal(0, selections);
            timeline.TimelineViewState = timeline.TimelineViewState with { CollapsedTrackIds = [request.Id] };
            Prepare(window);
            Assert.True(timeline.IsTrackCollapsed(track));
            Assert.Null(timeline.GetEffectClipRectangle(row));
            Assert.True(timeline.IsEffectClipCollapsed(row));
            Assert.NotNull(timeline.GetClipRectangle(cue));

            ClickExpander(window, timeline, track);

            Assert.False(request.IsCollapsed);
            timeline.TimelineViewState = timeline.TimelineViewState with { CollapsedTrackIds = [] };
            Prepare(window);
            Assert.Equal(expanded, timeline.GetEffectClipRectangle(row));
            Assert.True(timeline.IsEffectClipCollapsed(row));
            Assert.Equal(0, selections);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TrackAndClipStateComeFromTheViewStateAndKeepIndependentAnimationCollapse()
    {
        var child = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])]
        };
        var row = new TimelineEffectClipId(child.Id, new(AnimationProperty.OPACITY));
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(new() { Layers = [child] }, null, null);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [] };
        var requests = new List<TimelineTrackCollapseEventArgs>();
        timeline.TrackCollapseRequested += (_, e) =>
        {
            requests.Add(e);
            var current = timeline.TimelineViewState;
            timeline.TimelineViewState = current with
            {
                CollapsedTrackIds = e.IsCollapsed ? current.CollapsedTrackIds.Add(e.Id) : current.CollapsedTrackIds.Remove(e.Id)
            };
        };
        var window = new Window { Width = 800, Height = 400, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);

            ClickExpander(window, timeline, child.TrackId);

            Assert.Equal(child.TrackId, Assert.Single(requests).Id);
            Assert.True(timeline.IsTrackCollapsed(child.TrackId));
            Assert.Null(timeline.GetTrackExpanderRectangle(child.Id));
            Assert.Null(timeline.GetEffectClipRectangle(row));

            ClickExpander(window, timeline, child.TrackId);

            Assert.False(timeline.IsTrackCollapsed(child.TrackId));
            Assert.NotNull(timeline.GetClipRectangle(child.Id));
            Assert.True(timeline.IsEffectClipCollapsed(row));
            Assert.Equal(40, timeline.GetEffectClipRectangle(row)!.Value.Height);
            Assert.Equal(new[] { child.TrackId, child.TrackId }, requests.Select(request => request.Id));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task IntegratedHeaderCollapseKeepsTheFocusedDraftAndContentHistory()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var track = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var cue = context.Session.Editor.AddSubtitle(new(0), new(4), "Draft", track);
        context.Session.Editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(1), 0.5));
        context.Session.SelectCue(cue);
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
        try
        {
            ClickExpander(context.Window, timeline, track);

            Assert.True(timeline.IsTrackCollapsed(track));
            Assert.Equal(track, Assert.Single(context.Session.TimelineViewState.CollapsedTrackIds));
            Assert.True(textBox.IsFocused);
            Assert.Equal("7e-", input.RawText);
            Assert.Same(before, context.Session.DocumentSnapshot);
            Assert.Same(preview, context.Session.PreviewDocument);
            Assert.Equal(undo, context.Session.Editor.UndoLabel);

            ClickExpander(context.Window, timeline, track);

            Assert.False(timeline.IsTrackCollapsed(track));
            Assert.Empty(context.Session.TimelineViewState.CollapsedTrackIds);
            Assert.True(textBox.IsFocused);
            Assert.Equal("7e-", input.RawText);
            Assert.Same(before, context.Session.DocumentSnapshot);
            Assert.Equal(undo, context.Session.Editor.UndoLabel);
        }
        finally
        {
            input.RawText = before.Subtitles.Single(line => line.Id == cue).Style.FontSize
                .ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    private static void ClickExpander(Window window, SubtitleTimelineControl timeline, Guid id)
    {
        Prepare(window);
        var point = timeline.TranslatePoint(timeline.GetTrackExpanderRectangle(id)!.Value.Center, window)!.Value;
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Prepare(window);
    }

    private static void Prepare(Window window)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
