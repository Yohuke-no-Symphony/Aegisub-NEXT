using AegiNext.Application;
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

public sealed class TimelineExpandedClipsUiTests
{
    [AvaloniaFact]
    public async Task LeftToolbarButtonsChangeTheSharedControlModesAndSurvivePanelReactivation()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.True(timeline.IsSnapEnabled);
        Assert.False(timeline.IsStepEnabled);
        UiTestActions.Click(context.Window, "TimelineSnapButton");
        UiTestActions.Click(context.Window, "TimelineStepButton");
        Assert.False(context.ViewModel.Timeline.IsSnapEnabled);
        Assert.True(context.ViewModel.Timeline.IsStepEnabled);
        Assert.False(timeline.IsSnapEnabled);
        Assert.True(timeline.IsStepEnabled);
        context.Window.Layouts.Activate("effects");
        context.Window.Layouts.Activate("timeline");
        Assert.Same(timeline, UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline"));
        Assert.False(timeline.IsSnapEnabled);
        Assert.True(timeline.IsStepEnabled);
    }

    [AvaloniaFact]
    public void EveryClipKeepsItsAnimationRowsVisibleAndAnUnselectedClipKeyCanBeDragged()
    {
        var firstCue = new SubtitleLine { Start = new(1), End = new(4), Text = "First" };
        var secondCue = new SubtitleLine { Start = new(5), End = new(9), Text = "Second" };
        var first = Layer(firstCue) with
        {
            Tracks = [new(AnimationProperty.POSITION, [new(new(1), new ScenePoint(10, 20))]),
                new(AnimationProperty.OPACITY, [new(new(1), 0.25)])]
        };
        var second = Layer(secondCue) with
        {
            Tracks = [new(AnimationProperty.ROTATION, [new(new(1), 40)]),
                new(AnimationProperty.OPACITY, [new(new(1), 0.25)])]
        };
        var editor = new ProjectEditor(new() { Subtitles = [firstCue, secondCue], Layers = [first, second] });
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(editor.Snapshot, firstCue.Id, first);
        var selectedId = Guid.Empty;
        var commits = 0;
        timeline.KeyframeSelected += (_, e) =>
        {
            selectedId = e.LayerId;
            timeline.EffectProperty = e.Property;
            var selected = editor.Snapshot.Layers.Single(layer => layer.Id == e.LayerId);
            timeline.SetDocument(editor.Snapshot, selected.SubtitleId, selected);
            e.SelectionAccepted = true;
        };
        timeline.KeyframeMoved += (_, e) =>
        {
            commits++;
            editor.UpdateLayer(e.LayerId, layer => layer with
            {
                Tracks = [.. layer.Tracks.Select(track => track.Property == e.Property
                    ? track with { Keyframes = [new(e.NewTime, e.NewValue!.Value)] } : track)]
            });
            var selected = editor.Snapshot.Layers.Single(layer => layer.Id == e.LayerId);
            timeline.SetDocument(editor.Snapshot, selected.SubtitleId, selected);
        };
        var window = new Window { Width = 900, Height = 360, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            Assert.Equal(new[] { AnimationProperty.POSITION, AnimationProperty.OPACITY }, timeline.GetAnimationProperties(first.Id));
            Assert.Equal(new[] { AnimationProperty.ROTATION, AnimationProperty.OPACITY }, timeline.GetAnimationProperties(second.Id));
            var firstOpacity = timeline.GetKeyframePoint(first.Id, AnimationProperty.OPACITY, new(1), 0.25)!.Value;
            var secondOpacity = timeline.GetKeyframePoint(second.Id, AnimationProperty.OPACITY, new(1), 0.25)!.Value;
            Assert.Equal(firstOpacity.Y, secondOpacity.Y, 8);
            var point = timeline.GetKeyframePoint(second.Id, AnimationProperty.ROTATION, new(1), 40)!.Value;
            Assert.Same(timeline, window.InputHitTest(point));
            window.MouseDown(point, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            Assert.Equal(second.Id, selectedId);
            window.MouseMove(point + new Vector(80, 0));
            window.MouseUp(point + new Vector(80, 0), MouseButton.Left);
            Assert.Equal(1, commits);
            Assert.Equal(new MediaTime(2), Assert.Single(editor.Snapshot.Layers.Single(layer => layer.Id == second.Id)
                .Tracks.Single(track => track.Property == AnimationProperty.ROTATION).Keyframes).Time);
            Assert.Same(first, editor.Snapshot.Layers.Single(layer => layer.Id == first.Id));
            Assert.Equal(2, timeline.GetAnimationProperties(first.Id).Count);
            timeline.SetDocument(editor.Snapshot, null, null);
            Assert.Equal(2, timeline.GetAnimationProperties(first.Id).Count);
            Assert.Equal(2, timeline.GetAnimationProperties(second.Id).Count);
            Assert.True(editor.Undo());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.01)]
    public void CoincidentVectorComponentsUseOneMarkerAndDragOnlyTime(double originalY)
    {
        var cue = new SubtitleLine { Start = new(1), End = new(5), Text = "Vector" };
        var layer = Layer(cue) with
        {
            Tracks = [new(AnimationProperty.SCALE, [new(new(1), new ScenePoint(1, originalY))])]
        };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, null, null);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [new(layer.Id, layer.Tracks[0].Target)] };
        TimelineKeyframeEventArgs? moved = null;
        var selectedComponents = TimelineComponentMask.NONE;
        timeline.KeyframeSelected += (_, e) =>
        {
            selectedComponents = e.Components;
            e.SelectionAccepted = true;
        };
        timeline.KeyframeMoved += (_, e) => moved = e;
        var window = new Window { Width = 650, Height = 240, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            var x = timeline.GetKeyframePoint(layer.Id, AnimationProperty.SCALE, new(1), new ScenePoint(1, originalY), 0)!.Value;
            var y = timeline.GetKeyframePoint(layer.Id, AnimationProperty.SCALE, new(1), new ScenePoint(1, originalY), 1)!.Value;
            Assert.Equal(x, y);
            var marker = Assert.Single(timeline.KeyframeMarkers);
            Assert.Equal(TimelineComponentMask.FIRST | TimelineComponentMask.SECOND, marker.Components);
            window.MouseDown(y, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            Assert.Equal(TimelineComponentMask.FIRST | TimelineComponentMask.SECOND, selectedComponents);
            window.MouseMove(y + new Vector(80, -8));
            window.MouseUp(y + new Vector(80, -8), MouseButton.Left);
            Assert.NotNull(moved);
            Assert.Equal(new MediaTime(2), moved.NewTime);
            Assert.Equal(new ScenePoint(1, originalY), moved.NewValue!.Value.Vector);
            Assert.Equal(TimelineComponentMask.FIRST | TimelineComponentMask.SECOND, moved.Components);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EndKeyLabelRendersOutsideClipWithoutBeingCutOffAndStaysInsideTheViewport()
    {
        var cue = new SubtitleLine { End = new(2), Text = "Labels" };
        var layer = Layer(cue) with
        {
            Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, 0), new(new(2), 1)])]
        };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 100 };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, null, null);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [new(layer.Id, layer.Tracks[0].Target)] };
        var window = new Window { Width = 600, Height = 240, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            window.UpdateLayout();
            using var baseline = Capture(window);
            Assert.Null(timeline.GetKeyframeLabelRectangle(layer.Id, AnimationProperty.OPACITY, new(2)));
            var point = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, new(2), 1)!.Value;
            window.MouseMove(point);
            using var bitmap = Capture(window);
            var clip = timeline.GetClipRectangle(layer.Id)!.Value;
            var label = timeline.GetKeyframeLabelRectangle(layer.Id, AnimationProperty.OPACITY, new(2))!.Value;
            Assert.True(label.Left > clip.Right);
            Assert.True(label.Right <= timeline.Bounds.Width);
            Assert.True(label.Top >= timeline.RulerHeight);
            Assert.True(label.Bottom < clip.Top);
            var origin = timeline.TranslatePoint(label.TopLeft, window)!.Value;
            var hasGlyph = false;
            for (var y = 2; y < 14; y++)
            {
                for (var x = 2; x < label.Width - 2; x++)
                {
                    var pixel = bitmap.GetPixel((int)(origin.X + x), (int)(origin.Y + y));
                    hasGlyph |= pixel.Red > 120 && pixel.Green > 120 && pixel.Blue > 120;
                }
            }
            Assert.True(hasGlyph);
            window.MouseMove(new(timeline.HeaderWidth + 10, timeline.Bounds.Height - 10));
            using var withoutHover = Capture(window);
            Assert.Null(timeline.HoveredKeyframe);
            Assert.Null(timeline.GetKeyframeLabelRectangle(layer.Id, AnimationProperty.OPACITY, new(2)));
            var sample = label.TopLeft + new Vector(1, 1);
            Assert.Equal(baseline.GetPixel((int)sample.X, (int)sample.Y), withoutHover.GetPixel((int)sample.X, (int)sample.Y));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 2000, 427, 1000)]
    [InlineData(true, 2000, 43, 100)]
    public void StepUsesTheVisibleMinorDivisionForRealClipTrim(bool step, double pixelsPerSecond,
        long expectedNumerator, long expectedDenominator)
    {
        var cue = new SubtitleLine { Start = new(1, 10), End = new(2, 5), Text = "Step" };
        var layer = Layer(cue);
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = pixelsPerSecond, IsSnapEnabled = false, IsStepEnabled = step
        };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        TimelineTimingEventArgs? moved = null;
        timeline.TimingChanged += (_, e) => moved = e;
        var window = new Window { Width = 850, Height = 240, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            timeline.ViewStart = 0.1;
            var clip = timeline.GetClipRectangle(layer.Id)!.Value;
            var origin = new Point(clip.Right - 2, clip.Center.Y);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(origin + new Vector(54, 0));
            window.MouseUp(origin + new Vector(54, 0), MouseButton.Left);
            Assert.NotNull(moved);
            Assert.Equal(new MediaTime(expectedNumerator, expectedDenominator), moved.End);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 201, 100)]
    [InlineData(true, 2033, 1000)]
    public void ClipDragSnapsItsEndToAnotherClipStartAndAltTemporarilyBypassesSnap(bool bypass,
        long expectedNumerator, long expectedDenominator)
    {
        var cue = new SubtitleLine { Start = new(1), End = new(2), Text = "Moving" };
        var secondTrack = new ProjectTrack { Name = "Other" };
        var neighbor = new SubtitleLine { Start = new(301, 100), End = new(4), Text = "Boundary" };
        var layer = Layer(cue);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 100, IsSnapEnabled = true };
        timeline.SetDocument(new()
        {
            Tracks = [ProjectTrack.Default, secondTrack], Subtitles = [cue, neighbor], Layers = [layer, Layer(neighbor) with { TrackId = secondTrack.Id }]
        }, cue.Id, layer);
        TimelineTimingEventArgs? moved = null;
        timeline.TimingChanged += (_, e) => moved = e;
        var window = new Window { Width = 700, Height = 240, Content = timeline };
        window.Show();
        try
        {
            Prepare(window);
            var origin = timeline.GetClipRectangle(layer.Id)!.Value.Center;
            var modifier = bypass ? RawInputModifiers.Alt : RawInputModifiers.None;
            window.MouseDown(origin, MouseButton.Left, modifier);
            window.MouseMove(origin + new Vector(103.3, 0), modifier);
            window.MouseUp(origin + new Vector(103.3, 0), MouseButton.Left, modifier);
            Assert.NotNull(moved);
            Assert.Equal(new MediaTime(expectedNumerator, expectedDenominator), moved.Start);
            Assert.Equal(new MediaTime(1), moved.End - moved.Start);
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
