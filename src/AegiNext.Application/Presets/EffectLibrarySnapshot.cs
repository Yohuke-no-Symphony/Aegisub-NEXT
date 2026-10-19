using AegiNext.Core.Presets;
using System.Collections.Immutable;
using AegiNext.Core.Fonts;

namespace AegiNext.Application.Presets;

/// <summary>同一次持久化提交中的样式和个人特效快照；修订仅用于检测并发更新。</summary>
public sealed record EffectLibrarySnapshot
{
    public ImmutableArray<FontFamilyBundle> FontFamilies { get; init; } = [];
    public Guid Revision { get; init; }
    public SubtitleStylePresetCollection Styles { get; init; } = new();
    public EffectScriptPresetDocument Effects { get; init; } = new();
}
