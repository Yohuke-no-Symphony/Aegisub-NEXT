using System.Text.Json.Serialization;

namespace AegiNext.Application.Presets;

internal sealed record EffectLibraryPackageFile
{
    [JsonRequired]
    public string Path { get; init; } = string.Empty;
    [JsonRequired]
    public string Sha256 { get; init; } = string.Empty;
}
