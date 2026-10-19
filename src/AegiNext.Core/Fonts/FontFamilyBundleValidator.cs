using System.Diagnostics.CodeAnalysis;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Fonts;

/// <summary>验证原始字体族的标识、完整性和内容去重预算，不解析字体格式或访问文件系统。</summary>
public static class FontFamilyBundleValidator
{
    public const int MAXIMUM_FONT_BYTES = 256 * 1024 * 1024;
    public const long MAXIMUM_TOTAL_FONT_BYTES = 1024L * 1024 * 1024;
    private static readonly HashSet<string> fontExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ttf", ".otf", ".ttc", ".otc" };

    /// <summary>验证单个字体族及其文件；相同内容在族内只允许出现一次。</summary>
    public static void Validate(FontFamilyBundle family)
    {
        Require(family is not null, "字体族不能为空。");
        ValidateFamilyName(family.FamilyName);
        Require(!family.Files.IsDefaultOrEmpty, "字体族必须包含原始字体文件。");
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var font in family.Files)
        {
            ValidateFont(font);
            Require(hashes.Add(font.Sha256), "字体族内不能重复携带相同的字体文件。");
        }
    }

    /// <summary>验证唯一字体族及整个集合按文件内容去重后的总预算。</summary>
    public static void Validate(IEnumerable<FontFamilyBundle> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        Require(families is not ImmutableArray<FontFamilyBundle> immutable || !immutable.IsDefault, "字体族集合不能为默认值。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var family in families)
        {
            Validate(family);
            Require(names.Add(family.FamilyName), "字体族名称重复；名称不区分大小写。");
            foreach (var font in family.Files)
            {
                if (hashes.Add(font.Sha256))
                {
                    total += font.Data.Length;
                    Require(total <= MAXIMUM_TOTAL_FONT_BYTES, "原始字体总量超过 1 GiB。");
                }
            }
        }
    }

    /// <summary>验证本机累计字体库的结构与完整性，保留单文件预算，不限制累计总容量。</summary>
    public static void ValidateLibrary(IEnumerable<FontFamilyBundle> families)
    {
        ArgumentNullException.ThrowIfNull(families);
        Require(families is not ImmutableArray<FontFamilyBundle> immutable || !immutable.IsDefault, "字体族集合不能为默认值。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in families)
        {
            Validate(family);
            Require(names.Add(family.FamilyName), "字体族名称重复；名称不区分大小写。");
        }
    }

    /// <summary>验证可持久化的字体族标识。</summary>
    public static void ValidateFamilyName(string name)
    {
        Require(name is { Length: > 0 and <= 512 }, "字体族名称长度必须为 1 至 512。");
        ProjectValidator.ValidateText(name);
        Require(name == name.Trim() && !name.Any(char.IsControl) && name.IsNormalized(NormalizationForm.FormC),
            "字体族名称必须采用 NFC Unicode，且不能包含控制字符或首尾空白。");
    }

    /// <summary>验证单个原始字体文件的规范文件名、字节预算及 SHA-256。</summary>
    public static void ValidateFont(EmbeddedSubtitleFont font)
    {
        Require(font is not null, "字体文件不能为空。");
        var name = font.FileName;
        Require(name is { Length: > 0 and <= 128 }, "字体文件名无效。");
        ProjectValidator.ValidateText(name);
        Require(!name.Any(character => char.IsControl(character) || "/\\:<>\"|?*".Contains(character, StringComparison.Ordinal)) &&
            !name.EndsWith('.') && !name.EndsWith(' ') && name.IsNormalized(NormalizationForm.FormC) &&
            fontExtensions.Contains(Path.GetExtension(name)), "字体必须使用规范的 TTF、OTF、TTC 或 OTC 文件名。");
        Require(!IsWindowsDeviceName(name.Split('.')[0]), "字体不能使用平台保留文件名。");
        Require(!font.Data.IsDefaultOrEmpty && font.Data.Length <= MAXIMUM_FONT_BYTES, "字体为空或超过 256 MiB。");
        Require(font.Sha256 is { Length: 64 } && font.Sha256.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f'),
            "字体 SHA-256 必须是 64 位小写十六进制。");
        Require(string.Equals(Convert.ToHexStringLower(SHA256.HashData(font.Data.AsSpan())), font.Sha256, StringComparison.Ordinal),
            "字体内容与 SHA-256 不一致。");
    }

    private static bool IsWindowsDeviceName(string stem)
    {
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Length == 4 && stem[3] is >= '1' and <= '9' &&
            (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
