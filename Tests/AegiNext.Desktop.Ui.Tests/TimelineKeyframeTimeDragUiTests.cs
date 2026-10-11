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

public sealed class TimelineKeyframeTimeDragUiTests
{
    [AvaloniaTheory]
    [InlineData("scalar", 0)]
    [InlineData("vector", 0)]
    [InlineData("vector", 1)]
    [InlineData("combined", 0)]
    [InlineData("color", 0)]
    [InlineData("color", 3)]
    public void VerticalDragKeepsEveryComponentAndCreatesNoEditWhileDiagonalDragMovesOnlyTime(string kind, int component)
    {
        var property = kind switch
        {
            "scalar" => AnimationProperty.OPACITY,
            "color" => AnimationProperty.FILL,
            _ => AnimationProperty.SCALE
        };
        var value = kind switch
        {
            "scalar" => AnimationValue.FromScalar(0.25),
            "color" => AnimationValue.FromColor(new(4, 4, 4, 0.5)),
            "combined" => AnimationValue.FromVector(new(1, 1)),
            _ => AnimationValue.FromVector(new(1, 2))
        };
        var key = new Keyframe(new(1), value, KeyframeInterpolation.EASE_OUT);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), End = new(8),
            Tracks = [new(property, [key])]
        };
        var source = new ProjectDocument { Layers = [layer] };
        var editor = new ProjectEditor(source);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetDocument(source, null, layer);
        timeline.TimelineViewState = TimelineEffectClipTestState.ExpandAll(source);
        var edits = 0;
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = true;
        timeline.KeyframeMoved += (_, e) =>
        {
            edits++;
            Assert.Equal(value, e.NewValue);
            editor.UpdateLayer(e.LayerId, current => current with
            {
                Tracks = [new(property, [key with { Time = e.NewTime, Value = e.NewValue!.Value }])]
            });
            timeline.SetDocument(editor.Snapshot, null, editor.Snapshot.Layers[0]);
        };
        var window = new Window { Width = 700, Height = 400, Content = timeline };
        window.Show();
        try
        {
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var point = timeline.GetKeyframePoint(layer.Id, property, key.Time, value, component)!.Value;
            var markers = timeline.KeyframeMarkers.ToArray();
            Assert.Same(timeline, window.InputHitTest(point));

            foreach (var vertical in new[] { -100, 100 })
            {
                window.MouseDown(point, MouseButton.Left);
                Assert.True(timeline.HasActiveDrag);
                window.MouseMove(point + new Vector(0, vertical));
                Assert.Equal(markers, timeline.KeyframeMarkers);
                window.MouseUp(point + new Vector(0, vertical), MouseButton.Left);
                Assert.False(timeline.HasActiveDrag);
                Assert.Equal(0, edits);
                Assert.Same(source, editor.Snapshot);
                Assert.False(editor.CanUndo);
            }

            var destination = point + new Vector(80, -100);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(destination);
            Assert.All(timeline.KeyframeMarkers, marker =>
            {
                var original = Assert.Single(markers, item => item.Components == marker.Components);
                Assert.Equal(new MediaTime(2), marker.Identity.Time);
                Assert.Equal(value, marker.Value);
                Assert.Equal(original.Position.Y, marker.Position.Y);
            });
            Assert.Same(source, editor.Snapshot);
            window.MouseUp(destination, MouseButton.Left);
            Assert.Equal(1, edits);
            var moved = Assert.Single(editor.Snapshot.Layers[0].Tracks[0].Keyframes);
            Assert.Equal(key with { Time = new(2) }, moved);
            Assert.True(editor.Undo());
            Assert.Same(source, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.True(editor.Redo());
            Assert.Equal(moved, Assert.Single(editor.Snapshot.Layers[0].Tracks[0].Keyframes));
        }
        finally
        {
            window.Close();
        }
    }
}
