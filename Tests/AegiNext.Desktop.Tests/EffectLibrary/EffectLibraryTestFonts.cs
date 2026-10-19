using System.Security.Cryptography;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Startup;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests.EffectLibrary;

internal static class EffectLibraryTestFonts
{
    internal static FontFamilyBundle Family { get; } = ReadFamily();
    internal static SystemFontCatalog Catalog { get; } = new([Family], SystemFontCatalog.Empty);

    internal static DesktopApplicationContext CreateOwner(string directory)
    {
        return new(new(directory), new(), new SubtitleFontSelectionService(Catalog));
    }

    internal static EffectLibraryTransferService CreateTransfer(EffectLibraryRepository repository)
    {
        return new(repository, new FontFamilySource(() => Catalog), new FontFamilyValidator());
    }

    private static FontFamilyBundle ReadFamily()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        return new("Noto Sans", [new("NotoSans.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes])]);
    }
}
