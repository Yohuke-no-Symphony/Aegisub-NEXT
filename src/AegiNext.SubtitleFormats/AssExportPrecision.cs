using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssExportPrecision
{
    private const double COLOR_TOLERANCE = 1e-12;

    internal static void AddStyle(SubtitleStyle style, Guid id, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics,
        bool includeBlur = true)
    {
        AddColor(style.Fill, id, diagnostics);
        AddColor(style.Stroke, id, diagnostics);
        AddColor(style.ShadowColor, id, diagnostics);
        AddNumbers(id, diagnostics, style.FontSize, style.StrokeWidth, style.LetterSpacing,
            style.ShadowOffset.X, style.ShadowOffset.Y,
            style.Margins.Left, style.Margins.Right, style.Margins.Vertical);
        if (includeBlur)
        {
            AddNumbers(id, diagnostics, AssBlurConversion.Value(style, false));
        }
    }

    internal static void AddNumbers(Guid id, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics, params double[] values)
    {
        AddNumbers(id, diagnostics, null, values);
    }

    internal static void AddNumbers(Guid id, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics,
        ISet<string>? reported, params double[] values)
    {
        if (values.Any(value => !AssFormatValues.Number(AssFormatValues.Number(value)).Equals(value)) &&
            (reported is null || reported.Add("Ass.NumberPrecision")))
        {
            diagnostics.Add(new("Ass.NumberPrecision", "Exported ASS numbers use up to 9 decimal places. Some layout values were approximated.", SubtitleId: id));
        }
    }

    internal static void AddTime(MediaTime start, MediaTime end, Guid id, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics)
    {
        if (!ExactCentiseconds(start) || !ExactCentiseconds(end))
        {
            diagnostics.Add(new("Ass.TimeQuantization", "ASS dialogue times support only centiseconds. Start times were rounded down and end times were rounded up.", SubtitleId: id));
        }
    }

    internal static void AddBlurRange(double amount, Guid id, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics,
        ISet<string>? reported = null)
    {
        if (AssFormatValues.Number(AssFormatValues.Number(amount)) <= 100 ||
            diagnostics.Any(diagnostic => diagnostic.Code == "Ass.BlurRange" && diagnostic.SubtitleId == id) ||
            reported is not null && !reported.Add("Ass.BlurRange"))
        {
            return;
        }
        diagnostics.Add(new("Ass.BlurRange", "Exported ASS edge blur exceeds the libass limit of 100. Players using this limit will clamp blur and cannot preserve the original blur appearance.", SubtitleId: id));
    }

    private static bool ExactCentiseconds(MediaTime time)
    {
        return new MediaTime(time.ToTimestamp(new(1, 100), MediaTimeRounding.TO_EVEN).Value, 100) == time;
    }

    internal static void AddColor(SceneColor color, Guid id, ImmutableArray<SubtitleFormatDiagnostic>.Builder diagnostics,
        int componentMask = 0, ISet<string>? reported = null)
    {
        var serialized = AssFormatValues.Color(AssFormatValues.Color(color));
        if ((componentMask == 0 || (componentMask & 7) != 0) &&
            color.Red is >= 0 and <= 1 && color.Green is >= 0 and <= 1 && color.Blue is >= 0 and <= 1 &&
            (Math.Abs(color.Red - serialized.Red) > COLOR_TOLERANCE || Math.Abs(color.Green - serialized.Green) > COLOR_TOLERANCE ||
                Math.Abs(color.Blue - serialized.Blue) > COLOR_TOLERANCE) &&
            (reported is null || reported.Add("Ass.ColorPrecision")))
        {
            diagnostics.Add(new("Ass.ColorPrecision", "ASS colors support only 8-bit sRGB. Some colors were approximated.", SubtitleId: id));
        }
        if ((componentMask == 0 || (componentMask & 8) != 0) &&
            Math.Abs(color.Alpha - serialized.Alpha) > COLOR_TOLERANCE &&
            (reported is null || reported.Add("Ass.AlphaPrecision")))
        {
            diagnostics.Add(new("Ass.AlphaPrecision", "ASS alpha supports only 8-bit precision. Some alpha values were approximated.", SubtitleId: id));
        }
    }
}
