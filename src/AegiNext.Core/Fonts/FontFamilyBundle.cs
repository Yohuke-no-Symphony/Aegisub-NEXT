using System.Collections.Immutable;
using AegiNext.Core.Presets;

namespace AegiNext.Core.Fonts;

/// <summary>以原始字体文件携带一个字体族；集合文件保持完整，不按实例或字形裁剪。</summary>
public sealed record FontFamilyBundle(string FamilyName, ImmutableArray<EmbeddedSubtitleFont> Files);
