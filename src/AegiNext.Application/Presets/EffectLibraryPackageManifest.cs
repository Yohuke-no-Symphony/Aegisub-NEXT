using System.Collections.Immutable;
using System.Text.Json.Serialization;
using AegiNext.Application.Fonts;

namespace AegiNext.Application.Presets;

internal sealed record EffectLibraryPackageManifest
{
    [JsonRequired]
    public int Version { get; init; } = 2;
    [JsonRequired]
    public string Format { get; init; } = "aeginext.library";
    [JsonRequired]
    public ImmutableArray<EffectLibraryPackageFile> Styles { get; init; } = [];
    [JsonRequired]
    public ImmutableArray<EffectLibraryPackageFile> Effects { get; init; } = [];
    [JsonRequired]
    public ImmutableArray<FontFamilyReference> FontFamilies { get; init; } = [];
}
