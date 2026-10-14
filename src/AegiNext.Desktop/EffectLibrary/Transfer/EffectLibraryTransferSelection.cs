using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

internal sealed record EffectLibraryTransferSelection(ImmutableArray<SubtitleStylePreset> Styles,
    ImmutableArray<EffectScriptPreset> Effects, bool ContainsBuiltinEffects = false);
