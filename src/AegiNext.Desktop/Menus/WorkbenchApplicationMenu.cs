using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace AegiNext.Desktop.Menus;

internal sealed class WorkbenchApplicationMenu : IDisposable
{
    private readonly WorkbenchMenuCatalog catalog;
    private readonly NativeMenu menu;
    private readonly NativeMenuItem settings;
    private readonly NativeMenuItem effectLibrary;
    private readonly WorkbenchMenuGroup effectLibraryDefinition;
    private readonly Dictionary<WorkbenchCommand, NativeMenuItem> libraryItems = [];

    internal WorkbenchApplicationMenu(Avalonia.Application application, WorkbenchMenuCatalog catalog,
        Func<string, Bitmap> iconProvider)
    {
        this.catalog = catalog;
        menu = NativeMenu.GetMenu(application) ?? new NativeMenu();
        if (NativeMenu.GetMenu(application) is null)
        {
            NativeMenu.SetMenu(application, menu);
        }

        settings = new() { Command = catalog.GetCommand(WorkbenchCommand.OPEN_SETTINGS) };
        menu.Items.Insert(0, settings);
        var libraryMenu = new NativeMenu();
        foreach (var command in new[] { WorkbenchCommand.OPEN_STYLE_LIBRARY,
                     WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY, WorkbenchCommand.OPEN_EFFECT_LIBRARY_TRANSFER })
        {
            var item = new NativeMenuItem { Command = catalog.GetCommand(command) };
            libraryItems.Add(command, item);
            libraryMenu.Items.Add(item);
        }
        effectLibraryDefinition = WorkbenchMenuCatalog.Groups.Single(group => group.Key == "Edit").Children.Single(group => group.Key == "EffectLibrary");
        effectLibrary = new() { Menu = libraryMenu, Icon = iconProvider(effectLibraryDefinition.IconKey!) };
        menu.Items.Insert(1, effectLibrary);
        catalog.Changed += OnCatalogChanged;
        Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        catalog.Changed -= OnCatalogChanged;
        menu.Items.Remove(settings);
        menu.Items.Remove(effectLibrary);
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        settings.Header = Localization.Get("Settings." + (WorkbenchCommand.OPEN_SETTINGS.ToString()));
        settings.Gesture = catalog.GetGesture(WorkbenchCommand.OPEN_SETTINGS);
        effectLibrary.Header = Localization.Get(effectLibraryDefinition.LabelKey);
        foreach (var entry in libraryItems)
        {
            entry.Value.Header = WorkbenchMenuCatalog.GetLabel(entry.Key);
            entry.Value.Gesture = catalog.GetGesture(entry.Key);
        }
    }
}
