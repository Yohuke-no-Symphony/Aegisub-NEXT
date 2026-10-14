using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests;

public sealed class EffectLibraryMenuTests
{
    [Fact]
    public void EffectLibraryHasIndependentEditorAndTransferCommands()
    {
        var edit = WorkbenchMenuCatalog.Groups.Single(group => group.Key == "Edit");
        var library = Assert.Single(edit.Children, group => group.Key == "EffectLibrary");
        Assert.Equal<WorkbenchCommand?>([WorkbenchCommand.OPEN_STYLE_LIBRARY,
            WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY, WorkbenchCommand.OPEN_EFFECT_LIBRARY_TRANSFER], library.Commands);
        Assert.DoesNotContain(Enum.GetNames<SettingsPage>(), page => page is "STYLES" or "EFFECTS");
        var bindings = ShortcutDefaults.CreateBindings();
        Assert.All(library.Commands, command => Assert.Empty(bindings.Single(binding => binding.Command == command).Gesture));
    }
}
