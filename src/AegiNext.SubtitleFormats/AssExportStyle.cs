using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed record AssExportStyle(string Name, string Fields, ImmutableDictionary<string, string> Baseline,
    SubtitleMargins DialogueMargins)
{
    internal static AssExportStyle Create(string name, SubtitleStyle style, SubtitleMargins dialogueMargins = default)
    {
        if (style.FontFamily.Contains(',', StringComparison.Ordinal))
        {
            throw new InvalidDataException("ASS style font names cannot contain commas.");
        }
        var fontSize = AssFormatValues.Number(style.FontSize);
        var primary = AssFormatValues.Color(style.Fill);
        var outline = AssFormatValues.Color(style.Stroke);
        var shadowColor = AssFormatValues.Color(style.ShadowColor);
        var spacing = AssFormatValues.Number(style.LetterSpacing);
        var outlineWidth = AssFormatValues.Number(style.StrokeWidth);
        var shadowOffset = AssFormatValues.Number(style.ShadowOffset.Y);
        var alignment = AssFormatValues.Alignment(style.Alignment).ToString(CultureInfo.InvariantCulture);
        var marginLeft = AssFormatValues.Number(style.Margins.Left);
        var marginRight = AssFormatValues.Number(style.Margins.Right);
        var marginVertical = AssFormatValues.Number(style.Margins.Vertical);
        var fields = string.Create(CultureInfo.InvariantCulture,
            $"{style.FontFamily},{fontSize},{primary},{primary},{outline},{shadowColor},{(style.Bold ? -1 : 0)},{(style.Italic ? -1 : 0)},{(style.Underline ? -1 : 0)},{(style.Strikethrough ? -1 : 0)},100,100,{spacing},0,1,{outlineWidth},{shadowOffset},{alignment},{marginLeft},{marginRight},{marginVertical},1");
        var inheritedShadow = NonNegative(shadowOffset);
        var baseline = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        baseline.Add("fn", style.FontFamily);
        baseline.Add("fs", fontSize);
        baseline.Add("fsp", NonNegative(spacing));
        baseline.Add("b", style.Bold ? "1" : "0");
        baseline.Add("i", style.Italic ? "1" : "0");
        baseline.Add("u", style.Underline ? "1" : "0");
        baseline.Add("s", style.Strikethrough ? "1" : "0");
        AddColor(baseline, "1", primary);
        AddColor(baseline, "2", primary);
        AddColor(baseline, "3", outline);
        AddColor(baseline, "4", shadowColor);
        baseline.Add("bord", outlineWidth);
        baseline.Add("xshad", inheritedShadow);
        baseline.Add("yshad", inheritedShadow);
        baseline.Add("an", alignment);
        baseline.Add("q", "1");
        baseline.Add("blur", "0");
        baseline.Add("fscx", "100");
        baseline.Add("fscy", "100");
        baseline.Add("frz", "0");
        baseline.Add("frx", "0");
        baseline.Add("fry", "0");
        return new(name, fields, baseline.ToImmutable(), dialogueMargins);
    }

    private static string NonNegative(string value) => AssFormatValues.Number(Math.Max(AssFormatValues.Number(value), 0));

    private static void AddColor(ImmutableDictionary<string, string>.Builder baseline, string channel, string value)
    {
        var color = AssFormatValues.Color(value);
        baseline.Add(channel + "c", AssFormatValues.Color(color, false));
        baseline.Add(channel + "a", AssFormatValues.Alpha(color));
    }
}
