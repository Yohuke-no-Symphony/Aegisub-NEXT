using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineMarkerHoverUiTests
{
    [AvaloniaFact]
    public void SeparatedVectorPointsUseIdenticalTimeCoordinateAndDraggingYMovesOnlyTime()
    {
        var cue = new SubtitleLine { Start = new(1), End = new(5), Text = "Separated" };
        var value = AnimationValue.FromVector(new(1, 2));
        var layer = Layer(cue) with { Tracks = [new(AnimationProperty.SCALE, [new(new(1), value)])] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, null, null);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [new(layer.Id, layer.Tracks[0].Target)] };
        TimelineKeyframeEventArgs? changed = null;
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = true;
        timeline.KeyframeMoved += (_, e) => changed = e;
        var window = new Window { Width = 650, Height = 240, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            var x = timeline.GetKeyframePoint(layer.Id, AnimationProperty.SCALE, new(1), value, 0)!.Value;
            var y = timeline.GetKeyframePoint(layer.Id, AnimationProperty.SCALE, new(1), value, 1)!.Value;
            Assert.Equal(x.X, y.X);
            Assert.NotEqual(x.Y, y.Y);
            Assert.Equal(2, timeline.KeyframeMarkers.Count);
            window.MouseDown(y, MouseButton.Left);
            window.MouseMove(y + new Vector(80, -5));
            window.MouseUp(y + new Vector(80, -5), MouseButton.Left);
            Assert.NotNull(changed);
            Assert.Equal(TimelineComponentMask.SECOND, changed.Components);
            Assert.Equal(new MediaTime(2), changed.NewTime);
            Assert.Equal(value, changed.NewValue);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CombinedMarkerHasYellowPixelsAndWhiteOutlineWithoutPermanentNumbers()
    {
        var cue = new SubtitleLine { End = new(4), Text = "Yellow marker" };
        var value = AnimationValue.FromVector(new(1, 1));
        var layer = Layer(cue) with { Tracks = [new(AnimationProperty.SCALE, [new(new(1), value)])] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80 };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, null, null);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [new(layer.Id, layer.Tracks[0].Target)] };
        var window = new Window { Width = 650, Height = 240, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            Prepare(window);
            var marker = Assert.Single(timeline.KeyframeMarkers);
            Assert.Equal(TimelineComponentMask.FIRST | TimelineComponentMask.SECOND, marker.Components);
            Assert.Null(timeline.HoveredKeyframe);
            using var bitmap = Capture(window);
            var yellow = 0;
            var white = 0;
            var origin = timeline.TranslatePoint(marker.Position, window)!.Value;
            for (var y = -7; y <= 7; y++)
            {
                for (var x = -7; x <= 7; x++)
                {
                    var pixel = bitmap.GetPixel((int)(origin.X + x), (int)(origin.Y + y));
                    yellow += pixel.Red > 180 && pixel.Green > 120 && pixel.Blue < 100 ? 1 : 0;
                    white += pixel.Red > 220 && pixel.Green > 220 && pixel.Blue > 220 ? 1 : 0;
                }
            }
            Assert.True(yellow > 10);
            Assert.True(white > 5);
            window.MouseMove(marker.Position);
            Assert.NotNull(timeline.HoveredKeyframe);
            Assert.NotNull(timeline.GetKeyframeLabelRectangle(layer.Id, AnimationProperty.SCALE, new(1), 1));
            timeline.CancelGesture();
            Assert.Null(timeline.HoveredKeyframe);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FullColorTrackKeepsRgbTogetherAndAlphaInItsOwnNormalizedArea()
    {
        var cue = new SubtitleLine { End = new(4), Text = "HDR color" };
        var value = AnimationValue.FromColor(new(4, 4, 4, 0.5));
        var layer = Layer(cue) with { Tracks = [new(AnimationProperty.FILL, [new(new(1), value)])] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, null, null);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [new(layer.Id, layer.Tracks[0].Target)] };
        TimelineKeyframeEventArgs? changed = null;
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = true;
        timeline.KeyframeMoved += (_, e) => changed = e;
        var window = new Window { Width = 650, Height = 300, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            Assert.Equal(new[] { AnimationProperty.FILL }, timeline.GetAnimationProperties(layer.Id));
            var rgb = Assert.Single(timeline.KeyframeMarkers, marker => marker.Components ==
                (TimelineComponentMask.FIRST | TimelineComponentMask.SECOND | TimelineComponentMask.THIRD));
            var alpha = Assert.Single(timeline.KeyframeMarkers, marker => marker.Components == TimelineComponentMask.FOURTH);
            Assert.Equal(rgb.Position.X, alpha.Position.X);
            Assert.True(alpha.Curve.Top > rgb.Curve.Bottom);
            Assert.Equal(0, alpha.Minimum);
            Assert.Equal(1, alpha.Maximum);
            window.MouseDown(alpha.Position, MouseButton.Left);
            window.MouseMove(alpha.Position + new Vector(80, -200));
            window.MouseUp(alpha.Position + new Vector(80, -200), MouseButton.Left);
            Assert.NotNull(changed);
            Assert.Equal(TimelineComponentMask.FOURTH, changed.Components);
            Assert.Equal(new MediaTime(2), changed.NewTime);
            Assert.Equal(value, changed.NewValue);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HoverClearsOnZoomAndSnapshotReplacementWhileOtherClipCurvesStayVisible()
    {
        var first = new SubtitleLine { End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(5), Text = "Second" };
        var firstLayer = Layer(first) with { Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])] };
        var secondLayer = Layer(second) with { Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])] };
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [firstLayer, secondLayer] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80 };
        timeline.SetDocument(document, null, null);
        var window = new Window { Width = 650, Height = 240, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            window.MouseMove(timeline.GetKeyframePoint(firstLayer.Id, AnimationProperty.OPACITY, new(1), 0.5)!.Value);
            Assert.NotNull(timeline.HoveredKeyframe);
            timeline.SetViewport(timeline.Viewport.ZoomAt(1.5, 80, 10, timeline.ContentHeight), 10);
            Assert.Null(timeline.HoveredKeyframe);
            Assert.Single(timeline.GetAnimationProperties(secondLayer.Id));
            timeline.SetDocument(document with { Layers = [firstLayer with { Tracks = [] }, secondLayer] }, null, null);
            Assert.Null(timeline.HoveredKeyframe);
            Assert.Empty(timeline.GetAnimationProperties(firstLayer.Id));
            Assert.Single(timeline.GetAnimationProperties(secondLayer.Id));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AdjacentClipEndpointsStayDistinctAndHitPrefersTheSelectedClip()
    {
        var first = new SubtitleLine { End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(2), End = new(4), Text = "Second" };
        var firstLayer = Layer(first) with { Tracks = [new(AnimationProperty.SCALE, [new(new(2), new ScenePoint(1, 1))])] };
        var secondLayer = Layer(second) with { Tracks = [new(AnimationProperty.SCALE, [new(MediaTime.Zero, new ScenePoint(1, 1))])] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80 };
        timeline.SetDocument(new() { Subtitles = [first, second], Layers = [firstLayer, secondLayer] }, second.Id, secondLayer);
        Guid? selected = null;
        timeline.KeyframeSelected += (_, e) => selected = e.LayerId;
        var window = new Window { Width = 650, Height = 240, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            Assert.Equal(2, timeline.KeyframeMarkers.Count);
            var marker = Assert.Single(timeline.KeyframeMarkers, item => item.Identity.LayerId == secondLayer.Id);
            Assert.All(timeline.KeyframeMarkers, item => Assert.Equal(marker.Position, item.Position));
            window.MouseDown(marker.Position, MouseButton.Left);
            window.MouseUp(marker.Position, MouseButton.Left);
            Assert.Equal(secondLayer.Id, selected);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NeighboringTimesDoNotMergeAndEmptyTracksDoNotHideOtherClipCurves()
    {
        var first = new SubtitleLine { End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(5), Text = "Second" };
        var firstLayer = Layer(first) with
        {
            Tracks = [new(AnimationProperty.SCALE,
                [new(new(1), new ScenePoint(1, 1)), new(new(1001, 1000), new ScenePoint(1, 1))])]
        };
        var secondLayer = Layer(second) with { Tracks = [new(AnimationProperty.SCALE, [])] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80 };
        timeline.SetDocument(new() { Subtitles = [first, second], Layers = [firstLayer, secondLayer] }, null, null);
        var window = new Window { Width = 650, Height = 240, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            Assert.Equal(2, timeline.KeyframeMarkers.Count);
            Assert.NotEqual(timeline.KeyframeMarkers[0].Identity.Time, timeline.KeyframeMarkers[1].Identity.Time);
            Assert.All(timeline.KeyframeMarkers, marker =>
            {
                Assert.Equal(firstLayer.Id, marker.Identity.LayerId);
                Assert.Equal(TimelineComponentMask.FIRST | TimelineComponentMask.SECOND, marker.Components);
            });
            Assert.Equal(new[] { AnimationProperty.SCALE }, timeline.GetAnimationProperties(firstLayer.Id));
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static void Prepare(Window window)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
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
