using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal ImmutableArray<Guid> StyleLayerIds(LayerKind kind)
    {
        var layerIds = TimelineClipIds();
        if (layerIds.IsEmpty)
        {
            return [];
        }
        var selected = layerIds.ToHashSet();
        return editor.Snapshot.Layers.Any(layer => selected.Contains(layer.Id) && layer.Kind != kind)
            ? [] : layerIds;
    }

    internal bool HasStyleSelection(LayerKind kind)
    {
        return !StyleLayerIds(kind).IsEmpty;
    }

    internal ImmutableArray<Guid> StyleSubtitleIds()
    {
        var layerIds = StyleLayerIds(LayerKind.SUBTITLE);
        if (layerIds.IsEmpty)
        {
            return [];
        }
        var selected = layerIds.ToHashSet();
        return [.. editor.Snapshot.Layers.Where(layer => selected.Contains(layer.Id))
            .Select(layer => layer.SubtitleId!.Value).Distinct()];
    }
}
