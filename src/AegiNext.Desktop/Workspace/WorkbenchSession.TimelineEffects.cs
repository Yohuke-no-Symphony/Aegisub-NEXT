using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool SelectTimelineEffectClip(TimelineEffectClipId id)
    {
        if (IsUpdating || !TimelineContextIsCurrent(null) || !HasTimelineEffectClip(editor.Snapshot, id) ||
            !TryCommitDrafts())
        {
            return false;
        }

        var layer = editor.Snapshot.Layers.FirstOrDefault(layer => layer.Id == id.LayerId);
        if (layer is null || !HasTimelineEffectClip(editor.Snapshot, id))
        {
            return false;
        }

        PrepareTimelineEffectSelection(layer);
        InvalidateTimingSession();
        using var updateLease = BeginWorkbenchUpdate();
        SelectedLayerId = layer.Id;
        SelectedCueId = layer.SubtitleId;
        ViewModel.Effects.SelectedIds = [layer.Id];
        ViewModel.Effects.Document = editor.Snapshot;
        ViewModel.Effects.SelectedLayer = layer;
        SelectedKeyTime = null;
        SceneEditing.TransformOperationId = null;
        ViewModel.Effects.Target = id.Target;
        SceneEditing.MaskNodeId = id.Target.NodeId;
        ViewModel.Timeline.EffectTarget = id.Target;
        ViewModel.Effects.SelectInstance(id.InstanceId);
        SynchronizeSubtitleSelectionFromLayers();
        RevealSubtitleColorTagTargets(layer.SubtitleId is { } cueId ? [cueId] : []);
        RefreshDocument();
        return true;
    }

    internal void PrepareTimelineEffectSelection(ProjectLayer layer)
    {
        ViewModel.CancelGestures();
        CancelSceneGesture();
        if (SceneEditing.Mode == CanvasEditMode.CROP && layer.Image is null ||
            SceneEditing.Mode == CanvasEditMode.SHAPE_PATH && layer.Shape?.Kind != ShapeKind.PATH)
        {
            using var updateLease = BeginWorkbenchUpdate();
            ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        }
    }

    internal Task ClearTimelineEffectClipAsync(TimelineEffectClipId id, ProjectDocument expected) =>
        RunCommandAsync(() => TimelineContextIsCurrent(expected)
            ? EditAsync(() =>
            {
                ViewModel.CancelGestures();
                if (id.InstanceId is { } instanceId)
                {
                    editor.RemoveEffectInstance(id.LayerId, instanceId);
                }
                else
                {
                    editor.ClearAnimationTracks([id.LayerId], id.Target);
                }
                ClearKeyframeSelection();
            }) : Task.CompletedTask);

    internal Task DeleteTimelineKeyframeAsync(TimelineKeyframeIdentity id, ProjectDocument expected) =>
        RunCommandAsync(() => TimelineContextIsCurrent(expected)
            ? EditAsync(() =>
            {
                var track = editor.Snapshot.Layers.FirstOrDefault(layer => layer.Id == id.LayerId)?.Tracks
                    .FirstOrDefault(track => track.Target == id.Target);
                if (track is null || (id.OperationId is { } operationId
                    ? !track.Transforms.Any(operation => operation.Id == operationId)
                    : !track.Keyframes.Any(frame => frame.Time == id.Time)))
                {
                    return;
                }

                ViewModel.CancelGestures();
                if (id.OperationId is { } transformId)
                {
                    editor.RemoveAnimationTransform(id.LayerId, id.Target, transformId);
                }
                else
                {
                    editor.UpdateLayer(id.LayerId, layer => layer with
                    {
                        Tracks = layer.Tracks.Select(value => value.Target == id.Target
                            ? value with { Keyframes = value.Keyframes.Where(frame => frame.Time != id.Time).ToImmutableArray() }
                            : value).Where(value => !value.Keyframes.IsEmpty || !value.Transforms.IsEmpty).ToImmutableArray()
                    });
                }
                ClearKeyframeSelection();
            }) : Task.CompletedTask);

    internal static bool HasTimelineEffectClip(ProjectDocument document, TimelineEffectClipId id) =>
        document.Layers.Any(layer => layer.Id == id.LayerId && (id.InstanceId is { } instanceId
            ? layer.Effects.Any(instance => instance.Id == instanceId)
            : layer.Tracks.Any(track => track.Target == id.Target && (!track.Keyframes.IsEmpty || !track.Transforms.IsEmpty))));

    internal Task CommitEffectInstanceTimingAsync(TimelineEffectInstanceTimingEventArgs value) =>
        RunCommandAsync(() => TimelineContextIsCurrent(value.ExpectedDocument) && value.Id.InstanceId is { } instanceId &&
            HasTimelineEffectClip(editor.Snapshot, value.Id)
                ? EditAsync(() => editor.SetEffectInstanceTiming(value.Id.LayerId, instanceId, value.Start, value.End))
                : Task.CompletedTask);
}
