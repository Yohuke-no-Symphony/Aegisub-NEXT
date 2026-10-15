using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed class ClipMaskEditingCoordinator(WorkbenchSession session)
{
    private readonly ClipMaskPositionEditingCoordinator positionEditing = new(session);
    private readonly EffectCompilationCache effectPlans = new();
    private ProjectDocument? gestureSource;
    private ProjectLayer? gestureLayer;
    private ClipMaskEffectEditContext? gestureEffectEdit;
    private AnimationEditTarget? gestureTarget;
    private ProjectDocument? lastValidPreview;
    private ProjectDocument? lastValidSource;
    private Guid? fieldLayerId;
    private Guid? fieldNodeId;
    internal MaskNumericField[] Fields { get; private set; } = [];
    internal LayerPositionDraft PositionDraft => positionEditing.Draft;
    internal bool CanEdit => session.SelectedLayer is { Kind: LayerKind.SUBTITLE, SubtitleId: not null };
    internal bool CanCreate => CanEdit && session.SelectedLayer?.Mask is null;
    internal bool IsEditing => session.SceneEditing.Mode is CanvasEditMode.MASK_RECTANGLE or CanvasEditMode.MASK_VECTOR or CanvasEditMode.MASK_DRAW_VECTOR;
    internal bool IsTopologyLocked => session.SelectedLayer is { } layer && CreateEffectEditContext(layer).IsTopologyLocked;
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
        positionEditing.Refresh(layer);
        if (!force && Fields.Any(field => field.IsDirty) && fieldLayerId == layer?.Id && fieldNodeId == nodeId)
        {
            session.ViewModel.Masks.Refresh();
            session.ViewModel.Effects.RefreshMaskPropertyGrid();
            return;
        }
        if (force || fieldLayerId != layer?.Id || fieldNodeId != nodeId)
        {
            var nextFields = CreateFields(layer).ToArray();
            if (fieldLayerId != layer?.Id || fieldNodeId != nodeId || !HaveSameFieldTargets(Fields, nextFields))
            {
                Fields = nextFields;
            }
        }
        fieldLayerId = layer?.Id;
        fieldNodeId = nodeId;
        session.ViewModel.Effects.RefreshMaskState();
        session.ViewModel.Masks.Refresh();
        session.ViewModel.Effects.RefreshMaskPropertyGrid();
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
    }

    internal void BeginPositionEdit(string panelId) => positionEditing.BeginEdit(panelId);
    internal bool RestorePositionField(string? fieldKey) => positionEditing.Restore(fieldKey);
    internal ProjectDocument Prepare(ProjectDocument document) => positionEditing.Prepare(document);

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

    internal bool HasDrafts => positionEditing.HasDrafts || Fields.Any(draftField => draftField.IsDirty);

    internal void RebindRelocatedSource(ProjectDocument document)
    {
        positionEditing.RebindRelocatedSource(document);
        lastValidSource = null;
        lastValidPreview = null;
    }

    internal void AcceptDrafts()
    {
        positionEditing.Accept();
        lastValidPreview = null;
        lastValidSource = null;
    }

    internal void Restore(MaskNumericField field)
    {
        if (Fields.Contains(field) && field.Row is { } row)
        {
            row.Restore(field.Component == 0 ? row.XFieldKey : row.YFieldKey);
            return;
        }
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
        var topologyLocked = gestureEffectEdit?.IsTopologyLocked ?? true;
        CancelGesture();
        if (source is null || e.LayerId != source.Id || session.SelectedLayerId != source.Id ||
            !ReferenceEquals(gestureSource, session.DocumentSnapshot) || topologyLocked)
        {
            return;
        }
        DeleteNode(source, e.NodeId);
    }

    internal void CommitSegmentInsertion(CanvasMaskSegmentEventArgs e)
    {
        var source = gestureLayer;
        var topologyLocked = gestureEffectEdit?.IsTopologyLocked ?? true;
        CancelGesture();
        if (source is null || e.LayerId != source.Id || session.SelectedLayerId != source.Id ||
            !ReferenceEquals(gestureSource, session.DocumentSnapshot) || topologyLocked || source.Mask is not VectorClipMask vector)
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
        gestureEffectEdit = CreateEffectEditContext(layer);
        session.SceneEditing.GestureTarget = target;
        _ = session.RunCommandAsync(session.PauseForSceneEditAsync);
        return true;
    }

    private ClipMaskEffectEditContext CreateEffectEditContext(ProjectLayer layer)
    {
        var subtitle = layer.SubtitleId is { } id ? session.DocumentSnapshot.Subtitles.Single(line => line.Id == id) : null;
        var time = session.AnimationTarget?.LocalTime ?? LayerAnimationTiming.ClampTime(layer,
            session.ProjectPosition - layer.Start + layer.AnimationOffset);
        return ClipMaskEffectEditContext.Create(layer, subtitle, time, effectPlans);
    }

    internal void CancelGesture()
    {
        gestureLayer = null;
        gestureEffectEdit = null;
        gestureTarget = null;
        session.SceneEditing.GestureTarget = null;
    }

    internal void CommitGesture(CanvasMaskEditEventArgs e)
    {
        var source = gestureLayer;
        var target = gestureTarget;
        var effectEdit = gestureEffectEdit;
        var originalMask = effectEdit?.VisibleMask;
        CancelGesture();
        if (source is null || target is null || effectEdit is null || e.LayerId != source.Id || session.SelectedLayerId != source.Id || !ReferenceEquals(gestureSource, session.DocumentSnapshot))
        {
            return;
        }
        var document = session.DocumentSnapshot;
        var prepared = document;
        if (originalMask is null || !ClipMaskAnimation.HasSameTopology(originalMask, e.Mask))
        {
            if (effectEdit.IsTopologyLocked)
            {
                throw new InvalidOperationException(Localization.Get("Workbench.MaskTopologyLocked"));
            }
            if (!effectEdit.TryGetTopologyDraft(e.Mask, out var nativeMask))
            {
                return;
            }
            prepared = WorkspaceDraftOperations.UpdateLayer(document, source.Id, layer => layer with
            {
                Mask = nativeMask,
                Tracks = layer.Tracks.Where(track => !AnimationPropertyMetadata.IsMaskProperty(track.Property) ||
                    !AnimationPropertyMetadata.IsNodeProperty(track.Property) && track.Property is not (AnimationProperty.MASK_RECTANGLE_TOP_LEFT or AnimationProperty.MASK_RECTANGLE_BOTTOM_RIGHT)).ToImmutableArray()
            });
        }
        else
        {
            if (!effectEdit.TryGetNativePose(e.Mask, out var nativeMask))
            {
                return;
            }
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
                    var before = ClipMaskAnimation.GetBaseValue(effectEdit.NativeMask!, identity);
                    var after = ClipMaskAnimation.GetBaseValue(nativeMask, identity);
                    if (before != after)
                    {
                        prepared = AnimationEditOperations.SetValue(prepared, target, identity, after);
                    }
                }
            }
            if (originalMask.Placement != e.Mask.Placement || originalMask.ReferenceFrame != e.Mask.ReferenceFrame)
            {
                prepared = WorkspaceDraftOperations.UpdateLayer(prepared, source.Id, layer => layer with
                {
                    Mask = layer.Mask! with { Placement = e.Mask.Placement, ReferenceFrame = e.Mask.ReferenceFrame }
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
