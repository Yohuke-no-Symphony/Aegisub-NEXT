using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
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

public sealed class TimelineTrackReorderUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HeaderDragMovesWholeMixedTrackAfterReleaseAndCreatesOneUndo(bool upward, bool collapsed)
    {
        await using var context = new MainWindowTestContext();
        var original = CreateDocument();
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        var animatedTrack = original.Tracks[0].Id;
        var animation = new TimelineEffectClipId(original.Layers.First(layer => layer.TrackId == animatedTrack && layer.Tracks.Any(track => track.Property == AnimationProperty.OPACITY)).Id, new(AnimationProperty.OPACITY));
        if (collapsed)
        {
            context.Session.SetTimelineTrackCollapsed(animatedTrack, true);
            Flush(context.Window);
        }
        var source = original.Tracks[upward ? 2 : 0].Id;
        var target = original.Tracks[upward ? 0 : 2].Id;
        var origin = HeaderPoint(context, timeline, source);
        var rowBefore = timeline.GetEffectClipRectangle(animation);
        var offsetBefore = timeline.Viewport.VerticalOffset;
        var targetHeader = timeline.GetTrackHeaderRectangle(target)!.Value;
        var destination = WindowPoint(context, timeline, new(60, upward ? targetHeader.Top + 1 : targetHeader.Bottom - 1));
        Assert.NotEqual(timeline.GetTrackHeaderRectangle(original.Tracks[0].Id)!.Value.Height,
            timeline.GetTrackHeaderRectangle(original.Tracks[1].Id)!.Value.Height);

        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        Flush(context.Window);

        Assert.True(timeline.HasActiveDrag);
        Assert.NotNull(timeline.TrackInsertionY);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        if (rowBefore is { } before)
        {
            var during = timeline.GetEffectClipRectangle(animation)!.Value;
            Assert.Equal(before.Height, during.Height);
            Assert.Equal(before.Top + offsetBefore, during.Top + timeline.Viewport.VerticalOffset, 6);
        }
        else
        {
            Assert.Null(timeline.GetEffectClipRectangle(animation));
        }

        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);

        var reordered = context.Session.DocumentSnapshot;
        Assert.Equal(upward ? new[] { original.Tracks[2].Id, original.Tracks[0].Id, original.Tracks[1].Id }
            : new[] { original.Tracks[1].Id, original.Tracks[2].Id, original.Tracks[0].Id }, reordered.Tracks.Select(track => track.Id));
        Assert.Equal(original.Layers, reordered.Layers);
        Assert.Equal(original.Subtitles, reordered.Subtitles);
        Assert.False(timeline.HasActiveDrag);
        Assert.Null(timeline.TrackInsertionY);
        Assert.Equal(collapsed, timeline.IsTrackCollapsed(animatedTrack));
        Assert.All(original.Layers, clip => Assert.NotNull(timeline.GetClipRectangle(clip.Id)));
        if (!collapsed)
        {
            var header = timeline.GetTrackHeaderRectangle(animatedTrack)!.Value;
            var animatedRow = timeline.GetEffectClipRectangle(animation)!.Value;
            Assert.InRange(animatedRow.Top, header.Top, header.Bottom);
            Assert.InRange(animatedRow.Bottom, header.Top, header.Bottom);
        }
        Assert.True(context.Session.Editor.Undo());
        Flush(context.Window);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Flush(context.Window);
        Assert.Same(reordered, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData("click")]
    [InlineData("threshold")]
    [InlineData("return")]
    [InlineData("escape")]
    [InlineData("capture")]
    [InlineData("replacement")]
    [InlineData("detach")]
    [InlineData("undo")]
    [InlineData("selection")]
    public async Task NoOpAndCancelledHeaderGesturesNeverCreateAnEdit(string action)
    {
        await using var context = new MainWindowTestContext();
        var original = CreateDocument();
        context.Session.Editor.Reset(original);
        var beforeHistory = original;
        if (action == "undo")
        {
            context.Session.Editor.Apply("Prior content", document => document with { Name = "Before undo" });
            original = context.Session.DocumentSnapshot;
        }
        var timeline = Prepare(context);
        var origin = HeaderPoint(context, timeline, original.Tracks[0].Id);
        var target = HeaderPoint(context, timeline, original.Tracks[2].Id, ensureVisible: false);
        var destination = action is "click" or "threshold" ? origin + new Vector(0, action == "threshold" ? 2 : 0) : target;
        IPointer? pointer = null;
        timeline.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            RoutingStrategies.Bubble, handledEventsToo: true);
        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        var expected = original;
        switch (action)
        {
            case "return":
                context.Window.MouseMove(origin);
                destination = origin;
                break;
            case "escape":
                UiTestActions.Press(context.Window, Key.Escape);
                break;
            case "capture":
                Assert.NotNull(pointer);
                pointer.Capture(null);
                break;
            case "replacement":
                expected = original with { Name = "Replacement" };
                context.Session.Editor.Reset(expected);
                break;
            case "undo":
                Assert.True(context.Session.Editor.Undo());
                expected = beforeHistory;
                break;
            case "selection":
                Assert.True(context.Session.SelectTrack(original.Tracks[1].Id));
                break;
            case "detach":
                var panel = Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>());
                var content = panel.Content;
                panel.Content = null;
                Flush(context.Window);
                Assert.False(timeline.HasActiveDrag);
                panel.Content = content;
                Flush(context.Window);
                break;
        }
        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);

        Assert.Same(expected, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(timeline.HasActiveDrag);
        Assert.Null(timeline.TrackInsertionY);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CollapseAndSoloButtonsHandleTheirEntireBoundsWithoutStartingReorder(bool solo)
    {
        await using var context = new MainWindowTestContext();
        var original = CreateDocument();
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        var id = original.Tracks[0].Id;
        var bounds = solo ? timeline.GetTrackSoloToggleRectangle(id)!.Value : timeline.GetTrackExpanderRectangle(id)!.Value;
        var origin = WindowPoint(context, timeline, bounds.TopLeft + new Vector(0.1, 0.1));
        var destination = HeaderPoint(context, timeline, original.Tracks[2].Id, ensureVisible: false);

        context.Window.MouseDown(origin, MouseButton.Left);
        Assert.False(timeline.HasActiveDrag);
        context.Window.MouseMove(destination);
        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);

        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Null(timeline.TrackInsertionY);
        if (solo)
        {
            Assert.Equal(id, timeline.SoloTrackId);
        }
        else
        {
            Assert.True(timeline.IsTrackCollapsed(id));
        }
    }

    [AvaloniaFact]
    public async Task InvalidDraftRejectsHeaderSelectionAndCannotStartOrCommitReorder()
    {
        await using var context = new MainWindowTestContext();
        var original = CreateDocument();
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(original.Subtitles[0].Id);
        var timeline = Prepare(context);
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "FontSizeInput");
        input.BringIntoView();
        Flush(context.Window);
        var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(textBox.Focus());
        input.RawText = "7e-";
        Dispatcher.UIThread.RunJobs();
        var origin = HeaderPoint(context, timeline, original.Tracks[2].Id);
        var destination = HeaderPoint(context, timeline, original.Tracks[0].Id, ensureVisible: false);
        try
        {
            context.Window.MouseDown(origin, MouseButton.Left);
            context.Window.MouseMove(destination);
            context.Window.MouseUp(destination, MouseButton.Left);
            Flush(context.Window);

            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.False(timeline.HasActiveDrag);
            Assert.Equal("7e-", input.RawText);
        }
        finally
        {
            input.RawText = original.Subtitles[0].Style.FontSize.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    [AvaloniaFact]
    public async Task ValidDraftCommitsBeforeTheHeaderGestureFreezesItsSnapshot()
    {
        await using var context = new MainWindowTestContext();
        var original = CreateDocument();
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(original.Subtitles[0].Id);
        var timeline = Prepare(context);
        var input = UiTestActions.Find<NumericDraftInput>(context.Window, "FontSizeInput");
        input.BringIntoView();
        Flush(context.Window);
        Assert.True(Assert.Single(input.GetVisualDescendants().OfType<TextBox>()).Focus());
        input.RawText = "40";
        Dispatcher.UIThread.RunJobs();
        var origin = HeaderPoint(context, timeline, original.Tracks[2].Id);
        var destination = HeaderPoint(context, timeline, original.Tracks[0].Id, ensureVisible: false);

        context.Window.MouseDown(origin, MouseButton.Left);
        var afterDraft = context.Session.DocumentSnapshot;
        Assert.NotSame(original, afterDraft);
        Assert.Equal(40, afterDraft.Subtitles[0].Style.FontSize);
        context.Window.MouseMove(destination);
        Assert.Same(afterDraft, context.Session.DocumentSnapshot);
        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);

        Assert.Equal(new[] { original.Tracks[2].Id, original.Tracks[0].Id, original.Tracks[1].Id },
            context.Session.DocumentSnapshot.Tracks.Select(track => track.Id));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(afterDraft, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void MixedClipCrossTrackDragChecksShapeCollisionsAndCarriesAnimationRows()
    {
        var target = new ProjectTrack { Name = "Target" };
        var cue = new SubtitleLine { Start = new(1), End = new(3), Text = "Text" };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 30, 20), Start = new(4), End = new(6),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])]
        };
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "images/fixture.png");
        var image = new ProjectLayer { Kind = LayerKind.IMAGE, Image = new(asset.Id, 40, 20), Start = new(7), End = new(9) };
        var obstacle = shape with { Id = Guid.NewGuid(), TrackId = target.Id, Tracks = [] };
        var original = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, target], Assets = [asset], Subtitles = [cue],
            Layers = [new() { Id = cue.Id, SubtitleId = cue.Id, Start = cue.Start, End = cue.End }, shape, image, obstacle]
        };
        var editor = new ProjectEditor(original);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 60, IsSnapEnabled = false };
        timeline.SetDocument(original, null, null);
        timeline.ClipSelectionChanged += (_, e) =>
        {
            var clip = editor.Snapshot.Layers.Single(layer => layer.Id == e.Id);
            timeline.SetDocument(editor.Snapshot, clip.SubtitleId, clip, e.SelectedIds);
        };
        var commits = 0;
        timeline.TimingChanged += (_, e) =>
        {
            commits++;
            editor.MoveClip(e.Id, e.TrackId!.Value, e.Start, e.End, e.Mode, e.IsMove);
            timeline.SetDocument(editor.Snapshot, null, editor.Snapshot.Layers.Single(layer => layer.Id == shape.Id));
        };
        var window = new Window { Width = 900, Height = 420, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            Assert.All(original.Layers, clip => Assert.NotNull(timeline.GetClipRectangle(clip.Id)));
            var origin = timeline.GetClipRectangle(shape.Id)!.Value.Center;
            var targetY = timeline.GetTrackHeaderRectangle(target.Id)!.Value.Bottom - 14;
            var rejected = new Point(origin.X, targetY);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(rejected);
            Assert.Same(original, editor.Snapshot);
            window.MouseUp(rejected, MouseButton.Left);
            Assert.Equal(0, commits);
            Assert.False(editor.CanUndo);

            origin = timeline.GetClipRectangle(shape.Id)!.Value.Center;
            var destination = new Point(origin.X + 3 * timeline.PixelsPerSecond, targetY);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Same(original, editor.Snapshot);
            var animation = new TimelineEffectClipId(shape.Id, new(AnimationProperty.OPACITY));
            Assert.NotNull(timeline.GetEffectClipRectangle(animation));
            window.MouseUp(destination, MouseButton.Left);
            Flush(window);

            var moved = editor.Snapshot.Layers.Single(clip => clip.Id == shape.Id);
            Assert.Equal(target.Id, moved.TrackId);
            Assert.Equal(new AegiNext.Core.Timing.MediaTime(7), moved.Start);
            Assert.Equal(shape.Tracks, moved.Tracks);
            Assert.NotNull(timeline.GetEffectClipRectangle(animation));
            Assert.Same(image, editor.Snapshot.Layers.Single(clip => clip.Id == image.Id));
            Assert.Equal(1, commits);
            Assert.True(editor.Undo());
            Assert.Same(original, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldHeaderAtViewportEdgeScrollsAcrossHiddenTracksAndCommitsOnce(bool upward)
    {
        var tracks = Enumerable.Range(0, 24).Select(index => new ProjectTrack { Name = $"Track {index}" }).ToArray();
        var original = new ProjectDocument { Tracks = [.. tracks] };
        var editor = new ProjectEditor(original);
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(original, null, null);
        var commits = 0;
        timeline.TrackReorderCompleted += (_, e) =>
        {
            commits++;
            Assert.Same(editor.Snapshot, e.ExpectedDocument);
            editor.MoveTrack(e.TrackId, e.Index);
            timeline.SetDocument(editor.Snapshot, null, null, trackId: e.TrackId);
        };
        var window = new Window { Width = 800, Height = 180, Content = timeline };
        window.Show();
        try
        {
            Flush(window);
            var maximum = timeline.ContentHeight - timeline.Viewport.Height;
            if (upward)
            {
                timeline.SetViewport(timeline.Viewport with { VerticalOffset = maximum }, 10);
            }
            var id = tracks[upward ? ^1 : 0].Id;
            var header = timeline.GetTrackHeaderRectangle(id)!.Value;
            var origin = new Point(60, header.Top + 14);
            Assert.Same(timeline, window.InputHitTest(origin));
            var destination = new Point(60, upward ? timeline.RulerHeight - 5 : timeline.Bounds.Height + 5);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            var boundary = upward ? 0 : maximum;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (timeline.Viewport.VerticalOffset != boundary)
            {
                Assert.True(DateTime.UtcNow < deadline, $"Edge scrolling stopped at {timeline.Viewport.VerticalOffset}, expected {boundary}.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
                Flush(window);
            }

            Assert.Same(original, editor.Snapshot);
            Assert.NotNull(timeline.TrackInsertionY);
            window.MouseUp(destination, MouseButton.Left);
            Flush(window);

            Assert.Equal(id, editor.Snapshot.Tracks[upward ? 0 : ^1].Id);
            Assert.Equal(1, commits);
            Assert.True(editor.Undo());
            Assert.Same(original, editor.Snapshot);
            Assert.False(editor.CanUndo);
            var offsetAfterRelease = timeline.Viewport.VerticalOffset;
            await Task.Delay(80, TestContext.Current.CancellationToken);
            Flush(window);
            Assert.Equal(offsetAfterRelease, timeline.Viewport.VerticalOffset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SoloProjectionCannotStartHeaderReorderAcrossHiddenTracks()
    {
        await using var context = new MainWindowTestContext();
        var original = CreateDocument();
        context.Session.Editor.Reset(original);
        var timeline = Prepare(context);
        var solo = original.Tracks[1].Id;
        context.ViewModel.Timeline.ToggleTrackSolo(solo);
        Flush(context.Window);
        var origin = HeaderPoint(context, timeline, solo);

        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(origin + new Vector(0, 70));
        context.Window.MouseUp(origin + new Vector(0, 70), MouseButton.Left);
        Flush(context.Window);

        Assert.False(timeline.HasActiveDrag);
        Assert.Null(timeline.TrackInsertionY);
        Assert.Equal(solo, timeline.SoloTrackId);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static ProjectDocument CreateDocument()
    {
        var first = ProjectTrack.Default with
        {
            Name = "Mixed", StylePresetName = "Default", StylePresetId = Guid.NewGuid(), DefaultStyle = new()
        };
        var second = new ProjectTrack { Name = "Second" };
        var third = new ProjectTrack { Name = "Third" };
        var cue = new SubtitleLine { Start = new(1), End = new(3), Text = "字幕 ABC" };
        return new()
        {
            Tracks = [first, second, third], Subtitles = [cue],
            Layers =
            [
                new()
                {
                    Id = cue.Id, TrackId = first.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
                    Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])]
                },
                new()
                {
                    TrackId = first.Id, Name = "Shape", Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
                    Start = new(4), End = new(6), Tracks = [new(AnimationProperty.ROTATION, [new(new(1), 45)])]
                }
            ]
        };
    }

    private static SubtitleTimelineControl Prepare(MainWindowTestContext context)
    {
        context.Window.Height = 1040;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Flush(context.Window);
        return timeline;
    }

    private static Point HeaderPoint(MainWindowTestContext context, SubtitleTimelineControl timeline, Guid trackId, bool ensureVisible = true)
    {
        var rectangle = timeline.GetTrackHeaderRectangle(trackId)!.Value;
        if (ensureVisible && (rectangle.Top + 14 < timeline.RulerHeight || rectangle.Top + 14 >= timeline.Bounds.Height))
        {
            context.ViewModel.Timeline.Viewport = context.ViewModel.Timeline.Viewport with
            {
                VerticalOffset = Math.Max(0, timeline.Viewport.VerticalOffset + rectangle.Top - timeline.RulerHeight)
            };
            Flush(context.Window);
            rectangle = timeline.GetTrackHeaderRectangle(trackId)!.Value;
        }
        var point = WindowPoint(context, timeline, new(60, rectangle.Top + 14));
        if (ensureVisible)
        {
            Assert.Same(timeline, context.Window.InputHitTest(point));
        }
        return point;
    }

    private static Point WindowPoint(MainWindowTestContext context, SubtitleTimelineControl timeline, Point point) =>
        timeline.TranslatePoint(point, context.Window)!.Value;

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }
}
