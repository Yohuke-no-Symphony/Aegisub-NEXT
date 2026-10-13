using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal event EventHandler? NumericGestureCancellationRequested;
    internal long EffectPropertyDraftRevision => draftRevision;

    internal void NotifyEffectPropertyDraftChanged()
    {
        draftRevision++;
        NotifyTaskInputChanged();
    }

    internal void ClearEffectPropertyDraftError(string field)
    {
        if (ViewModel.InvalidFieldKey == field)
        {
            ViewModel.InvalidPanelId = null;
            ViewModel.InvalidFieldKey = null;
            ViewModel.Effects.InvalidFieldKey = null;
            ViewModel.Effects.ValidationError = null;
            ViewModel.Masks.InvalidFieldKey = null;
            ViewModel.Masks.ValidationError = null;
        }
    }

    internal void RefreshEffectsInspectorTarget()
    {
        using var update = BeginWorkbenchUpdate();
        RefreshInspector();
    }

    internal AnimationValue EffectPropertyValue(ProjectLayer layer, AnimationTrackTarget target)
    {
        var subtitle = layer.SubtitleId is { } id ? DocumentSnapshot.Subtitles.FirstOrDefault(line => line.Id == id) : null;
        target = RotationTargetCompatibility.Canonicalize(target);
        var track = InspectorTrack(layer, target);
        return track is not null && AnimationTarget is { } edit
            ? SceneEvaluator.EvaluateTrack(track, edit.LocalTime)
            : SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, target);
    }

    internal AnimationTrack? InspectorTrack(ProjectLayer layer, AnimationTrackTarget target)
    {
        target = RotationTargetCompatibility.Canonicalize(target);
        var subtitle = layer.SubtitleId is { } id ? DocumentSnapshot.Subtitles.FirstOrDefault(line => line.Id == id) : null;
        var tracks = target.Property == AnimationProperty.ROTATION_3D
            ? RotationAnimationTrackMigration.Normalize(layer, subtitle) : layer.Tracks;
        return tracks.FirstOrDefault(track => track.Target == target);
    }

    internal Task CreateSelectedTextAnimationRangeAsync() => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedCue is not { } subtitle || Details.TextSelectionLength <= 0)
        {
            return;
        }
        var start = Details.TextSelectionStart;
        var length = Details.TextSelectionLength;
        var boundaries = new SubtitleTextBoundaries(subtitle.Text);
        if (!boundaries.Contains(start) || !boundaries.Contains(start + length))
        {
            throw new InvalidOperationException(Localization.Get("Workbench.AnimationRangeGraphemeSelection"));
        }
        var range = new SubtitleAnimationRange(Guid.NewGuid(), start, length);
        editor.SetSubtitleAnimationRange(subtitle.Id, range);
        ViewModel.Effects.SelectedScope = ViewModel.Effects.Scopes.Single(scope => scope.Id == range.Id);
    }));

    internal Task DeleteSelectedTextAnimationRangeAsync() => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedCue is { } subtitle && SceneEditing.Target.TextRangeId is { } id)
        {
            editor.RemoveSubtitleAnimationRange(subtitle.Id, id);
            ViewModel.Effects.Target = SceneEditing.Target with { TextRangeId = null };
        }
    }));

    internal Task MoveSelectedTextAnimationRangeAsync(int direction) => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedCue is not { } subtitle || SceneEditing.Target.TextRangeId is not { } id)
        {
            return;
        }
        var index = Array.FindIndex(subtitle.AnimationRanges.ToArray(), range => range.Id == id);
        var next = index + direction;
        if (index >= 0 && next >= 0 && next < subtitle.AnimationRanges.Length)
        {
            editor.MoveSubtitleAnimationRange(subtitle.Id, id, next);
        }
    }));

    internal Task ToggleEffectPropertyAnimationAsync(AnimationProperty property)
    {
        var target = RotationTargetCompatibility.Canonicalize(SceneEditing.Target with { Property = property, NodeId = null });
        var enabled = SelectedLayer is not { } layer || InspectorTrack(layer, target) is null;
        return SetEffectPropertyAnimationAsync(target, enabled);
    }

    internal void SelectAnimationProperty(Guid layerId, AnimationTrackTarget target)
    {
        if (SelectedLayerId == layerId)
        {
            ViewModel.Effects.Target = target;
        }
    }

    internal void OpenAnimationPropertyDetails(Guid layerId, AnimationTrackTarget target)
    {
        target = RotationTargetCompatibility.Canonicalize(target);
        SelectAnimationProperty(layerId, target);
        if (SelectedLayerId == layerId && SceneEditing.Target == target)
        {
            ViewModel.Effects.AnimationExpanded = true;
            ViewModel.ActivatePanel("effects");
        }
    }

    internal Task SetEffectPropertyAnimationAsync(AnimationTrackTarget target, bool enabled) =>
        SelectedLayerId is { } layerId ? SetAnimationPropertyEnabledAsync(layerId, target, enabled) : Task.CompletedTask;

    internal Task SetAnimationPropertyEnabledAsync(Guid layerId, AnimationTrackTarget target, bool enabled) => RunCommandAsync(() => EditAsync(() =>
    {
        target = RotationTargetCompatibility.Canonicalize(target);
        if (SelectedLayer is not { } layer || layer.Id != layerId)
        {
            return;
        }
        if (!enabled)
        {
            editor.UpdateLayer(layer.Id, current => current with
            {
                Tracks = current.Tracks.Where(track => RotationTargetCompatibility.Canonicalize(track.Target) != target).ToImmutableArray()
            });
            ClearKeyframeSelection();
        }
        else if (InspectorTrack(layer, target) is null)
        {
            AddEffectPropertyKeyframe(target);
        }
        ViewModel.Effects.Target = target;
    }));

    internal Task AddEffectPropertyKeyframeAsync(AnimationTrackTarget target) =>
        SelectedLayerId is { } layerId ? AddAnimationPropertyKeyframeAsync(layerId, target) : Task.CompletedTask;

    internal Task AddAnimationPropertyKeyframeAsync(Guid layerId, AnimationTrackTarget target) => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedLayerId == layerId)
        {
            AddEffectPropertyKeyframe(target);
        }
    }));

    internal bool CanAddEffectPropertyKeyframe(Guid layerId)
    {
        if (SelectedLayer is not { } layer || layer.Id != layerId)
        {
            return false;
        }
        var time = SelectedKeyTime ?? ProjectPosition - layer.Start + layer.AnimationOffset;
        return LayerAnimationTiming.ClampTime(layer, time) == time;
    }

    private void AddEffectPropertyKeyframe(AnimationTrackTarget target)
    {
        target = RotationTargetCompatibility.Canonicalize(target);
        if (SelectedLayer is not { } layer || AnimationTarget is not { } edit || !CanAddEffectPropertyKeyframe(layer.Id) ||
            InspectorTrack(layer, target)?.Transforms.IsEmpty == false)
        {
            return;
        }
        var value = EffectPropertyValue(layer, target);
        editor.Apply("Add property keyframe", document => WorkspaceDraftOperations.SetKeyframe(
            AnimationEditOperations.NormalizeRotation(document, layer.Id, target), layer.Id, target, new(edit.LocalTime, value)));
        SelectKeyframe(new(layer.Id, target, edit.LocalTime, edit.LocalTime));
    }

    internal Task SetEffectColorSpaceAsync(AnimationTrackTarget target, AnimationColorSpace space) => RunCommandAsync(() => EditAsync(() =>
    {
        if (SelectedLayer is not { } layer)
        {
            return;
        }
        editor.UpdateLayer(layer.Id, current => current with
        {
            Tracks = current.Tracks.Select(track => track.Target == target ? track with { ColorSpace = space } : track).ToImmutableArray()
        });
    }));

    internal Task ResetEffectPropertyAsync(AnimationTrackTarget target) =>
        SelectedLayerId is { } layerId ? ResetAnimationPropertyAsync(layerId, target) : Task.CompletedTask;

    internal Task ResetAnimationPropertyAsync(Guid layerId, AnimationTrackTarget target) => RunCommandAsync(() => EditAsync(() =>
    {
        target = RotationTargetCompatibility.Canonicalize(target);
        if (SelectedLayer is not { } layer || layer.Id != layerId || AnimationTarget is not { } edit ||
            InspectorTrack(layer, target)?.IsOrdered == true)
        {
            return;
        }
        var subtitle = layer.SubtitleId is { } id ? DocumentSnapshot.Subtitles.Single(line => line.Id == id) : null;
        var value = SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, target);
        var prepared = AnimationEditOperations.SetValue(editor.Snapshot, edit, target, value);
        if (prepared != editor.Snapshot)
        {
            editor.Apply("Reset animated property to base value", _ => prepared);
        }
    }));
}
