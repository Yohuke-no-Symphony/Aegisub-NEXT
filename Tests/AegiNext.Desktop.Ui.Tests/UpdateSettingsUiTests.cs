using AegiNext.Desktop.I18n;
using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Updates;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Updates;
using AegiNext.Desktop.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class UpdateSettingsUiTests
{
    [AvaloniaFact]
    public void UpdatingLanguageAndPreferencesPreservesTheSelectedChannelWithoutUserChanges()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.UPDATES);
            var automatic = UiTestActions.Find<CheckBox>(window, "AutoCheckUpdatesInput");
            var channel = UiTestActions.Find<ComboBox>(window, "UpdateChannelInput");
            var changes = new List<UpdateSettingsChangedEventArgs>();
            window.UpdatesChanged += (_, args) => changes.Add(args);
            var selected = Assert.IsType<UpdateChannelChoice>(channel.SelectedItem);

            Assert.Equal(new AboutViewModel().Version,
                UiTestActions.Find<TextBlock>(window, "UpdateSettingsCurrentVersion").Text);
            Assert.True(automatic.IsChecked);
            Assert.Equal(UpdateChannel.INCLUDE_PRERELEASE, selected.Channel);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(selected, channel.SelectedItem);
            Assert.Equal(Localization.Get("Settings.Updates"), window.ViewModel.PageTitle);
            Assert.Equal(Localization.Get("Settings.UpdateChannelIncludePrerelease"), selected.Label);
            Assert.Empty(changes);

            window.UpdatePreferences(new() { AutoCheckUpdates = false, UpdateChannel = UpdateChannel.STABLE });
            Assert.False(automatic.IsChecked);
            Assert.Equal(UpdateChannel.STABLE, Assert.IsType<UpdateChannelChoice>(channel.SelectedItem).Channel);
            Assert.Empty(changes);
            window.SelectPage(SettingsPage.TASKS);
            window.SelectPage(SettingsPage.UPDATES);
            Assert.Equal(UpdateChannel.STABLE, Assert.IsType<UpdateChannelChoice>(channel.SelectedItem).Channel);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task UpdatePagePersistsChangesAndReopeningReleasesTheClosedWindowBindings()
    {
        using var environment = new UiTestEnvironment();
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath),
            initialPreferences: new() { AutoCheckUpdates = true });
        await application.Initialization;
        using var coordinator = new SettingsWindowCoordinator(application);
        var owner = new Window();
        try
        {
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.UPDATES);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);

            UiTestActions.Click(window, "AutoCheckUpdatesInput");
            var channel = UiTestActions.Find<ComboBox>(window, "UpdateChannelInput");
            channel.SelectedItem =
                window.ViewModel.Updates.Channels.Single(choice => choice.Channel == UpdateChannel.STABLE);
            await application.Completion;

            Assert.False(application.Preferences.AutoCheckUpdates);
            Assert.Equal(UpdateChannel.STABLE, application.Preferences.UpdateChannel);
            Assert.Equal(application.Preferences, application.PreferencesStore.Load());
            coordinator.Close();
            window.ViewModel.Updates.AutoCheckUpdates = true;
            Assert.False(application.Preferences.AutoCheckUpdates);
            await coordinator.OpenAsync(owner, page: SettingsPage.UPDATES);
            var reopened = Assert.IsType<SettingsWindow>(coordinator.Window);
            Assert.NotSame(window, reopened);
            Assert.False(reopened.ViewModel.Updates.AutoCheckUpdates);
            Assert.Equal(UpdateChannel.STABLE, reopened.ViewModel.Updates.SelectedChannel!.Channel);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task OpeningUpdateSettingsPreservesTheIndependentLibraryDraftWithoutConfirmation(int choice)
    {
        using var environment = new UiTestEnvironment();
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath));
        await application.Initialization;
        var dialogs = new StartupTestDialogService { PresetChoice = choice };
        using var coordinator = new SettingsWindowCoordinator(application, _ => dialogs);
        using var libraryCoordinator = new EffectLibraryWindowCoordinator(application, _ => dialogs);
        var owner = new Window();
        try
        {
            owner.Show();
            await libraryCoordinator.OpenAsync(owner, page: EffectLibraryPage.STYLES);
            var library = Assert.IsType<EffectLibraryWindow>(libraryCoordinator.Window);
            library.ViewModel.Styles.AddCommand.Execute(null);
            await library.ViewModel.Styles.SelectionCompletion;
            library.ViewModel.Styles.Name = "Update navigation draft";
            var draft = library.ViewModel.Styles.Draft;
            Assert.True(library.ViewModel.Styles.IsDirty);

            await coordinator.OpenAsync(owner, page: SettingsPage.UPDATES);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);

            Assert.Equal(0, dialogs.PresetConfirmationRequests);
            Assert.Equal(SettingsPage.UPDATES, window.CurrentPage);
            Assert.Equal(EffectLibraryPage.STYLES, library.CurrentPage);
            Assert.Equal(draft, library.ViewModel.Styles.Draft);
            Assert.Equal("Update navigation draft", library.ViewModel.Styles.Name);
            Assert.True(library.ViewModel.Styles.IsDirty);
            Assert.Empty(application.StyleLibrary.Snapshot.Presets);
            Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            Assert.Single(owner.OwnedWindows.OfType<EffectLibraryWindow>());
        }
        finally
        {
            libraryCoordinator.Dispose();
            coordinator.Close();
            owner.Close();
        }
    }
}
