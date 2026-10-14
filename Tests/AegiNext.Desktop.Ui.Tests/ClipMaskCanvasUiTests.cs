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

public sealed class ClipMaskCanvasUiTests
{
    [AvaloniaFact]
    public void RectanglePointerGestureCommitsOnceAndUsesEngineeringCoordinates()
    {
        var line = new SubtitleLine { Text = "Mask ABC 中文 123", End = new(5) };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End };
        var document = new ProjectDocument { Width = 1000, Height = 500, Subtitles = [line], Layers = [layer] };
        var editor = new ProjectEditor(document);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, layer, new(0));
        var commits = 0;
        canvas.MaskEdited += (_, e) => { commits++; editor.SetClipMask(e.LayerId, e.Mask); };
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var board = canvas.ProjectRectangle;
            var scale = board.Width / document.Width;
            var first = new Point(board.X + 100 * scale, board.Y + 50 * scale);
            var last = new Point(board.X + 600 * scale, board.Y + 300 * scale);
            window.MouseDown(first, MouseButton.Left);
            window.MouseMove(last);
            Assert.True(canvas.HasActiveDrag);
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(last, MouseButton.Left);
            Assert.Equal(1, commits);
            var mask = Assert.IsType<RectangleClipMask>(editor.Snapshot.Layers[0].Mask);
            Assert.Equal(100, mask.TopLeft.X, 6);
            Assert.Equal(50, mask.TopLeft.Y, 6);
            Assert.Equal(600, mask.BottomRight.X, 6);
            Assert.Equal(300, mask.BottomRight.Y, 6);
            Assert.Equal(new ScenePoint(0.5, 0.5), mask.Placement.Anchor);
            Assert.Equal(new ScenePoint(0.5, 0.5), mask.Placement.Pivot);
            Assert.Equal(100, mask.ReferenceFrame.Origin.X, 6);
            Assert.Equal(50, mask.ReferenceFrame.Origin.Y, 6);
            Assert.Equal(500, mask.ReferenceFrame.Size.X, 6);
            Assert.Equal(250, mask.ReferenceFrame.Size.Y, 6);
            Assert.Equal(-150, mask.Placement.Offset.X, 6);
            Assert.Equal(-75, mask.Placement.Offset.Y, 6);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OpenBezierContourRemainsDraftUntilClosingAndEscapeCancelsTheNextContour()
    {
        var line = new SubtitleLine { End = new(5) };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End };
        var document = new ProjectDocument { Width = 1000, Height = 500, Subtitles = [line], Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_DRAW_VECTOR };
        canvas.SetScene(document, layer, new(0));
        var commits = new List<CanvasMaskEditEventArgs>();
        canvas.MaskEdited += (_, e) => commits.Add(e);
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var board = canvas.ProjectRectangle;
            var first = new Point(board.X + board.Width * 0.2, board.Y + board.Height * 0.2);
            var second = new Point(board.X + board.Width * 0.7, board.Y + board.Height * 0.2);
            var third = new Point(board.X + board.Width * 0.4, board.Y + board.Height * 0.7);
            window.MouseDown(first, MouseButton.Left);
            window.MouseMove(first + new Vector(25, 0));
            window.MouseUp(first + new Vector(25, 0), MouseButton.Left);
            window.MouseDown(second, MouseButton.Left);
            window.MouseUp(second, MouseButton.Left);
            window.MouseDown(third, MouseButton.Left);
            window.MouseUp(third, MouseButton.Left);
            Assert.Empty(commits);
            Assert.True(canvas.HasOpenMaskContour);
            window.MouseDown(first, MouseButton.Left);
            window.MouseUp(first, MouseButton.Left);
            var mask = Assert.IsType<VectorClipMask>(Assert.Single(commits).Mask);
            var contour = Assert.Single(mask.Contours);
            Assert.Equal(3, contour.Nodes.Length);
            Assert.NotEqual(default, contour.Nodes[0].OutHandle);
            Assert.Equal(-contour.Nodes[0].InHandle.X, contour.Nodes[0].OutHandle.X);
            Assert.False(canvas.HasOpenMaskContour);
            canvas.EditMode = CanvasEditMode.MASK_DRAW_VECTOR;
            window.MouseDown(third, MouseButton.Left);
            window.MouseUp(third, MouseButton.Left);
            UiTestActions.Press(window, Key.Escape);
            Assert.False(canvas.HasOpenMaskContour);
            Assert.Single(commits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AltPointerDragCreatesAHandleForAStraightNodeWithoutMovingTheNode()
    {
        var line = new SubtitleLine { End = new(5) };
        var first = new MaskNode { Position = new(100, 100) };
        var mask = new VectorClipMask { Contours = [new() { Nodes = [first, new() { Position = new(500, 100) }, new() { Position = new(300, 400) }] }] };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = mask };
        var document = new ProjectDocument { Width = 1000, Height = 500, Subtitles = [line], Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, layer, new(0));
        CanvasMaskEditEventArgs? edited = null;
        canvas.MaskEdited += (_, e) => edited = e;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var board = canvas.ProjectRectangle;
            var scale = board.Width / document.Width;
            var point = new Point(board.X + first.Position.X * scale, board.Y + first.Position.Y * scale);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Alt);
            window.MouseMove(point + new Vector(70, 0), RawInputModifiers.Alt);
            window.MouseUp(point + new Vector(70, 0), MouseButton.Left, RawInputModifiers.Alt);
            Assert.NotNull(edited);
            var node = Assert.IsType<VectorClipMask>(edited.Mask).Contours[0].Nodes[0];
            Assert.Equal(first.Position, node.Position);
            Assert.Equal(70 / scale, node.OutHandle.X, 6);
            Assert.Equal(default, node.InHandle);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CaptureLossCancelsRectangleWithoutPublishingItsDraft()
    {
        var line = new SubtitleLine { End = new(5) };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, layer, new(0));
        var commits = 0;
        var cancels = 0;
        IPointer? pointer = null;
        canvas.AddHandler(EffectCanvasControl.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Bubble, handledEventsToo: true);
        canvas.MaskEdited += (_, _) => commits++;
        canvas.MaskGestureCancelled += (_, _) => cancels++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var first = canvas.ProjectRectangle.Center;
            window.MouseDown(first, MouseButton.Left);
            window.MouseMove(first + new Vector(60, 40));
            Assert.NotNull(pointer);
            pointer.Capture(null);
            window.MouseUp(first + new Vector(60, 40), MouseButton.Left);
            Assert.Equal(0, commits);
            Assert.Equal(1, cancels);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RectangleToolClickWithoutAreaNeverCreatesOrResetsAMask(bool existing)
    {
        var line = new SubtitleLine { End = new(5) };
        var mask = new RectangleClipMask { TopLeft = new(100, 50), BottomRight = new(250, 150) };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = existing ? mask : null };
        var document = new ProjectDocument { Width = 1000, Height = 500, Subtitles = [line], Layers = [layer] };
        var editor = new ProjectEditor(document);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, layer, new(0));
        var commits = 0;
        canvas.MaskEdited += (_, e) => { commits++; editor.SetClipMask(e.LayerId, e.Mask); };
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = canvas.ProjectRectangle.Center;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.Equal(0, commits);
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.False(canvas.HasActiveDrag);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DraggingAnExistingRectangleHandleChangesSizeAndKeepsItsPivot()
    {
        var line = new SubtitleLine { End = new(5) };
        var mask = new RectangleClipMask { TopLeft = new(100, 50), BottomRight = new(250, 150), Placement = new() { Pivot = new(0.5, 0.5), Offset = new(175, 100) }, ReferenceFrame = new() { Origin = new(100, 50), Size = new(150, 100) } };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = mask };
        var document = new ProjectDocument { Width = 1000, Height = 500, Subtitles = [line], Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, layer, new(0));
        CanvasMaskEditEventArgs? changed = null;
        canvas.MaskEdited += (_, e) => changed = e;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var board = canvas.ProjectRectangle;
            var scale = board.Width / document.Width;
            var first = new Point(board.X + 250 * scale, board.Y + 150 * scale);
            var last = new Point(board.X + 400 * scale, board.Y + 300 * scale);
            window.MouseDown(first, MouseButton.Left);
            window.MouseMove(last);
            window.MouseUp(last, MouseButton.Left);
            Assert.NotNull(changed);
            var resized = Assert.IsType<RectangleClipMask>(changed.Mask);
            Assert.Equal(mask.TopLeft, resized.TopLeft);
            Assert.Equal(mask.Transform, resized.Transform);
            Assert.Equal(mask.Placement, resized.Placement);
            Assert.Equal(mask.ReferenceFrame, resized.ReferenceFrame);
            Assert.Equal(400, resized.BottomRight.X, 6);
            Assert.Equal(300, resized.BottomRight.Y, 6);
        }
        finally
        {
            window.Close();
        }
    }
}
