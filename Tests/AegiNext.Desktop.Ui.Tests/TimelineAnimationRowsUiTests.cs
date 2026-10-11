using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineAnimationRowsUiTests
{
    [AvaloniaFact]
    public void AllExistingPropertiesHaveSeparateRowsAndDraggingOneKeepsOtherAnimationTracks()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), End = new(8),
            Tracks =
            [
                new(AnimationProperty.OPACITY, [new(new(1), 0.25)]),
                new(AnimationProperty.POSITION, [new(new(1), new ScenePoint(10, 20))]),
                new(AnimationProperty.ROTATION, [new(new(1), 40)])
            ]
        };
        var editor = new ProjectEditor(new() { Layers = [layer] });
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 60 };
        timeline.SetDocument(editor.Snapshot, null, layer);
        timeline.KeyframeSelected += (_, e) =>
        {
            timeline.EffectProperty = e.Property;
            e.SelectionAccepted = true;
        };
        timeline.KeyframeMoved += (_, e) =>
        {
            Assert.Equal(AnimationProperty.POSITION, e.Property);
            editor.UpdateLayer(e.LayerId, value => value with
            {
                Tracks = [.. value.Tracks.Select(track => track.Property == e.Property
                    ? track with { Keyframes = [new(e.NewTime, e.NewValue!.Value)] } : track)]
            });
            timeline.SetDocument(editor.Snapshot, null, Assert.Single(editor.Snapshot.Layers));
        };
        var window = new Window { Width = 700, Height = 400, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new[] { AnimationProperty.POSITION, AnimationProperty.ROTATION, AnimationProperty.OPACITY },
                timeline.GetAnimationProperties(layer.Id));
            var x = timeline.GetKeyframePoint(layer.Id, AnimationProperty.POSITION, new(1), new ScenePoint(10, 20))!.Value;
            var rotation = timeline.GetKeyframePoint(layer.Id, AnimationProperty.ROTATION, new(1), 40)!.Value;
            var opacity = timeline.GetKeyframePoint(layer.Id, AnimationProperty.OPACITY, new(1), 0.25)!.Value;
            Assert.True(x.Y > rotation.Y && rotation.Y > opacity.Y);
            Assert.True(x.Y < timeline.GetClipRectangle(layer.Id)!.Value.Top);
            Assert.Same(timeline, window.InputHitTest(x));
            window.MouseDown(x, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(x + new Vector(60, 0));
            window.MouseUp(x + new Vector(60, 0), MouseButton.Left);
            var changed = Assert.Single(editor.Snapshot.Layers);
            Assert.Equal(new MediaTime(2), Assert.Single(changed.Tracks.Single(track => track.Property == AnimationProperty.POSITION).Keyframes).Time);
            Assert.Same(layer.Tracks[0], changed.Tracks[0]);
            Assert.Same(layer.Tracks[2], changed.Tracks[2]);
            Assert.True(editor.Undo());
            Assert.Equal(layer.Tracks, Assert.Single(editor.Snapshot.Layers).Tracks);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClipEdgesUseResizeCursorThroughTrimAndRestoreAfterRelease()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), Start = new(1), End = new(4) };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 60 };
        timeline.SetDocument(new() { Layers = [layer] }, null, layer);
        var window = new Window { Width = 500, Height = 200, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var clip = timeline.GetClipRectangle(layer.Id)!.Value;
            var left = new Point(clip.Left + 2, clip.Center.Y);
            var right = new Point(clip.Right - 2, clip.Center.Y);
            window.MouseMove(left);
            var cursor = timeline.Cursor;
            Assert.NotNull(cursor);
            window.MouseMove(right);
            Assert.Same(cursor, timeline.Cursor);
            window.MouseDown(right, MouseButton.Left);
            window.MouseMove(right + new Vector(40, 0));
            Assert.Same(cursor, timeline.Cursor);
            window.MouseUp(right + new Vector(40, 0), MouseButton.Left);
            Assert.Null(timeline.Cursor);
            window.MouseMove(clip.Center);
            Assert.Null(timeline.Cursor);
        }
        finally
        {
            window.Close();
        }
    }
}
