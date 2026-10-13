using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool BeginSpatialGesture(CanvasSpatialGestureStartingEventArgs request)
    {
        if (request.Target.Property is not (AnimationProperty.ROTATION_3D or AnimationProperty.PERSPECTIVE_DISTANCE) ||
            request.Target.State != SubtitleAnimationState.NORMAL || request.Target.NodeId is not null ||
            !CanSelectGraphicCanvasMode(CanvasEditMode.SPATIAL_TRANSFORM) || !TryCommitDrafts() ||
            SelectedLayer is not { } layer || layer.Id != request.LayerId || AnimationTarget is not { } target ||
            request.Target.TextRangeId != target.Target?.TextRangeId ||
            layer.Tracks.Any(track => !track.Transforms.IsEmpty &&
                (track.Target == request.Target || request.Target.Property == AnimationProperty.ROTATION_3D &&
                    track.Target.TextRangeId == request.Target.TextRangeId && track.Target.State == SubtitleAnimationState.NORMAL &&
                    track.Target.NodeId is null &&
                    track.Property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y)))
        {
            return false;
        }
        if (request.Target.TextRangeId is { } rangeId &&
            (layer.SubtitleId is not { } subtitleId || !editor.Snapshot.Subtitles.Single(line => line.Id == subtitleId)
                .AnimationRanges.Any(range => range.Id == rangeId)))
        {
            return false;
        }
        canvasGestureSource = editor.Snapshot;
        SceneEditing.GestureTarget = target with { Target = request.Target };
        request.LocalTime = target.LocalTime;
        request.IsKeyframe = target.IsKeyframe;
        _ = RunCommandAsync(PauseForSceneEditAsync);
        return true;
    }

    internal Task CommitSpatialAsync(CanvasSpatialEditEventArgs value) => RunCommandAsync(() => EditAsync(() =>
    {
        var layer = SelectedLayer;
        var target = SceneEditing.GestureTarget;
        var captured = canvasGestureSource;
        CancelCanvasGesture();
        if (layer is null || target is null || layer.Id != value.LayerId || target.Target != value.Target ||
            !ReferenceEquals(captured, editor.Snapshot) ||
            value.Value.Kind != AnimationPropertyMetadata.GetValueKind(value.Target.Property) ||
            Enumerable.Range(0, value.Value.ComponentCount).Any(component =>
                !double.IsFinite(value.Value.GetComponent(component)) ||
                value.Value.GetComponent(component) < AnimationPropertyMetadata.GetMinimum(value.Target.Property) ||
                value.Value.GetComponent(component) > AnimationPropertyMetadata.GetMaximum(value.Target.Property)))
        {
            return;
        }
        var document = editor.Snapshot;
        var line = layer.SubtitleId is { } subtitleId ? document.Subtitles.Single(line => line.Id == subtitleId) : null;
        var original = SubtitleAnimationEvaluation.GetBaseValue(layer, line, value.Target);
        var displayed = original.IsVector3
            ? AnimationValue.FromVector3(AnimationEditOperations.Value(layer, value.Target.Property, target, original.Vector3))
            : AnimationValue.FromScalar(AnimationEditOperations.Value(layer, value.Target.Property, target, original.Scalar));
        if (displayed == value.Value)
        {
            return;
        }
        var prepared = AnimationEditOperations.SetValue(document, target, value.Target, value.Value);
        editor.Apply("Edit spatial transform in video preview", _ => prepared);
    }));
}
