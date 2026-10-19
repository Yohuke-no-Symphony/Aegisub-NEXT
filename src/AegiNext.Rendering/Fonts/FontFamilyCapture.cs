using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Rendering.Fonts;

internal static class FontFamilyCapture
{
    internal static string ResolvePrimaryFamily(string familyName, EmbeddedSubtitleFont? primaryFont)
    {
        FontFamilyBundleValidator.ValidateFamilyName(familyName);
        if (primaryFont is null)
        {
            return familyName;
        }
        FontFamilyBundleValidator.ValidateFont(primaryFont);
        var faces = OpenTypeFontFileReader.Read(primaryFont);
        var selected = faces.FirstOrDefault(face => FontFamilyValidator.MatchesFamily(face.Metadata, familyName)) ?? faces[0];
        return selected.Metadata.FamilyName!;
    }

    internal static FontFamilyBundle Create(string canonicalFamily, IEnumerable<EmbeddedSubtitleFont> files,
        EmbeddedSubtitleFont? primaryFont)
    {
        var originals = (primaryFont is null ? files : files.Prepend(primaryFont))
            .DistinctBy(file => file.Sha256, StringComparer.Ordinal).OrderBy(file => file.Sha256, StringComparer.Ordinal).ToImmutableArray();
        var result = new FontFamilyBundle(canonicalFamily, originals);
        FontFamilyBundleValidator.Validate(result);
        return result;
    }

    internal static bool Matches(SystemFontFace face, string familyName)
    {
        return string.Equals(face.FamilyName, familyName, StringComparison.OrdinalIgnoreCase) ||
            face.Aliases.Contains(familyName, StringComparer.OrdinalIgnoreCase);
    }
}
