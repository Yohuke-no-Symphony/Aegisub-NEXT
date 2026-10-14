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
        Assert.Equal("Workbench.MenuEffectLibrary", library.LocalizationKey);
        Assert.Equal("Workbench.MenuEffectLibrary", library.LabelKey);
        Assert.Equal("EffectLibrary", library.IconKey);
        Assert.Equal<WorkbenchCommand?>([WorkbenchCommand.OPEN_STYLE_LIBRARY,
            WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY, WorkbenchCommand.OPEN_EFFECT_LIBRARY_TRANSFER], library.Commands);
        Assert.DoesNotContain(Enum.GetNames<SettingsPage>(), page => page is "STYLES" or "EFFECTS");
        var bindings = ShortcutDefaults.CreateBindings();
        Assert.All(library.Commands, command => Assert.Empty(bindings.Single(binding => binding.Command == command).Gesture));
    }

    /// <summary>效果库的显式语义 metadata 不改变既有菜单组的默认 key 和图标。</summary>
    [Fact]
    public void OtherGroupsRetainTheirEstablishedLocalizationKeysAndOptionalIcons()
    {
        foreach (var group in WorkbenchMenuCatalog.Groups)
        {
            Assert.Equal("Workbench." + group.Key, group.LabelKey);
            Assert.Null(group.IconKey);
        }
        var formats = WorkbenchMenuCatalog.Groups.Single(group => group.Key == "Format").Children;
        Assert.All(formats, group =>
        {
            Assert.Equal("Workbench." + group.Key, group.LabelKey);
            Assert.Null(group.IconKey);
        });
    }
}
