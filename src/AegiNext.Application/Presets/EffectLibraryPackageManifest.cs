using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace AegiNext.Application.Presets;

internal sealed record EffectLibraryPackageManifest
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    [JsonRequired]
    public ImmutableArray<EffectLibraryPackageFile> Styles { get; init; } = [];
    [JsonRequired]
    public ImmutableArray<EffectLibraryPackageFile> Effects { get; init; } = [];
}
