using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using Avalonia.Controls;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class PresetDeletionTestHost : IDisposable
{
    private readonly SettingsWindowCoordinator? settings;
    private readonly EffectLibraryWindowCoordinator? library;

    internal PresetDeletionTestHost(DesktopApplicationContext context, PresetDeletionDialogStub dialogs,
        PresetDeletionTestPage page)
    {
        if (page == PresetDeletionTestPage.EXPORT_PRESETS)
        {
            settings = new(context, _ => dialogs);
        }
        else
        {
            library = new(context, _ => dialogs);
        }
    }

    internal Window? Window => (Window?)library?.Window ?? settings?.Window;
    internal Task DeletionCompletion => library?.DeletionCompletion ?? settings!.ExportCompletion;

    internal Task OpenAsync(Window owner, PresetDeletionTestPage page)
    {
        if (settings is not null)
        {
            return settings.OpenAsync(owner, page: SettingsPage.EXPORT_PRESETS);
        }
        return library!.OpenAsync(owner, page: page == PresetDeletionTestPage.STYLES
            ? EffectLibraryPage.STYLES : EffectLibraryPage.EFFECTS);
    }

    public void Dispose()
    {
        library?.Dispose();
        settings?.Dispose();
    }
}
