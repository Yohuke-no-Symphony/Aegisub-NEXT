using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineEffectClipLayout(ImmutableDictionary<Guid, ImmutableArray<TimelineEffectClip>> Layers, double Height)
{
    internal static TimelineEffectClipLayout Empty { get; } = new(ImmutableDictionary<Guid, ImmutableArray<TimelineEffectClip>>.Empty, 0);
    internal ImmutableArray<TimelineEffectClip> ForLayer(Guid layerId) => Layers.GetValueOrDefault(layerId, []);
}
