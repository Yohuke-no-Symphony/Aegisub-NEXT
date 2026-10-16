using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal SubtitleFontSelectionService Fonts => applicationContext.Fonts;
    private readonly Rendering.LayerPlacementResolver layerPlacement = new();
    private Exception? placementDiagnostic;

    internal SubtitlePositionMeasurement MeasureStylePosition(SubtitleStylePreset preset)
    {
        var text = SelectedCue?.Text ?? Localization.Get("Workbench.SubtitlePreviewText");
        return MeasureStylePosition(preset, text);
    }

    internal SubtitlePositionMeasurement MeasureStylePosition(SubtitleStylePreset preset, string text)
    {
        var document = Editor.Snapshot;
        return Rendering.SubtitleStylePositionMeasurer.Measure(preset, document.Width, document.Height, text, Fonts.Catalog);
    }
    internal void NotifyStyleLibraryChanged()
    {
        StyleLibraryChanged?.Invoke(this, EventArgs.Empty);
        ViewModel.RefreshCommands();
    }
    internal Task ApplySelectedStyleAsync()
    {
        return HasStyleSelection(LayerKind.SUBTITLE) && ViewModel.Styles.Subtitle.SelectedPreset is { } selected
            ? styles.ApplyAsync(styleLibrary.Snapshot.Presets.Single(value => value.Id == selected.Id))
            : Task.CompletedTask;
    }
}
