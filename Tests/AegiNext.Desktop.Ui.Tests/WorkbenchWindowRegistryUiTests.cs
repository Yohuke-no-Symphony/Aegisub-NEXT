using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WorkbenchWindowRegistryUiTests
{
    [AvaloniaFact]
    public void FloatingHostRoutesOneCommandAndRetainsTheSameBodyDuringMenuChanges()
    {
        using var environment = new UiTestEnvironment();
        var commands = Enum.GetValues<WorkbenchCommand>().ToDictionary(id => id,
            id => new WorkbenchCommandAdapter(() => { }, () => true));
        var count = 0;
        commands[WorkbenchCommand.TIMING_ENTER] = new(() => count++, () => true);
        var catalog = new WorkbenchMenuCatalog(id => commands[id]);
        using var registry = new WorkbenchWindowRegistry(catalog, () => { });
        var mainBody = new Border();
        var floatingBody = new Border();
        var main = new Window { Width = 940, Height = 620, Content = mainBody };
        var floating = new Window { Width = 580, Height = 380, Content = floatingBody };
        try
        {
            registry.Register(main, () => "AegiNEXT - Project •");
            registry.Register(floating, () => "AegiNEXT - Project • — Preview", role: WorkbenchWindowRole.FLOATING);
            main.Show();
            floating.Show(main);
            floating.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.None, null);
            floating.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.None, null);
            floating.KeyRelease(Key.F8, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(1, count);
            floating.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.None, null);
            floating.KeyRelease(Key.F8, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(2, count);

            registry.UpdatePreferences(new() { WindowMenuOnMac = true });
            Assert.Same(mainBody, main.GetLogicalDescendants().OfType<Border>().Single(value => ReferenceEquals(value, mainBody)));
            Assert.Same(floatingBody, floating.GetLogicalDescendants().OfType<Border>().Single(value => ReferenceEquals(value, floatingBody)));
            Assert.Equal("AegiNEXT - Project •", main.Title);
            Assert.Equal("AegiNEXT - Project • — Preview", floating.Title);
            foreach (var window in new[] { main, floating })
            {
                var title = window.GetLogicalDescendants().OfType<WindowTitleBar>().Single();
                if (ReferenceEquals(window, main))
                {
                    Assert.NotNull(title.MenuContent);
                }
                else
                {
                    Assert.Null(title.MenuContent);
                }
                if (OperatingSystem.IsMacOS())
                {
                    Assert.Empty(NativeMenu.GetMenu(window)!.Items);
                }
            }

            registry.UpdatePreferences(new());
            Assert.All(new[] { main, floating }, window => Assert.NotEmpty(NativeMenu.GetMenu(window)!.Items));
            floating.Close();
            Assert.Contains(main, registry.Windows);
            Assert.DoesNotContain(floating, registry.Windows);
        }
        finally
        {
            floating.Close();
            main.Close();
            Assert.Empty(registry.Windows);
        }
    }

    [AvaloniaFact]
    public void NarrowWindowOverflowRetainsEveryMenuCommandAndStablePresetIds()
    {
        using var environment = new UiTestEnvironment();
        var commands = Enum.GetValues<WorkbenchCommand>().ToDictionary(id => id,
            id => new WorkbenchCommandAdapter(() => { }, () => true));
        var presetCommand = new WorkbenchCommandAdapter(() => { }, () => true);
        var catalog = new WorkbenchMenuCatalog(id => commands[id]);
        catalog.UpdateLayouts([new("personal-stable-id", "My layout", true, presetCommand)], true);
        using var registry = new WorkbenchWindowRegistry(catalog, () => { });
        var window = new Window { Width = 300, Height = 300, Content = new Border() };
        try
        {
            registry.Register(window, () => "AegiNext");
            registry.UpdatePreferences(new() { WindowMenuOnMac = true });
            window.Show();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var titleBar = window.GetLogicalDescendants().OfType<WindowTitleBar>().Single();
            var bar = Assert.IsType<WindowMenuBar>(titleBar.MenuContent);
            var menu = Assert.IsType<Menu>(bar.Content);
            var overflow = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
            Assert.Equal("☰", overflow.Header);
            var leaves = MenuLeaves(overflow).ToArray();
            var menuCommands = commands.Where(pair => pair.Key is not (WorkbenchCommand.END_TEXT_INPUT or
                WorkbenchCommand.COPY_CLIPS or WorkbenchCommand.PASTE_CLIPS or
                WorkbenchCommand.AUDITION_BEFORE_SUBTITLE or WorkbenchCommand.AUDITION_AFTER_SUBTITLE or
                WorkbenchCommand.AUDITION_SUBTITLE_BEGIN or WorkbenchCommand.AUDITION_SUBTITLE_END or
                WorkbenchCommand.AUDITION_SUBTITLE or
                WorkbenchCommand.ADVANCE_SUBTITLE_ROW or WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK or
                WorkbenchCommand.APPLY_TIMING_POST_PROCESSOR or WorkbenchCommand.SEEK_CLIP_START or WorkbenchCommand.SEEK_CLIP_END))
                .Select(pair => pair.Value).ToArray();
            Assert.Equal(menuCommands.Length + 1, leaves.Length);
            Assert.All(menuCommands, command => Assert.Single(leaves, leaf => ReferenceEquals(leaf.Command, command)));
            Assert.DoesNotContain(leaves, leaf => ReferenceEquals(leaf.Command, commands[WorkbenchCommand.END_TEXT_INPUT]));
            Assert.Same(presetCommand, leaves.Single(leaf => Equals(leaf.Header, "My layout")).Command);
            Assert.True(leaves.Single(leaf => Equals(leaf.Header, "My layout")).IsChecked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ModifiedLayoutRetainsPlainGroupLabelsAndNativeRootIdentity()
    {
        using var environment = new UiTestEnvironment();
        var commands = Enum.GetValues<WorkbenchCommand>().ToDictionary(id => id,
            _ => new WorkbenchCommandAdapter(() => { }, () => true));
        var catalog = new WorkbenchMenuCatalog(id => commands[id]);
        using var registry = new WorkbenchWindowRegistry(catalog, () => { });
        var window = new Window { Width = 1200, Height = 500 };
        try
        {
            registry.Register(window, () => "AegiNext");
            registry.UpdatePreferences(new() { WindowMenuOnMac = true });
            window.Show();
            var native = NativeMenu.GetMenu(window);
            var title = Assert.Single(window.GetLogicalDescendants().OfType<WindowTitleBar>());
            var bar = Assert.IsType<WindowMenuBar>(title.MenuContent);
            var menu = Assert.IsType<Menu>(bar.Content);
            var layoutGroup = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Layouts"));
            catalog.UpdateLayouts([], true);
            Assert.True(catalog.IsLayoutModified);
            Assert.Same(native, NativeMenu.GetMenu(window));
            Assert.Same(layoutGroup, Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Layouts")));
            registry.UpdatePreferences(new() { WindowMenuOnMac = false });
            Assert.Same(native, NativeMenu.GetMenu(window));
            Assert.Single(native!.Items.OfType<NativeMenuItem>(), item => item.Header == "Layouts");
            Assert.DoesNotContain(native.Items.OfType<NativeMenuItem>(), item =>
                item.Header?.Contains("Modified", StringComparison.Ordinal) == true);
        }
        finally
        {
            window.Close();
        }
    }
    private static IEnumerable<MenuItem> MenuLeaves(MenuItem item)
    {
        foreach (var child in item.Items.OfType<MenuItem>())
        {
            if (child.Command is not null)
            {
                yield return child;
            }
            foreach (var leaf in MenuLeaves(child))
            {
                yield return leaf;
            }
        }
    }

}
