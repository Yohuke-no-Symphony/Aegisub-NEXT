using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

public sealed partial class EffectCanvasControl
{
    private ClipMask? maskDraft;
    private ClipMask? maskOriginal;
    private ClipMaskEffectEditContext? maskEffectEdit;
    private MaskCanvasHandle? maskHandle;
    private bool maskDragging;
    private MaskCanvasHandle? maskDeletionHandle;
    private MaskCanvasSegmentHandle? maskInsertionHandle;
    private bool creatingRectangle;
    private bool ignoreMaskCaptureLoss;
    private ImmutableArray<MaskNode> openContour = [];
    private Guid? openContourId;
    private Guid? maskSelectedNodeId;
    private bool creatingNodeHandle;
    public event EventHandler<CanvasGestureStartingEventArgs>? MaskGestureStarting;
    public event EventHandler<CanvasMaskEditEventArgs>? MaskEdited;
    public event EventHandler<CanvasMaskEditEventArgs>? MaskNodeSelected;
    public event EventHandler<CanvasMaskNodeEventArgs>? MaskNodeDeleteRequested;
    public event EventHandler<CanvasMaskSegmentEventArgs>? MaskSegmentInsertRequested;
    public event EventHandler? MaskEditingExited;
    public event EventHandler? MaskGestureCancelled;
    internal Guid? MaskSelectedNodeId
    {
        get => maskSelectedNodeId;
        set
        {
            maskSelectedNodeId = value;
            InvalidateVisual();
        }
    }
    private bool IsMaskMode => EditMode is CanvasEditMode.MASK_RECTANGLE or CanvasEditMode.MASK_VECTOR or CanvasEditMode.MASK_DRAW_VECTOR;
    internal bool HasOpenMaskContour => !openContour.IsEmpty;

    private ClipMaskEffectEditContext CreateMaskEffectEditContext()
    {
        var layer = selected!;
        var subtitle = layer.SubtitleId is { } id ? document.Subtitles.Single(line => line.Id == id) : null;
        return ClipMaskEffectEditContext.Create(layer, subtitle, position - layer.Start + layer.AnimationOffset, effectPlans);
    }

    private bool IsMaskTopologyLocked => selected is not null && CreateMaskEffectEditContext().IsTopologyLocked;

