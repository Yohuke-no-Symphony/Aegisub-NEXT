using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

/// <summary>当前 .aegistyles ZIP 交换文件，包含样式元数据和完整字体族。</summary>
public sealed record SubtitleStylePackage
{
    public SubtitleStylePresetCollection Styles { get; init; } = new();
    public ImmutableArray<FontFamilyBundle> FontFamilies { get; init; } = [];
}
