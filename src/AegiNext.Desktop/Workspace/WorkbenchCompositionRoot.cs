using AegiNext.Application;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Desktop.Startup;
using AegiNext.Export;

namespace AegiNext.Desktop.Workspace;

internal static class WorkbenchCompositionRoot
{
    internal static WorkbenchSession Create(IWorkbenchDialogService dialogs,
        Func<Action<VideoPreviewUpdate>, VideoPreviewController>? controllerFactory = null,
        IWorkbenchExportService? exportService = null,
        WorkbenchStartupPreferences? startup = null,
        DesktopApplicationContext? applicationContext = null)
    {
        var editor = new ProjectEditor();
        return new(dialogs, controllerFactory, editor: editor, preferencesStore: startup?.Store,
            exportService: exportService ?? new VideoWorkbenchExportService(new VideoExporter(), new SubtitleMuxExporter()),
            initialPreferences: startup?.Preferences, applicationContext: applicationContext);
    }

    internal static void ApplyLanguagePreference(string languageId)
    {
        var selected = string.Equals(languageId, "system", StringComparison.OrdinalIgnoreCase)
            ? "system"
            : Localization.KnownLanguages.FirstOrDefault(info =>
                string.Equals(info.LanguageID, languageId, StringComparison.OrdinalIgnoreCase))?.LanguageID ?? "en-US";
        if (Localization.SelectedLanguageID != selected)
        {
            Localization.SetLanguage(selected);
        }
    }
}
