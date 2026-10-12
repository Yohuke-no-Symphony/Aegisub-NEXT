using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Rendering.Fonts;
using AegiNext.SubtitleFormats;

namespace AegiNext.Rendering.SubtitleFormats;

/// <summary>使用已捕获的字体目录，为 ASS 字重请求选择特征完全匹配的真实字体变体。</summary>
public sealed class AssFontWeightResolver : IAssFontWeightResolver
{
    private readonly ImmutableDictionary<string, ImmutableArray<SystemFontFace>> families;

    /// <summary>捕获字体及家族别名，后续解析不受输入集合变化影响。</summary>
    public AssFontWeightResolver(IEnumerable<SystemFontFace> faces)
    {
        families = faces.SelectMany(face => face.Aliases.Append(face.FamilyName)
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => (Name: name, Face: face)))
            .GroupBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToImmutableDictionary(group => group.Key, group => group.Select(candidate => candidate.Face).ToImmutableArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public SubtitleFontVariant? ResolveVariant(string fontFamily, int weight, int width, bool italic)
    {
        if (!families.TryGetValue(fontFamily, out var faces))
        {
            return null;
        }
        return faces.Where(face => face.Variant.Weight == weight && face.Variant.Width == width && face.Variant.Italic == italic)
            .OrderBy(face => !string.Equals(face.FamilyName, fontFamily, StringComparison.OrdinalIgnoreCase))
            .ThenBy(face => face.IsVariable)
            .ThenBy(face => face.Variant.Name, StringComparer.Ordinal)
            .ThenBy(face => face.Variant.PostScriptName, StringComparer.Ordinal)
            .FirstOrDefault()?.Variant;
    }
}
