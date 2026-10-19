using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

/// <summary>从原始 OpenType、TTC 或 OTC 字节校验字体族，并验证可完整打开其字体 face。</summary>
public sealed class FontFamilyValidator : IFontFamilyValidator
{
    /// <inheritdoc />
    public void Validate(FontFamilyBundle family)
    {
        FontFamilyBundleValidator.Validate(family);
        foreach (var font in family.Files)
        {
            var faces = OpenTypeFontFileReader.Read(font);
            var matching = faces.Where(face => MatchesFamily(face.Metadata, family.FamilyName)).ToArray();
            if (matching.Length == 0)
            {
                throw new InvalidDataException($"Font file '{font.FileName}' does not contain family '{family.FamilyName}'.");
            }
            foreach (var face in matching)
            {
                var coordinates = face.Metadata.Axes.ToImmutableDictionary(axis => axis.Tag, axis => axis.Default);
                var bytes = PreparedFontFace.Create(font, face.CollectionIndex, coordinates);
                using var data = SKData.CreateCopy(bytes.AsSpan());
                using var typeface = SKTypeface.FromData(data);
                if (typeface is null || typeface.GlyphCount == 0)
                {
                    throw new InvalidDataException($"Font file '{font.FileName}' cannot be opened.");
                }
            }
        }
    }

    internal static bool MatchesFamily(OpenTypeFontMetadata metadata, string familyName)
    {
        return string.Equals(metadata.FamilyName, familyName, StringComparison.OrdinalIgnoreCase) ||
            metadata.FamilyAliases.Contains(familyName, StringComparer.OrdinalIgnoreCase);
    }
}
