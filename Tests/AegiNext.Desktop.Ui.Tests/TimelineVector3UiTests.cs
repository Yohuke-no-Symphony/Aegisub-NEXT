using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineVector3UiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExpandedVectorRendersAllAxesAndMovesOneSharedKeyframe(bool dark)
    {
        var value = new SceneVector3(-45, 20, 80);
        var frame = new Keyframe(new(1), value)
        {
            ComponentCurves = [new(KeyframeInterpolation.EASE_IN), new(KeyframeInterpolation.POWER) { Exponent = 2.5 }]
        };
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION_3D);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), End = new(8),
            Tracks = [new(target, [frame, new(new(3), new SceneVector3(45, 50, 90))])]
        };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(new() { Layers = [layer] }, null, layer);
        timeline.TimelineViewState = new() { ExpandedAnimationClips = [new(layer.Id, target)] };
        timeline.KeyframeSelected += (_, selected) => selected.SelectionAccepted = true;
        TimelineKeyframeEventArgs? edit = null;
        timeline.KeyframeMoved += (_, moved) => edit = moved;
        var window = new Window
        {
            Width = 850, Height = 400, Content = timeline,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            using var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            Assert.Equal(new[] { AnimationProperty.ROTATION_3D }, timeline.GetAnimationProperties(layer.Id));
            var markers = timeline.KeyframeMarkers.Where(marker => marker.Identity.Time == frame.Time).ToArray();
            Assert.Equal(3, markers.Length);
            Assert.Single(markers.Select(marker => marker.Identity).Distinct());
            Assert.Contains(markers, marker => marker.Components == TimelineComponentMask.THIRD);
            var point = timeline.GetKeyframePoint(layer.Id, target, frame.Time, value, 2)!.Value;
            window.MouseMove(point);
            Assert.Equal(TimelineComponentMask.THIRD, timeline.HoveredKeyframe!.Components);
            window.MouseDown(point, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(point + new Vector(80, 0));
            window.MouseUp(point + new Vector(80, 0), MouseButton.Left);

            Assert.NotNull(edit);
            Assert.Equal(target, edit.Target);
            Assert.Equal(new MediaTime(2), edit.NewTime);
            Assert.Equal(AnimationValue.FromVector3(value), edit.NewValue);
            Assert.Equal(TimelineComponentMask.THIRD, edit.Components);
            Assert.False(timeline.HasActiveDrag);
            Assert.Equal(new MediaTime(1), frame.Time);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrderedVectorMarkersOnlyExposeTheAffectedAxis(bool collapsed)
    {
        var operation = new AnimationTransformOperation(Guid.NewGuid(), new(1), new(3), new SceneVector3(-45, 80, 60))
        {
            ComponentMask = 2
        };
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION_3D);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), End = new(8),
            Tracks = [new(target, []) { InitialValue = new SceneVector3(-45, 20, 60), Transforms = [operation] }]
        };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(new() { Layers = [layer] }, null, layer);
        if (!collapsed)
        {
            timeline.TimelineViewState = new() { ExpandedAnimationClips = [new(layer.Id, target)] };
        }
        var window = new Window { Width = 850, Height = 400, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            using var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            var markers = timeline.KeyframeMarkers.ToArray();
            Assert.Equal(2, markers.Length);
            Assert.All(markers, marker =>
            {
                Assert.Equal(operation.Id, marker.Identity.OperationId);
                Assert.Equal(TimelineComponentMask.SECOND, marker.Components);
            });
            window.MouseMove(markers[1].Position);
            Assert.Equal(TimelineComponentMask.SECOND, timeline.HoveredKeyframe!.Components);
        }
        finally
        {
            window.Close();
        }
    }
}
