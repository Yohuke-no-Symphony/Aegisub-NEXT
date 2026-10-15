using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
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
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClipTimingUiTests
{
    [AvaloniaTheory]
    [InlineData(LayerKind.SUBTITLE, true)]
    [InlineData(LayerKind.SUBTITLE, false)]
    [InlineData(LayerKind.SHAPE, true)]
    [InlineData(LayerKind.SHAPE, false)]
    [InlineData(LayerKind.IMAGE, true)]
    [InlineData(LayerKind.IMAGE, false)]
    public async Task ClipEdgeCropPreviewsWithoutEditingAndReleasePreservesTheContentClockWithOneUndo(LayerKind kind, bool left)
    {
        await using var context = new MainWindowTestContext();
        var before = await PrepareAsync(context, kind);
        var timeline = PrepareTimeline(context);
        var clip = timeline.GetClipRectangle(before.Layers[0].Id)!.Value;
        var local = new Point(left ? clip.Left + 2 : clip.Right - 2, clip.Center.Y);
        var origin = timeline.TranslatePoint(local, context.Window)!.Value;
        var destination = origin + new Vector(left ? 80 : -56, 0);
        Assert.Same(timeline, context.Window.InputHitTest(origin));
        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);

        Assert.True(timeline.HasActiveDrag);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        var preview = timeline.GetClipRectangle(before.Layers[0].Id)!.Value;
        Assert.Equal(left ? clip.Left + 80 : clip.Left, preview.Left, 6);
        Assert.Equal(left ? clip.Right : clip.Right - 56, preview.Right, 6);
        context.Window.MouseUp(destination, MouseButton.Left);
        await context.Session.WaitForProjectIdleAsync();
        Flush(context.Window);

        var layer = Assert.Single(context.Session.DocumentSnapshot.Layers);
        Assert.Equal(left ? new MediaTime(5, 4) : MediaTime.Zero, layer.Start);
        Assert.Equal(left ? new MediaTime(5) : new MediaTime(33, 8), layer.End);
        Assert.Equal(left ? new MediaTime(5, 4) : MediaTime.Zero, layer.AnimationOffset);
        var retainedRange = LayerAnimationTiming.GetRange(layer);
        foreach (var contentTime in new[] { retainedRange.Minimum, new MediaTime(2), retainedRange.Maximum })
        {
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(before.Layers[0].Tracks[0], contentTime),
                SceneEvaluator.EvaluateScalarTrack(layer.Tracks[0], contentTime), 12);
        }
        if (kind == LayerKind.SUBTITLE)
        {
            Assert.Equal(layer.Start, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Start);
            Assert.Equal(layer.End, context.Session.DocumentSnapshot.Subtitles[0].End);
        }
        if (kind == LayerKind.IMAGE)
        {
            Assert.Equal(left ? new MediaTime(11, 2) : new MediaTime(3), layer.Image!.Playback!.SourceOffset);
        }
        Assert.False(context.Session.HasProjectDrafts);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("unfinished")]
    [InlineData("00:00:06.000")]
    public async Task InvalidSubtitleListTimingPreservesBothDraftsAndBlocksSelectionAndOtherPanelCommits(string invalidStart)
    {
        await using var context = new MainWindowTestContext();
        var original = await PrepareAsync(context, LayerKind.SUBTITLE);
        var second = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(6), End = new(10) };
        var before = original with { Layers = original.Layers.Add(second) };
        context.Session.Editor.Reset(before);
        var row = Assert.Single(context.ViewModel.Subtitles.Rows);
        row.StartText = invalidStart;
        row.EndText = "00:00:04.125";
        context.ViewModel.Effects.ScaleXText = "1.6";
        Assert.False(context.Session.TryCommitDrafts(false));
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.Equal(invalidStart, row.StartText);
        Assert.Equal("00:00:04.125", row.EndText);
        Assert.Equal("subtitles", context.ViewModel.InvalidPanelId);
        Assert.Equal(row.Id, context.ViewModel.Subtitles.InvalidRowId);
        Assert.Null(context.ViewModel.Effects.ValidationError);
        context.Session.SelectLayer(second.Id, [second.Id]);
        Assert.Equal(before.Layers[0].Id, context.Session.SelectedLayerId);
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        row.StartText = "00:00:01.250";
        Assert.True(context.Session.TryCommitDrafts(false));
        Assert.Equal(new MediaTime(5, 4), context.Session.SelectedLayer!.Start);
        Assert.Equal(new MediaTime(33, 8), context.Session.SelectedLayer.End);
        Assert.Equal(1.6, context.Session.SelectedLayer.Transform.ScaleX);
        Assert.Null(context.ViewModel.Subtitles.InvalidRowId);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task RightEdgeCropLeavesAnUntouchedSubmillisecondStartExact()
    {
        await using var context = new MainWindowTestContext();
        var original = await PrepareAsync(context, LayerKind.SHAPE);
        var before = original with { Layers = [LayerAnimationTiming.Retime(original.Layers[0], new(-1, 3), new(5), TimelineEditMode.CROP)] };
        context.Session.Editor.Reset(before);
        var timeline = PrepareTimeline(context);
        var clip = timeline.GetClipRectangle(before.Layers[0].Id)!.Value;
        var origin = timeline.TranslatePoint(new(clip.Right - 2, clip.Center.Y), context.Window)!.Value;
        var destination = origin + new Vector(-56, 0);
        Assert.Same(timeline, context.Window.InputHitTest(origin));
        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        Assert.Same(before, context.Session.DocumentSnapshot);
        context.Window.MouseUp(destination, MouseButton.Left);
        await context.Session.WaitForProjectIdleAsync();
        Assert.Equal(new MediaTime(-1, 3), context.Session.SelectedLayer!.Start);
        Assert.Equal(new MediaTime(-1, 3), context.Session.SelectedLayer.AnimationOffset);
        Assert.Equal(new MediaTime(33, 8), context.Session.SelectedLayer.End);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacingTheSourceOrDetachingThePanelCancelsUncommittedTiming(bool detach)
    {
        await using var context = new MainWindowTestContext();
        var before = await PrepareAsync(context, LayerKind.SHAPE);
        using var panel = new TimelinePanelView(context.ViewModel.Timeline, context.Session);
        var window = new Window { Width = 800, Height = 280, Content = panel };
        try
        {
            window.Show();
            context.ViewModel.Timeline.IsSnapEnabled = false;
            context.ViewModel.Timeline.IsStepEnabled = false;
            context.ViewModel.Timeline.Viewport = new(0, 64);
            Flush(window);
            var timeline = panel.FindControl<SubtitleTimelineControl>("Timeline")!;
            var clip = timeline.GetClipRectangle(before.Layers[0].Id)!.Value;
            var local = new Point(clip.Left + 2, clip.Center.Y);
            Assert.True(new Rect(timeline.Bounds.Size).Contains(local));
            var origin = timeline.TranslatePoint(local, window)!.Value;
            Assert.True(new Rect(window.ClientSize).Contains(origin));
            var destination = origin + new Vector(80, 0);
            Assert.Same(timeline, window.InputHitTest(origin));
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.True(timeline.HasActiveDrag);
            if (detach)
            {
                window.Content = null;
                window.Content = panel;
            }
            else
            {
                context.Session.Editor.UpdateLayer(before.Layers[0].Id, layer => layer with { Name = "Changed source" });
            }
            Flush(window);
            var current = context.Session.DocumentSnapshot;
            Assert.False(timeline.HasActiveDrag);
            window.MouseUp(destination, MouseButton.Left);
            await context.Session.WaitForProjectIdleAsync();
            Assert.Same(current, context.Session.DocumentSnapshot);
            Assert.Equal(MediaTime.Zero, context.Session.SelectedLayer!.Start);
            if (detach)
            {
                Assert.Same(before, current);
            }
            else
            {
                Assert.Equal("Changed source", context.Session.SelectedLayer.Name);
                Assert.True(context.Session.Editor.Undo());
                Assert.Same(before, context.Session.DocumentSnapshot);
            }
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TimelineHasNoBoundaryInputsAndTheInstanceInspectorStillEditsItsOwnTimeRange()
    {
        await using var context = new MainWindowTestContext();
        var original = await PrepareAsync(context, LayerKind.SHAPE);
        var layer = original.Layers[0];
        var before = ProjectEditingOperations.AddEffectInstances(original, [layer.Id], "Fade", BuiltinEffectScripts.Get("fade-in").Source);
        context.Session.Editor.Reset(before);
        var panel = context.Window.Panels[WorkbenchPanelIds.TIMELINE];
        Assert.Null(panel.FindControl<Control>("TimelineClipTimingSection"));
        Assert.Null(panel.FindControl<TextBox>("TimelineClipStartInput"));
        Assert.Null(panel.FindControl<TextBox>("TimelineClipEndInput"));
        var timeline = PrepareTimeline(context);
        Assert.Equal(28, timeline.TranslatePoint(default, panel)!.Value.Y, 3);
        var instance = Assert.Single(before.Layers[0].Effects);
        Assert.True(context.ViewModel.Timeline.SelectEffectClip(new(new(layer.Id, new(AnimationProperty.OPACITY), instance.Id))));
        context.Window.Layouts.Activate(WorkbenchPanelIds.EFFECTS);
        Enter(context.Window, InstanceInput(context, "InstanceStartInput"), "1.25");
        UiTestActions.Press(context.Window, Key.Enter);
        Flush(context.Window);
        var startChanged = context.Session.DocumentSnapshot;
        Assert.Equal(new MediaTime(5, 4), startChanged.Layers[0].Effects[0].Start);
        Enter(context.Window, InstanceInput(context, "InstanceEndInput"), "4.125");
        UiTestActions.Press(context.Window, Key.Enter);
        Flush(context.Window);
        var changed = context.Session.DocumentSnapshot.Layers[0];
        Assert.Equal(new MediaTime(5, 4), changed.Effects[0].Start);
        Assert.Equal(new MediaTime(33, 8), changed.Effects[0].End);
        Assert.Equal(layer.Start, changed.Start);
        Assert.Equal(layer.End, changed.End);
        Assert.Equal(layer.AnimationOffset, changed.AnimationOffset);
        Assert.Equal(layer.Tracks, changed.Tracks);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(startChanged, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static async Task<ProjectDocument> PrepareAsync(MainWindowTestContext context, LayerKind kind)
    {
        await context.Session.ApplicationContext.Initialization;
        var cue = new SubtitleLine { Start = MediaTime.Zero, End = new(5), Text = "Timing 中文 123" };
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/clip-timing.png");
        var layer = new ProjectLayer
        {
            Id = kind == LayerKind.SUBTITLE ? cue.Id : Guid.NewGuid(), Kind = kind,
            Start = MediaTime.Zero, End = new(5), SubtitleId = kind == LayerKind.SUBTITLE ? cue.Id : null,
            Shape = kind == LayerKind.SHAPE ? new(ShapeKind.RECTANGLE, 100, 80) : null,
            Image = kind == LayerKind.IMAGE ? new(asset.Id, 100, 80) { Playback = new() { Speed = 2, SourceOffset = new(3) } } : null,
            Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0.25), new(new(5), 0.75)])]
        };
        var document = new ProjectDocument { Subtitles = kind == LayerKind.SUBTITLE ? [cue] : [],
            Assets = kind == LayerKind.IMAGE ? [asset] : [], Layers = [layer] };
        context.Session.Editor.Reset(document);
        context.Session.SelectLayer(layer.Id, [layer.Id]);
        context.Window.Layouts.Activate(WorkbenchPanelIds.TIMELINE);
        Flush(context.Window);
        return document;
    }

    private static SubtitleTimelineControl PrepareTimeline(MainWindowTestContext context)
    {
        context.ViewModel.Timeline.IsSnapEnabled = false;
        context.ViewModel.Timeline.IsStepEnabled = false;
        context.ViewModel.Timeline.Viewport = new(0, 64);
        Flush(context.Window);
        return UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
    }

    private static TextBox InstanceInput(MainWindowTestContext context, string name)
    {
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, name);
        input.BringIntoView();
        Flush(context.Window);
        return Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
    }

    private static void Enter(Window window, TextBox input, string text)
    {
        Assert.True(input.Focus());
        UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
        window.KeyTextInput(text);
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
