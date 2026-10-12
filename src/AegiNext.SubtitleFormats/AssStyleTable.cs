using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssStyleTable
{
    internal static Dictionary<Guid, string> Write(ImmutableArray<SubtitleLine> lines, StringBuilder output,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var reserved = lines.Select(line => SafeName(line.StyleName)).ToHashSet(StringComparer.Ordinal);
        var originalNames = lines.Select(line => line.StyleName).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var nextSuffix = new Dictionary<string, int>(StringComparer.Ordinal);
        var styles = new Dictionary<(string Name, string Fields), string>();
        var names = new Dictionary<Guid, string>();
        foreach (var line in lines)
        {
            var fields = Fields(line.Style);
            var key = (line.StyleName, fields);
            if (!styles.TryGetValue(key, out var name))
            {
                var basis = SafeName(line.StyleName);
                name = basis;
                if (used.Contains(name) || name != line.StyleName && originalNames.Contains(name))
                {
                    var suffix = nextSuffix.GetValueOrDefault(basis, 2);
                    do
                    {
                        name = SuffixedName(basis, suffix++);
                    }
                    while (reserved.Contains(name) || used.Contains(name));
                    nextSuffix[basis] = suffix;
                }
                used.Add(name);
                styles.Add(key, name);
                output.Append("Style: ").Append(name).Append(',').AppendLine(fields);
            }
            names.Add(line.Id, name);
            if (name != line.StyleName)
            {
                diagnostics.Add(new("Ass.StyleName", $"The ASS style name contains separators or surrounding whitespace, or conflicts with a different style of the same name. It was exported as '{name}'; the project style name is unchanged.", SubtitleId: line.Id));
            }
        }
        return names;
    }

    private static string SafeName(string name)
    {
        return name.Replace(',', '_').Trim();
    }

    private static string SuffixedName(string name, int suffix)
    {
        var ending = "_" + suffix.ToString(CultureInfo.InvariantCulture);
        var length = Math.Min(name.Length, 1024 - ending.Length);
        if (char.IsHighSurrogate(name[length - 1]))
        {
            length--;
        }
        return name[..length] + ending;
    }

    private static string Fields(SubtitleStyle style)
    {
        if (style.FontFamily.Contains(',', StringComparison.Ordinal))
        {
            throw new InvalidDataException("ASS style font names cannot contain commas.");
        }
        return string.Create(CultureInfo.InvariantCulture,
            $"{style.FontFamily},{AssFormatValues.Number(style.FontSize)},{AssFormatValues.Color(style.Fill)},{AssFormatValues.Color(style.Fill)},{AssFormatValues.Color(style.Stroke)},{AssFormatValues.Color(style.ShadowColor)},{(style.Bold ? -1 : 0)},{(style.Italic ? -1 : 0)},{(style.Underline ? -1 : 0)},{(style.Strikethrough ? -1 : 0)},100,100,{AssFormatValues.Number(style.LetterSpacing)},0,1,{AssFormatValues.Number(style.StrokeWidth)},{AssFormatValues.Number(style.ShadowOffset.Y)},{AssFormatValues.Alignment(style.Alignment)},{AssFormatValues.Number(style.Margins.Left)},{AssFormatValues.Number(style.Margins.Right)},{AssFormatValues.Number(style.Margins.Vertical)},1");
    }
}
