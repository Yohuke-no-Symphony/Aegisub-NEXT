using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SpatialCanvasUiTests
{
    [AvaloniaTheory]
    [InlineData(AnimationProperty.ROTATION_X)]
    [InlineData(AnimationProperty.ROTATION_Y)]
    [InlineData(AnimationProperty.PERSPECTIVE_DISTANCE)]
    public void SpatialPointerChangesPreviewAndPublishesOneAbsoluteValueOnRelease(AnimationProperty property)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80),
            Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5) }
        };
        var document = new ProjectDocument { Width = 400, Height = 400, Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.SPATIAL_TRANSFORM };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = new List<CanvasSpatialEditEventArgs>();
        canvas.SpatialEdited += (_, e) => edits.Add(e);
        var window = Open(canvas);
        try
        {
            var start = canvas.SpatialHandlePoints()[property];
            var end = start + (property == AnimationProperty.ROTATION_X ? new Vector(0, -35) :
                property == AnimationProperty.ROTATION_Y ? new Vector(35, 0) : new Vector(120, 0));
            window.MouseDown(start, MouseButton.Left);
            Assert.True(canvas.HasActiveDrag);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Assert.Empty(edits);
            Assert.True(canvas.HasActiveDrag);
            window.MouseUp(end, MouseButton.Left);

            var edit = Assert.Single(edits);
            Assert.Equal(new AnimationTrackTarget(property == AnimationProperty.PERSPECTIVE_DISTANCE ? property :
                AnimationProperty.ROTATION_3D), edit.Target);
            var component = property == AnimationProperty.ROTATION_Y ? 1 : 0;
            Assert.Equal(property == AnimationProperty.PERSPECTIVE_DISTANCE ? 312.5 * Math.E : 35,
                edit.Value.GetComponent(component), 7);
            if (edit.Value.IsVector3)
            {
                Assert.All(Enumerable.Range(0, 3).Where(index => index != component),
                    index => Assert.Equal(0, edit.Value.GetComponent(index)));
            }
            Assert.False(canvas.HasActiveDrag);
            Assert.Equal(new LayerTransform(), layer.Transform);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EscapeOrTargetChangeDiscardsTheTemporarySpatialEdit(bool changeTarget)
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80), Transform = new(200, 200)
        };
        var document = new ProjectDocument { Width = 400, Height = 400, Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.SPATIAL_TRANSFORM };
        canvas.SetScene(document, layer, MediaTime.Zero);
        var edits = 0;
        var cancelled = 0;
        canvas.SpatialEdited += (_, _) => edits++;
        canvas.GestureCancelled += (_, _) => cancelled++;
        var window = Open(canvas);
        try
        {
            var start = canvas.SpatialHandlePoints()[AnimationProperty.ROTATION_Y];
            var end = start + new Vector(30, 0);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            if (changeTarget)
            {
                canvas.SpatialTarget = new(AnimationProperty.ROTATION_X);
            }
            else
            {
                Assert.True(canvas.TryExecuteFocusCommand(WorkbenchCommand.END_TEXT_INPUT, canvas));
            }
            window.MouseUp(end, MouseButton.Left);

            Assert.Equal(0, edits);
            Assert.Equal(1, cancelled);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectedAppearanceRangePublishesItsNormalSpatialTarget()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine
        {
            Text = "ABCD", End = new(4), AnimationRanges = [range],
            Style = new() { FontFamily = "sans-serif", FontSize = 32, Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5) } }
        };
        var layer = new ProjectLayer { SubtitleId = line.Id, End = line.End };
        var source = new ProjectDocument { Width = 400, Height = 400, Subtitles = [line], Layers = [layer] };
        using var canvas = new EffectCanvasControl
        {
            EditMode = CanvasEditMode.SPATIAL_TRANSFORM,
            SpatialTarget = new(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE)
        };
        canvas.SetScene(source, layer, MediaTime.Zero);
        CanvasSpatialEditEventArgs? result = null;
        canvas.SpatialEdited += (_, e) => result = e;
        var window = Open(canvas);
        try
        {
            var start = canvas.SpatialHandlePoints()[AnimationProperty.ROTATION_X];
            var end = start + new Vector(0, -20);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);

            Assert.NotNull(result);
            Assert.Equal(new AnimationTrackTarget(AnimationProperty.ROTATION_3D, TextRangeId: range.Id), result.Target);
            Assert.Equal(20, result.Value.Vector3.X, 7);
            Assert.Equal(0, result.Value.Vector3.Y);
            Assert.Equal(0, result.Value.Vector3.Z);
            Assert.Same(range, line.AnimationRanges[0]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DraggingOneAxisKeepsTheFrozenVectorAndUsesOneTarget()
    {
        var original = new SceneVector3(20, 30, 40);
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80),
            Transform = new(200, 200),
            Tracks = [new(AnimationProperty.ROTATION_3D, [new(new(0), original), new(new(2), new SceneVector3(50, 60, 70))])]
        };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.SPATIAL_TRANSFORM };
        canvas.SetScene(new() { Width = 400, Height = 400, Layers = [layer] }, layer, new(1));
        CanvasSpatialEditEventArgs? result = null;
        canvas.SpatialEdited += (_, value) => result = value;
        var window = Open(canvas);
        try
        {
            var start = canvas.SpatialHandlePoints()[AnimationProperty.ROTATION_Y];
            var end = start + new Vector(25, 0);
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);

            Assert.NotNull(result);
            Assert.Equal(new AnimationTrackTarget(AnimationProperty.ROTATION_3D), result.Target);
            Assert.Equal(new SceneVector3(35, 70, 55), result.Value.Vector3);
            Assert.Equal(original, layer.Tracks[0].Keyframes[0].Value.Vector3);
            Assert.Equal(2, layer.Tracks[0].Keyframes.Length);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OrderedVectorRotationLocksAllRotationHandlesAndLeavesDistanceEditable()
    {
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80), Transform = new(200, 200),
            Tracks =
            [
                new(AnimationProperty.ROTATION_3D, [])
                {
                    InitialValue = new SceneVector3(10, 20, 30),
                    Transforms = [new(Guid.NewGuid(), new(0), new(2), new SceneVector3(45, 20, 30)) { ComponentMask = 1 }]
                }
            ]
        };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.SPATIAL_TRANSFORM };
        canvas.SetScene(new() { Width = 400, Height = 400, Layers = [layer] }, layer, new(1));
        var window = Open(canvas);
        try
        {
            var handles = canvas.SpatialHandlePoints();
            Assert.Single(handles);
            Assert.Contains(AnimationProperty.PERSPECTIVE_DISTANCE, handles.Keys);
            Assert.False(canvas.BeginSpatialDrag(new(0, 0), AnimationProperty.ROTATION_X));
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StartingCallbacksCannotRecreateAGestureAfterCancellationOrDisposal()
    {
        foreach (var action in new[] { "cancel", "dispose", "mode" })
        {
            var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80), Transform = new(200, 200) };
            using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.SPATIAL_TRANSFORM };
            canvas.SetScene(new() { Width = 400, Height = 400, Layers = [layer] }, layer, MediaTime.Zero);
            var window = Open(canvas);
            try
            {
                var start = canvas.SpatialHandlePoints()[AnimationProperty.ROTATION_Y];
                canvas.SpatialGestureStarting += (_, _) =>
                {
                    if (action == "cancel")
                    {
                        canvas.CancelGesture();
                    }
                    else if (action == "dispose")
                    {
                        canvas.Dispose();
                    }
                    else
                    {
                        canvas.EditMode = CanvasEditMode.POSITION;
                    }
                };

                Assert.False(canvas.BeginSpatialDrag(start, AnimationProperty.ROTATION_Y));
                Assert.False(canvas.HasActiveDrag);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void DisposingTheCanvasCancelsAnActiveSpatialGesture()
    {
        var layer = new ProjectLayer { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 100, 80), Transform = new(200, 200) };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.SPATIAL_TRANSFORM };
        canvas.SetScene(new() { Width = 400, Height = 400, Layers = [layer] }, layer, MediaTime.Zero);
        var window = Open(canvas);
        try
        {
            Assert.True(canvas.BeginSpatialDrag(canvas.SpatialHandlePoints()[AnimationProperty.ROTATION_Y], AnimationProperty.ROTATION_Y));
            canvas.Dispose();
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Open(EffectCanvasControl canvas)
    {
        var window = new Window { Width = 424, Height = 424, Content = canvas };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
