using AegiNext.Application.Presets;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Tests.Presets;

internal static class TestStyleLibraryStorage
{
    internal static string ManifestPath(string directory) => Path.Combine(directory, "manifest.json");

    internal static CapturedSubtitleStyle Captured(SubtitleStylePreset preset)
        => new(preset, [TestStylePackageStore.Family(preset.Style.FontFamily)]);

    internal static async Task SaveAsync(SubtitleStylePresetCollection styles, string directory)
    {
        using var repository = new EffectLibraryRepository(directory);
        await repository.UpdateAsync(Guid.Empty, current => current with
        {
            Styles = styles, FontFamilies = TestStylePackageStore.Package(styles).FontFamilies
        });
    }

    internal static async Task<SubtitleStylePresetCollection> LoadAsync(string directory)
    {
        using var repository = new EffectLibraryRepository(directory);
        await repository.LoadAsync();
        return repository.Snapshot.Styles;
    }
}
