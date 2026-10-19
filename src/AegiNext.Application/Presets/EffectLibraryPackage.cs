using AegiNext.Core.Presets;
using System.Collections.Immutable;
using AegiNext.Core.Fonts;

namespace AegiNext.Application.Presets;

/// <summary>只包含个人样式和完整个人特效的可共享包，不携带设置或存储修订。</summary>
public sealed record EffectLibraryPackage
{
    public SubtitleStylePresetCollection Styles { get; init; } = new();
    public EffectScriptPresetDocument Effects { get; init; } = new();
    public ImmutableArray<FontFamilyBundle> FontFamilies { get; init; } = [];
}
