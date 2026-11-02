using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

public static partial class AssSubtitleFormat
{
    /// <summary>仅提取 ASS v4+ 样式及脚本信息，保留全部声明的样式和源参考尺寸，不读取对白。</summary>
    public static AssStyleImportResult ParseStyles(string source)
    {
        AssFormatValues.CheckText(source);
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rows = ReadStyleRows(source, info);
        if (rows.Count == 0)
        {
            throw new InvalidDataException("ASS must contain a nonempty V4+ Styles section.");
        }
        if (info.TryGetValue("ScriptType", out var scriptType) && !scriptType.Equals("v4.00+", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Only ASS v4+ styles are supported.");
        }
        var diagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
        AssScriptInfoContext script;
        SubtitleScalingSettings scaling;
        try
        {
            script = ReadScriptInfo(info, null, null, diagnostics);
            scaling = new() { ReferenceWidth = script.Width, ReferenceHeight = script.Height };
            ProjectValidator.ValidateSubtitleScaling(scaling);
        }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException("ASS Script Info: " + error.Message, error);
        }
        if (script.WrapStyle is 0 or 3)
        {
            diagnostics.Add(new("Ass.WrapModeApproximation", "ASS balanced smart wrapping was converted to native natural wrapping. Line-width distribution may differ."));
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var styles = ImmutableArray.CreateBuilder<AssImportedStyle>();
        foreach (var row in rows)
        {
            var name = Get(row.Fields, "Name", string.Empty);
            try
            {
                ProjectValidator.ValidateSubtitleStyleName(name);
                if (!names.Add(name))
                {
                    throw new InvalidDataException("ASS style names are duplicated.");
                }
                var styleDiagnostics = ImmutableArray.CreateBuilder<SubtitleFormatDiagnostic>();
                var definition = ParseStyle(row.Fields, script.ScaleX, script.ScaleY, script.WrapStyle,
                    script.Resolution, styleDiagnostics, scaling);
                AddStyleLossDiagnostics(row.Fields, definition, styleDiagnostics);
                styles.Add(new(name, definition.Style, styleDiagnostics.Select(diagnostic => diagnostic with
                {
                    SourceStart = row.SourceStart,
                    SourceLength = row.SourceLength
                }).ToImmutableArray()));
            }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"ASS style '{name}' at line {row.LineNumber}: {error.Message}", error);
            }
        }
        AddMissingResolutionDiagnostic(script, true, diagnostics);
        return new(styles.ToImmutable(), diagnostics.ToImmutable());
    }

    private static List<AssStyleSourceRow> ReadStyleRows(string source, Dictionary<string, string> info)
    {
        var rows = new List<AssStyleSourceRow>();
        var section = string.Empty;
        string[]? format = null;
        var offset = 0;
        var lineNumber = 0;
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } original)
        {
            var sourceStart = offset;
            offset += original.Length;
            if (offset < source.Length && source[offset] == '\r')
            {
                offset++;
            }
            if (offset < source.Length && source[offset] == '\n')
            {
                offset++;
            }
            lineNumber++;
            var row = lineNumber == 1 ? original.TrimStart('\uFEFF') : original;
            var trimmed = row.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith(';'))
            {
                continue;
            }
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                section = trimmed;
                continue;
            }
            var isInfo = section.Equals("[Script Info]", StringComparison.OrdinalIgnoreCase);
            if (!isInfo && !section.Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var content = row.TrimStart();
            var colon = content.IndexOf(':');
            if (colon < 0)
            {
                throw new InvalidDataException($"ASS line {lineNumber} is missing a field separator.");
            }
            var key = content[..colon].Trim();
            var value = content[(colon + 1)..].TrimStart();
            if (isInfo)
            {
                info[key] = value;
                continue;
            }
            try
            {
                if (key.Equals("Format", StringComparison.OrdinalIgnoreCase))
                {
                    format = ParseFormat(value);
                }
                else if (key.Equals("Style", StringComparison.OrdinalIgnoreCase))
                {
                    if (format is null)
                    {
                        throw new InvalidDataException("ASS styles are missing a Format declaration.");
                    }
                    if (value.Count(character => character == ',') != format.Length - 1)
                    {
                        throw new InvalidDataException("ASS field counts do not match.");
                    }
                    rows.Add(new(ParseFields(value, format), sourceStart, original.Length, lineNumber));
                }
            }
            catch (InvalidDataException error)
            {
                throw new InvalidDataException($"ASS style table at line {lineNumber}: {error.Message}", error);
            }
        }
        return rows;
    }

    private static void AddStyleLossDiagnostics(Dictionary<string, string> fields, AssStyleDefinition definition,
        ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (definition.Scale != new ScenePoint(1, 1))
        {
            diagnostics.Add(new("Ass.StyleScale", "ASS style ScaleX and ScaleY are layer transforms and were not saved in the subtitle style."));
        }
        if (!definition.Rotation.Equals(0d))
        {
            diagnostics.Add(new("Ass.StyleRotation", "ASS style Angle is a layer transform and was not saved in the subtitle style."));
        }
        if (AssFormatValues.Integer(Get(fields, "BorderStyle", "1")) != 1)
        {
            diagnostics.Add(new("Ass.StyleGeometry", "The ASS style background box was not imported."));
        }
        if (fields.ContainsKey("SecondaryColour"))
        {
            diagnostics.Add(new("Ass.StyleSecondaryColor", "The ASS karaoke secondary color was not saved in the subtitle style."));
        }
    }
}
