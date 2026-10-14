using AegiNext.Desktop.I18n;
using System.Globalization;
using System.Runtime.ExceptionServices;
using AegiNext.Application.Presets;
using AegiNext.Application;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Appearance;
using AegiNext.Desktop.Settings.Colors;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.EffectLibrary.Styles;
using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.EffectLibrary.Effects;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsStartupUiTests
{
    private const string LEGACY_PREFERENCES = """{"Version":1,"Language":"zh-CN","Theme":0,"Volume":0.20605469}""";

    [AvaloniaFact]
    public async Task WelcomeOpensANonmodalLibraryWithoutAProjectAndKeepsSettingsSeparate()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath));
        await application.Initialization;
        var sessionCreations = 0;
        await using var coordinator = new DesktopStartupCoordinator(_ => { }, () => { }, application,
            sessionFactory: (dialogs, context) =>
            {
                sessionCreations++;
                return new(dialogs, applicationContext: context);
            });
        coordinator.Start();
        var welcome = coordinator.WelcomeWindow;

        UiTestActions.Click(welcome, "WelcomeEffectLibraryButton");
        await welcome.ViewModel.StyleLibraryCommand.ExecutionTask!.WaitAsync(TimeSpan.FromSeconds(5));
        var library = Assert.Single(welcome.OwnedWindows.OfType<EffectLibraryWindow>());

        Assert.True(library.IsVisible);
        Assert.True(welcome.IsEffectivelyEnabled);
        Assert.Equal(EffectLibraryPage.STYLES, library.CurrentPage);
        Assert.False(library.ViewModel.Styles.HasSelectedSubtitle);
        Assert.False(UiTestActions.Find<Button>(library, "CaptureStyleButton").IsEffectivelyEnabled);
        Assert.Null(coordinator.MainWindow);
        Assert.Equal(0, sessionCreations);

        var settingsOperation = welcome.ViewModel.SettingsCommand.ExecuteAsync(null);
        var settings = Assert.Single(welcome.OwnedWindows.OfType<SettingsWindow>());
        Assert.True(settings.IsVisible);
        Assert.True(library.IsVisible);
        Assert.NotSame(settings, library);
        Assert.False(settingsOperation.IsCompleted);
        settings.Close();
        await settingsOperation.WaitAsync(TimeSpan.FromSeconds(5));

        await welcome.ViewModel.StyleLibraryCommand.ExecuteAsync(null);
        Assert.Same(library, Assert.Single(welcome.OwnedWindows.OfType<EffectLibraryWindow>()));
        Assert.True(welcome.IsEffectivelyEnabled);
        Assert.Equal(0, sessionCreations);
    }

    /// <summary>A missing optional package preserves the requested ID while the first visible frame uses English.</summary>
    [AvaloniaFact]
    public async Task MissingSavedLanguageFallsBackWithoutLosingTheRequestedLanguageOrOtherPreferences()
    {
        using var environment = new UiTestEnvironment();
        var preferences = new WorkbenchPreferences
        {
            Language = "fr-FR", Theme = WorkbenchTheme.DARK, Volume = 0.375f, WindowMenuOnMac = true
        };
        using var store = new WorkbenchPreferencesStore(environment.DirectoryPath);
        await store.SaveAsync(preferences, TestContext.Current.CancellationToken);
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath));
        await application.Initialization;
        await using var session = new WorkbenchSession(new StartupTestDialogService(), applicationContext: application);
        Assert.Equal(ProjectOpenStatus.OPENED,
            (await session.CreateProjectAsync(new ProjectCreationRequest("Untitled project", environment.DirectoryPath))).Status);
        var main = new MainWindow(session);
        SettingsWindow? settings = null;
        try
        {
            Assert.Equal("en-US", Localization.CurrentLanguageID);
            Assert.Equal(preferences, main.Session.Preferences);
            Assert.Contains("Untitled project", main.Title, StringComparison.Ordinal);
            main.Show();
            main.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.OPEN_SETTINGS).Execute(null);
            settings = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
            Assert.Null(UiTestActions.Find<ComboBox>(settings, "LanguageCombo").SelectedItem);
            Assert.Equal("Settings", settings.Title);
            UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = (int)WorkbenchTheme.LIGHT;
            Assert.Equal("fr-FR", main.Session.Preferences.Language);
            Assert.Equal(0.375f, main.Session.Preferences.Volume);
            Assert.True(main.Session.Preferences.WindowMenuOnMac);
        }
        finally
        {
            settings?.Close();
            main.Close();
            await main.DisposeAsync();
        }

        Assert.Equal("fr-FR", store.Load().Language);
        Assert.Null(store.LoadError);
    }

    [AvaloniaFact]
    public void OpeningAndRefreshingSettingsNeverPassesTheWindowModelToCompiledPageBindings()
    {
        using var environment = new UiTestEnvironment();
        var failures = new List<Exception>();
        var uiThreadId = Environment.CurrentManagedThreadId;
        EventHandler<FirstChanceExceptionEventArgs> capture = (_, args) =>
        {
            if (Environment.CurrentManagedThreadId == uiThreadId && args.Exception is InvalidCastException &&
                (args.Exception.Message.Contains("AegiNext.Desktop.Settings.", StringComparison.Ordinal) ||
                 args.Exception.Message.Contains("AegiNext.Desktop.EffectLibrary.", StringComparison.Ordinal)))
            {
                failures.Add(args.Exception);
            }
        };
        SettingsWindow? window = null;
        EffectLibraryWindow? library = null;
        AppDomain.CurrentDomain.FirstChanceException += capture;
        try
        {
            window = new(new WorkbenchPreferences { Language = "en-US" });
            library = new(new WorkbenchPreferences { Language = "en-US" });
            window.Show();
            library.Show();
            foreach (var language in new[] { "en-US", "zh-CN", "en-US" })
            {
                Localization.SetLanguage(language);
                window.RefreshLanguage();
                library.RefreshLanguage();
                foreach (var page in Enum.GetValues<SettingsPage>())
                {
                    window.SelectPage(page);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.Same(window.ViewModel.Appearance,
                        UiTestActions.Find<AppearanceSettingsView>(window, "AppearanceView").DataContext);
                    Assert.Same(window.ViewModel.Colors,
                        UiTestActions.Find<ColorsSettingsView>(window, "ColorsView").DataContext);
                    Assert.Same(window.ViewModel.Shortcuts,
                        UiTestActions.Find<ShortcutSettingsView>(window, "ShortcutsView").DataContext);
                    Assert.Null(window.FindControl<StyleLibraryView>("StylesView"));
                }
                foreach (var page in Enum.GetValues<EffectLibraryPage>())
                {
                    library.SelectPage(page);
                    Dispatcher.UIThread.RunJobs();
                    library.UpdateLayout();
                    Assert.Same(library.ViewModel.Styles,
                        UiTestActions.Find<StyleLibraryView>(library, "StylesView").DataContext);
                    Assert.Same(library.ViewModel.Effects,
                        UiTestActions.Find<EffectScriptLibraryView>(library, "EffectsView").DataContext);
                }
            }
        }
        finally
        {
            library?.Close();
            window?.Close();
            AppDomain.CurrentDomain.FirstChanceException -= capture;
        }

        Assert.Empty(failures);
    }

    [AvaloniaFact]
    public async Task PersistedLegacyPreferencesAndStyleLibraryAreAppliedBeforeTheWindowIsShown()
    {
        using var environment = new UiTestEnvironment();
        var preferencesPath = Path.Combine(environment.DirectoryPath, "preferences.json");
        var stylesPath = Path.Combine(environment.DirectoryPath, "subtitle-styles.aegistyles");
        await File.WriteAllTextAsync(preferencesPath, LEGACY_PREFERENCES, TestContext.Current.CancellationToken);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "nano", new());
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [preset] }, stylesPath,
            TestContext.Current.CancellationToken);
        using var preferencesStore = new WorkbenchPreferencesStore(environment.DirectoryPath);
        using var library = new SubtitleStylePresetLibrary(stylesPath);
        var preferences = preferencesStore.Load();
        await library.LoadAsync(TestContext.Current.CancellationToken);
        Localization.SetLanguage(preferences.Language);

        var window = new SettingsWindow(preferences);
        var libraryWindow = new EffectLibraryWindow(preferences);
        try
        {
            window.UpdateShortcuts(preferences.ShortcutBindings);
            window.UpdateStyles(library.Snapshot.Presets);
            libraryWindow.UpdateStyles(library.Snapshot.Presets);
            libraryWindow.UpdateSelectionAvailability(false);
            libraryWindow.SetStyleOperationBusy(false);
            Assert.False(window.IsVisible);
            Assert.False(libraryWindow.IsVisible);

            window.Show();
            libraryWindow.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Null(preferencesStore.LoadError);
            Assert.Equal("设置", window.Title);
            Assert.Equal("zh-CN", Assert.IsType<LanguageInfo>(UiTestActions.Find<ComboBox>(window, "LanguageCombo").SelectedItem).LanguageID);
            Assert.Equal(0, UiTestActions.Find<ComboBox>(window, "ThemeCombo").SelectedIndex);
            foreach (var page in Enum.GetValues<SettingsPage>())
            {
                window.SelectPage(page);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(window.IsVisible);
                Assert.Equal(page, window.CurrentPage);
                Assert.False(window.ViewModel.HasError);
            }
            Assert.Equal(Localization.Get("EffectLibrary.Title"), libraryWindow.Title);
            Assert.Equal("nano", UiTestActions.Find<TextBox>(libraryWindow, "StyleNameInput").Text);
            Assert.Equal(LEGACY_PREFERENCES, await File.ReadAllTextAsync(preferencesPath, TestContext.Current.CancellationToken));
        }
        finally
        {
            libraryWindow.Close();
            window.Close();
            Assert.False(window.IsVisible);
        }
    }
}
