using System.Collections.Immutable;
using AegiNext.Application.Fonts;

namespace AegiNext.Application.Presets;

internal sealed record EffectLibraryManifest
{
    public int Version { get; init; } = 2;
    public Guid Revision { get; init; }
    public ImmutableArray<string> Styles { get; init; } = [];
    public ImmutableArray<string> Effects { get; init; } = [];
    public ImmutableArray<FontFamilyReference> FontFamilies { get; init; } = [];
}
