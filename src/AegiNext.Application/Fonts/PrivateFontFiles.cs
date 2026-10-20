using System.Collections.Immutable;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Fonts;

internal static class PrivateFontFiles
{
    internal static string Name(string digest)
    {
        if (digest is not { Length: 64 } || digest.Any(character => !char.IsAsciiDigit(character) && character is not (>= 'a' and <= 'f')))
        {
            throw new InvalidDataException("字体 SHA-256 无效。");
        }
        return digest + ".font";
    }

    internal static ImmutableArray<FontFamilyReference> References(IEnumerable<FontFamilyBundle> families)
    {
        FontFamilyBundleValidator.ValidateLibrary(families);
        return families.OrderBy(family => family.FamilyName, StringComparer.OrdinalIgnoreCase).Select(family =>
            new FontFamilyReference(family.FamilyName, family.Files.OrderBy(file => file.Sha256, StringComparer.Ordinal)
                .Select(file => new FontFileReference(file.FileName, file.Sha256)).ToImmutableArray())).ToImmutableArray();
    }

    internal static bool SameFamily(FontFamilyBundle first, FontFamilyBundle second)
    {
        return string.Equals(first.FamilyName, second.FamilyName, StringComparison.OrdinalIgnoreCase) &&
            first.Files.Select(file => file.Sha256).Order(StringComparer.Ordinal)
                .SequenceEqual(second.Files.Select(file => file.Sha256).Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }

    internal static ImmutableArray<FontFamilyBundle> Merge(ImmutableArray<FontFamilyBundle> current,
        IEnumerable<FontFamilyBundle> incoming)
    {
        var result = current.ToDictionary(family => family.FamilyName, StringComparer.OrdinalIgnoreCase);
        foreach (var family in incoming)
        {
            result[family.FamilyName] = family;
        }
        var merged = result.Values.OrderBy(family => family.FamilyName, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        FontFamilyBundleValidator.ValidateLibrary(merged);
        return merged;
    }

    internal static void ValidateCoverage(SubtitleStylePresetCollection styles, ImmutableArray<FontFamilyBundle> families,
        bool includesFonts = true)
    {
        if (!includesFonts)
        {
            if (families.IsDefault || !families.IsEmpty)
            {
                throw new InvalidDataException("不携带字体的交换包必须使用空字体族集合。");
            }
            return;
        }
        FontFamilyBundleValidator.Validate(families);
        var names = families.Select(family => family.FamilyName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (styles.Presets.Any(preset => !names.Contains(preset.Style.FontFamily)))
        {
            throw new InvalidDataException("样式交换文件必须携带每个样式使用的完整字体族。");
        }
    }

    internal static void ValidateInputBudget(IEnumerable<FontFamilyBundle> families, long metadataBytes)
    {
        var total = metadataBytes;
        foreach (var font in families.SelectMany(family => family.Files).DistinctBy(font => font.Sha256))
        {
            total = checked(total + font.Data.Length);
        }
        if (total > FontFamilyBundleValidator.MAXIMUM_TOTAL_FONT_BYTES)
        {
            throw new InvalidDataException("本次导入去重后内容超过 1 GiB。");
        }
    }

    internal static FontFamilyBundle ShareFontData(FontFamilyBundle family, IDictionary<string, ImmutableArray<byte>> files)
    {
        return family with
        {
            Files = family.Files.Select(font =>
            {
                if (files.TryGetValue(font.Sha256, out var data))
                {
                    return font with { Data = data };
                }
                files.Add(font.Sha256, font.Data);
                return font;
            }).ToImmutableArray()
        };
    }
}
