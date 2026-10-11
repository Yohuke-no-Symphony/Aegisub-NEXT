using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineMultiClipMoveUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrabbingAnySelectedClipPreviewsOneOffsetAndCommitsTheWholeSelectionOnce(bool crossTrack)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var otherTrack = new ProjectTrack { Name = "Other track" };
        var first = new SubtitleLine { Start = new(1), End = new(3), Text = "First" };
        var second = new SubtitleLine
        {
            Start = new(4), End = new(6), Text = "Second"
        };
        var document = new ProjectDocument
        {
            Tracks = crossTrack ? [ProjectTrack.Default, otherTrack] : [ProjectTrack.Default],
            Subtitles = [first, second], Layers = [Layer(first), Layer(second) with { TrackId = crossTrack ? otherTrack.Id : ProjectTrack.DEFAULT_TRACK_ID }]
        };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, ToggleModifier());
        Assert.Equal(new[] { first.Id, second.Id }.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        var firstRectangle = timeline.GetClipRectangle(first.Id)!.Value;
        var secondRectangle = timeline.GetClipRectangle(second.Id)!.Value;
        var origin = WindowPoint(context, timeline, firstRectangle.Center);
        var destination = origin + new Vector(timeline.PixelsPerSecond, 90);

        context.Window.MouseDown(origin, MouseButton.Left);
        Assert.True(timeline.HasActiveDrag);
        Assert.Equal(first.Id, context.Session.SelectedLayerId);
        Assert.Equal(2, context.ViewModel.Timeline.SelectedLayerIds.Count);
        context.Window.MouseMove(destination);
        Flush(context.Window);

        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal(firstRectangle.Left + timeline.PixelsPerSecond, timeline.GetClipRectangle(first.Id)!.Value.Left, 6);
        Assert.Equal(secondRectangle.Left + timeline.PixelsPerSecond, timeline.GetClipRectangle(second.Id)!.Value.Left, 6);
        Assert.Equal(firstRectangle.Top, timeline.GetClipRectangle(first.Id)!.Value.Top, 6);
        Assert.Equal(secondRectangle.Top, timeline.GetClipRectangle(second.Id)!.Value.Top, 6);

        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);
        Assert.False(timeline.HasActiveDrag);
        Assert.Equal(first with { Start = new(2), End = new(4) }, context.Session.DocumentSnapshot.Subtitles.Single(cue => cue.Id == first.Id));
        Assert.Equal(second with { Start = new(5), End = new(7) }, context.Session.DocumentSnapshot.Subtitles.Single(cue => cue.Id == second.Id));
        Assert.Equal(new[] { first.Id, second.Id }.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.Session.Editor.Redo());
        Assert.Equal(new MediaTime(2), context.Session.DocumentSnapshot.Subtitles.Single(cue => cue.Id == first.Id).Start);
    }

    [AvaloniaFact]
    public async Task AnyMemberCollisionTurnsEveryMovingClipRedAndReleaseRestoresTheWholeSelection()
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(4), End = new(5), Text = "Second" };
        var obstacle = new SubtitleLine { Start = new(6), End = new(7), Text = "Obstacle" };
        var document = new ProjectDocument
        {
            Subtitles = [first, second, obstacle], Layers = [Layer(first), Layer(second), Layer(obstacle)]
        };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, ToggleModifier());
        var originals = new[] { first.Id, second.Id }.ToDictionary(id => id, id => timeline.GetClipRectangle(id)!.Value);
        var origin = WindowPoint(context, timeline, originals[first.Id].Center);
        var destination = origin + new Vector(2 * timeline.PixelsPerSecond, 0);
        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        Flush(context.Window);

        Assert.Same(document, context.Session.DocumentSnapshot);
        using (var image = Capture(context.Window))
        {
            foreach (var id in originals.Keys)
            {
                var rectangle = timeline.GetClipRectangle(id)!.Value;
                var sample = WindowPoint(context, timeline, new(rectangle.Right - 10, rectangle.Bottom - 5));
                var pixel = image.GetPixel((int)sample.X, (int)sample.Y);
                Assert.True(pixel.Red > pixel.Green + 15 && pixel.Red > pixel.Blue + 10,
                    $"Moving clip {id} must expose its invalid red fill; actual pixel is {pixel}.");
            }
        }

        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.All(originals, pair => Assert.Equal(pair.Value, timeline.GetClipRectangle(pair.Key)));
        Assert.Equal(originals.Keys.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
    }

    [AvaloniaFact]
    public async Task GrabbingLaterMemberAtZeroBoundaryPreservesEveryMembersSpacing()
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "Earliest" };
        var second = new SubtitleLine { Start = new(4), End = new(5), Text = "Grabbed" };
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, ToggleModifier());
        var origin = WindowPoint(context, timeline, timeline.GetClipRectangle(second.Id)!.Value.Center);
        var destination = origin - new Vector(5 * timeline.PixelsPerSecond, 0);
        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);

        Assert.Equal(MediaTime.Zero, context.Session.DocumentSnapshot.Subtitles.Single(cue => cue.Id == first.Id).Start);
        Assert.Equal(new MediaTime(3), context.Session.DocumentSnapshot.Subtitles.Single(cue => cue.Id == second.Id).Start);
        Assert.All(context.Session.DocumentSnapshot.Subtitles, cue => Assert.Equal(new MediaTime(1), cue.End - cue.Start));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task MovingTheSelectionProjectsEveryMembersKeyframeWithoutChangingItsLocalContent()
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(3), Text = "First" };
        var second = new SubtitleLine { Start = new(4), End = new(6), Text = "Second" };
        var firstLayer = Layer(first) with { Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25)])] };
        var secondLayer = Layer(second) with { Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.75)])] };
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [firstLayer, secondLayer] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, ToggleModifier());
        var firstKey = timeline.GetKeyframePoint(first.Id, AnimationProperty.OPACITY, new(1), 0.25)!.Value;
        var secondKey = timeline.GetKeyframePoint(second.Id, AnimationProperty.OPACITY, new(1), 0.75)!.Value;
        var origin = WindowPoint(context, timeline, timeline.GetClipRectangle(first.Id)!.Value.Center);
        var destination = origin + new Vector(timeline.PixelsPerSecond, 0);
        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        Flush(context.Window);

        Assert.Equal(firstKey + new Vector(timeline.PixelsPerSecond, 0),
            timeline.GetKeyframePoint(first.Id, AnimationProperty.OPACITY, new(1), 0.25));
        Assert.Equal(secondKey + new Vector(timeline.PixelsPerSecond, 0),
            timeline.GetKeyframePoint(second.Id, AnimationProperty.OPACITY, new(1), 0.75));
        Assert.Same(document, context.Session.DocumentSnapshot);
        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);
        Assert.Equal(firstLayer.Tracks, context.Session.DocumentSnapshot.Layers.Single(layer => layer.Id == first.Id).Tracks);
        Assert.Equal(secondLayer.Tracks, context.Session.DocumentSnapshot.Layers.Single(layer => layer.Id == second.Id).Tracks);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task SameTrackDragCanCrossAnObstacleAndLandBeyondItWithoutBeingClamped()
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "Moving" };
        var obstacle = new SubtitleLine { Start = new(3), End = new(4), Text = "Obstacle" };
        var document = new ProjectDocument { Subtitles = [first, obstacle], Layers = [Layer(first), Layer(obstacle)] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        var origin = WindowPoint(context, timeline, timeline.GetClipRectangle(first.Id)!.Value.Center);
        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(origin + new Vector(2 * timeline.PixelsPerSecond, 0));
        Assert.Same(document, context.Session.DocumentSnapshot);
        var destination = origin + new Vector(4 * timeline.PixelsPerSecond, 0);
        context.Window.MouseMove(destination);
        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);

        Assert.Equal(new MediaTime(5), context.Session.DocumentSnapshot.Subtitles.Single(cue => cue.Id == first.Id).Start);
        Assert.Equal(obstacle, context.Session.DocumentSnapshot.Subtitles.Single(cue => cue.Id == obstacle.Id));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapeOrReturningToTheOriginCancelsWithoutAnUndo(bool escape)
    {
        await using var context = new MainWindowTestContext();
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Second" };
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        Select(context, timeline, first.Id);
        Select(context, timeline, second.Id, ToggleModifier());
        var origin = WindowPoint(context, timeline, timeline.GetClipRectangle(first.Id)!.Value.Center);
        var destination = origin + new Vector(timeline.PixelsPerSecond, 0);
        context.Window.MouseDown(origin, MouseButton.Left);
        context.Window.MouseMove(destination);
        if (escape)
        {
            UiTestActions.Press(context.Window, Key.Escape);
            Assert.False(timeline.HasActiveDrag);
        }
        else
        {
            context.Window.MouseMove(origin);
            destination = origin;
        }

        context.Window.MouseUp(destination, MouseButton.Left);
        Flush(context.Window);
        Assert.False(timeline.HasActiveDrag);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
    }

    [AvaloniaFact]
    public async Task EffectBlockBlankSelectsItsTargetAndOutsideBlankClearsClipAndKeyframeSelections()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var first = new SubtitleLine { Start = MediaTime.Zero, End = new(8), Text = "Animated" };
        var layer = Layer(first) with { Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25)])] };
        var document = new ProjectDocument { Subtitles = [first], Layers = [layer] };
        context.Session.Editor.Reset(document);
        var timeline = Prepare(context);
        var key = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, new(1), 0.25)!.Value;
        var keyPoint = WindowPoint(context, timeline, key);
        context.Window.MouseDown(keyPoint, MouseButton.Left);
        context.Window.MouseUp(keyPoint, MouseButton.Left);
        Assert.Equal(new MediaTime(1), context.Session.SelectedKeyTime);
        var position = timeline.Position;
        var blankPoint = WindowPoint(context, timeline, key + new Vector(4 * timeline.PixelsPerSecond, 0));
        Assert.Same(timeline, context.Window.InputHitTest(blankPoint));
        context.Window.MouseDown(blankPoint, MouseButton.Left);
        context.Window.MouseUp(blankPoint, MouseButton.Left);
        Flush(context.Window);

        Assert.Equal(layer.Id, context.Session.SelectedLayerId);
        Assert.Equal(new TimelineEffectClipId(layer.Id, new(AnimationProperty.OPACITY)),
            context.ViewModel.Timeline.SelectedEffectClipId);
        Assert.Null(context.Session.SelectedKeyTime);
        Assert.Equal(position, timeline.Position);
        Assert.False(timeline.HasActiveDrag);

        var outside = WindowPoint(context, timeline, new(timeline.GetClipRectangle(layer.Id)!.Value.Right + 40, key.Y));
        Assert.Same(timeline, context.Window.InputHitTest(outside));
        context.Window.MouseDown(outside, MouseButton.Left);
        context.Window.MouseUp(outside, MouseButton.Left);
        Flush(context.Window);

        Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, context.Session.CurrentTrackId);
        Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, context.ViewModel.Timeline.SelectedTrackId);
        Assert.Null(context.Session.SelectedCue);
        Assert.Null(context.Session.SelectedLayer);
        Assert.Null(context.Session.SelectedKeyTime);
        Assert.Empty(context.ViewModel.Timeline.SelectedLayerIds);
        Assert.Empty(context.ViewModel.Effects.SelectedIds);
        Assert.False(timeline.HasActiveDrag);
        Assert.Equal(position, timeline.Position);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
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
        var point = WindowPoint(context, timeline, timeline.GetClipRectangle(id)!.Value.Center);
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left, modifiers);
        context.Window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(context.Window);
        Assert.False(timeline.HasActiveDrag);
    }

    private static Point WindowPoint(MainWindowTestContext context, SubtitleTimelineControl timeline, Point local) =>
        timeline.TranslatePoint(local, context.Window)!.Value;

    private static RawInputModifiers ToggleModifier() => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static SKBitmap Capture(Window window)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
