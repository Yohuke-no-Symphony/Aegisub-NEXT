using System.Globalization;
using System.Text;
using Avalonia.Media;

namespace AegiNext.Desktop.Startup;

internal sealed class RecentProjectIcon
{
    private const uint HASH_OFFSET = 2166136261;
    private const uint HASH_PRIME = 16777619;
    private const double LIGHT_SATURATION = 0.50;
    private const double LIGHT_LIGHTNESS = 0.82;
    private const double DARK_SATURATION = 0.38;
    private const double DARK_LIGHTNESS = 0.32;

    internal RecentProjectIcon(string name)
    {
        Initials = GetInitials(name);
        var identity = name.Trim().Normalize(NormalizationForm.FormC);
        var hash = HASH_OFFSET;
        foreach (var value in Encoding.UTF8.GetBytes(identity))
        {
            hash = unchecked((hash ^ value) * HASH_PRIME);
        }

        var hue = hash % 360;
        Background = new(new HslColor(1, hue, LIGHT_SATURATION, LIGHT_LIGHTNESS).ToRgb());
        DarkBackground = new(new HslColor(1, hue, DARK_SATURATION, DARK_LIGHTNESS).ToRgb());
    }

    internal string Initials { get; }
    internal SolidColorBrush Background { get; }
    internal SolidColorBrush DarkBackground { get; }
    internal IBrush Foreground { get; } = Brushes.Black;
    internal IBrush DarkForeground { get; } = Brushes.White;

    private static string GetInitials(string name)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(name.Trim());
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        var initials = new List<string>(2);
        var characters = new List<string>(2);
        var startsWord = true;
        Rune? previous = null;
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            var current = Rune.GetRuneAt(element, 0);
            if (!Rune.IsLetterOrDigit(current))
            {
                startsWord = true;
                previous = null;
                continue;
            }

            if (characters.Count < 2)
            {
                characters.Add(element);
            }

            var nextIsLower = index + 1 < elements.Count && Rune.IsLower(Rune.GetRuneAt(elements[index + 1], 0));
            var camelBoundary = previous is { } prior && Rune.IsUpper(current) && (Rune.IsLower(prior) || nextIsLower);
            if ((startsWord || camelBoundary) && initials.Count < 2)
            {
                initials.Add(element);
            }

            startsWord = false;
            previous = current;
        }

        if (characters.Count == 0)
        {
            return elements.Count == 0 ? "?" : string.Concat(elements.Take(2));
        }
        return string.Concat(initials.Count >= 2 ? initials : characters).ToUpperInvariant();
    }
}
