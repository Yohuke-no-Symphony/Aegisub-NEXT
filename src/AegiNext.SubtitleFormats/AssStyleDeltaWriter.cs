using System.Globalization;
using System.Text;
using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal sealed class AssStyleDeltaWriter(AssExportStyle style)
{
    private readonly Dictionary<string, string> values = new(style.Baseline, StringComparer.Ordinal);

    internal string StyleTags(SubtitleStyle target, string blur, bool includeFill, bool includeVisual)
    {
        var result = new StringBuilder();
        Add(result, "fn", target.FontFamily);
        Add(result, "fs", AssFormatValues.Number(target.FontSize));
        Add(result, "fsp", AssFormatValues.Number(target.LetterSpacing));
        var weight = !target.FontAssetId.HasValue && target.FontVariant is { Weight: >= 100 and <= 1000 } variant
            ? variant.Weight : target.Bold ? 1 : 0;
        Add(result, "b", weight.ToString(CultureInfo.InvariantCulture));
        Add(result, "i", target.Italic || !target.FontAssetId.HasValue && target.FontVariant is { Italic: true } ? "1" : "0");
        Add(result, "u", target.Underline ? "1" : "0");
        Add(result, "s", target.Strikethrough ? "1" : "0");
        if (includeFill)
        {
            AddColor(result, "1", target.Fill);
        }
        if (includeVisual)
        {
            AddVisual(result, target, blur);
        }
        Add(result, "q", target.WrapMode == SubtitleWrapMode.NO_WRAP ? "2" : "1");
        return result.ToString();
    }

    internal string ColorTags(string channel, SceneColor color)
    {
        var result = new StringBuilder();
        AddColor(result, channel, color);
        return result.ToString();
    }

    internal string VisualTags(SubtitleStyle target, string blur)
    {
        var result = new StringBuilder();
        AddVisual(result, target, blur);
        return result.ToString();
    }

    private void AddVisual(StringBuilder result, SubtitleStyle target, string blur)
    {
        AddColor(result, "3", target.Stroke);
        AddColor(result, "4", target.ShadowColor);
        Add(result, "bord", AssFormatValues.Number(target.StrokeWidth));
        Add(result, "xshad", AssFormatValues.Number(target.ShadowOffset.X));
        Add(result, "yshad", AssFormatValues.Number(target.ShadowOffset.Y));
        Add(result, "blur", blur);
    }

    private void AddColor(StringBuilder result, string channel, SceneColor color)
    {
        Add(result, channel + "c", AssFormatValues.Color(color, false));
        Add(result, channel + "a", AssFormatValues.Alpha(color));
    }

    private void Add(StringBuilder result, string name, string value)
    {
        if (values[name] == value)
        {
            return;
        }
        result.Append('\\').Append(name).Append(value);
        values[name] = value;
    }
}
