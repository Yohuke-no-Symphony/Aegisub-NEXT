using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssStyleTable
{
    internal static Dictionary<Guid, AssExportStyle> Write(ImmutableArray<SubtitleLine> lines, StringBuilder output,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        var reserved = lines.Select(line => SafeName(line.StyleName)).ToHashSet(StringComparer.Ordinal);
        var originalNames = lines.Select(line => line.StyleName).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var nextSuffix = new Dictionary<string, int>(StringComparer.Ordinal);
        var styles = new Dictionary<(string Name, string Fields), AssExportStyle>();
        var names = new Dictionary<Guid, AssExportStyle>();
        foreach (var line in lines)
        {
            var exported = AssExportStyle.Create(line.StyleName, line.Style);
            var key = (line.StyleName, exported.Fields);
            if (!styles.TryGetValue(key, out var style))
            {
                var basis = SafeName(line.StyleName);
                var name = basis;
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
                style = exported with { Name = name };
                styles.Add(key, style);
                output.Append("Style: ").Append(name).Append(',').AppendLine(style.Fields);
            }
            names.Add(line.Id, style);
            if (style.Name != line.StyleName)
            {
                diagnostics.Add(new("Ass.StyleName", $"The ASS style name contains separators or surrounding whitespace, or conflicts with a different style of the same name. It was exported as '{style.Name}'; the project style name is unchanged.", SubtitleId: line.Id));
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
}
