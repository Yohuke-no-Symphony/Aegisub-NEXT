using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssBlurConversion
{
    internal static readonly double SigmaPerUnit = 2 / Math.Sqrt(Math.Log(256));

    internal static double Sigma(SubtitleStyle style)
    {
        return style.StrokeWidth > 0 ? style.StrokeBlur : style.FillBlur;
    }

    internal static double Value(SubtitleStyle style, bool projection)
    {
        return projection ? style.ShadowBlur : Sigma(style) / SigmaPerUnit;
    }
}
