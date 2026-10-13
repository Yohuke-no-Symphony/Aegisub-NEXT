using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;

namespace AegiNext.Desktop.Workspace;

internal static class WorkspaceDraftOperations
{
    internal static ProjectDocument UpdateSubtitle(ProjectDocument document, Guid id, Func<SubtitleLine, SubtitleLine> edit)
    {
        var original = document.Subtitles.Single(value => value.Id == id);
        var changed = edit(original);
        if (changed == original)
        {
            return document;
        }

        return document with
        {
            Subtitles = document.Subtitles.SetItem(document.Subtitles.IndexOf(original), changed),
            Layers = MapLayers(document.Layers, layer => layer.SubtitleId == id ? LayerAnimationTiming.Clip(layer with
            {
                Start = changed.Start, End = changed.End,
                AnimationOffset = layer.AnimationOffset + changed.Start - original.Start
            }) : layer)
        };
    }

    internal static ProjectDocument UpdateLayer(ProjectDocument document, Guid id, Func<ProjectLayer, ProjectLayer> edit)
    {
        return document with { Layers = MapLayers(document.Layers, layer => layer.Id == id ? LayerAnimationTiming.Clip(edit(layer)) : layer) };
    }

    internal static ProjectDocument SetKeyframe(ProjectDocument document, Guid id, AnimationProperty property, Keyframe keyframe) =>
        SetKeyframe(document, id, new AnimationTrackTarget(property), keyframe);

    internal static ProjectDocument SetKeyframe(ProjectDocument document, Guid id, AnimationTrackTarget target, Keyframe keyframe)
    {
        return UpdateLayer(document, id, layer =>
        {
            var track = layer.Tracks.FirstOrDefault(value => value.Target == target);
            if (track is not null && !track.Transforms.IsEmpty)
            {
                throw new InvalidOperationException("Ordered transforms must be edited by their operation identity.");
            }
            if (track?.Keyframes.FirstOrDefault(value => value.Time == keyframe.Time) == keyframe)
            {
                return layer;
            }
            var frames = (track?.Keyframes ?? []).Where(value => value.Time != keyframe.Time).Append(keyframe)
                .OrderBy(value => value.Time).ToImmutableArray();
            var updated = track is null ? new AnimationTrack(target, frames) : track with { Keyframes = frames };
            return layer with
            {
                Tracks = track is null ? layer.Tracks.Add(updated) : layer.Tracks.SetItem(layer.Tracks.IndexOf(track), updated)
            };
        });
    }

    private static ImmutableArray<ProjectLayer> MapLayers(ImmutableArray<ProjectLayer> layers, Func<ProjectLayer, ProjectLayer> edit)
    {
        var changed = false;
        var builder = ImmutableArray.CreateBuilder<ProjectLayer>(layers.Length);
        foreach (var layer in layers)
        {
            var next = edit(layer);
            changed |= next != layer;
            builder.Add(next);
        }

        return changed ? builder.MoveToImmutable() : layers;
    }
}
