using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Core.Projects;

/// <summary>个人库和工程共用的字幕分类标记边界校验。</summary>
public static class SubtitleColorTagValidator
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目常量采用 ALL_UPPER。")]
    public const int MAXIMUM_TAGS = 10000;
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目常量采用 ALL_UPPER。")]
    public const int MAXIMUM_NAME_LENGTH = 128;

    /// <summary>验证非空身份、短名称和严格六位 HEX 色值，不修改定义。</summary>
    public static void Validate(SubtitleColorTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (tag.Id == Guid.Empty || string.IsNullOrWhiteSpace(tag.Name) || tag.Name.Length > MAXIMUM_NAME_LENGTH ||
            tag.Name.Any(char.IsControl) || tag.ColorHex is not { Length: 7 } || tag.ColorHex[0] != '#' ||
            !tag.ColorHex.Skip(1).All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The subtitle color tag ID, name, or HEX color is invalid.");
        }
        ProjectValidator.ValidateText(tag.Name);
    }

    /// <summary>验证完整标记集合及唯一身份，允许同名异色和同色异名。</summary>
    public static void Validate(ImmutableArray<SubtitleColorTag> tags)
    {
        if (tags.IsDefault || tags.Length > MAXIMUM_TAGS)
        {
            throw new InvalidDataException("The subtitle color tag collection is invalid or exceeds the count budget.");
        }
        var identities = new HashSet<Guid>();
        foreach (var tag in tags)
        {
            if (tag is null)
            {
                throw new InvalidDataException("A subtitle color tag cannot be null.");
            }
            Validate(tag);
            if (!identities.Add(tag.Id))
            {
                throw new InvalidDataException("Duplicate subtitle color tag ID.");
            }
        }
    }
}
