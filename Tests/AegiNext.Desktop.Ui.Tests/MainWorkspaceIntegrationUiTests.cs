using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MainWorkspaceIntegrationUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryMenusAndBothPanelsReuseOneWindowAlongsideSettings(bool windowMenu)
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await main.Session.ApplicationContext.Initialization;
        main.Session.UpdatePreferences(main.Session.Preferences with { WindowMenuOnMac = windowMenu });
        await main.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SETTINGS);
        var settings = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        EffectLibraryWindow? library = null;
        foreach (var (command, page) in new[]
                 {
                     (WorkbenchCommand.OPEN_STYLE_LIBRARY, EffectLibraryPage.STYLES),
                     (WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY, EffectLibraryPage.EFFECTS),
                     (WorkbenchCommand.OPEN_EFFECT_LIBRARY_TRANSFER, EffectLibraryPage.TRANSFER)
                 })
        {
            var title = UiTestActions.Find<WindowTitleBar>(main, "TitleBar");
            if (title.MenuContent is WindowMenuBar menuBar)
            {
                var menu = Assert.IsType<Menu>(menuBar.Content);
                Assert.Single(MenuLeaves(menu.Items.OfType<MenuItem>()), item =>
                    ReferenceEquals(item.Command, main.GetCommand(command)));
            }
            else
            {
                Assert.Single(NativeLeaves(Assert.IsType<NativeMenu>(NativeMenu.GetMenu(main))), item =>
                    ReferenceEquals(item.Command, main.GetCommand(command)));
            }
            await main.ViewModel.ExecuteCommandAsync(command);
            var opened = Assert.Single(main.OwnedWindows.OfType<EffectLibraryWindow>());
            if (library is not null)
            {
                Assert.Same(library, opened);
            }
            library = opened;
            Assert.Equal(page, library.CurrentPage);
            Assert.True(settings.IsVisible);
        }
        Assert.NotNull(library);
        await UiTestActions.CreateSubtitleAsync(context);
        main.Layouts.Show(WorkbenchPanelIds.STYLES);
        UiTestActions.Click(main, "ManageStylesButton");
        await library.ViewModel.NavigationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(library, Assert.Single(main.OwnedWindows.OfType<EffectLibraryWindow>()));
        Assert.Equal(EffectLibraryPage.STYLES, library.CurrentPage);
        main.Layouts.Show(WorkbenchPanelIds.EFFECT_PRESETS);
        UiTestActions.Click(main, "ManageEffectScriptsButton");
        await library.ViewModel.NavigationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(library, Assert.Single(main.OwnedWindows.OfType<EffectLibraryWindow>()));
        Assert.Equal(EffectLibraryPage.EFFECTS, library.CurrentPage);
        Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        Assert.True(settings.IsVisible);
    }

    [AvaloniaFact]
    public async Task MainLayoutRoundTripKeepsEveryViewModelControllerAndPlaybackPosition()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await main.Session.Styles.Completion;
        await context.OpenMediaAsync();
        await context.Controller.SeekAsync(new(5));
        var views = main.Panels.ToDictionary(value => value.Key, value => value.Value);
        var models = views.ToDictionary(value => value.Key, value => value.Value.DataContext);
        var controller = main.Session.Controller;
        var original = main.DocumentSnapshot;
        var undoLabel = main.Session.Editor.UndoLabel;
        main.ViewModel.Timeline.PixelsPerSecond = 72;
        main.ViewModel.Timeline.ViewStart = 3;
        main.ViewModel.Export.Quality = 27;

        Assert.True(await main.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.TIMING));
        Flush(main);
        Assert.True(await main.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.EFFECTS));
        Flush(main);
        Assert.True(await main.Layouts.RestoreDefaultAsync());
        Flush(main);

        Assert.Equal(WorkbenchPanelIds.All.Count, main.Panels.Count);
        foreach (var id in WorkbenchPanelIds.All)
        {
            Assert.Same(views[id], main.Panels[id]);
            Assert.Same(views[id], main.Layouts.PanelAdapters[id].View);
            Assert.Same(models[id], main.Panels[id].DataContext);
        }

        Assert.Same(controller, main.Session.Controller);
        Assert.Same(context.Controller, controller);
        Assert.Same(original, main.DocumentSnapshot);
        Assert.Equal(undoLabel, main.Session.Editor.UndoLabel);
        Assert.Equal(new MediaTime(5), controller.Snapshot.Position);
        Assert.Equal(72, main.ViewModel.Timeline.PixelsPerSecond);
        Assert.Equal(3, main.ViewModel.Timeline.ViewStart);
        Assert.Equal(27, main.ViewModel.Export.Quality);
    }

    [AvaloniaFact]
    public async Task FloatingHostRoutesOneKeyOnceAndCloseHidesPanelThatMenuReopens()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await context.OpenMediaAsync();
        var preview = main.Panels[WorkbenchPanelIds.PREVIEW];
        var model = preview.DataContext;
        main.Layouts.Float(WorkbenchPanelIds.PREVIEW);
        Flush(main);
        var floating = Assert.Single(main.Layouts.FloatingWindows);
        Assert.Contains(floating, main.WindowRegistry.Windows);
        Assert.Same(preview, main.Layouts.PanelAdapters[WorkbenchPanelIds.PREVIEW].View);
        floating.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.None, null);
        floating.KeyPress(Key.F8, RawInputModifiers.None, PhysicalKey.None, null);
        floating.KeyRelease(Key.F8, RawInputModifiers.None, PhysicalKey.None, null);
        Flush(main);
        Assert.Single(main.DocumentSnapshot.Subtitles);
        floating.Close();
        Flush(main);

        Assert.True(main.IsVisible);
        Assert.False(main.Session.IsClosing);
        Assert.False(main.Layouts.IsVisible(WorkbenchPanelIds.PREVIEW));
        Assert.DoesNotContain(floating, main.WindowRegistry.Windows);
        Assert.Empty(main.Layouts.FloatingWindows);
        var leaf = NativeLeaves(Assert.IsType<NativeMenu>(NativeMenu.GetMenu(main)))
            .Single(value => ReferenceEquals(value.Command, main.GetCommand(WorkbenchCommand.VIEW_PREVIEW)));
        leaf.Command!.Execute(null);
        Flush(main);

        Assert.True(main.Layouts.IsVisible(WorkbenchPanelIds.PREVIEW));
        Assert.Same(preview, main.Panels[WorkbenchPanelIds.PREVIEW]);
        Assert.Same(model, preview.DataContext);
        Assert.Same(context.Controller, main.Session.Controller);
        Assert.Single(main.DocumentSnapshot.Subtitles);
    }

    [AvaloniaFact]
    public async Task PresetSwitchValidatesRealInputsAndCommitsBothSubtitleRowsAsOneUndo()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await main.Session.Styles.Completion;
        var document = CreateSubtitleDocument();
        main.Session.Editor.Reset(document);
        main.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        Flush(main);
        var rows = main.ViewModel.Subtitles.Rows;
        var panel = main.Panels[WorkbenchPanelIds.SUBTITLES];
        var firstText = FindRowInput(panel, rows[0], 4);
        var secondStart = FindRowInput(panel, rows[1], 1);
        UiTestActions.SetText(firstText, "First pending");
        UiTestActions.SetText(secondStart, "invalid");
        try
        {
            Assert.False(await main.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.ENCODE));
            Flush(main);

            Assert.Equal(WorkspaceLayoutPresets.STANDARD, main.Layouts.CurrentPresetId);
            Assert.Same(document, main.DocumentSnapshot);
            Assert.False(main.Session.Editor.CanUndo);
            Assert.Equal("First pending", firstText.Text);
            Assert.Equal("invalid", secondStart.Text);
            Assert.Equal(rows[1].Id, main.ViewModel.Subtitles.InvalidRowId);
            Assert.Equal("subtitles", main.ViewModel.InvalidPanelId);
            Assert.True(secondStart.IsFocused);
            UiTestActions.SetText(secondStart, "00:00:03.000");
            UiTestActions.SetText(FindRowInput(panel, rows[1], 4), "Second pending");
            Assert.True(await main.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.ENCODE));
            Flush(main);

            Assert.Equal("First pending", main.DocumentSnapshot.Subtitles[0].Text);
            Assert.Equal("Second pending", main.DocumentSnapshot.Subtitles[1].Text);
            Assert.Equal("Commit workspace drafts", main.Session.Editor.UndoLabel);
            Assert.True(main.Session.Editor.Undo());
            Assert.Same(document, main.DocumentSnapshot);
            Assert.False(main.Session.Editor.CanUndo);
            Assert.True(await main.Layouts.RestoreDefaultAsync());
            Flush(main);
            Assert.Same(document, main.DocumentSnapshot);
            Assert.False(main.Session.Editor.CanUndo);
        }
        finally
        {
            foreach (var row in main.ViewModel.Subtitles.Rows)
            {
                row.Accept(main.DocumentSnapshot.Subtitles.Single(value => value.Id == row.Id));
            }
        }
    }

    [AvaloniaFact]
    public async Task AppearanceLanguageAndMenuModeImmediatelyUpdateMainFloatingAndSettingsHosts()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await main.Session.Styles.Completion;
        main.Layouts.Float(WorkbenchPanelIds.PREVIEW);
        Flush(main);
        var floating = Assert.Single(main.Layouts.FloatingWindows);
        await main.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SETTINGS);
        var settings = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        await main.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(main.OwnedWindows.OfType<EffectLibraryWindow>());
        var windows = new Window[] { main, floating, settings, library };
        Assert.Equal(4, main.WindowRegistry.Windows.Count);
        Assert.Single(settings.GetLogicalDescendants().OfType<WindowTitleBar>());
        var nativeRoots = windows.ToDictionary(window => window,
            window => Assert.IsType<NativeMenu>(NativeMenu.GetMenu(window)));
        var location = UiTestActions.Find<ComboBox>(settings, "MenuLocationCombo");

        foreach (var languageID in new[] { "zh-CN", "en-US" })
        {
            UiTestActions.SelectLanguage(settings, languageID);
            location.SelectedIndex = 1;
            Flush(main);
            Assert.True(main.Session.Preferences.WindowMenuOnMac);
            Assert.Equal(languageID, main.Session.Preferences.Language);
            Assert.Equal(Localization.Get("Settings.Settings"), settings.Title);
            Assert.Equal(Localization.Get("EffectLibrary.Title"), library.Title);
            Assert.Equal(Localization.Get("Layout." + WorkbenchPanelIds.PREVIEW),
                main.Layouts.PanelAdapters[WorkbenchPanelIds.PREVIEW].Title);
            foreach (var window in windows)
            {
                var title = Assert.Single(window.GetLogicalDescendants().OfType<WindowTitleBar>());
                Assert.Equal(window.Title, title.Title);
                Assert.Same(nativeRoots[window], NativeMenu.GetMenu(window));
                if (!ReferenceEquals(window, main))
                {
                    Assert.Null(title.MenuContent);
                    if (OperatingSystem.IsMacOS())
                    {
                        Assert.Empty(nativeRoots[window].Items);
                    }
                    continue;
                }
                var bar = Assert.IsType<WindowMenuBar>(title.MenuContent);
                var menu = Assert.IsType<Menu>(bar.Content);
                var leaves = MenuLeaves(menu.Items.OfType<MenuItem>());
                var preview = leaves.Single(item =>
                    ReferenceEquals(item.Command, main.GetCommand(WorkbenchCommand.VIEW_PREVIEW)));
                Assert.Equal(Localization.Get("Settings." + WorkbenchCommand.VIEW_PREVIEW.ToString()), preview.Header);
                Assert.Same(main.GetCommand(WorkbenchCommand.OPEN_SETTINGS),
                    leaves.Single(item =>
                        ReferenceEquals(item.Command, main.GetCommand(WorkbenchCommand.OPEN_SETTINGS))).Command);
                Assert.Same(nativeRoots[window], NativeMenu.GetMenu(window));
                if (OperatingSystem.IsMacOS())
                {
                    Assert.Empty(nativeRoots[window].Items);
                }
            }

            location.SelectedIndex = 0;
            Flush(main);
            Assert.False(main.Session.Preferences.WindowMenuOnMac);
            foreach (var window in windows)
            {
                Assert.Same(nativeRoots[window], NativeMenu.GetMenu(window));
                var preview = NativeLeaves(nativeRoots[window]).Single(item =>
                    ReferenceEquals(item.Command, main.GetCommand(WorkbenchCommand.VIEW_PREVIEW)));
                Assert.Equal(Localization.Get("Settings." + WorkbenchCommand.VIEW_PREVIEW.ToString()), preview.Header);
                var title = Assert.Single(window.GetLogicalDescendants().OfType<WindowTitleBar>());
                if (!OperatingSystem.IsMacOS() && ReferenceEquals(window, main))
                {
                    Assert.NotNull(title.MenuContent);
                }
                else
                {
                    Assert.Null(title.MenuContent);
                }
            }
        }
    }

    [AvaloniaFact]
    public async Task LibraryStyleSaveReenablesPageAfterAwaitedLibraryOperationAndAllowsAnotherEdit()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await main.Session.Styles.Completion;
        await main.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var library = Assert.Single(main.OwnedWindows.OfType<EffectLibraryWindow>());
        library.SelectPage(EffectLibraryPage.STYLES);
        UiTestActions.Click(library, "AddStyleButton");
        await library.ViewModel.Styles.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        UiTestActions.SetText(UiTestActions.Find<TextBox>(library, "StyleNameInput"), "Saved from actual library");
        UiTestActions.Click(library, "SaveStyleButton");
        await library.ViewModel.Styles.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Flush(library);

        Assert.Equal("Saved from actual library", Assert.Single(main.Session.StyleLibrary.Snapshot.Presets).Name);
        Assert.False(library.ViewModel.Styles.IsBusy);
        Assert.True(UiTestActions.Find<Button>(library, "DuplicateStyleButton").IsEffectivelyEnabled);
        UiTestActions.Click(library, "DuplicateStyleButton");
        await library.ViewModel.Styles.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        UiTestActions.SetText(UiTestActions.Find<TextBox>(library, "StyleNameInput"), "Second saved style");
        UiTestActions.Click(library, "SaveStyleButton");
        await library.ViewModel.Styles.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Flush(library);

        Assert.Equal(2, main.Session.StyleLibrary.Snapshot.Presets.Length);
        Assert.False(library.ViewModel.Styles.IsBusy);
        Assert.True(UiTestActions.Find<Button>(library, "SaveStyleButton").IsEffectivelyEnabled);
    }

    private static TextBox FindRowInput(Control panel, SubtitleRow row, int column)
    {
        return panel.GetVisualDescendants().OfType<TextBox>()
            .Single(value => ReferenceEquals(value.DataContext, row) && Grid.GetColumn(value) == column);
    }

    private static IEnumerable<MenuItem> MenuLeaves(IEnumerable<MenuItem> roots)
    {
        foreach (var item in roots)
        {
            if (item.Command is not null)
            {
                yield return item;
            }

            foreach (var child in MenuLeaves(item.Items.OfType<MenuItem>()))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<NativeMenuItem> NativeLeaves(NativeMenu root)
    {
        foreach (var item in root.Items.OfType<NativeMenuItem>())
        {
            if (item.Command is not null)
            {
                yield return item;
            }

            if (item.Menu is { } child)
            {
                foreach (var leaf in NativeLeaves(child))
                {
                    yield return leaf;
                }
            }
        }
    }

    private static ProjectDocument CreateSubtitleDocument()
    {
        var first = new SubtitleLine { Text = "First", Start = MediaTime.Zero, End = new(2) };
        var second = new SubtitleLine { Text = "Second", Start = new(3), End = new(5) };
        return new()
        {
            Subtitles = [first, second],
            Layers =
            [
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, Start = first.Start, End = first.End },
                new() { Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End }
            ]
        };
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
