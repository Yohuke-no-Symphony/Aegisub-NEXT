using System.Collections.Immutable;
using System.ComponentModel;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class ClipMaskEditingCoordinator(WorkbenchSession session)
{
    private ProjectDocument? draftSource;
    private AnimationEditTarget? draftTarget;
    private ProjectDocument? gestureSource;
    private ProjectLayer? gestureLayer;
    private ClipMask? gestureMask;
    private AnimationEditTarget? gestureTarget;
    private ProjectDocument? lastValidPreview;
    private ProjectDocument? lastValidSource;
    private bool loading;
    private string activePivotPanel = "masks";
    private readonly Dictionary<string, string> pivotEditingPanels = new(StringComparer.Ordinal);
    private Guid? fieldLayerId;
    private Guid? fieldNodeId;
    internal MaskNumericField[] Fields { get; private set; } = [];
    internal bool CanEdit => session.SelectedLayer is { Kind: LayerKind.SUBTITLE, SubtitleId: not null };
    internal bool CanCreate => CanEdit && session.SelectedLayer?.Mask is null;
    internal bool IsEditing => session.SceneEditing.Mode is CanvasEditMode.MASK_RECTANGLE or CanvasEditMode.MASK_VECTOR or CanvasEditMode.MASK_DRAW_VECTOR;
    internal bool IsTopologyLocked => session.SelectedLayer is { } layer && ClipMaskAnimation.IsTopologyLocked(layer);
    internal bool CanDeleteSelectedNode => !IsTopologyLocked && session.SelectedLayer?.Mask is VectorClipMask vector &&
        vector.Contours.SelectMany(contour => contour.Nodes).Any(node => node.Id == session.SceneEditing.MaskNodeId);
    internal bool CanDeleteSelectedContour => !IsTopologyLocked && session.SelectedLayer?.Mask is VectorClipMask vector &&
        vector.Contours.Any(contour => contour.Id == session.SceneEditing.MaskContourId);
    internal bool CanSubdivideSelectedNode => CanDeleteSelectedNode && session.SelectedLayer?.Mask is VectorClipMask vector &&
        vector.Contours.Sum(contour => (long)contour.Nodes.Length) < 10000;

    internal AnimationTrackTarget ResolveAnimationTarget(AnimationProperty property)
    {
        if (!AnimationPropertyMetadata.IsNodeProperty(property) || session.SelectedLayer?.Mask is not VectorClipMask vector)
        {
            return new(property);
        }
        var selectedNode = vector.Contours.SelectMany(contour => contour.Nodes)
            .FirstOrDefault(node => node.Id == session.SceneEditing.MaskNodeId);
        var contour = vector.Contours.FirstOrDefault(item => item.Id == session.SceneEditing.MaskContourId) ?? vector.Contours[0];
        return new(property, selectedNode?.Id ?? contour.Nodes[0].Id);
    }

    internal void Refresh(bool force = false)
    {
        var layer = session.SelectedLayer;
        if (gestureLayer is not null && (!ReferenceEquals(gestureSource, session.DocumentSnapshot) || layer?.Id != gestureLayer.Id))
        {
            CancelGesture();
        }
        if (session.SceneEditing.MaskNodeId is { } selectedNode && (layer?.Mask is not VectorClipMask nodeMask ||
            !nodeMask.Contours.SelectMany(contour => contour.Nodes).Any(node => node.Id == selectedNode)))
        {
            session.SceneEditing.MaskNodeId = null;
        }
        var nextTarget = session.SceneEditing.Target;
        if (AnimationPropertyMetadata.IsMaskProperty(nextTarget.Property))
        {
            var identity = nextTarget;
            if (layer?.Mask is null)
            {
                nextTarget = new(AnimationProperty.OPACITY);
            }
            else if (AnimationPropertyMetadata.IsNodeProperty(identity.Property))
            {
                nextTarget = session.SceneEditing.MaskNodeId is { } node
                    ? new(identity.Property, node) : new(AnimationProperty.MASK_POSITION);
            }
            else if (layer.Mask is not RectangleClipMask && identity.Property is (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT))
            {
                nextTarget = new(AnimationProperty.MASK_POSITION);
            }
        }
        if (nextTarget != session.SceneEditing.Target)
        {
            using var updateLease = session.BeginWorkbenchUpdate();
            try
            {
                session.ViewModel.Effects.Target = nextTarget;
            }
            finally
            {
                updateLease.Dispose();
            }
        }
        session.ViewModel.Timeline.SelectedMaskNodeId = session.SceneEditing.MaskNodeId;
        if (layer?.Mask is VectorClipMask selectedVector)
        {
            var contour = selectedVector.Contours.FirstOrDefault(item => item.Nodes.Any(node => node.Id == session.SceneEditing.MaskNodeId));
            session.SceneEditing.MaskContourId = contour?.Id ?? selectedVector.Contours[0].Id;
        }
        else
        {
            session.SceneEditing.MaskContourId = null;
        }
        var nodeId = session.SceneEditing.MaskNodeId;
        if (!force && Fields.Any(field => field.IsDirty) && fieldLayerId == layer?.Id && fieldNodeId == nodeId)
        {
            session.ViewModel.Masks.Refresh();
            session.ViewModel.Effects.RefreshMaskPropertyGrid();
            return;
        }
        loading = true;
        try
        {
            if (force || fieldLayerId != layer?.Id || fieldNodeId != nodeId)
            {
                var nextFields = CreateFields(layer).ToArray();
                if (fieldLayerId != layer?.Id || fieldNodeId != nodeId || !HaveSameFieldTargets(Fields, nextFields))
                {
                    foreach (var field in Fields)
                    {
                        if (field.Target is null)
                        {
                            field.Draft.PropertyChanged -= OnFieldChanged;
                        }
                    }
                    Fields = nextFields;
                    foreach (var field in Fields)
                    {
                        if (field.Target is null)
                        {
                            field.Draft.PropertyChanged += OnFieldChanged;
                        }
                    }
                }
            }
            fieldLayerId = layer?.Id;
            fieldNodeId = nodeId;
            var mask = layer is null ? null : SceneEvaluator.EvaluateMask(layer, session.AnimationTarget?.LocalTime ?? new(0));
            if (mask is not null)
            {
                foreach (var field in Fields.Where(field => field.Target is null))
                {
                    field.LoadPivot(field.Component == 0 ? mask.Transform.Pivot.X : mask.Transform.Pivot.Y);
                }
            }
            draftSource = null;
            draftTarget = null;
            session.ViewModel.Effects.RefreshMaskState();
            session.ViewModel.Masks.Refresh();
            session.ViewModel.Effects.RefreshMaskPropertyGrid();
        }
        finally
        {
            loading = false;
        }
    }

    private static bool HaveSameFieldTargets(MaskNumericField[] left, MaskNumericField[] right)
    {
        return left.Length == right.Length && left.Zip(right).All(pair =>
            pair.First.Key == pair.Second.Key && pair.First.Target == pair.Second.Target &&
            pair.First.Component == pair.Second.Component && pair.First.Minimum == pair.Second.Minimum &&
            pair.First.Maximum == pair.Second.Maximum && ReferenceEquals(pair.First.Row, pair.Second.Row));
    }

    private IEnumerable<MaskNumericField> CreateFields(ProjectLayer? layer)
    {
        if (layer?.Mask is null)
        {
            yield break;
        }
        foreach (var row in session.PropertyEditing.GetMaskRows(layer.Id, session.SceneEditing.MaskNodeId))
        {
            var target = row.Target;
            for (var component = 0; component < AnimationPropertyMetadata.GetComponentCount(target.Property); component++)
            {
                yield return new($"{target.Property}.{component}", "Workbench." + target.Property, target, component,
                    AnimationPropertyMetadata.GetMinimum(target.Property, component), AnimationPropertyMetadata.GetMaximum(target.Property, component), row);
            }
        }
        yield return new("MaskPivotX", "Workbench.MaskPivotX", null, 0, -1e9, 1e9);
        yield return new("MaskPivotY", "Workbench.MaskPivotY", null, 1, -1e9, 1e9);
    }

    internal void BeginPivotEdit(string panelId) => activePivotPanel = panelId;

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (loading || session.IsUpdating || e.PropertyName != nameof(AegiNext.Desktop.Editing.NumericValueDraft.RawText))
        {
            return;
        }
        var changedField = Fields.First(field => ReferenceEquals(field.Draft, sender));
        pivotEditingPanels[changedField.Key] = activePivotPanel;
        session.NotifyEffectPropertyDraftChanged();
        draftSource ??= session.DocumentSnapshot;
        draftTarget ??= session.AnimationTarget;
        session.SceneEditing.DraftTarget ??= draftTarget;
        _ = session.RunCommandAsync(session.PauseForSceneEditAsync);
        session.RefreshMaskPreview();
    }

    internal ProjectDocument Prepare(ProjectDocument document)
    {
        if (!Fields.Any(field => field.Target is null && field.IsDirty) || draftTarget is not { } frozen)
        {
            return document;
        }
        if (!ReferenceEquals(draftSource, session.DocumentSnapshot) || frozen.LayerId != session.SelectedLayerId)
        {
            throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
        }
        var layer = document.Layers.Single(value => value.Id == frozen.LayerId);
        var mask = layer.Mask ?? throw new InvalidOperationException(Localization.Get("Workbench.ClipMask"));
        var changed = document;
        var pivotFields = Fields.Where(field => field.Target is null).ToArray();
        if (pivotFields.Any(field => field.IsDirty))
        {
            var pivot = new ScenePoint(Parse(pivotFields[0]), Parse(pivotFields[1]));
            changed = WorkspaceDraftOperations.UpdateLayer(changed, layer.Id, value => value with
            {
                Mask = value.Mask! with { Transform = value.Mask!.Transform with { Pivot = pivot } }
            });
        }
        return changed;
    }

    private double Parse(MaskNumericField field)
    {
        var number = field.Draft.Parse();
        if (number is null || number < field.Minimum || number > field.Maximum)
        {
            session.ViewModel.InvalidPanelId = pivotEditingPanels.GetValueOrDefault(field.Key, "masks");
            session.ViewModel.InvalidFieldKey = field.Key;
            throw new InvalidDataException(field.Label);
        }
        return (double)number;
    }

    internal ProjectDocument Overlay(ProjectDocument document)
    {
        try
        {
            var result = Prepare(document);
            if (!ReferenceEquals(result, document))
            {
                ProjectValidator.Validate(result);
            }
            lastValidPreview = result;
            lastValidSource = session.DocumentSnapshot;
            return result;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            return ReferenceEquals(lastValidSource, session.DocumentSnapshot) && fieldLayerId == session.SelectedLayerId ? lastValidPreview ?? document : document;
        }
    }

    internal bool HasDrafts => Fields.Any(draftField => draftField.IsDirty);

    internal void RebindRelocatedSource(ProjectDocument document)
    {
        if (draftSource is not null)
        {
            draftSource = document;
        }
        lastValidSource = null;
        lastValidPreview = null;
    }

    internal void AcceptDrafts()
    {
        pivotEditingPanels.Clear();
        lastValidPreview = null;
        lastValidSource = null;
        draftSource = null;
        draftTarget = null;
    }

    internal void Restore(MaskNumericField field)
    {
        if (field.Row is { } row)
        {
            row.Restore(field.Component == 0 ? row.XFieldKey : row.YFieldKey);
            return;
        }
        loading = true;
        try
        {
            field.Draft.Load(field.Original);
        }
        finally
        {
            loading = false;
        }
        pivotEditingPanels.Remove(field.Key);
        session.NotifyEffectPropertyDraftChanged();
        session.ClearEffectPropertyDraftError(field.Key);
        session.RefreshMaskPreview();
    }

    internal void CreateRectangle()
    {
        if (CanCreate)
        {
            EnterEditing(CanvasEditMode.MASK_RECTANGLE);
        }
    }

    internal void AddContour()
    {
        if (CanEdit && !IsTopologyLocked && session.TryCommitDrafts())
        {
            session.ViewModel.CancelGestures();
            session.ViewModel.Effects.EditMode = CanvasEditMode.MASK_DRAW_VECTOR;
            session.ViewModel.Masks.Refresh();
            session.ViewModel.Effects.RefreshMaskPropertyGrid();
            session.RefreshMaskPreview();
        }
    }

    internal void CreateVector()
    {
        if (CanCreate)
        {
            EnterEditing(CanvasEditMode.MASK_VECTOR);
        }
    }

    internal void ToggleEditing()
    {
        if (!CanEdit || session.SelectedLayer?.Mask is null)
        {
            return;
        }
        if (IsEditing)
        {
            ExitEditing();
            return;
        }
        switch (session.SelectedLayer.Mask)
        {
            case RectangleClipMask:
                EnterEditing(CanvasEditMode.MASK_RECTANGLE);
                break;
            case VectorClipMask:
                EnterEditing(CanvasEditMode.MASK_VECTOR);
                break;
        }
    }

    private void EnterEditing(CanvasEditMode mode)
    {
        if (!session.TryCommitDrafts())
        {
            session.ViewModel.Masks.Refresh();
            session.ViewModel.Effects.RefreshMaskPropertyGrid();
            return;
        }
        session.ViewModel.CancelGestures();
        session.ViewModel.Effects.EditMode = mode;
        session.ViewModel.Masks.Refresh();
        session.RefreshMaskPreview();
    }

    internal void ExitEditing()
    {
        session.ViewModel.CancelGestures();
        session.ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        session.ViewModel.Masks.Refresh();
        session.RefreshMaskPreview();
    }

    internal void Clear()
    {
        if (session.SelectedLayer is { } layer)
        {
            session.ViewModel.CancelGestures();
            if (IsEditing)
            {
                session.ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
            }
            session.Editor.ClearClipMask(layer.Id);
            session.SceneEditing.MaskNodeId = null;
            Refresh(true);
        }
    }

    internal void ClearNodeAnimation()
    {
        if (session.SelectedLayer is { } layer)
        {
            session.ViewModel.CancelGestures();
            session.Editor.ClearMaskNodeAnimation(layer.Id);
            Refresh(true);
        }
    }

    internal void Invert()
    {
        if (session.SelectedLayer is { Mask: { } mask } layer)
        {
            session.Editor.SetClipMask(layer.Id, mask with { Inverted = !mask.Inverted });
        }
    }

    internal void DeleteSelectedNode()
    {
        if (CanDeleteSelectedNode && session.SceneEditing.MaskNodeId is { } nodeId && session.SelectedLayer is { } layer)
        {
            session.ViewModel.CancelGestures();
            DeleteNode(layer, nodeId);
        }
    }

    internal void DeleteSelectedContour()
    {
        if (CanDeleteSelectedContour && session.SceneEditing.MaskContourId is { } contourId && session.SelectedLayer is { Mask: VectorClipMask vector } layer)
        {
            session.ViewModel.CancelGestures();
            var survivor = vector.Contours.Where(contour => contour.Id != contourId).SelectMany(contour => contour.Nodes).FirstOrDefault();
            session.Editor.RemoveClipMaskContour(layer.Id, contourId);
            session.SceneEditing.MaskNodeId = survivor?.Id;
            Refresh(true);
            session.RefreshMaskPreview();
        }
    }

    internal void CommitNodeDeletion(CanvasMaskNodeEventArgs e)
    {
        var source = gestureLayer;
        CancelGesture();
        if (source is null || e.LayerId != source.Id || session.SelectedLayerId != source.Id ||
            !ReferenceEquals(gestureSource, session.DocumentSnapshot) || ClipMaskAnimation.IsTopologyLocked(source))
        {
            return;
        }
        DeleteNode(source, e.NodeId);
    }

    internal void CommitSegmentInsertion(CanvasMaskSegmentEventArgs e)
    {
        var source = gestureLayer;
        CancelGesture();
        if (source is null || e.LayerId != source.Id || session.SelectedLayerId != source.Id ||
            !ReferenceEquals(gestureSource, session.DocumentSnapshot) || ClipMaskAnimation.IsTopologyLocked(source) || source.Mask is not VectorClipMask vector)
        {
            return;
        }
        if (!double.IsFinite(e.Progress) || e.Progress <= 0 || e.Progress >= 1)
        {
            return;
        }
        SubdivideSegment(source, vector, e.ContourId, e.NodeId, e.Progress);
    }

    private void DeleteNode(ProjectLayer layer, Guid nodeId)
    {
        if (layer.Mask is not VectorClipMask vector)
        {
            return;
        }
        var contour = vector.Contours.FirstOrDefault(item => item.Nodes.Any(node => node.Id == nodeId));
        if (contour is null)
        {
            return;
        }
        var index = contour.Nodes.IndexOf(contour.Nodes.Single(node => node.Id == nodeId));
        var survivor = contour.Nodes.Length > 1 ? contour.Nodes[(index + 1) % contour.Nodes.Length] :
            vector.Contours.Where(item => item.Id != contour.Id).SelectMany(item => item.Nodes).FirstOrDefault();
        session.Editor.RemoveClipMaskNode(layer.Id, nodeId);
        session.SceneEditing.MaskNodeId = survivor?.Id;
        Refresh(true);
        session.RefreshMaskPreview();
    }

    internal bool BeginGesture()
    {
        if (!CanEdit || !session.TryCommitDrafts() || session.AnimationTarget is not { } target || session.SelectedLayer is not { } layer)
        {
            return false;
        }
        session.NotifyTaskInputChanged();
        gestureSource = session.DocumentSnapshot;
        gestureLayer = layer;
        gestureTarget = target;
        gestureMask = SceneEvaluator.EvaluateMask(layer, target.LocalTime);
        session.SceneEditing.GestureTarget = target;
        _ = session.RunCommandAsync(session.PauseForSceneEditAsync);
        return true;
    }

    internal void CancelGesture()
    {
        gestureLayer = null;
        gestureMask = null;
        gestureTarget = null;
        session.SceneEditing.GestureTarget = null;
    }

    internal void CommitGesture(CanvasMaskEditEventArgs e)
    {
        var source = gestureLayer;
        var target = gestureTarget;
        var originalMask = gestureMask;
        CancelGesture();
        if (source is null || target is null || e.LayerId != source.Id || session.SelectedLayerId != source.Id || !ReferenceEquals(gestureSource, session.DocumentSnapshot))
        {
            return;
        }
        var document = session.DocumentSnapshot;
        var prepared = document;
        if (originalMask is null || !ClipMaskAnimation.HasSameTopology(originalMask, e.Mask))
        {
            if (ClipMaskAnimation.IsTopologyLocked(source))
            {
                throw new InvalidOperationException(Localization.Get("Workbench.MaskTopologyLocked"));
            }
            prepared = WorkspaceDraftOperations.UpdateLayer(document, source.Id, layer => layer with
            {
                Mask = e.Mask,
                Tracks = layer.Tracks.Where(track => !AnimationPropertyMetadata.IsMaskProperty(track.Property) ||
                    !AnimationPropertyMetadata.IsNodeProperty(track.Property) && track.Property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)).ToImmutableArray()
            });
        }
        else
        {
            foreach (var property in AnimationPropertyMetadata.CurrentProperties.Where(AnimationPropertyMetadata.IsMaskProperty))
            {
                var ids = AnimationPropertyMetadata.IsNodeProperty(property)
                    ? e.Mask is VectorClipMask vector ? vector.Contours.SelectMany(contour => contour.Nodes).Select(node => (Guid?)node.Id) : []
                    : new Guid?[] { null };
                foreach (var id in ids)
                {
                    if (e.Mask is not RectangleClipMask && property is (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT))
                    {
                        continue;
                    }
                    var identity = new AnimationTrackTarget(property, id);
                    var before = ClipMaskAnimation.GetBaseValue(originalMask, identity);
                    var after = ClipMaskAnimation.GetBaseValue(e.Mask, identity);
                    if (before != after)
                    {
                        prepared = AnimationEditOperations.SetValue(prepared, target, identity, after);
                    }
                }
            }
            if (originalMask.Transform.Pivot != e.Mask.Transform.Pivot)
            {
                prepared = WorkspaceDraftOperations.UpdateLayer(prepared, source.Id, layer => layer with
                {
                    Mask = layer.Mask! with { Transform = layer.Mask.Transform with { Pivot = e.Mask.Transform.Pivot } }
                });
            }
        }
        if (prepared != document)
        {
            session.Editor.Apply("Edit subtitle clip mask", _ => prepared);
        }
        session.SceneEditing.MaskNodeId = e.NodeId ?? session.SceneEditing.MaskNodeId;
        if (e.Mask is VectorClipMask && session.SceneEditing.Mode == CanvasEditMode.MASK_DRAW_VECTOR)
        {
            session.ViewModel.Effects.EditMode = CanvasEditMode.MASK_VECTOR;
        }
        Refresh(true);
    }

    internal void SelectGestureNode(Guid? id)
    {
        session.SceneEditing.MaskNodeId = id;
        session.ViewModel.Timeline.SelectedMaskNodeId = id;
        session.RefreshMaskPreview();
    }

    internal void SelectNode(Guid? id)
    {
        if (!session.TryCommitDrafts())
        {
            return;
        }
        session.SceneEditing.MaskNodeId = id;
        Refresh(true);
        session.RefreshMaskPreview();
    }

    internal void Subdivide()
    {
        if (IsTopologyLocked || session.SelectedLayer is not { Mask: VectorClipMask vector } layer)
        {
            return;
        }
        var contour = vector.Contours.FirstOrDefault(item => item.Nodes.Any(node => node.Id == session.SceneEditing.MaskNodeId));
        if (contour is null || contour.Nodes.Length == 0)
        {
            return;
        }
        SubdivideSegment(layer, vector, contour.Id, session.SceneEditing.MaskNodeId!.Value);
    }

    private void SubdivideSegment(ProjectLayer layer, VectorClipMask vector, Guid contourId, Guid nodeId, double progress = 0.5)
    {
        var contour = vector.Contours.FirstOrDefault(item => item.Id == contourId);
        if (contour is null || vector.Contours.Sum(item => (long)item.Nodes.Length) >= 10000)
        {
            return;
        }
        var first = contour.Nodes.FirstOrDefault(node => node.Id == nodeId);
        if (first is null)
        {
            return;
        }
        var index = contour.Nodes.IndexOf(first);
        var changed = ClipMaskGeometryOperations.SubdivideSegment(vector, contourId, nodeId, progress);
        session.Editor.SetClipMask(layer.Id, changed);
        session.SceneEditing.MaskNodeId = changed.Contours[vector.Contours.IndexOf(contour)].Nodes[index + 1].Id;
        Refresh(true);
        session.RefreshMaskPreview();
    }
}
