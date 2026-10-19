using AegiNext.Application.Presets;

namespace AegiNext.Application.Tests.Presets;

internal static class TestEffectLibraryPackageStore
{
    internal const int MAXIMUM_FILE_BYTES = EffectLibraryPackageStore.MAXIMUM_FILE_BYTES;

    internal static byte[] Serialize(EffectLibraryPackage package)
        => EffectLibraryPackageStore.Serialize(WithFonts(package));

    internal static Task SaveAsync(EffectLibraryPackage package, string path, CancellationToken cancellationToken = default)
        => EffectLibraryPackageStore.SaveAsync(WithFonts(package), path, cancellationToken);

    internal static Task<EffectLibraryPackage> LoadAsync(string path, CancellationToken cancellationToken = default)
        => EffectLibraryPackageStore.LoadAsync(path, cancellationToken);

    internal static EffectLibraryPackage Deserialize(ReadOnlySpan<byte> bytes) => EffectLibraryPackageStore.Deserialize(bytes);

    private static EffectLibraryPackage WithFonts(EffectLibraryPackage package)
        => package.FontFamilies.IsEmpty ? package with { FontFamilies = TestStylePackageStore.Package(package.Styles).FontFamilies } : package;
}