    private bool CanEditMaskGesture(ClipMaskEffectEditContext effectEdit, MaskCanvasHandle? hit, bool inside)
    {
        var layer = selected!;
        bool CanEditTarget(AnimationTrackTarget target, int components = 0) => effectEdit.CanEdit(target, components) &&
            !layer.Tracks.Any(track => track.IsOrdered && track.Target == target);

        if (hit is { Corner: >= 0 })
        {
            var topLeft = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
            var bottomRight = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT);
            return hit.Corner switch
            {
                0 => CanEditTarget(topLeft),
                1 => CanEditTarget(topLeft, 2) && CanEditTarget(bottomRight, 1),
                2 => CanEditTarget(bottomRight),
                _ => CanEditTarget(topLeft, 1) && CanEditTarget(bottomRight, 2)
            };
        }
        if (hit is not null)
        {
            return CanEditTarget(hit.Target);
        }
        return inside ? CanEditTarget(new(AnimationProperty.MASK_POSITION)) :
            CanEditTarget(new(AnimationProperty.MASK_POSITION)) && CanEditTarget(new(AnimationProperty.MASK_SCALE)) &&
            CanEditTarget(new(AnimationProperty.MASK_ROTATION)) && CanEditTarget(new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT)) &&
            CanEditTarget(new(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT));
    }

    internal ProjectLayer PrepareMaskDraftLayer(ClipMask visibleDraft)
    {
        var effectEdit = maskEffectEdit ?? CreateMaskEffectEditContext();
        return effectEdit.TryGetNativePose(visibleDraft, out var nativePose)
            ? selected! with
            {
                Mask = nativePose,
                Tracks = selected!.Tracks.Where(track => !AnimationPropertyMetadata.IsMaskProperty(track.Property)).ToImmutableArray()
            }
            : selected!;
    }

    private ClipMask? EvaluatedMask()
    {
        if (maskDraft is not null || selected is null)
        {
            return maskDraft;
        }
        var subtitle = selected.SubtitleId is { } id ? document.Subtitles.Single(line => line.Id == id) : null;
        return SceneEvaluator.EvaluateMask(selected, position - selected.Start + selected.AnimationOffset, subtitle, effectPlans);
    }

    private Matrix MaskMatrix(ClipMask mask)
    {
        var projection = ClipMaskPlacementGeometry.CreateMatrix(mask, new(document.Width, document.Height));
        return new Matrix(projection.M11, projection.M21, projection.M31,
            projection.M12, projection.M22, projection.M32, projection.M13, projection.M23, projection.M33) * Fit();
    }

    private static Point MaskPoint(ScenePoint point) => new(point.X, point.Y);
    private static ScenePoint Offset(ScenePoint point, Vector delta) => new(point.X + delta.X, point.Y + delta.Y);
    private static ScenePoint Add(ScenePoint a, ScenePoint b) => new(a.X + b.X, a.Y + b.Y);

    internal IEnumerable<MaskCanvasNodeLabel> MaskNodeLabels()
    {
        if (EditMode is not (CanvasEditMode.MASK_VECTOR or CanvasEditMode.MASK_DRAW_VECTOR))
        {
            yield break;
        }
        var mask = EvaluatedMask() as VectorClipMask;
        if (mask is not null)
        {
            var matrix = MaskMatrix(mask);
            for (var contourIndex = 0; contourIndex < mask.Contours.Length; contourIndex++)
            {
                var contour = mask.Contours[contourIndex];
                for (var nodeIndex = 0; nodeIndex < contour.Nodes.Length; nodeIndex++)
                {
                    var node = contour.Nodes[nodeIndex];
                    yield return new(node.Id, FormattableString.Invariant($"{contourIndex + 1}:{nodeIndex + 1}"), MaskPoint(node.Position) * matrix);
                }
            }
        }
        var fit = mask is null ? Fit() : MaskMatrix(mask);
        for (var index = 0; index < openContour.Length; index++)
        {
            yield return new(openContour[index].Id, FormattableString.Invariant($"{(mask?.Contours.Length ?? 0) + 1}:{index + 1}"), MaskPoint(openContour[index].Position) * fit);
        }
    }

    private MaskCanvasSegmentHandle? HitMaskSegment(VectorClipMask mask, Point point, bool open)
    {
        var matrix = MaskMatrix(mask);
        MaskCanvasSegmentHandle? closest = null;
        var distance = 100d;
        foreach (var contour in mask.Contours)
        {
            for (var index = 0; index < contour.Nodes.Length - (open ? 1 : 0); index++)
            {
                var node = contour.Nodes[index];
                var next = contour.Nodes[(index + 1) % contour.Nodes.Length];
                var start = MaskPoint(node.Position) * matrix;
                var end = MaskPoint(next.Position) * matrix;
                var hit = CubicBezierHitTest.FindNearest(start, MaskPoint(Add(node.Position, node.OutHandle)) * matrix,
                    MaskPoint(Add(next.Position, next.InHandle)) * matrix, end, point, Math.Sqrt(distance));
                if (hit is { Progress: > 0 and < 1 } result && result.DistanceSquared <= distance &&
                    DistanceSquared(result.Position, start) > 0.25 && DistanceSquared(result.Position, end) > 0.25)
                {
                    distance = result.DistanceSquared;
                    closest = new(contour.Id, node.Id, result.Position, result.Progress);
                }
            }
        }
        return closest;
    }
    private MaskCanvasHandle[] MaskHandles(ClipMask mask)
    {
        var matrix = MaskMatrix(mask);
        if (mask is RectangleClipMask rectangle)
        {
            return new[]
            {
                new MaskCanvasHandle(new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT), MaskPoint(rectangle.TopLeft) * matrix, 0),
                new MaskCanvasHandle(new(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT), new Point(rectangle.BottomRight.X, rectangle.TopLeft.Y) * matrix, 1),
                new MaskCanvasHandle(new(AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT), MaskPoint(rectangle.BottomRight) * matrix, 2),
                new MaskCanvasHandle(new(AnimationProperty.MASK_RECTANGLE_TOP_LEFT), new Point(rectangle.TopLeft.X, rectangle.BottomRight.Y) * matrix, 3)
            };
        }
        if (mask is VectorClipMask vector)
        {
            return vector.Contours.SelectMany(contour => contour.Nodes).SelectMany(node =>
            {
                var handles = new List<MaskCanvasHandle>
                {
                    new(new(AnimationProperty.MASK_NODE_POSITION, node.Id), MaskPoint(node.Position) * matrix)
                };
                if (node.Id == maskSelectedNodeId)
                {
                    handles.Add(new(new(AnimationProperty.MASK_NODE_IN_HANDLE, node.Id), MaskPoint(Add(node.Position, node.InHandle)) * matrix));
                    handles.Add(new(new(AnimationProperty.MASK_NODE_OUT_HANDLE, node.Id), MaskPoint(Add(node.Position, node.OutHandle)) * matrix));
                }
                return handles;
            }).ToArray();
        }
        return [];
    }

    private StreamGeometry MaskGeometry(ClipMask mask) => MaskCanvasGeometry.Create(mask, MaskMatrix(mask));

    private void DrawClipMask(DrawingContext context)
    {
        var pen = new Pen(Brushes.Gold, 1.5);
        if (EvaluatedMask() is { } mask)
        {
            var geometry = MaskGeometry(mask);
            var excluded = mask.Inverted ? (Geometry)geometry :
                new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(ProjectRectangle), geometry);
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(112, 64, 64, 64)), null, excluded);
            context.DrawGeometry(null, pen, geometry);
            if (IsMaskMode)
            {
                foreach (var handle in MaskHandles(mask))
                {
                    context.DrawEllipse(handle.Target.Property == AnimationProperty.MASK_NODE_POSITION || handle.Corner >= 0 ? Brushes.Gold : Brushes.White,
                        pen, handle.Position, 4, 4);
                }
                if (mask is VectorClipMask vector && vector.Contours.SelectMany(contour => contour.Nodes).FirstOrDefault(node => node.Id == maskSelectedNodeId) is { } node)
                {
                    var matrix = MaskMatrix(mask);
                    context.DrawLine(pen, MaskPoint(Add(node.Position, node.InHandle)) * matrix, MaskPoint(node.Position) * matrix);
                    context.DrawLine(pen, MaskPoint(node.Position) * matrix, MaskPoint(Add(node.Position, node.OutHandle)) * matrix);
                }
            }
        }
        if (!openContour.IsEmpty)
        {
            var fit = EvaluatedMask() is VectorClipMask existing ? MaskMatrix(existing) : Fit();
            for (var index = 0; index < openContour.Length; index++)
            {
                var node = openContour[index];
                context.DrawEllipse(Brushes.Gold, pen, MaskPoint(node.Position) * fit, 4, 4);
                if (index > 0)
                {
                    var previous = openContour[index - 1];
                    var geometry = new StreamGeometry();
                    using (var stream = geometry.Open())
                    {
                        stream.BeginFigure(MaskPoint(previous.Position) * fit, false);
                        stream.CubicBezierTo(MaskPoint(Add(previous.Position, previous.OutHandle)) * fit,
                            MaskPoint(Add(node.Position, node.InHandle)) * fit, MaskPoint(node.Position) * fit);
                        stream.EndFigure(false);
                    }
                    context.DrawGeometry(null, pen, geometry);
                }
            }
        }
        foreach (var label in MaskNodeLabels())
        {
            if (!ProjectRectangle.Contains(label.Position))
            {
                continue;
            }
            using var layout = WorkbenchTextFormatting.CreateLayout(this, label.Text, 11, Brushes.Gold, lineHeight: 16);
            var width = layout.Width + 6;
            var height = layout.Height + 2;
            var rectangle = new Rect(
                Math.Clamp(label.Position.X + 8, ProjectRectangle.Left, Math.Max(ProjectRectangle.Left, ProjectRectangle.Right - width)),
                Math.Clamp(label.Position.Y - height - 4, ProjectRectangle.Top, Math.Max(ProjectRectangle.Top, ProjectRectangle.Bottom - height)), width, height);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(192, 16, 20, 26)), null, rectangle, 2, 2);
            layout.Draw(context, WorkbenchTextFormatting.CenteredOrigin(layout, rectangle.Deflate(new Thickness(3, 0))));
        }
        DrawMaskSnapGuides(context);
    }

    private bool StartMaskGesture()
    {
        var starting = new CanvasGestureStartingEventArgs();
        MaskGestureStarting?.Invoke(this, starting);
        if (starting.Cancel)
        {
            return false;
        }
        BeginMaskSnapping();
        return true;
    }

    private void MaskPointerPressed(PointerPressedEventArgs e)
    {
        if (selected is not { Kind: LayerKind.SUBTITLE, SubtitleId: not null } || !Fit().TryInvert(out var inverseFit))
        {
            return;
        }
        Focusable = true;
        Focus();
        var point = e.GetPosition(this);
        bezierCursorModifiers = e.KeyModifiers;
        UpdateBezierCursor();
        var existingMask = EvaluatedMask();
        var action = BezierEditingModifiers.GetAction(e.KeyModifiers);
        if (action == BezierEditAction.DELETE)
        {
            e.Handled = true;
            if (!openContour.IsEmpty || existingMask is not VectorClipMask || IsMaskTopologyLocked)
            {
                return;
            }
            var nodeHit = MaskHandles(existingMask).Where(handle => handle.Target.Property == AnimationProperty.MASK_NODE_POSITION)
                .OrderBy(handle => DistanceSquared(handle.Position, point)).FirstOrDefault(handle => DistanceSquared(handle.Position, point) <= 100);
            if (nodeHit is null || !StartMaskGesture())
            {
                return;
            }
            maskDeletionHandle = nodeHit;
            e.Pointer.Capture(this);
            capturedPointer = e.Pointer;
            return;
        }
        if (action == BezierEditAction.INSERT)
        {
            if (EditMode is CanvasEditMode.MASK_VECTOR or CanvasEditMode.MASK_DRAW_VECTOR)
            {
                e.Handled = true;
                var vector = existingMask as VectorClipMask;
                var insertingDraft = !openContour.IsEmpty;
                if (IsMaskTopologyLocked || (vector?.Contours.Sum(contour => (long)contour.Nodes.Length) ?? 0) + openContour.Length >= 10000 ||
                    insertingDraft && openContour.Length < 2)
                {
                    return;
                }
                var insertionMask = insertingDraft
                    ? (vector ?? new VectorClipMask()) with { Contours = [new() { Id = openContourId!.Value, Nodes = openContour }] }
                    : vector;
                if (insertionMask is null)
                {
                    return;
                }
                var segment = HitMaskSegment(insertionMask, point, insertingDraft);
                if (segment is null || !insertingDraft && !StartMaskGesture())
                {
                    return;
                }
                maskInsertionHandle = segment;
                e.Pointer.Capture(this);
                capturedPointer = e.Pointer;
                return;
            }
        }
        var creationMatrix = existingMask is VectorClipMask ? MaskMatrix(existingMask) : Fit();
        if (EditMode == CanvasEditMode.MASK_DRAW_VECTOR || EditMode == CanvasEditMode.MASK_VECTOR && selected.Mask is null)
        {
            if (IsMaskTopologyLocked)
            {
                return;
            }
            if (!openContour.IsEmpty && openContour.Length >= 2 && DistanceSquared(MaskPoint(openContour[0].Position) * creationMatrix, point) <= 100)
            {
                CloseMaskContour();
                e.Handled = true;
                return;
            }
            if (openContour.IsEmpty && !StartMaskGesture())
            {
                return;
            }
            var snapped = SnapMaskPoint(point);
            var engineering = creationMatrix.TryInvert(out var creationInverse) ? snapped * creationInverse : snapped * inverseFit;
            openContourId ??= Guid.NewGuid();
            openContour = openContour.Add(new() { Position = new(engineering.X, engineering.Y) });
            dragStart = point;
            creatingNodeHandle = true;
            e.Pointer.Capture(this);
            capturedPointer = e.Pointer;
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        var mask = EvaluatedMask();
        if (mask is null && EditMode != CanvasEditMode.MASK_RECTANGLE)
        {
            return;
        }
        var handles = mask is null ? [] : MaskHandles(mask);
        var hit = handles.OrderBy(handle => DistanceSquared(handle.Position, point)).FirstOrDefault(handle => DistanceSquared(handle.Position, point) <= 100);
        if (hit?.Target is { Property: AnimationProperty.MASK_NODE_POSITION, NodeId: { } handleNodeId } && (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            hit = hit with
            {
                Target = new((e.KeyModifiers & KeyModifiers.Shift) != 0 ? AnimationProperty.MASK_NODE_IN_HANDLE : AnimationProperty.MASK_NODE_OUT_HANDLE, handleNodeId)
            };
        }
        var inside = mask is not null && MaskGeometry(mask).FillContains(point);
        if (hit is null && !inside && (EditMode != CanvasEditMode.MASK_RECTANGLE || IsMaskTopologyLocked))
        {
            return;
        }
        var effectEdit = CreateMaskEffectEditContext();
        if (!CanEditMaskGesture(effectEdit, hit, inside))
        {
            return;
        }
        if (!StartMaskGesture())
        {
            return;
        }
        maskOriginal = mask;
        maskEffectEdit = effectEdit;
        maskHandle = hit;
        creatingRectangle = hit is null && !inside;
        maskDraft = mask;
        if (creatingRectangle)
        {
            maskRectangleStart = SnapMaskPoint(point) * inverseFit;
        }
        if (hit?.Target.NodeId is { } nodeId && mask is not null)
        {
            maskSelectedNodeId = nodeId;
            MaskNodeSelected?.Invoke(this, new(selected.Id, mask, nodeId));
        }
        dragStart = point;
        maskDragging = true;
        e.Pointer.Capture(this);
        capturedPointer = e.Pointer;
        e.Handled = true;
        InvalidateVisual();
    }

    private void MaskPointerMoved(PointerEventArgs e)
    {
        bezierCursorModifiers = e.KeyModifiers;
        UpdateBezierCursor();
        if ((!maskDragging && !creatingNodeHandle) || !Fit().TryInvert(out var inverseFit))
        {
            return;
        }
        var point = e.GetPosition(this);
        var previousDraft = maskDraft;
        var delta = point * inverseFit - dragStart * inverseFit;
        var draftMatrix = EvaluatedMask() is VectorClipMask existing ? MaskMatrix(existing) : Fit();
        var handleDelta = draftMatrix.TryInvert(out var inverseDraft) ? point * inverseDraft - dragStart * inverseDraft : delta;
        if (creatingNodeHandle && !openContour.IsEmpty)
        {
            var node = openContour[^1];
            openContour = openContour.SetItem(openContour.Length - 1, node with
            {
                InHandle = new(-handleDelta.X, -handleDelta.Y), OutHandle = new(handleDelta.X, handleDelta.Y)
            });
        }
        else if (creatingRectangle)
        {
            var first = maskRectangleStart;
            var current = SnapMaskPoint(point) * inverseFit;
            var topLeft = new ScenePoint(Math.Min(first.X, current.X), Math.Min(first.Y, current.Y));
            var bottomRight = new ScenePoint(Math.Max(first.X, current.X), Math.Max(first.Y, current.Y));
            maskDraft = topLeft.X == bottomRight.X || topLeft.Y == bottomRight.Y ? maskOriginal : ClipMaskPlacementGeometry.CreatePlacement(new RectangleClipMask
            {
                TopLeft = topLeft,
                BottomRight = bottomRight
            }, new(document.Width, document.Height));
        }
        else if (maskOriginal is { } original)
        {
            if (maskHandle is null)
            {
                var translated = original with { Transform = original.Transform with { Position = Offset(original.Transform.Position, delta) } };
                maskDraft = SnapMaskTranslation(translated, inverseFit);
            }
            else if (MaskMatrix(original).TryInvert(out var inverse))
            {
                maskSnapFeedback = default;
                if (maskHandle.Corner >= 0 || maskHandle.Target.Property == AnimationProperty.MASK_NODE_POSITION)
                {
                    var candidate = maskHandle.Position + (point - dragStart);
                    point += SnapMaskPoint(candidate) - candidate;
                }
                var localDelta = point * inverse - dragStart * inverse;
                if (original is RectangleClipMask rectangle)
                {
                    var left = rectangle.TopLeft.X;
                    var top = rectangle.TopLeft.Y;
                    var right = rectangle.BottomRight.X;
                    var bottom = rectangle.BottomRight.Y;
                    switch (maskHandle.Corner)
                    {
                        case 0: left += localDelta.X; top += localDelta.Y; break;
                        case 1: right += localDelta.X; top += localDelta.Y; break;
                        case 2: right += localDelta.X; bottom += localDelta.Y; break;
                        case 3: left += localDelta.X; bottom += localDelta.Y; break;
                    }
                    maskDraft = rectangle with
                    {
                        TopLeft = new(Math.Min(left, right), Math.Min(top, bottom)),
                        BottomRight = new(Math.Max(left, right), Math.Max(top, bottom))
                    };
                }
                else
                {
                    var target = maskHandle.Target;
                    var value = ClipMaskAnimation.GetBaseValue(original, target).Vector;
                    maskDraft = ClipMaskAnimation.SetBaseValue(original, target, Offset(value, localDelta));
                }
            }
        }
        if (maskDraft is { } visibleDraft && maskEffectEdit is { } effectEdit && !effectEdit.TryGetNativePose(visibleDraft, out _))
        {
            maskDraft = previousDraft;
        }
        sceneRevision++;
        InvalidateVisual();
    }

    private void MaskPointerReleased(PointerReleasedEventArgs e)
    {
        var pointer = capturedPointer;
        capturedPointer = null;
        ignoreMaskCaptureLoss = true;
        pointer?.Capture(null);
        ignoreMaskCaptureLoss = false;
        bezierCursorModifiers = e.KeyModifiers;
        UpdateBezierCursor();
        if (maskInsertionHandle is { } insertion)
        {
            maskInsertionHandle = null;
            var insertingDraft = insertion.ContourId == openContourId;
            if (!insertingDraft)
            {
                ResetMaskSnapping();
            }
            if (insertingDraft)
            {
                if (DistanceSquared(insertion.Position, e.GetPosition(this)) <= 100)
                {
                    var source = new VectorClipMask { Contours = [new() { Id = insertion.ContourId, Nodes = openContour }] };
                    openContour = ClipMaskGeometryOperations.SubdivideSegment(source, insertion.ContourId, insertion.NodeId, insertion.Progress).Contours[0].Nodes;
                }
            }
            else if (selected is { } layer && DistanceSquared(insertion.Position, e.GetPosition(this)) <= 100)
            {
                MaskSegmentInsertRequested?.Invoke(this, new(layer.Id, insertion.ContourId, insertion.NodeId, insertion.Progress));
            }
            else
            {
                MaskGestureCancelled?.Invoke(this, EventArgs.Empty);
            }
            InvalidateVisual();
            return;
        }
        if (maskDeletionHandle is { } deletion)
        {
            maskDeletionHandle = null;
            ResetMaskSnapping();
            if (selected is { } layer && deletion.Target.NodeId is { } deletedNodeId && DistanceSquared(deletion.Position, e.GetPosition(this)) <= 100)
            {
                MaskNodeDeleteRequested?.Invoke(this, new(layer.Id, deletedNodeId));
            }
            else
            {
                MaskGestureCancelled?.Invoke(this, EventArgs.Empty);
            }
            InvalidateVisual();
            return;
        }
        if (creatingNodeHandle)
        {
            creatingNodeHandle = false;
            maskSnapFeedback = default;
            InvalidateVisual();
            return;
        }
        var mask = maskDraft;
        var layerId = selected?.Id;
        var nodeId = maskHandle?.Target.NodeId;
        var invalidCreation = creatingRectangle && (ReferenceEquals(mask, maskOriginal) || mask is not RectangleClipMask rectangle ||
            rectangle.TopLeft.X == rectangle.BottomRight.X || rectangle.TopLeft.Y == rectangle.BottomRight.Y);
        creatingRectangle = false;
        maskDragging = false;
        maskOriginal = null;
        maskEffectEdit = null;
        maskDraft = null;
        maskHandle = null;
        ResetMaskSnapping();
        if (invalidCreation)
        {
            MaskGestureCancelled?.Invoke(this, EventArgs.Empty);
        }
        else if (mask is not null && layerId is { } id)
        {
            MaskEdited?.Invoke(this, new(id, mask, nodeId));
        }
        InvalidateVisual();
    }

    private void CloseMaskContour()
    {
        if (selected is null || openContour.IsEmpty || IsMaskTopologyLocked)
        {
            return;
        }
        var existing = EvaluatedMask() as VectorClipMask;
        var contour = new MaskContour { Id = openContourId!.Value, Nodes = openContour };
        var all = (existing?.Contours ?? []).Add(contour);
        var vector = existing is null
            ? ClipMaskPlacementGeometry.CreatePlacement(new VectorClipMask { Contours = all }, new(document.Width, document.Height))
            : existing with { Contours = all };
        var nodeId = openContour[0].Id;
        openContour = [];
        openContourId = null;
        creatingNodeHandle = false;
        ResetMaskSnapping();
        MaskEdited?.Invoke(this, new(selected.Id, vector, nodeId));
        editMode = CanvasEditMode.MASK_VECTOR;
        InvalidateVisual();
    }

    private void CancelMaskGesture()
    {
        var active = HasActiveMaskGesture;
        maskDeletionHandle = null;
        maskInsertionHandle = null;
        creatingRectangle = false;
        maskDragging = false;
        maskDraft = null;
        maskOriginal = null;
        maskEffectEdit = null;
        maskHandle = null;
        creatingNodeHandle = false;
        openContour = [];
        openContourId = null;
        ResetMaskSnapping();
        if (active)
        {
            MaskGestureCancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelDrag();
        DetachBezierCursorHost();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (HasActiveDrag && e.Key == Key.Escape)
        {
            CancelDrag();
            e.Handled = true;
        }
        else if (EditMode == CanvasEditMode.CREATE_PATH && e.Key == Key.Enter)
        {
            CompleteShapePath();
            e.Handled = true;
        }
        else if (IsMaskMode && e.Key == Key.Enter && !openContour.IsEmpty)
        {
            CloseMaskContour();
            e.Handled = true;
        }
    }
}
