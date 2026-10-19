using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Fonts;

/// <summary>系统字体目录中的不可变候选及其原生字体定位信息。</summary>
public sealed record SystemFontFace
{
    public string FamilyName { get; init; } = string.Empty;
    public ImmutableArray<string> Aliases { get; init; } = [];
    public SubtitleFontVariant Variant { get; init; } = new();
    public bool IsVariable { get; init; }
    public string SourceFamilyName { get; init; } = string.Empty;
    public int StyleIndex { get; init; }
    public int NamedInstanceIndex { get; init; }
    public int CollectionIndex { get; init; }
    public bool IsStyleEnumerated { get; init; }
    /// <summary>该候选由应用字体库的原始文件提供，不依赖操作系统安装。</summary>
    public bool IsApplicationFont => ApplicationSource is not null;
    internal ApplicationFontFaceSource? ApplicationSource { get; init; }
    public string DisplayName => string.IsNullOrWhiteSpace(Variant.Name) ? FamilyName : $"{FamilyName} {Variant.Name}";
}
