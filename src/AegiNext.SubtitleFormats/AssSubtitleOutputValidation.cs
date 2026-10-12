using AegiNext.Core.Projects;

namespace AegiNext.SubtitleFormats;

internal static class AssSubtitleOutputValidation
{
    internal static void ValidateStyle(SubtitleStyle style)
    {
        Number(style.FontSize, positive: true);
        Number(style.LetterSpacing);
        Number(style.StrokeWidth, nonnegative: true);
        Number(style.FillBlur, nonnegative: true);
        Number(style.StrokeBlur, nonnegative: true);
        Number(style.ShadowBlur, nonnegative: true);
        Number(style.Margins.Left, nonnegative: true);
        Number(style.Margins.Right, nonnegative: true);
        Number(style.Margins.Vertical, nonnegative: true);
        Number(style.ShadowOffset.X);
        Number(style.ShadowOffset.Y);
        if (style.Position is { } position)
        {
            Number(position.Offset.X);
            Number(position.Offset.Y);
        }
    }

    private static void Number(double value, bool nonnegative = false, bool positive = false)
    {
        if (!double.IsFinite(value) || nonnegative && value < 0 || positive && value <= 0)
        {
            throw new InvalidDataException("Derived dimensions in ASS output must be finite and valid.");
        }
    }
}
