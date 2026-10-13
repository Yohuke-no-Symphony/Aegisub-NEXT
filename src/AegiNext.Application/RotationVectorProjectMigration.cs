using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class RotationVectorProjectMigration
{
    internal static ProjectDocument Normalize(ProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ProjectValidator.Validate(document);
        if (!document.Layers.Any(layer => layer.Tracks.Any(track => IsLegacyRotation(track.Property))) &&
            !document.Presets.Any(preset => preset.Tracks.Any(track => IsLegacyRotation(track.Property))) &&
            !document.TimelineViewState.ExpandedAnimationClips.Any(clip => IsLegacyRotation(clip.Target.Property)) &&
            !document.TimelineViewState.CollapsedAnimationRows.Any(row => IsLegacyRotation(row.Property)))
        {
            return document;
        }
        var subtitles = document.Subtitles.ToDictionary(line => line.Id);
        var layers = document.Layers.Select(layer =>
        {
            var subtitle = layer.SubtitleId is { } subtitleId ? subtitles.GetValueOrDefault(subtitleId) : null;
            var tracks = RotationAnimationTrackMigration.Normalize(layer, subtitle);
            return tracks == layer.Tracks ? layer : layer with { Tracks = tracks };
        }).ToImmutableArray();
        var presets = document.Presets.Select(preset =>
        {
            var tracks = RotationAnimationTrackMigration.Normalize(new() { Tracks = preset.Tracks }, null);
            return tracks == preset.Tracks ? preset : preset with { Tracks = tracks };
        }).ToImmutableArray();
        var timeline = document.TimelineViewState;
        timeline = timeline with
        {
            ExpandedAnimationClips = timeline.ExpandedAnimationClips.Select(clip => clip with
            {
                Target = clip.Target with { Property = CanonicalProperty(clip.Target.Property) }
            }).Distinct().ToImmutableArray(),
            CollapsedAnimationRows = timeline.CollapsedAnimationRows.Select(row => row with
            {
                Property = CanonicalProperty(row.Property)
            }).Distinct().ToImmutableArray()
        };
        return document with { Layers = layers, Presets = presets, TimelineViewState = timeline };
    }

    private static AnimationProperty CanonicalProperty(AnimationProperty property)
    {
        return IsLegacyRotation(property) ? AnimationProperty.ROTATION_3D : property;
    }

    private static bool IsLegacyRotation(AnimationProperty property) =>
        property is AnimationProperty.ROTATION or AnimationProperty.ROTATION_X or AnimationProperty.ROTATION_Y;
}
