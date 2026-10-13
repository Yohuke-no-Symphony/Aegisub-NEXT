using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PathGeometry = AegiNext.Core.Projects.PathGeometry;

namespace AegiNext.Desktop.Controls;

public sealed partial class EffectCanvasControl
{
    private CanvasTransformGesture? transformGesture;
    private bool graphicGestureActive;
    private ProjectLayerGeometry? graphicGestureGeometry;
    private Matrix graphicGestureFit;
    private Point graphicGestureStart;
    private int graphicHandle = -1;
    private bool graphicTopologyGesture;
    private Point graphicPressPosition;
    private int shapeSegmentIndex;
    private bool creationActive;
    private bool creationPointerDown;
    private bool closeCreationOnRelease;
    private bool finishCreationOnRelease;
    private bool ignoreGraphicCaptureLoss;
    private Point creationStart;
    private Rect creationBounds;
    private readonly List<GraphicPathNode> creationNodes = [];

    public event EventHandler<CanvasLayerSelectionEventArgs>? LayerSelectionRequested;
    public event EventHandler<CanvasGestureStartingEventArgs>? GraphicGestureStarting;
    public event EventHandler? GraphicGestureCancelled;
    public event EventHandler<CanvasGraphicEditEventArgs>? GraphicEdited;
    public event EventHandler<CanvasGestureStartingEventArgs>? ShapeCreationStarting;
    public event EventHandler? ShapeCreationCancelled;
    public event EventHandler<CanvasShapeCreatedEventArgs>? ShapeCreated;
    public event EventHandler? GraphicEditingExited;
    public event EventHandler? ImagePasteRequested;
    internal event EventHandler? DrawingCommandStateChanged;

    internal bool CanExecuteDrawingCommand(CanvasDrawingCommandEventArgs request)
    {
        if (disposed || !ReferenceEquals(document, request.Source) || selected?.Id != request.LayerId || EditMode != request.Mode)
        {
            return false;
        }
        return request.Command switch
        {
            CanvasDrawingCommand.FINISH_PATH => EditMode == CanvasEditMode.CREATE_PATH && creationActive &&
                !creationPointerDown && creationNodes.Count >= 2,
            CanvasDrawingCommand.STRAIGHT_SEGMENT or CanvasDrawingCommand.CURVE_SEGMENT or CanvasDrawingCommand.TOGGLE_CLOSED_PATH =>
                EditMode == CanvasEditMode.SHAPE_PATH && selected?.Shape?.Path is not null && !HasActiveDrag,
            _ => false
        };
    }

    internal bool ExecuteDrawingCommand(CanvasDrawingCommandEventArgs request)
    {
        if (!CanExecuteDrawingCommand(request))
        {
            return false;
        }
        switch (request.Command)
        {
            case CanvasDrawingCommand.STRAIGHT_SEGMENT:
                SetSelectedShapeSegmentCurve(false);
                break;
            case CanvasDrawingCommand.CURVE_SEGMENT:
                SetSelectedShapeSegmentCurve(true);
                break;
            case CanvasDrawingCommand.TOGGLE_CLOSED_PATH:
                ToggleShapePathClosed();
                break;
            case CanvasDrawingCommand.FINISH_PATH:
                CompleteShapePath();
                break;
        }
        return true;
    }

    private ShapeKind creationShapeKind = ShapeKind.RECTANGLE;
    internal ShapeKind CreationShapeKind
    {
        get => creationShapeKind;
        set
        {
            if (creationShapeKind != value)
            {
                CancelDrag();
                creationShapeKind = value;
            }
        }
    }
    internal bool IsGraphicMode => EditMode is CanvasEditMode.CROP or CanvasEditMode.SHAPE_PATH;
    internal bool IsCreationMode => EditMode is CanvasEditMode.CREATE_SHAPE or CanvasEditMode.CREATE_PATH;

    internal ScenePoint? ProjectPoint(Point point)
    {
        if (!ProjectRectangle.Contains(point) || !Fit().TryInvert(out var inverse))
        {
            return null;
        }
        var result = point * inverse;
        return new(result.X, result.Y);
    }

