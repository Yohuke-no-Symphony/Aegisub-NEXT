using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Timeline;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClipTimingUiTests
{
    [AvaloniaTheory]
    [InlineData(LayerKind.SUBTITLE)]
    [InlineData(LayerKind.SHAPE)]
    [InlineData(LayerKind.IMAGE)]
    public async Task TimeInputsPreviewCropAndEnterCommitsOneUndoWithTheContentClockPreserved(LayerKind kind)
    {
        await using var context = new MainWindowTestContext();
        var before = await PrepareAsync(context, kind);
        var start = UiTestActions.Find<TextBox>(context.Window, "TimelineClipStartInput");
        var end = UiTestActions.Find<TextBox>(context.Window, "TimelineClipEndInput");
        Enter(context.Window, start, "00:00:01.250");
        Enter(context.Window, end, "00:00:04.125");

        var preview = context.Session.PreviewDocument;
        var layer = Assert.Single(preview.Layers);
        Assert.Equal(new MediaTime(5, 4), layer.Start);
        Assert.Equal(new MediaTime(33, 8), layer.End);
        Assert.Equal(new MediaTime(5, 4), layer.AnimationOffset);
        Assert.Equal(SceneEvaluator.EvaluateTrack(before.Layers[0].Tracks[0], new(2)),
            SceneEvaluator.EvaluateTrack(layer.Tracks[0], new(2)));
        if (kind == LayerKind.SUBTITLE)
        {
            Assert.Equal(layer.Start, Assert.Single(preview.Subtitles).Start);
            Assert.Equal(layer.End, preview.Subtitles[0].End);
        }
        if (kind == LayerKind.IMAGE)
        {
            Assert.Equal(new MediaTime(11, 2), layer.Image!.Playback!.SourceOffset);
        }
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.HasProjectDrafts);
        Assert.True(end.IsFocused);

        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();

        var committed = context.Session.DocumentSnapshot.Layers[0];
        Assert.Equal(layer.Start, committed.Start);
        Assert.Equal(layer.End, committed.End);
        Assert.Equal(layer.AnimationOffset, committed.AnimationOffset);
        Assert.Equal(layer.Tracks[0].Keyframes.ToArray(), committed.Tracks[0].Keyframes.ToArray());
        Assert.False(context.Session.HasProjectDrafts);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("unfinished")]
    [InlineData("00:00:06.000")]
    public async Task InvalidTimingPreservesBothDraftsAndBlocksSelectionAndOtherPanelCommits(string invalidStart)
    {
        await using var context = new MainWindowTestContext();
        var original = await PrepareAsync(context, LayerKind.SHAPE);
        var second = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(6), End = new(10) };
        var before = original with { Layers = original.Layers.Add(second) };
        context.Session.Editor.Reset(before);
        var timeline = context.ViewModel.Timeline;
        timeline.LayerStart = invalidStart;
        timeline.LayerEnd = "00:00:04.125";
        context.ViewModel.Effects.ScaleXText = "1.6";

        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.Equal(invalidStart, timeline.LayerStart);
        Assert.Equal("00:00:04.125", timeline.LayerEnd);
        Assert.Equal("timeline", context.ViewModel.InvalidPanelId);
        Assert.NotNull(timeline.ClipTimingValidationError);
        Assert.Null(context.ViewModel.Effects.ValidationError);
        context.Session.SelectLayer(second.Id, [second.Id]);
        Assert.Equal(before.Layers[0].Id, context.Session.SelectedLayerId);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);

        timeline.LayerStart = "00:00:01.250";
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(new MediaTime(5, 4), context.Session.SelectedLayer!.Start);
        Assert.Equal(new MediaTime(33, 8), context.Session.SelectedLayer.End);
        Assert.Equal(1.6, context.Session.SelectedLayer.Transform.ScaleX);
        Assert.Null(timeline.ClipTimingValidationError);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task EscapeRestoresOnlyTheFocusedTimeFieldAndKeepsTheOtherPendingBoundary()
    {
        await using var context = new MainWindowTestContext();
        var before = await PrepareAsync(context, LayerKind.SHAPE);
        var end = UiTestActions.Find<TextBox>(context.Window, "TimelineClipEndInput");
        var start = UiTestActions.Find<TextBox>(context.Window, "TimelineClipStartInput");
        Enter(context.Window, end, "00:00:04.125");
        Enter(context.Window, start, "unfinished");
        Assert.False(context.Session.TryCommitDrafts(false));

        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();

        Assert.True(start.IsFocused);
        Assert.Equal("00:00:00.000", start.Text);
        Assert.Equal("00:00:04.125", end.Text);
        Assert.Equal(new MediaTime(33, 8), context.Session.PreviewDocument.Layers[0].End);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task LeavingTimeInputsCommitsOneUndoAndUntouchedSubmillisecondTimeRemainsExact()
    {
        await using var context = new MainWindowTestContext();
        var original = await PrepareAsync(context, LayerKind.SHAPE);
        var before = original with
        {
            Layers = [LayerAnimationTiming.Retime(original.Layers[0], new(-1, 3), new(5), TimelineEditMode.CROP)]
        };
        context.Session.Editor.Reset(before);
        var end = UiTestActions.Find<TextBox>(context.Window, "TimelineClipEndInput");
        Enter(context.Window, end, "00:00:04.125");
        Assert.Same(before, context.Session.DocumentSnapshot);

        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline").Focus());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new MediaTime(-1, 3), context.Session.SelectedLayer!.Start);
        Assert.Equal(new MediaTime(33, 8), context.Session.SelectedLayer.End);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task AChangedSourceCannotReceiveTheFrozenTimingDraftAndEscapeResetsItsOrigin()
    {
        await using var context = new MainWindowTestContext();
        var before = await PrepareAsync(context, LayerKind.SHAPE);
        var start = UiTestActions.Find<TextBox>(context.Window, "TimelineClipStartInput");
        Enter(context.Window, start, "00:00:01.250");
        context.Session.Editor.UpdateLayer(before.Layers[0].Id, layer => layer with { Name = "Changed source" });
        var changed = context.Session.DocumentSnapshot;

        Assert.Equal("00:00:01.250", start.Text);
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Same(changed, context.Session.DocumentSnapshot);
        Assert.Equal("timeline", context.ViewModel.InvalidPanelId);
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.Session.HasClipTimingDrafts);
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Same(changed, context.Session.DocumentSnapshot);
        Enter(context.Window, start, "00:00:01.500");
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new MediaTime(3, 2), context.Session.SelectedLayer!.Start);
        Assert.Equal("Changed source", context.Session.SelectedLayer.Name);
    }

    [AvaloniaFact]
    public async Task DetachingAndReattachingCancelsQueuedFocusCommitWithoutDiscardingTheDraft()
    {
        await using var context = new MainWindowTestContext();
        var before = await PrepareAsync(context, LayerKind.SHAPE);
        using var panel = new TimelinePanelView(context.ViewModel.Timeline, context.Session);
        var window = new Window { Width = 480, Height = 280, Content = panel };
        try
        {
            window.Show();
            window.UpdateLayout();
            var start = panel.FindControl<TextBox>("TimelineClipStartInput")!;
            Enter(window, start, "00:00:01.250");
            Assert.True(panel.FindControl<SubtitleTimelineControl>("Timeline")!.Focus());
            window.Content = null;
            window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(before, context.Session.DocumentSnapshot);
            Assert.Equal("00:00:01.250", start.Text);
            Assert.True(context.Session.HasClipTimingDrafts);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.True(context.Session.TryCommitDrafts(false));
            Assert.Equal(new MediaTime(5, 4), context.Session.SelectedLayer!.Start);
        }
        finally
        {
            context.ViewModel.Timeline.RestoreClipTimingField("TimelineClipStartInput");
            context.ViewModel.Timeline.RestoreClipTimingField("TimelineClipEndInput");
            window.Close();
        }
    }

    private static async Task<ProjectDocument> PrepareAsync(MainWindowTestContext context, LayerKind kind)
    {
        await context.Session.ApplicationContext.Initialization;
        var cue = new SubtitleLine { Start = MediaTime.Zero, End = new(5), Text = "Timing 中文 123" };
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/clip-timing.png");
        var layer = new ProjectLayer
        {
            Id = kind == LayerKind.SUBTITLE ? cue.Id : Guid.NewGuid(),
            Kind = kind,
            Start = MediaTime.Zero,
            End = new(5),
            SubtitleId = kind == LayerKind.SUBTITLE ? cue.Id : null,
            Shape = kind == LayerKind.SHAPE ? new(ShapeKind.RECTANGLE, 100, 80) : null,
            Image = kind == LayerKind.IMAGE ? new(asset.Id, 100, 80)
            {
                Playback = new() { Speed = 2, SourceOffset = new(3) }
            } : null,
            Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.25), new(new(5), 0.75)])]
        };
        var document = new ProjectDocument
        {
            Subtitles = kind == LayerKind.SUBTITLE ? [cue] : [],
            Assets = kind == LayerKind.IMAGE ? [asset] : [],
            Layers = [layer]
        };
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        context.Window.Layouts.Activate(WorkbenchPanelIds.TIMELINE);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        return document;
    }

    private static void Enter(Window window, TextBox input, string text)
    {
        Assert.True(input.Focus());
        UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }
}
