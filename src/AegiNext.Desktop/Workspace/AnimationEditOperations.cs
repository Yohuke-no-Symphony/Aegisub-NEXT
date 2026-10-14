using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal static class AnimationEditOperations
{
    internal static double Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, double fallback)
    {
        if (RotationTargetCompatibility.IsRotation(property) && property != AnimationProperty.ROTATION_3D)
        {
            var component = RotationTargetCompatibility.Component(property);
            var rotation = AnimationValue.FromVector3(new(layer.Transform.RotationX, layer.Transform.RotationY, layer.Transform.Rotation))
                .WithComponent(component, fallback).Vector3;
            return AnimationValue.FromVector3(Value(layer, AnimationProperty.ROTATION_3D, target, rotation)).GetComponent(component);
        }
        var track = layer.Tracks.FirstOrDefault(value => value.Target == ResolveTarget(target, property));
        return track is null ? fallback : SceneEvaluator.EvaluateScalarTrack(track, target.LocalTime);
    }

    internal static SceneVector3 Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, SceneVector3 fallback)
    {
        var identity = ResolveTarget(target, property);
        var tracks = property == AnimationProperty.ROTATION_3D
            ? RotationAnimationTrackMigration.Merge(layer.Tracks, identity, fallback) : layer.Tracks;
        var track = tracks.FirstOrDefault(value => value.Target == identity);
        return track is null ? fallback : SceneEvaluator.EvaluateTrack(track, target.LocalTime).Vector3;
    }

    internal static ScenePoint Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, ScenePoint fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == ResolveTarget(target, property));
        return track is null ? fallback : SceneEvaluator.EvaluateVectorTrack(track, target.LocalTime);
    }

    internal static SceneColor Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, SceneColor fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == ResolveTarget(target, property));
        return track is null ? fallback : SceneEvaluator.EvaluateColorTrack(track, target.LocalTime);
    }

    internal static ProjectDocument SetValue(ProjectDocument document, AnimationEditTarget target, AnimationProperty property, AnimationValue value) =>
        SetValue(document, target, ResolveTarget(target, property), value);

    internal static ProjectDocument SetValue(ProjectDocument document, AnimationEditTarget target, AnimationTrackTarget animationTarget, AnimationValue value)
    {
        var layer = document.Layers.Single(value => value.Id == target.LayerId);
        if (RotationTargetCompatibility.IsRotation(animationTarget.Property))
        {
            var subtitle = layer.SubtitleId is { } subtitleId ? document.Subtitles.Single(line => line.Id == subtitleId) : null;
            var tracks = RotationAnimationTrackMigration.Normalize(layer, subtitle);
            if (!tracks.SequenceEqual(layer.Tracks))
            {
                document = WorkspaceDraftOperations.UpdateLayer(document, layer.Id, current => current with { Tracks = tracks });
                layer = document.Layers.Single(current => current.Id == layer.Id);
            }
            var property = animationTarget.Property;
            animationTarget = RotationTargetCompatibility.Canonicalize(animationTarget);
            if (property != AnimationProperty.ROTATION_3D)
            {
                var fallback = SubtitleAnimationEvaluation.GetBaseValue(layer, subtitle, animationTarget).Vector3;
                value = AnimationValue.FromVector3(Value(layer, AnimationProperty.ROTATION_3D,
                    target with { Target = animationTarget }, fallback)).WithComponent(RotationTargetCompatibility.Component(property), value.Scalar);
            }
        }
        var track = layer.Tracks.FirstOrDefault(value => value.Target == animationTarget);
        if (track is not null && !track.Transforms.IsEmpty)
        {
            throw new InvalidOperationException("Ordered transforms must be edited by their operation identity.");
        }
        var range = LayerAnimationTiming.GetRange(layer);
        if (animationTarget.Property == AnimationProperty.PATH_PROGRESS)
        {
            if (!WorkbenchSession.CanUseAnimationProperty(layer, animationTarget))
            {
                throw new InvalidOperationException(Localization.Get("Workbench.PathProgressRequiresPath"));
            }
            if (track is null || target.LocalTime < range.Minimum || target.LocalTime > range.Maximum)
            {
                throw new InvalidOperationException(Localization.Get("Workbench.PathProgressRequiresKeyframe"));
            }
        }
        if ((target.IsKeyframe || track is not null) && target.LocalTime >= range.Minimum && target.LocalTime <= range.Maximum)
        {
            var existing = track?.Keyframes.FirstOrDefault(frame => frame.Time == target.LocalTime);
            return WorkspaceDraftOperations.SetKeyframe(document, layer.Id, animationTarget,
                existing is null ? new(target.LocalTime, value) : existing with { Value = value });
        }

        return SubtitleAnimationEditing.SetBaseValue(document, layer.Id, animationTarget, value);
    }

    internal static ProjectDocument NormalizeRotation(ProjectDocument document, Guid layerId, AnimationTrackTarget target)
    {
        if (!RotationTargetCompatibility.IsRotation(target.Property))
        {
            return document;
        }
        var layer = document.Layers.Single(current => current.Id == layerId);
        var subtitle = layer.SubtitleId is { } id ? document.Subtitles.Single(line => line.Id == id) : null;
        var tracks = RotationAnimationTrackMigration.Normalize(layer, subtitle);
        return tracks.SequenceEqual(layer.Tracks) ? document :
            WorkspaceDraftOperations.UpdateLayer(document, layerId, current => current with { Tracks = tracks });
    }

    private static AnimationTrackTarget ResolveTarget(AnimationEditTarget target, AnimationProperty property) =>
        target.Target is { } identity && !AnimationPropertyMetadata.IsMaskProperty(property)
            ? identity with { Property = property, NodeId = null,
                State = AnimationPropertyMetadata.IsSubtitleVisualProperty(property) ? identity.State : SubtitleAnimationState.NORMAL } : new(property);

}
