using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

/// <summary>当前 .aegistyles ZIP 交换文件，明确声明是否携带完整字体族。</summary>
public sealed record SubtitleStylePackage
{
    public SubtitleStylePresetCollection Styles { get; init; } = new();
    public bool IncludesFonts { get; init; } = true;
    public ImmutableArray<FontFamilyBundle> FontFamilies { get; init; } = [];
}