    private bool GraphicPointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        if (IsCreationMode)
        {
            CreationPointerPressed(e, point);
            return true;
        }
        if (IsGraphicMode)
        {
            e.Handled = true;
            if (selected is null || !RefreshGeometry(EditorDocument()))
            {
                return true;
            }
            if (EditMode == CanvasEditMode.CROP && selected.Image is not null)
            {
                BeginCropGesture(e, point);
            }
            else if (EditMode == CanvasEditMode.SHAPE_PATH && selected.Shape?.Kind == ShapeKind.PATH)
            {
                BeginShapePathGesture(e, point);
            }
            return true;
        }
        if (EditMode != CanvasEditMode.POSITION)
        {
            return false;
        }
        if (selected is not null && RefreshGeometry(EditorDocument()))
        {
            var handles = TransformHandles();
            var index = Array.FindIndex(handles, value => DistanceSquared(value, point) <= 64);
            if (index >= 0 && BeginDrag(point))
            {
                RefreshGeometry(EditorDocument());
                var evaluated = SceneEvaluator.Evaluate(EditorDocument(), position).FirstOrDefault(item => item.Source.Id == selected!.Id);
                if (evaluated is not null && selectedGeometry is not null)
                {
                    transformGesture = new(selected!, evaluated.Transform, selectedGeometry, layerMatrix * Fit(), point, index)
                    {
                        WorldToScreen = Fit()
                    };
                    capturedPointer = e.Pointer;
                    e.Pointer.Capture(this);
                    Focusable = true;
                    Focus();
                    e.Handled = true;
                    return true;
                }
                CancelDrag();
            }
        }
        if (ProjectPoint(point) is not { } project)
        {
            return false;
        }
        Guid? hit;
        try
        {
            renderer ??= new(new DirectoryProjectAssetResolver(directory));
            hit = renderer.HitTestLayer(EditorDocument(), position, new((float)project.X, (float)project.Y));
        }
        catch (Exception error) when (IsRenderingFailure(error))
        {
            ReportRenderingFailure(error);
            return true;
        }
        if (hit is { } id && selected?.Id != id)
        {
            LayerSelectionRequested?.Invoke(this, new(id));
            e.Handled = true;
            return true;
        }
        return false;
    }

    private bool GraphicPointerMoved(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        if (creationPointerDown)
        {
            if (ProjectPoint(point) is { } world)
            {
                var current = new Point(world.X, world.Y);
                if (EditMode == CanvasEditMode.CREATE_SHAPE)
                {
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        var delta = current - creationStart;
                        var side = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y));
                        current = creationStart + new Vector(Math.CopySign(side, delta.X), Math.CopySign(side, delta.Y));
                    }
                    creationBounds = new(Math.Min(creationStart.X, current.X), Math.Min(creationStart.Y, current.Y),
                        Math.Abs(current.X - creationStart.X), Math.Abs(current.Y - creationStart.Y));
                }
                else if (creationNodes.Count > 0 && !closeCreationOnRelease)
                {
                    var node = creationNodes[^1];
                    creationNodes[^1] = node with { Outgoing = current, Incoming = node.Position - (current - node.Position) };
                }
                InvalidateVisual();
            }
            e.Handled = true;
            return true;
        }
        if (transformGesture is { } transform)
        {
            draft = GraphicCanvasGeometry.TransformLayer(transform, point, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            sceneRevision++;
            InvalidateVisual();
            e.Handled = true;
            return true;
        }
        if (!graphicGestureActive || selected is null || graphicGestureGeometry is not { } frozenGeometry ||
            !TryGraphicUnmap(frozenGeometry, graphicGestureFit, point, out var currentLocal))
        {
            return IsCreationMode;
        }
        if (EditMode == CanvasEditMode.CROP && selected.Image is { } image)
        {
            draft = selected with { Image = image with { Crop = GraphicCanvasGeometry.Crop(image.Crop,
                graphicGestureStart, new(currentLocal.X / image.Width, currentLocal.Y / image.Height), graphicHandle) } };
        }
        else if (EditMode == CanvasEditMode.SHAPE_PATH && selected.Shape is { Path: { } path } shape && graphicHandle >= 0)
        {
            draft = selected with { Shape = shape with { Path = GraphicCanvasGeometry.MovePathPoint(path, graphicHandle, currentLocal - graphicGestureStart) } };
        }
        sceneRevision++;
        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private bool GraphicPointerReleased(PointerReleasedEventArgs e)
    {
        if (creationPointerDown)
        {
            e.Handled = true;
            if (EditMode == CanvasEditMode.CREATE_SHAPE)
            {
                var bounds = creationBounds;
                var kind = CreationShapeKind;
                if (bounds.Width < 1 || bounds.Height < 1)
                {
                    CancelDrag();
                    return true;
                }
                CancelDrag(false);
                ShapeCreated?.Invoke(this, new(kind, new(bounds.X, bounds.Y), bounds.Width, bounds.Height));
            }
            else
            {
                var close = closeCreationOnRelease;
                var finish = finishCreationOnRelease;
                creationPointerDown = false;
                ReleaseCreationPointer();
                DrawingCommandStateChanged?.Invoke(this, EventArgs.Empty);
                if (close || finish)
                {
                    CompleteShapePath(close);
                }
            }
            return true;
        }
        if (!graphicGestureActive)
        {
            return false;
        }
        if (graphicTopologyGesture && DistanceSquared(graphicPressPosition, e.GetPosition(this)) > 100)
        {
            CancelDrag();
            e.Handled = true;
            return true;
        }
        var result = draft;
        CancelDrag(false);
        if (result is not null)
        {
            GraphicEdited?.Invoke(this, new(result.Id, result.Image, result.Shape));
        }
        e.Handled = true;
        return true;
    }

    private void BeginCropGesture(PointerPressedEventArgs e, Point point)
    {
        var image = selected!.Image!;
        var geometry = selectedGeometry!;
        var fit = Fit();
        if (!TryGraphicUnmap(geometry, fit, point, out var local))
        {
            return;
        }
        var crop = image.Crop;
        var corners = CropCorners(image).Select(corner => GraphicMap(geometry, fit, corner)).ToArray();
        var index = Array.FindIndex(corners, value => DistanceSquared(value, point) <= 64);
        var normalized = new Point(local.X / image.Width, local.Y / image.Height);
        if (index < 0 && new Rect(crop.X, crop.Y, crop.Width, crop.Height).Contains(normalized))
        {
            index = 4;
        }
        if (index < 0 && (normalized.X is < 0 or > 1 || normalized.Y is < 0 or > 1))
        {
            return;
        }
        if (BeginGraphicGesture(e, geometry, fit))
        {
            graphicGestureStart = normalized;
            graphicHandle = index;
        }
    }

    private bool BeginGraphicGesture(PointerPressedEventArgs e, ProjectLayerGeometry geometry, Matrix fit)
    {
        CancelDrag();
        var starting = new CanvasGestureStartingEventArgs();
        GraphicGestureStarting?.Invoke(this, starting);
        if (starting.Cancel || selected is null)
        {
            return false;
        }
        graphicGestureActive = true;
        DrawingCommandStateChanged?.Invoke(this, EventArgs.Empty);
        graphicGestureGeometry = geometry;
        graphicGestureFit = fit;
        draft = selected;
        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        Focusable = true;
        Focus();
        return true;
    }

    private void BeginShapePathGesture(PointerPressedEventArgs e, Point point)
    {
        var path = selected!.Shape!.Path!;
        var geometry = selectedGeometry!;
        var fit = Fit();
        if (!TryGraphicUnmap(geometry, fit, point, out var local))
        {
            return;
        }
        var points = Points(path);
        var pointIndex = -1;
        for (var index = 0; index < points.Length; index += 3)
        {
            if (DistanceSquared(GraphicMap(geometry, fit, points[index]), point) <= 64)
            {
                pointIndex = index;
                break;
            }
        }
        var anchorIndex = pointIndex;
        if (pointIndex < 0)
        {
            pointIndex = Array.FindIndex(points, value => DistanceSquared(GraphicMap(geometry, fit, value), point) <= 64);
        }
        var action = BezierEditingModifiers.GetAction(e.KeyModifiers);
        var segmentIndex = -1;
        var progress = 0d;
        var nearest = 100d;
        for (var index = 0; index < path.Segments.Length; index++)
        {
            var segment = path.Segments[index];
            var start = index == 0 ? path.Start : path.Segments[index - 1].End;
            var hit = ProjectedBezierGeometry.FindNearest(start, segment, geometry.Projection, fit, point, Math.Sqrt(nearest));
            if (hit is { } result && result.Progress is > 0 and < 1)
            {
                segmentIndex = index;
                progress = result.Progress;
                nearest = result.DistanceSquared;
            }
        }
        if (pointIndex < 0 && segmentIndex < 0)
        {
            return;
        }
        shapeSegmentIndex = Math.Max(0, segmentIndex >= 0 ? segmentIndex : (pointIndex - 1) / 3);
        if (action == BezierEditAction.DELETE)
        {
            pointIndex = anchorIndex;
            if (pointIndex < 0 || path.Segments.Length <= (path.Closed ? 2 : 1))
            {
                return;
            }
        }
        if (action == BezierEditAction.INSERT && (segmentIndex < 0 || path.Segments.Length >= MAX_PATH_SEGMENTS))
        {
            return;
        }
        if (!BeginGraphicGesture(e, geometry, fit))
        {
            return;
        }
        graphicGestureStart = local;
        graphicTopologyGesture = action != BezierEditAction.NONE;
        graphicPressPosition = point;
        graphicHandle = action == BezierEditAction.NONE ? pointIndex : -1;
        var edited = action switch
        {
            BezierEditAction.INSERT => PathOperations.SplitSegment(path, segmentIndex, progress),
            BezierEditAction.DELETE => RemoveShapeNode(path, pointIndex / 3),
            _ => path
        };
        draft = selected! with { Shape = selected!.Shape! with { Path = edited } };
        InvalidateVisual();
    }

    private static PathGeometry RemoveShapeNode(PathGeometry path, int index)
    {
        var explicitClosure = path.Closed && path.Segments[^1].End == path.Start;
        if (explicitClosure && index == path.Segments.Length)
        {
            index = 0;
        }
        var result = PathOperations.RemovePoint(path, index);
        if (explicitClosure && index == 0)
        {
            var last = result.Segments[^1];
            result = result with { Segments = result.Segments.SetItem(result.Segments.Length - 1,
                last with { End = result.Start }) };
        }
        return result;
    }

    private void CreationPointerPressed(PointerPressedEventArgs e, Point point)
    {
        e.Handled = true;
        if (ProjectPoint(point) is not { } value)
        {
            return;
        }
        var world = new Point(value.X, value.Y);
        if (!creationActive)
        {
            CancelDrag();
            var starting = new CanvasGestureStartingEventArgs();
            ShapeCreationStarting?.Invoke(this, starting);
            if (starting.Cancel)
            {
                return;
            }
            creationActive = true;
            creationStart = world;
            creationBounds = new(world, world);
        }
        if (EditMode == CanvasEditMode.CREATE_PATH)
        {
            closeCreationOnRelease = creationNodes.Count >= 3 && DistanceSquared(creationNodes[0].Position * Fit(), point) <= 100;
            finishCreationOnRelease = e.ClickCount > 1 && creationNodes.Count >= 2;
            if (!closeCreationOnRelease && !finishCreationOnRelease && creationNodes.Count < MAX_PATH_SEGMENTS)
            {
                creationNodes.Add(new(world, world, world));
            }
        }
        creationPointerDown = true;
        DrawingCommandStateChanged?.Invoke(this, EventArgs.Empty);
        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        Focusable = true;
        Focus();
        InvalidateVisual();
    }

    internal void CompleteShapePath(bool closed = false)
    {
        if (EditMode != CanvasEditMode.CREATE_PATH || !creationActive || creationPointerDown || creationNodes.Count < (closed ? 3 : 2))
        {
            return;
        }
        var points = creationNodes.SelectMany(node => new[] { node.Position, node.Incoming, node.Outgoing }).ToArray();
        var origin = new Point(points.Min(point => point.X), points.Min(point => point.Y));
        var width = Math.Max(1, points.Max(point => point.X) - origin.X);
        var height = Math.Max(1, points.Max(point => point.Y) - origin.Y);
        var path = GraphicCanvasGeometry.Path(creationNodes, closed, origin);
        CancelDrag(false);
        ShapeCreated?.Invoke(this, new(ShapeKind.PATH, new(origin.X, origin.Y), width, height, path));
    }

    internal void SetSelectedShapeSegmentCurve(bool curved)
    {
        if (selected?.Shape?.Path is not { } path || EditMode != CanvasEditMode.SHAPE_PATH)
        {
            return;
        }
        CommitShapePath(GraphicCanvasGeometry.SetSegmentCurve(path, Math.Clamp(shapeSegmentIndex, 0, path.Segments.Length - 1), curved));
    }

    internal void ToggleShapePathClosed()
    {
        if (selected?.Shape?.Path is not { } path || EditMode != CanvasEditMode.SHAPE_PATH)
        {
            return;
        }
        CommitShapePath(GraphicCanvasGeometry.ToggleClosed(path));
    }

    private void CommitShapePath(PathGeometry path)
    {
        CancelDrag();
        var starting = new CanvasGestureStartingEventArgs();
        GraphicGestureStarting?.Invoke(this, starting);
        if (!starting.Cancel && selected is { Shape: { } shape } layer)
        {
            GraphicEdited?.Invoke(this, new(layer.Id, layer.Image, shape with { Path = path }));
        }
    }

    private void ReleaseCreationPointer()
    {
        var pointer = capturedPointer;
        capturedPointer = null;
        ignoreGraphicCaptureLoss = true;
        try
        {
            pointer?.Capture(null);
        }
        finally
        {
            ignoreGraphicCaptureLoss = false;
        }
    }

    private void CancelGraphicGesture(bool notify)
    {
        var wasGraphic = graphicGestureActive;
        var wasCreation = creationActive;
        transformGesture = null;
        graphicGestureActive = false;
        graphicGestureGeometry = null;
        graphicHandle = -1;
        graphicTopologyGesture = false;
        creationActive = false;
        creationPointerDown = false;
        closeCreationOnRelease = false;
        finishCreationOnRelease = false;
        creationNodes.Clear();
        creationBounds = default;
        if (notify && wasGraphic)
        {
            GraphicGestureCancelled?.Invoke(this, EventArgs.Empty);
        }
        if (notify && wasCreation)
        {
            ShapeCreationCancelled?.Invoke(this, EventArgs.Empty);
        }
        DrawingCommandStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private Point[] TransformHandles()
    {
        if (selectedGeometry is not { } geometry)
        {
            return [];
        }
        var bounds = geometry.LocalBounds;
        ScenePoint[] localCorners = [new(bounds.Left, bounds.Top), new(bounds.Right, bounds.Top),
            new(bounds.Right, bounds.Bottom), new(bounds.Left, bounds.Bottom)];
        var fit = Fit();
        var result = localCorners.Select(point => GraphicMap(geometry, fit, point)).ToList();
        var midpoint = GraphicMap(geometry, fit, new(bounds.MidX, bounds.Top));
        var center = GraphicMap(geometry, fit, new(bounds.MidX, bounds.MidY));
        if (!double.IsFinite(midpoint.X) || !double.IsFinite(midpoint.Y) || !double.IsFinite(center.X) || !double.IsFinite(center.Y))
        {
            result.Add(new(double.NaN, double.NaN));
            return result.ToArray();
        }
        var direction = new Vector(midpoint.X - center.X, midpoint.Y - center.Y);
        direction = direction.Length > 0.001 ? direction / direction.Length : new(0, -1);
        result.Add(midpoint + direction * 28);
        return result.ToArray();
    }

    private void DrawTransformHandles(DrawingContext context)
    {
        var handles = TransformHandles();
        if (handles.Length != 5)
        {
            return;
        }
        var pen = new Pen(Brushes.DeepSkyBlue, 1.5);
        for (var index = 0; index < 4; index++)
        {
            if (double.IsFinite(handles[index].X) && double.IsFinite(handles[index].Y))
            {
                context.DrawRectangle(Brushes.White, pen, new Rect(handles[index] - new Vector(3.5, 3.5), new Size(7, 7)));
            }
        }
        if (double.IsFinite(handles[4].X) && double.IsFinite(handles[4].Y))
        {
            var bounds = selectedGeometry!.LocalBounds;
            var top = GraphicMap(selectedGeometry, Fit(), new(bounds.MidX, bounds.Top));
            context.DrawLine(pen, top, handles[4]);
            context.DrawEllipse(Brushes.White, pen, handles[4], 4, 4);
        }
    }

    private bool DrawGraphicOverlay(DrawingContext context)
    {
        var pen = new Pen(Brushes.DeepSkyBlue, 1.5);
        if (IsCreationMode)
        {
            if (EditMode == CanvasEditMode.CREATE_SHAPE && creationActive)
            {
                var fit = Fit();
                context.DrawRectangle(null, pen, new Rect(creationBounds.TopLeft * fit, creationBounds.BottomRight * fit));
            }
            if (creationNodes.Count > 0)
            {
                if (creationNodes.Count > 1)
                {
                    DrawShapePath(context, GraphicCanvasGeometry.Path(creationNodes, false, default), ProjectionMatrix3x3.Identity, Fit());
                }
                foreach (var node in creationNodes)
                {
                    context.DrawEllipse(Brushes.DeepSkyBlue, pen, node.Position * Fit(), 4, 4);
                }
            }
            return true;
        }
        if (!IsGraphicMode || selected is null || !RefreshGeometry(EditorDocument()))
        {
            return IsGraphicMode;
        }
        var layer = draft ?? selected;
        if (EditMode == CanvasEditMode.CROP && layer.Image is { } image)
        {
            var local = CropCorners(image);
            var fit = Fit();
            var points = local.Select(point => GraphicMap(selectedGeometry!, fit, point)).ToArray();
            for (var index = 0; index < 4; index++)
            {
                if (ProjectedBezierGeometry.TryProjectLine(local[index], local[(index + 1) % 4], selectedGeometry!.Projection,
                        fit, out var first, out var last))
                {
                    context.DrawLine(pen, first, last);
                }
                if (double.IsFinite(points[index].X) && double.IsFinite(points[index].Y))
                {
                    context.DrawRectangle(Brushes.White, pen, new Rect(points[index] - new Vector(4, 4), new Size(8, 8)));
                }
            }
        }
        else if (layer.Shape?.Path is { } path)
        {
            DrawShapePath(context, path, selectedGeometry!.Projection, Fit());
        }
        return true;
    }

    private static ScenePoint[] CropCorners(LayerImage image)
    {
        var crop = image.Crop;
        var left = crop.X * image.Width;
        var right = (crop.X + crop.Width) * image.Width;
        var top = crop.Y * image.Height;
        var bottom = (crop.Y + crop.Height) * image.Height;
        return [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];
    }

    private void DrawShapePath(DrawingContext context, PathGeometry path, ProjectionMatrix3x3 projection, Matrix fit)
    {
        var pen = new Pen(Brushes.DeepSkyBlue, 1.5);
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            var curveStart = path.Start;
            var drawingFigure = false;
            var allVisible = true;
            foreach (var segment in path.Segments)
            {
                var samples = ProjectedBezierGeometry.Sample(curveStart, segment, projection, fit,
                    new Rect(0, 0, Bounds.Width, Bounds.Height).Inflate(10));
                foreach (var sample in samples)
                {
                    if (sample.Position is not { } point)
                    {
                        allVisible = false;
                        if (drawingFigure)
                        {
                            drawing.EndFigure(false);
                            drawingFigure = false;
                        }
                    }
                    else if (!drawingFigure)
                    {
                        drawing.BeginFigure(point, false);
                        drawingFigure = true;
                    }
                    else
                    {
                        drawing.LineTo(point);
                    }
                }
                curveStart = segment.End;
            }
            if (drawingFigure)
            {
                drawing.EndFigure(path.Closed && allVisible);
            }
            if (path.Closed && !allVisible && curveStart != path.Start &&
                ProjectedBezierGeometry.TryProjectLine(curveStart, path.Start, projection, fit, out var first, out var last))
            {
                drawing.BeginFigure(first, false);
                drawing.LineTo(last);
                drawing.EndFigure(false);
            }
        }
        context.DrawGeometry(null, pen, geometry);
        var previous = path.Start;
        foreach (var segment in path.Segments)
        {
            if (ProjectedBezierGeometry.TryProjectLine(previous, segment.Control1, projection, fit, out var first, out var last))
            {
                context.DrawLine(new Pen(Brushes.Gray), first, last);
            }
            if (ProjectedBezierGeometry.TryProjectLine(segment.Control2, segment.End, projection, fit, out first, out last))
            {
                context.DrawLine(new Pen(Brushes.Gray), first, last);
            }
            previous = segment.End;
        }
        var points = Points(path);
        for (var index = 0; index < points.Length; index++)
        {
            if (projection.TryMap(points[index], out var world, SceneProjection.NEAR_PLANE_RATIO))
            {
                var point = new Point(world.X, world.Y) * fit;
                if (double.IsFinite(point.X) && double.IsFinite(point.Y))
                {
                    context.DrawEllipse(index % 3 == 0 ? Brushes.DeepSkyBlue : Brushes.White, pen, point, 4, 4);
                }
            }
        }
    }

    private static Point GraphicMap(ProjectLayerGeometry geometry, Matrix fit, ScenePoint local)
    {
        return geometry.TryMapLocalPoint(new((float)local.X, (float)local.Y), out var world)
            ? new Point(world.X, world.Y) * fit : new(double.NaN, double.NaN);
    }

    private static bool TryGraphicUnmap(ProjectLayerGeometry geometry, Matrix fit, Point point, out Point local)
    {
        local = default;
        if (!fit.TryInvert(out var inverse))
        {
            return false;
        }
        var world = point * inverse;
        if (!geometry.TryUnmapWorldPoint(new((float)world.X, (float)world.Y), out var source))
        {
            return false;
        }
        local = new(source.X, source.Y);
        return true;
    }
}
