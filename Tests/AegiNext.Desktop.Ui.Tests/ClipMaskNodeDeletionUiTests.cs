using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ClipMaskNodeDeletionUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlClickDeletesOnlyTheProjectedNodeOnReleaseWithOneUndo(bool drawMode)
    {
        var document = CreateDocument();
        var editor = new ProjectEditor(document);
        var layer = document.Layers[0];
        var mask = Assert.IsType<VectorClipMask>(layer.Mask);
        var node = mask.Contours[0].Nodes[0];
        using var canvas = new EffectCanvasControl { EditMode = drawMode ? CanvasEditMode.MASK_DRAW_VECTOR : CanvasEditMode.MASK_VECTOR, MaskSelectedNodeId = node.Id };
        canvas.SetScene(document, layer, new(0));
        var requests = 0;
        var geometryEdits = 0;
        canvas.MaskNodeDeleteRequested += (_, e) =>
        {
            requests++;
            Assert.Equal(layer.Id, e.LayerId);
            Assert.Equal(node.Id, e.NodeId);
            editor.RemoveClipMaskNode(e.LayerId, e.NodeId);
        };
        canvas.MaskEdited += (_, _) => geometryEdits++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = ProjectNode(canvas, document, mask, node.Position);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
            Assert.True(canvas.HasActiveDrag);
            Assert.Same(document, editor.Snapshot);
            Assert.Equal(0, requests);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
            Assert.Equal(1, requests);
            Assert.Equal(0, geometryEdits);
            Assert.False(canvas.HasActiveDrag);
            var changed = Assert.IsType<VectorClipMask>(editor.Snapshot.Layers[0].Mask);
            Assert.Equal(mask.Transform, changed.Transform);
            Assert.Equal(mask.Contours[0].Id, changed.Contours[0].Id);
            Assert.True(mask.Contours[0].Nodes.Skip(1).SequenceEqual(changed.Contours[0].Nodes));
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("capture")]
    [InlineData("escape")]
    [InlineData("release-away")]
    [InlineData("detach")]
    [InlineData("selection")]
    public void CancelledControlClickNeverPublishesDeletion(string cancellation)
    {
        var document = CreateDocument();
        var mask = Assert.IsType<VectorClipMask>(document.Layers[0].Mask);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, document.Layers[0], new(0));
        var requests = 0;
        var cancels = 0;
        IPointer? pointer = null;
        canvas.MaskNodeDeleteRequested += (_, _) => requests++;
        canvas.MaskGestureCancelled += (_, _) => cancels++;
        canvas.AddHandler(EffectCanvasControl.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Bubble, handledEventsToo: true);
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = ProjectNode(canvas, document, mask, mask.Contours[0].Nodes[0].Position);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
            Assert.True(canvas.HasActiveDrag);
            if (cancellation == "capture")
            {
                Assert.NotNull(pointer);
                pointer.Capture(null);
            }
            else if (cancellation == "escape")
            {
                UiTestActions.Press(window, Key.Escape);
            }
            else if (cancellation == "detach")
            {
                window.Content = null;
                window.Content = canvas;
                window.UpdateLayout();
            }
            else if (cancellation == "selection")
            {
                canvas.SetScene(document, null, new(0));
            }
            var released = cancellation == "release-away" ? point + new Vector(40, 40) : point;
            window.MouseUp(released, MouseButton.Left, RawInputModifiers.Control);
            Assert.False(canvas.HasActiveDrag);
            Assert.Equal(0, requests);
            Assert.Equal(1, cancels);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ControlClickCannotDeleteLockedNodesOrCreatePointsBetweenNodes(bool locked)
    {
        var document = CreateDocument();
        var layer = document.Layers[0];
        var mask = Assert.IsType<VectorClipMask>(layer.Mask);
        var node = mask.Contours[0].Nodes[0];
        if (locked)
        {
            layer = layer with { Tracks = [new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, node.Id), [new(new(0), node.Position)])] };
            document = document with { Layers = [layer] };
        }
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_DRAW_VECTOR };
        canvas.SetScene(document, layer, new(0));
        var requests = 0;
        canvas.MaskNodeDeleteRequested += (_, _) => requests++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = ProjectNode(canvas, document, mask, locked ? node.Position : new ScenePoint(400, 200));
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Control);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Control);
            Assert.False(canvas.HasActiveDrag);
            Assert.False(canvas.HasOpenMaskContour);
            Assert.Equal(0, requests);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectDocument CreateDocument()
    {
        var line = new SubtitleLine { End = new(5) };
        var mask = new VectorClipMask
        {
            Transform = new() { Position = new(17, 13), Scale = new(1.3, 0.9), Rotation = 25 },
            Placement = new() { Pivot = new(300, 250), Offset = new(300, 250) },
            Contours = [new() { Nodes = [new() { Position = new(200, 200) }, new() { Position = new(600, 200) }, new() { Position = new(400, 400) }] }]
        };
        return new() { Width = 1000, Height = 500, Subtitles = [line], Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = mask }] };
    }

    private static Point ProjectNode(EffectCanvasControl canvas, ProjectDocument document, ClipMask mask, ScenePoint position)
    {
        var projected = ClipMaskPlacementGeometry.CreateMatrix(mask, new(document.Width, document.Height)).Map(position);
        var board = canvas.ProjectRectangle;
        return new(board.X + projected.X * board.Width / document.Width, board.Y + projected.Y * board.Height / document.Height);
    }
}
