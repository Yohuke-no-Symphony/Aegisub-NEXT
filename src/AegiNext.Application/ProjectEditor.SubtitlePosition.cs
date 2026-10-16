using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>以一个可撤销事务清除字幕位置特效，保留锚点、轴心、偏移和其他效果。</summary>
    public void ResetSubtitlePositionEffects(Guid subtitleId)
    {
        Apply("Reset subtitle position effects", document =>
        {
            FindSubtitle(document, subtitleId);
            var layers = MapLayers(document.Layers, layer => layer.SubtitleId == subtitleId ? ResetPositionEffects(layer) : layer);
            return layers == document.Layers ? document : document with { Layers = layers };
        });
    }

    /// <summary>以一个可撤销事务恢复字幕自动排版位置，保留大小、旋转和其他效果。</summary>
    public void ResetSubtitlePosition(Guid subtitleId) => ResetSubtitlePositions([subtitleId]);

    /// <summary>原子恢复所选字幕的自动排版位置及位置特效，保留大小、旋转和其他效果。</summary>
    public void ResetSubtitlePositions(IReadOnlyCollection<Guid> subtitleIds)
    {
        ArgumentNullException.ThrowIfNull(subtitleIds);
        var selected = subtitleIds.ToHashSet();
        if (selected.Count == 0)
        {
            return;
        }
        Apply("Reset subtitle position", document =>
        {
            var indices = selected.Select(id => FindSubtitle(document, id)).ToArray();
            ImmutableArray<SubtitleLine>.Builder? subtitles = null;
            foreach (var index in indices)
            {
                var line = document.Subtitles[index];
                if (line.Style.Position is not null)
                {
                    subtitles ??= document.Subtitles.ToBuilder();
                    subtitles[index] = line with { Style = line.Style with { Position = null } };
                }
            }
            var lines = subtitles?.ToImmutable() ?? document.Subtitles;
            var layers = MapLayers(document.Layers, layer => layer.SubtitleId is { } id && selected.Contains(id)
                ? ResetPositionEffects(layer) : layer);
            return layers == document.Layers && lines == document.Subtitles ? document : document with
            {
                Subtitles = lines, Layers = layers
            };
        });
    }

    private static ProjectLayer ResetPositionEffects(ProjectLayer layer)
    {
        if (layer.Transform.Position == default && layer.MotionPath is null &&
            !layer.Tracks.Any(track => track.Property is AnimationProperty.POSITION or AnimationProperty.PATH_PROGRESS))
        {
            return layer;
        }

        return layer with
        {
            Transform = layer.Transform with { Position = default },
            MotionPath = null,
            Tracks = layer.Tracks.Where(track => track.Property is not (AnimationProperty.POSITION or AnimationProperty.PATH_PROGRESS)).ToImmutableArray()
        };
    }
}
