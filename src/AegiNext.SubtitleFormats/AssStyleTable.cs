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
        var names = new Dictionary<Guid, AssExportStyle>();
        var groups = lines.GroupBy(line => (line.StyleName,
            Fields: AssExportStyle.Create(line.StyleName, line.Style with { Margins = default }).Fields));
        foreach (var group in groups)
        {
            var first = group.First();
            var margins = first.Style.Margins;
            var left = AssFormatValues.Number(margins.Left);
            var right = AssFormatValues.Number(margins.Right);
            var vertical = AssFormatValues.Number(margins.Vertical);
            var sharedMargins = new SubtitleMargins(
                group.All(line => AssFormatValues.Number(line.Style.Margins.Left) == left) ? margins.Left : 0,
                group.All(line => AssFormatValues.Number(line.Style.Margins.Right) == right) ? margins.Right : 0,
                group.All(line => AssFormatValues.Number(line.Style.Margins.Vertical) == vertical) ? margins.Vertical : 0);
            var basis = SafeName(first.StyleName);
            var name = basis;
            if (used.Contains(name) || name != first.StyleName && originalNames.Contains(name))
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
            var style = AssExportStyle.Create(name, first.Style with { Margins = sharedMargins });
            output.Append("Style: ").Append(name).Append(',').AppendLine(style.Fields);
            left = AssFormatValues.Number(sharedMargins.Left);
            right = AssFormatValues.Number(sharedMargins.Right);
            vertical = AssFormatValues.Number(sharedMargins.Vertical);
            foreach (var line in group)
            {
                var dialogueMargins = new SubtitleMargins(
                    AssFormatValues.Number(line.Style.Margins.Left) == left ? 0 : line.Style.Margins.Left,
                    AssFormatValues.Number(line.Style.Margins.Right) == right ? 0 : line.Style.Margins.Right,
                    AssFormatValues.Number(line.Style.Margins.Vertical) == vertical ? 0 : line.Style.Margins.Vertical);
                names.Add(line.Id, dialogueMargins == default ? style : style with { DialogueMargins = dialogueMargins });
            }
        }
        foreach (var line in lines)
        {
            var name = names[line.Id].Name;
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
}
