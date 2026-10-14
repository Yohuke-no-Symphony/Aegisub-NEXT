using System.Collections.Immutable;

namespace AegiNext.Application.Presets;

internal sealed record EffectLibraryManifest
{
    public int Version { get; init; } = 1;
    public Guid Revision { get; init; }
    public ImmutableArray<string> Styles { get; init; } = [];
    public ImmutableArray<string> Effects { get; init; } = [];
}
