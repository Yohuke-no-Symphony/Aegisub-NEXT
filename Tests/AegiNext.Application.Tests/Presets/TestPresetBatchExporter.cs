using AegiNext.Application.Presets;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Tests.Presets;

internal static class TestPresetBatchExporter
{
    internal static Task ExportStylesAsync(IEnumerable<SubtitleStylePreset> styles, string directory, CancellationToken cancellationToken = default)
        => PresetBatchExporter.ExportStylesAsync(styles.Select(TestStyleLibraryStorage.Captured), directory, cancellationToken);

    internal static Task ExportStylesAsync(IEnumerable<SubtitleStylePreset> styles, string directory, Action beforePublish,
        CancellationToken cancellationToken = default)
        => PresetBatchExporter.ExportStylesAsync(styles.Select(TestStyleLibraryStorage.Captured), directory, beforePublish, cancellationToken);

    internal static Task ExportEffectsAsync(IEnumerable<EffectScriptPreset> effects, string directory, CancellationToken cancellationToken = default)
        => PresetBatchExporter.ExportEffectsAsync(effects, directory, cancellationToken);
}
