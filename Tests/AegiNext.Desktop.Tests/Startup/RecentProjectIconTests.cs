using AegiNext.Desktop.Startup;
using Avalonia.Media;

namespace AegiNext.Desktop.Tests.Startup;

public sealed class RecentProjectIconTests
{
    [Theory]
    [InlineData("Alpha Beta", 299)]
    [InlineData("中文 Project 123", 20)]
    [InlineData("Café", 249)]
    public void NamesKeepTheirKnownHueAcrossInstances(string name, double expectedHue)
    {
        var first = new RecentProjectIcon(name);
        var second = new RecentProjectIcon(name);

        Assert.Equal(first.Background.Color, second.Background.Color);
        Assert.Equal(first.DarkBackground.Color, second.DarkBackground.Color);
        Assert.InRange(first.Background.Color.ToHsl().H, expectedHue - 1, expectedHue + 1);
        Assert.InRange(first.DarkBackground.Color.ToHsl().H, expectedHue - 1, expectedHue + 1);
    }

    [Theory]
    [InlineData("Café", "Cafe\u0301")]
    [InlineData("项目", " 项目 ")]
    public void EquivalentNamesKeepTheSameColors(string firstName, string secondName)
    {
        var first = new RecentProjectIcon(firstName);
        var second = new RecentProjectIcon(secondName);

        Assert.Equal(first.Background.Color, second.Background.Color);
        Assert.Equal(first.DarkBackground.Color, second.DarkBackground.Color);
    }

    [Fact]
    public void ThemePalettesKeepInitialsReadableThroughoutTheHueRange()
    {
        var hueBands = new HashSet<int>();
        for (var index = 0; index < 5000; index++)
        {
            var icon = new RecentProjectIcon(FormattableString.Invariant($"Project {index}"));
            var light = icon.Background.Color;
            var dark = icon.DarkBackground.Color;
            hueBands.Add((int)(dark.ToHsl().H / 10) % 36);

            Assert.InRange(light.ToHsl().L, 0.80, 0.84);
            Assert.InRange(dark.ToHsl().L, 0.30, 0.34);
            Assert.InRange(light.ToHsl().S, 0.47, 0.53);
            Assert.InRange(dark.ToHsl().S, 0.35, 0.41);
            Assert.True(Contrast(light, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Foreground).Color) >= 4.5);
            Assert.True(Contrast(dark, Assert.IsAssignableFrom<ISolidColorBrush>(icon.DarkForeground).Color) >= 4.5);
        }

        Assert.Equal(36, hueBands.Count);
    }

    private static double Contrast(Color first, Color second)
    {
        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double Luminance(Color color) =>
        0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);

    private static double Linearize(byte component)
    {
        var value = component / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
