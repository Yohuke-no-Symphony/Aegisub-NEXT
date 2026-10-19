using AegiNext.Core.Presets;
using System.Collections.Immutable;
using AegiNext.Core.Fonts;

namespace AegiNext.Application.Presets;

internal sealed record EffectLibraryImportEntry
{
    internal ImmutableArray<FontFamilyBundle> FontFamilies { get; init; } = [];
    internal EffectLibraryItemKind Kind { get; init; }
    internal SubtitleStylePreset? Style { get; init; }
    internal EffectScriptPreset? Effect { get; init; }
    internal bool HasEffectName { get; init; }
    internal Guid? ExternalId { get; init; }
    internal string? DslId { get; init; }
    internal string SourceName { get; init; } = string.Empty;
    internal string Name => Style?.Name ?? Effect!.Name;
}
