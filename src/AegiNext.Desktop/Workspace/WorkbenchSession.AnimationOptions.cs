using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private Guid? animationOptionsOwner;
    private AnimationEditTarget? animationOptionsTarget;
    internal string? AnimationOptionsPanelId { get; private set; }

    internal event EventHandler? AnimationOptionsChanged;
    internal AnimationEditTarget? AnimationOptionsTarget => animationOptionsTarget;
    internal bool IsAnimationOptionsOwner(Guid owner) => animationOptionsOwner == owner;

    internal bool TryOpenAnimationOptions(Guid owner, Guid layerId, AnimationTrackTarget target, string panelId, bool restoreDraft = false, Guid? restoreOperationId = null)
    {
        target = RotationTargetCompatibility.Canonicalize(target);
        if (IsClosing || IsProjectBusy || SelectedLayerId != layerId || !restoreDraft && !TryCommitDrafts())
        {
            return false;
        }
        var layer = SelectedLayer!;
        var time = restoreDraft && SceneEditing.DraftTarget is { } draft && draft.Target == target
            ? draft.LocalTime : SceneEditing.Target == target && SelectedKeyTime is { } selectedTime
            ? selectedTime : LayerAnimationTiming.ClampTime(layer, ProjectPosition - layer.Start + layer.AnimationOffset);
        ViewModel.Effects.Target = target;
        if (SelectedLayerId != layerId || SceneEditing.Target != target)
        {
            return false;
        }
        var frame = InspectorTrack(layer, target)?.Keyframes.FirstOrDefault(value => value.Time == time);
        using (BeginWorkbenchUpdate())
        {
            SelectedKeyTime = frame?.Time;
            animationOptionsTarget = new(layerId, time, frame is not null, target);
            animationOptionsOwner = owner;
            AnimationOptionsPanelId = panelId;
            if (frame is null && !restoreDraft)
            {
                ViewModel.Effects.Interpolation = (int)KeyframeInterpolation.LINEAR;
                ViewModel.Effects.LoadPowerExponent(1);
            }
            if (restoreDraft && restoreOperationId is { } operationId &&
                InspectorTrack(layer, target)?.Transforms.Any(operation => operation.Id == operationId) == true)
            {
                SceneEditing.TransformOperationId = operationId;
            }
            RefreshKeyframeInspector();
            ViewModel.Effects.RefreshAnimationOptionsState();
        }
        AnimationOptionsChanged?.Invoke(this, EventArgs.Empty);
        _ = RunCommandAsync(PauseForSceneEditAsync);
        return true;
    }

    internal void CloseAnimationOptions(Guid owner)
    {
        if (!IsAnimationOptionsOwner(owner))
        {
            return;
        }
        animationOptionsOwner = null;
        animationOptionsTarget = null;
        ViewModel.Effects.RefreshAnimationOptionsState();
        AnimationOptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal bool CanDeleteSelectedAnimationKeyframe(Guid layerId, AnimationTrackTarget target)
    {
        return !IsClosing && !IsProjectBusy && SelectedAnimationKeyframe(layerId, target) is not null;
    }

    internal Task DeleteSelectedAnimationKeyframeAsync(Guid layerId, AnimationTrackTarget target)
    {
        return SelectedAnimationKeyframe(layerId, target) is { } identity
            ? DeleteTimelineKeyframeAsync(identity, DocumentSnapshot) : Task.CompletedTask;
    }

    internal Task DeleteAnimationTransformAsync(AnimationEditTarget edit, Guid operationId, ProjectDocument source)
    {
        return RunCommandAsync(() => EditAsync(() =>
        {
            if (edit.Target is not { } target || SelectedLayerId != edit.LayerId || SceneEditing.Target != target ||
                SceneEditing.TransformOperationId != operationId || !ReferenceEquals(source, DocumentSnapshot) ||
                SelectedLayer is not { } layer || InspectorTrack(layer, target)?.Transforms.Any(operation => operation.Id == operationId) != true)
            {
                return;
            }
            Editor.Apply("Delete animation transform", document =>
            {
                var normalized = AnimationEditOperations.NormalizeRotation(document, edit.LayerId, target);
                return WorkspaceDraftOperations.UpdateLayer(normalized, edit.LayerId, current => current with
                {
                    Tracks = current.Tracks.Select(track => track.Target == target ? track with
                    {
                        Transforms = track.Transforms.Where(operation => operation.Id != operationId).ToImmutableArray()
                    } : track).Where(track => !track.Keyframes.IsEmpty || !track.Transforms.IsEmpty).ToImmutableArray()
                });
            });
        }));
    }

    private TimelineKeyframeIdentity? SelectedAnimationKeyframe(Guid layerId, AnimationTrackTarget target)
    {
        target = RotationTargetCompatibility.Canonicalize(target);
        if (SelectedLayer is not { } layer || layer.Id != layerId || SceneEditing.Target != target)
        {
            return null;
        }
        var track = InspectorTrack(layer, target);
        if (SelectedKeyTime is { } time && track?.Keyframes.Any(frame => frame.Time == time) == true)
        {
            return new(layerId, track.Target, time);
        }
        if (ViewModel.Timeline.SelectedTimelineKeyframe is { OperationId: { } operationId } selected &&
            selected.LayerId == layerId && selected.Target == target && SceneEditing.TransformOperationId == operationId &&
            track?.Transforms.Any(operation => operation.Id == operationId) == true)
        {
            return selected;
        }
        return null;
    }
}
