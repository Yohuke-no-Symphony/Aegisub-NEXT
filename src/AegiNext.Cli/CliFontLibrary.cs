using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Rendering.Fonts;
using SkiaSharp;

namespace AegiNext.Cli;

internal sealed class CliFontLibrary(string? preferencesDirectory = null)
{
    internal Task<ImmutableArray<FontFamilyBundle>> LoadFamiliesAsync(CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(preferencesDirectory ?? Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AegiNext"));
        return EffectLibraryRepository.LoadFontFamiliesAsync(Path.Combine(root, "effect-library"), Path.Combine(root, "fonts"),
            cancellationToken);
    }

    internal async Task<SystemFontCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var families = await LoadFamiliesAsync(cancellationToken).ConfigureAwait(false);
        var system = new SystemFontCatalog(SKFontManager.Default, cancellationToken: cancellationToken);
        var catalog = families.IsEmpty ? system : new(families, system);
        cancellationToken.ThrowIfCancellationRequested();
        return catalog;
    }
}
