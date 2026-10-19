using System.Security.Cryptography;
using AegiNext.Core.Fonts;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Ui.Tests;

internal static class StyleExchangeUiTestFonts
{
    internal static FontFamilyBundle Family { get; } = ReadFamily();
    internal static SystemFontCatalog Catalog { get; } = new([Family], SystemFontCatalog.Empty);

    internal static DesktopApplicationContext CreateContext(string directory, WorkbenchPreferences? preferences = null)
    {
        return new(new(directory), preferences, new SubtitleFontSelectionService(Catalog));
    }

    private static FontFamilyBundle ReadFamily()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        return new("Noto Sans", [new("NotoSans.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes])]);
    }
}
