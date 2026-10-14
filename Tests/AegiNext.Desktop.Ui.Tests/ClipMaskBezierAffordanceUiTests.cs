using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ClipMaskBezierAffordanceUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShiftClickSubdividesTheProjectedCubicMidpointOnlyOnReleaseWithOneUndo(bool drawMode)
    {
        var document = Document();
        var layer = document.Layers[0];
        var mask = Assert.IsType<VectorClipMask>(layer.Mask);
        var contour = mask.Contours[0];
        var editor = new ProjectEditor(document);
        using var canvas = new EffectCanvasControl { EditMode = drawMode ? CanvasEditMode.MASK_DRAW_VECTOR : CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, layer, new(0));
        var requests = 0;
        canvas.MaskSegmentInsertRequested += (_, e) =>
        {
            requests++;
            Assert.Equal(layer.Id, e.LayerId);
            Assert.Equal(contour.Id, e.ContourId);
            Assert.Equal(contour.Nodes[0].Id, e.NodeId);
            editor.SetClipMask(e.LayerId, ClipMaskGeometryOperations.SubdivideSegment(mask, e.ContourId, e.NodeId, e.Progress));
        };
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var midpoint = Midpoint(canvas, document, mask);
            window.MouseDown(midpoint, MouseButton.Left, RawInputModifiers.Shift);
            Assert.True(canvas.HasActiveDrag);
            Assert.Same(document, editor.Snapshot);
            window.MouseUp(midpoint, MouseButton.Left, RawInputModifiers.Shift);
            Assert.Equal(1, requests);
            Assert.False(canvas.HasActiveDrag);
            Assert.False(canvas.HasOpenMaskContour);
            var changed = Assert.IsType<VectorClipMask>(editor.Snapshot.Layers[0].Mask);
            Assert.Equal(4, changed.Contours[0].Nodes.Length);
            Assert.Equal(mask.Transform, changed.Transform);
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.True(editor.Redo());
            Assert.Same(changed, editor.Snapshot.Layers[0].Mask);
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
    public void CancelledShiftClickNeverInsertsOrCreatesADraft(string cancellation)
    {
        var document = Document();
        var mask = Assert.IsType<VectorClipMask>(document.Layers[0].Mask);
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, document.Layers[0], new(0));
        var requests = 0;
        var cancels = 0;
        IPointer? pointer = null;
        canvas.MaskSegmentInsertRequested += (_, _) => requests++;
        canvas.MaskGestureCancelled += (_, _) => cancels++;
        canvas.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Bubble, handledEventsToo: true);
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = Midpoint(canvas, document, mask);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
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
            window.MouseUp(cancellation == "release-away" ? point + new Vector(40, 40) : point, MouseButton.Left, RawInputModifiers.Shift);
            Assert.Equal(0, requests);
            Assert.Equal(1, cancels);
            Assert.False(canvas.HasActiveDrag);
            Assert.False(canvas.HasOpenMaskContour);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShiftCannotInsertLockedNodesOrDuplicateAnExistingVertex(bool locked)
    {
        var document = Document();
        var layer = document.Layers[0];
        var mask = Assert.IsType<VectorClipMask>(layer.Mask);
        if (locked)
        {
            layer = layer with { Tracks = [new(new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, mask.Contours[0].Nodes[0].Id), [new(new(0), mask.Contours[0].Nodes[0].Position)])] };
            document = document with { Layers = [layer] };
        }
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_DRAW_VECTOR };
        canvas.SetScene(document, layer, new(0));
        var requests = 0;
        canvas.MaskSegmentInsertRequested += (_, _) => requests++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = locked ? Midpoint(canvas, document, mask) : Project(canvas, document, mask, mask.Contours[0].Nodes[0].Position);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
            Assert.Equal(0, requests);
            Assert.False(canvas.HasActiveDrag);
            Assert.False(canvas.HasOpenMaskContour);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ModifierCursorsUpdateWithoutMovingThePointerAndSurviveReattachment()
    {
        var document = Document();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, document.Layers[0], new(0));
        var input = new TextBox();
        var host = new StackPanel();
        host.Children.Add(input);
        host.Children.Add(canvas);
        canvas.Height = 300;
        var window = new Window { Width = 700, Height = 400, Content = host };
        window.Show();
        try
        {
            window.UpdateLayout();
            input.Focus();
            window.MouseMove(canvas.TranslatePoint(new Point(200, 100), window)!.Value);
            var normal = canvas.Cursor;
            window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
            var deletion = canvas.Cursor;
            Assert.NotNull(deletion);
            Assert.NotSame(normal, deletion);
            window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
            Assert.Same(normal, canvas.Cursor);
            window.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.ShiftLeft, null);
            var insertion = canvas.Cursor;
            Assert.NotSame(normal, insertion);
            Assert.NotSame(deletion, insertion);
            window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.ControlLeft, null);
            Assert.Same(deletion, canvas.Cursor);
            window.KeyRelease(Key.LeftCtrl, RawInputModifiers.Shift, PhysicalKey.ControlLeft, null);
            Assert.Same(insertion, canvas.Cursor);
            window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
            Assert.Same(normal, canvas.Cursor);
            host.Children.Remove(canvas);
            host.Children.Add(canvas);
            window.UpdateLayout();
            input.Focus();
            window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
            Assert.Same(deletion, canvas.Cursor);
            window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
            canvas.EditMode = CanvasEditMode.POSITION;
            Assert.Null(canvas.Cursor);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LabelsMatchContourAndPointOrderInEvaluatedProjectCoordinatesAndHideAfterExit()
    {
        var document = Document();
        var mask = Assert.IsType<VectorClipMask>(document.Layers[0].Mask);
        mask = mask with { Contours = mask.Contours.Add(new() { Nodes = [new() { Position = new(700, 250) }] }) };
        var layer = document.Layers[0] with { Mask = mask };
        document = document with { Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, layer, new(0));
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var labels = canvas.MaskNodeLabels().ToArray();
            Assert.Equal(["1:1", "1:2", "1:3", "2:1"], labels.Select(label => label.Text));
            Assert.Equal(mask.Contours.SelectMany(contour => contour.Nodes).Select(node => node.Id), labels.Select(label => label.NodeId));
            var expected = Project(canvas, document, mask, mask.Contours[0].Nodes[0].Position);
            Assert.Equal(expected.X, labels[0].Position.X, 8);
            Assert.Equal(expected.Y, labels[0].Position.Y, 8);
            using var capture = new RenderTargetBitmap(new(700, 400));
            capture.Render(canvas);
            var directory = Path.GetFullPath("artifacts/verification/clip-mask/bezier-affordances", FindRoot());
            Directory.CreateDirectory(directory);
            capture.Save(Path.Combine(directory, "node-labels.png"), PngBitmapEncoderOptions.Default);
            canvas.EditMode = CanvasEditMode.POSITION;
            Assert.Empty(canvas.MaskNodeLabels());
            Assert.Same(mask, layer.Mask);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RichTextInputUsesIBeamOverTextAndBlankSpaceAndStillAcceptsText()
    {
        using var editor = new RichSubtitleEditor();
        var document = Document();
        editor.SetContent(document, document.Subtitles[0], AppContext.BaseDirectory);
        var edits = 0;
        editor.TextEditRequested += (_, _) => edits++;
        var window = new Window { Width = 700, Height = 200, Content = editor };
        window.Show();
        try
        {
            window.UpdateLayout();
            window.MouseMove(new(30, 30));
            Assert.Equal("Ibeam", editor.Cursor?.ToString());
            window.MouseMove(new(650, 170));
            Assert.Equal("Ibeam", editor.Cursor?.ToString());
            window.MouseDown(new(30, 30), MouseButton.Left);
            window.MouseUp(new(30, 30), MouseButton.Left);
            window.KeyTextInput("中文 ABC 123");
            Assert.Equal(1, edits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CustomCursorBitmapsContainTheSameCrossAndDistinctPlusMinusIndicators()
    {
        var directory = Path.GetFullPath("artifacts/verification/clip-mask/bezier-affordances", FindRoot());
        Directory.CreateDirectory(directory);
        using var insertion = BezierCanvasCursors.CreateBitmap(true);
        using var deletion = BezierCanvasCursors.CreateBitmap(false);
        insertion.Save(Path.Combine(directory, "insert-cursor.png"), PngBitmapEncoderOptions.Default);
        deletion.Save(Path.Combine(directory, "delete-cursor.png"), PngBitmapEncoderOptions.Default);
        using var insertPixels = SKBitmap.Decode(Path.Combine(directory, "insert-cursor.png"));
        using var deletePixels = SKBitmap.Decode(Path.Combine(directory, "delete-cursor.png"));
        Assert.True(insertPixels.GetPixel(23, 18).Alpha > 200);
        Assert.Equal(0, deletePixels.GetPixel(23, 18).Alpha);
        Assert.Equal(insertPixels.GetPixel(23, 23), deletePixels.GetPixel(23, 23));
        Assert.Equal(insertPixels.GetPixel(9, 2), deletePixels.GetPixel(9, 2));
        Assert.True(insertPixels.GetPixel(9, 2).Alpha > 200);
    }

    [AvaloniaFact]
    public void AltShiftStillEditsTheIncomingHandleWithoutMovingTheNodeOrInserting()
    {
        var document = Document();
        var layer = document.Layers[0];
        var mask = Assert.IsType<VectorClipMask>(layer.Mask);
        var node = mask.Contours[0].Nodes[0];
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, layer, new(0));
        ClipMask? edited = null;
        var inserted = 0;
        canvas.MaskEdited += (_, e) => edited = e.Mask;
        canvas.MaskSegmentInsertRequested += (_, _) => inserted++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = Project(canvas, document, mask, node.Position);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Alt | RawInputModifiers.Shift);
            window.MouseMove(point + new Vector(30, 15), RawInputModifiers.Alt | RawInputModifiers.Shift);
            window.MouseUp(point + new Vector(30, 15), MouseButton.Left, RawInputModifiers.Alt | RawInputModifiers.Shift);
            var changed = Assert.IsType<VectorClipMask>(edited);
            Assert.Equal(0, inserted);
            Assert.Equal(3, changed.Contours[0].Nodes.Length);
            Assert.Equal(node.Position, changed.Contours[0].Nodes[0].Position);
            Assert.Equal(node.OutHandle, changed.Contours[0].Nodes[0].OutHandle);
            Assert.NotEqual(node.InHandle, changed.Contours[0].Nodes[0].InHandle);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShiftSubdivisionOfAnOpenContourStaysInTheDraftUntilTheContourIsClosed()
    {
        var document = Document();
        var layer = document.Layers[0] with { Mask = null };
        document = document with { Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_DRAW_VECTOR };
        canvas.SetScene(document, layer, new(0));
        var commits = new List<CanvasMaskEditEventArgs>();
        var requests = 0;
        canvas.MaskEdited += (_, e) => commits.Add(e);
        canvas.MaskSegmentInsertRequested += (_, _) => requests++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var board = canvas.ProjectRectangle;
            var first = new Point(board.X + board.Width * 0.2, board.Y + board.Height * 0.2);
            var next = new Point(board.X + board.Width * 0.6, board.Y + board.Height * 0.2);
            window.MouseDown(first, MouseButton.Left);
            window.MouseUp(first, MouseButton.Left);
            window.MouseDown(next, MouseButton.Left);
            window.MouseUp(next, MouseButton.Left);
            var middle = new Point((first.X + next.X) / 2, first.Y);
            window.MouseDown(middle, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(middle, MouseButton.Left, RawInputModifiers.Shift);
            Assert.Empty(commits);
            Assert.Equal(0, requests);
            Assert.True(canvas.HasOpenMaskContour);
            Assert.Equal(["1:1", "1:2", "1:3"], canvas.MaskNodeLabels().Select(label => label.Text));
            var labels = canvas.MaskNodeLabels().ToArray();
            Assert.Equal(middle.X, labels[1].Position.X, 8);
            Assert.Equal(middle.Y, labels[1].Position.Y, 8);
            var quarter = first + new Vector((next.X - first.X) * 0.15625, 0);
            window.MouseDown(quarter, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(quarter + new Vector(50, 50), MouseButton.Left, RawInputModifiers.Shift);
            Assert.Empty(commits);
            Assert.Equal(3, canvas.MaskNodeLabels().Count());
            UiTestActions.Press(window, Key.Enter);
            var mask = Assert.IsType<VectorClipMask>(Assert.Single(commits).Mask);
            Assert.Equal(3, mask.Contours[0].Nodes.Length);
            Assert.False(canvas.HasOpenMaskContour);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PreviewShiftClickUpdatesThePanelSelectionAndCommitsThroughTheWorkspaceOnce()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var document = session.DocumentSnapshot;
        var contour = new MaskContour
        {
            Nodes = [new() { Position = new(document.Width * 0.2, document.Height * 0.3), OutHandle = new(document.Width * 0.1, -document.Height * 0.05) },
                new() { Position = new(document.Width * 0.6, document.Height * 0.3), InHandle = new(-document.Width * 0.08, document.Height * 0.05) },
                new() { Position = new(document.Width * 0.4, document.Height * 0.7) }]
        };
        var mask = new VectorClipMask { Contours = [contour] };
        session.Editor.SetClipMask(session.SelectedLayer!.Id, mask);
        session.MaskEditing.ToggleEditing();
        var canvas = UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas");
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        var original = session.DocumentSnapshot;
        var midpoint = Midpoint(canvas, original, mask);
        Assert.True(canvas.ProjectRectangle.Contains(midpoint));
        var point = canvas.TranslatePoint(midpoint, context.Window)!.Value;
        Assert.Equal(CanvasEditMode.MASK_VECTOR, canvas.EditMode);
        Assert.True(canvas.IsEffectivelyVisible);
        var starts = 0;
        var cancels = 0;
        canvas.MaskGestureStarting += (_, _) => starts++;
        canvas.MaskGestureCancelled += (_, _) => cancels++;
        context.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        Assert.True(canvas.HasActiveDrag, $"starts={starts}, cancels={cancels}, point={point}, bounds={canvas.Bounds}, board={canvas.ProjectRectangle}, mode={canvas.EditMode}");
        Assert.Same(original, session.DocumentSnapshot);
        context.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();
        var changed = Assert.IsType<VectorClipMask>(session.SelectedLayer!.Mask);
        Assert.Equal(4, changed.Contours[0].Nodes.Length);
        var inserted = changed.Contours[0].Nodes[1];
        Assert.Equal(inserted.Id, session.SceneEditing.MaskNodeId);
        Assert.Equal(inserted.Id, context.ViewModel.Masks.SelectedPoint!.Id);
        Assert.True(session.Editor.Undo());
        Assert.Same(original, session.DocumentSnapshot);
        Assert.True(session.Editor.Redo());
        Assert.Equal(inserted.Id, Assert.IsType<VectorClipMask>(session.SelectedLayer!.Mask).Contours[0].Nodes[1].Id);
    }

    [AvaloniaTheory]
    [InlineData(0, 0.2)]
    [InlineData(0, 0.8)]
    [InlineData(1, 0.25)]
    [InlineData(2, 0.75)]
    public void ShiftInsertsAtTheClickedPositionAcrossCurvedStraightAndClosingSegments(int segmentIndex, double progress)
    {
        var document = Document();
        var layer = document.Layers[0];
        var mask = Assert.IsType<VectorClipMask>(layer.Mask);
        var contour = mask.Contours[0];
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, layer, new(0));
        CanvasMaskSegmentEventArgs? request = null;
        canvas.MaskSegmentInsertRequested += (_, e) => request = e;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var first = contour.Nodes[segmentIndex];
            var next = contour.Nodes[(segmentIndex + 1) % contour.Nodes.Length];
            var geometry = new AegiNext.Core.Projects.PathGeometry(first.Position,
                [new(new(first.Position.X + first.OutHandle.X, first.Position.Y + first.OutHandle.Y),
                    new(next.Position.X + next.InHandle.X, next.Position.Y + next.InHandle.Y), next.Position)]);
            var expected = SceneEvaluator.EvaluatePath(geometry, progress);
            var point = Project(canvas, document, mask, expected);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
            Assert.NotNull(request);
            Assert.Equal(contour.Id, request.ContourId);
            Assert.Equal(first.Id, request.NodeId);
            Assert.Equal(progress, request.Progress, 7);
            var changed = ClipMaskGeometryOperations.SubdivideSegment(mask, request.ContourId, request.NodeId, request.Progress);
            Assert.Equal(expected.X, changed.Contours[0].Nodes[segmentIndex + 1].Position.X, 6);
            Assert.Equal(expected.Y, changed.Contours[0].Nodes[segmentIndex + 1].Position.Y, 6);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShiftInsertsOnASingleNodeClosedCubicAwayFromItsMidpoint()
    {
        var document = Document();
        var node = new MaskNode { Position = new(400, 350), OutHandle = new(200, -250), InHandle = new(-150, -200) };
        var contour = new MaskContour { Nodes = [node] };
        var mask = new VectorClipMask { Contours = [contour], Transform = Assert.IsType<VectorClipMask>(document.Layers[0].Mask).Transform };
        var layer = document.Layers[0] with { Mask = mask };
        document = document with { Layers = [layer] };
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_VECTOR };
        canvas.SetScene(document, layer, new(0));
        CanvasMaskSegmentEventArgs? request = null;
        canvas.MaskSegmentInsertRequested += (_, e) => request = e;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var geometry = new AegiNext.Core.Projects.PathGeometry(node.Position,
                [new(new(node.Position.X + node.OutHandle.X, node.Position.Y + node.OutHandle.Y),
                    new(node.Position.X + node.InHandle.X, node.Position.Y + node.InHandle.Y), node.Position)]);
            var expected = SceneEvaluator.EvaluatePath(geometry, 0.25);
            var point = Project(canvas, document, mask, expected);
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
            Assert.NotNull(request);
            Assert.Equal(node.Id, request.NodeId);
            Assert.Equal(0.25, request.Progress, 7);
            var changed = ClipMaskGeometryOperations.SubdivideSegment(mask, request.ContourId, request.NodeId, request.Progress);
            Assert.Equal(2, changed.Contours[0].Nodes.Length);
            Assert.Equal(expected.X, changed.Contours[0].Nodes[1].Position.X, 6);
            Assert.Equal(expected.Y, changed.Contours[0].Nodes[1].Position.Y, 6);
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine { End = new(5), Text = "中文 ABC 123" };
        var mask = new VectorClipMask
        {
            Transform = new() { Position = new(17, 13), Scale = new(1.3, 0.9), Rotation = 25 },
            Placement = new() { Pivot = new(300, 250), Offset = new(300, 250) },
            Contours = [new() { Nodes = [new() { Position = new(200, 200), OutHandle = new(80, -90) }, new() { Position = new(600, 200), InHandle = new(-60, 100) }, new() { Position = new(400, 400) }] }]
        };
        return new() { Width = 1000, Height = 500, Subtitles = [line], Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Mask = mask }] };
    }

    private static Point Midpoint(EffectCanvasControl canvas, ProjectDocument document, VectorClipMask mask)
    {
        var nodes = mask.Contours[0].Nodes;
        var path = new AegiNext.Core.Projects.PathGeometry(nodes[0].Position,
            [new(new(nodes[0].Position.X + nodes[0].OutHandle.X, nodes[0].Position.Y + nodes[0].OutHandle.Y),
                new(nodes[1].Position.X + nodes[1].InHandle.X, nodes[1].Position.Y + nodes[1].InHandle.Y), nodes[1].Position)]);
        return Project(canvas, document, mask, SceneEvaluator.EvaluatePath(path, 0.5));
    }

    private static Point Project(EffectCanvasControl canvas, ProjectDocument document, ClipMask mask, ScenePoint position)
    {
        var projected = ClipMaskPlacementGeometry.CreateMatrix(mask, new(document.Width, document.Height)).Map(position);
        var board = canvas.ProjectRectangle;
        return new(board.X + projected.X * board.Width / document.Width, board.Y + projected.Y * board.Height / document.Height);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }
        return directory!.FullName;
    }
}
