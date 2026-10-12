using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.SubtitleFormats;

internal static class AssFormatValues
{
    internal static double Number(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
        {
            throw new InvalidDataException("Invalid ASS numeric value.");
        }
        return number;
    }

    internal static int Integer(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidDataException("Invalid ASS integer.");
        }
        return number;
    }

    internal static string Number(double value) => value.ToString("0.#########", CultureInfo.InvariantCulture);

    internal static TextAlignment Alignment(int value)
    {
        if (value is < 1 or > 9)
        {
            throw new InvalidDataException("ASS alignment must be between 1 and 9.");
        }
        return (TextAlignment)((2 - (value - 1) / 3) * 3 + (value - 1) % 3);
    }

    internal static int Alignment(TextAlignment value) => (2 - (int)value / 3) * 3 + (int)value % 3 + 1;

    internal static TextAlignment LegacyAlignment(int value)
    {
        return value switch
        {
            1 => TextAlignment.BOTTOM_LEFT,
            2 => TextAlignment.BOTTOM_CENTER,
            3 => TextAlignment.BOTTOM_RIGHT,
            5 => TextAlignment.TOP_LEFT,
            6 => TextAlignment.TOP_CENTER,
            7 => TextAlignment.TOP_RIGHT,
            9 => TextAlignment.MIDDLE_LEFT,
            10 => TextAlignment.MIDDLE_CENTER,
            11 => TextAlignment.MIDDLE_RIGHT,
            _ => throw new InvalidDataException("SSA alignment must be 1, 2, 3, 5, 6, 7, 9, 10, or 11.")
        };
    }

    internal static SceneColor Color(string value, SceneColor? previous = null)
    {
        var token = value.Trim().TrimEnd('&');
        uint bits;
        if (token.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bits))
            {
                throw new InvalidDataException("Invalid ASS color.");
            }
        }
        else if (long.TryParse(token, CultureInfo.InvariantCulture, out var signed))
        {
            bits = unchecked((uint)signed);
        }
        else
        {
            throw new InvalidDataException("Invalid ASS color.");
        }
        return new(Linear((bits & 255) / 255.0), Linear((bits >> 8 & 255) / 255.0),
            Linear((bits >> 16 & 255) / 255.0), previous?.Alpha ?? 1 - (bits >> 24) / 255.0);
    }

    internal static double Alpha(string value)
    {
        var token = value.Trim().TrimEnd('&');
        if (token.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            token = token[2..];
        }
        if (!byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha))
        {
            throw new InvalidDataException("Invalid ASS alpha value.");
        }
        return 1 - alpha / 255.0;
    }

    internal static string Color(SceneColor value, bool includeAlpha = true)
    {
        var bits = (uint)(Byte(value.Blue) << 16 | Byte(value.Green) << 8 | Byte(value.Red));
        if (includeAlpha)
        {
            bits |= (uint)Math.Round((1 - value.Alpha) * 255) << 24;
        }
        return "&H" + bits.ToString(includeAlpha ? "X8" : "X6", CultureInfo.InvariantCulture) + "&";
    }

    internal static string Alpha(SceneColor value) => "&H" + ((byte)Math.Round((1 - value.Alpha) * 255)).ToString("X2", CultureInfo.InvariantCulture) + "&";

    internal static MediaTime ParseTime(string value)
    {
        var parts = value.Split(':', '.');
        if (parts.Length != 4 || parts[0].Length == 0 || parts[1].Length != 2 || parts[2].Length != 2 || parts[3].Length != 2 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes >= 60 ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds >= 60 ||
            !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var fraction))
        {
            throw new InvalidDataException("ASS times must use H:MM:SS.cc format.");
        }
        try
        {
            return new(checked(((hours * 60 + minutes) * 60 + seconds) * 100 + fraction), 100);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("ASS time is out of range.", exception);
        }
    }

    internal static string Time(MediaTime value, MediaTimeRounding rounding)
    {
        var count = value.ToTimestamp(new(1, 100), rounding).Value;
        return string.Create(CultureInfo.InvariantCulture, $"{count / 360000}:{count / 6000 % 60:00}:{count / 100 % 60:00}.{count % 100:00}");
    }

    internal static void CheckText(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ProjectValidator.ValidateText(source);
    }

    private static double Linear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static int Byte(double value)
    {
        var bounded = Math.Clamp(value, 0, 1);
        return (int)Math.Round(255 * (bounded <= 0.0031308 ? bounded * 12.92 : 1.055 * Math.Pow(bounded, 1 / 2.4) - 0.055));
    }
}
