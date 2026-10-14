using AegiNext.Desktop.EffectLibrary;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Media.Decoding;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SharedSettingsUiTests
{
    [AvaloniaFact]
    public async Task SettingsWithoutAProjectOfferAllPagesAndPersistPersonalTemplatesAndDecodeDefaults()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new SettingsWindowCoordinator(context);
        using var libraryCoordinator = new EffectLibraryWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            foreach (var page in Enum.GetValues<SettingsPage>())
            {
                window.SelectPage(page);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(page, window.CurrentPage);
                Assert.False(window.ViewModel.HasError);
            }

            await libraryCoordinator.OpenAsync(owner);
            var library = Assert.IsType<EffectLibraryWindow>(libraryCoordinator.Window);
            foreach (var page in Enum.GetValues<EffectLibraryPage>())
            {
                library.SelectPage(page);
                Dispatcher.UIThread.RunJobs();
                library.UpdateLayout();
                Assert.Equal(page, library.CurrentPage);
                Assert.False(library.ViewModel.HasError);
            }
            library.SelectPage(EffectLibraryPage.STYLES);
            UiTestActions.Click(library, "AddStyleButton");
            Assert.False(UiTestActions.Find<Button>(library, "CaptureStyleButton").IsEffectivelyEnabled);
            Assert.False(library.ViewModel.Styles.HasPositionMeasurementError);
            UiTestActions.Find<TextBox>(library, "StyleNameInput").Text = "个人样式 01";
            UiTestActions.Click(library, "SaveStyleButton");
            await context.Completion;
            Assert.Equal("个人样式 01", Assert.Single(context.StyleLibrary.Snapshot.Presets).Name);

            library.SelectPage(EffectLibraryPage.EFFECTS);
            UiTestActions.Click(library, "AddEffectScriptButton");
            UiTestActions.Find<TextBox>(library, "EffectScriptNameInput").Text = "个人特效 01";
            UiTestActions.Click(library, "SaveEffectScriptButton");
            await context.Completion;
            Assert.Equal("个人特效 01", Assert.Single(context.EffectScriptLibrary.Snapshot.Presets).Name);

            window.SelectPage(SettingsPage.MEDIA);
            Assert.Equal(AegiNext.Desktop.I18n.Localization.Get("Settings.DecodeNoMedia"), window.ViewModel.Media.DecodeStatus);
            UiTestActions.Find<ComboBox>(window, "PreviewDecodeModeCombo").SelectedItem =
                window.ViewModel.Media.DecodeModes.Single(value => value.Mode == VideoDecodeMode.Software);
            await context.Completion;
            Assert.Equal(VideoDecodeMode.Software, context.Preferences.PreviewDecodeMode);
            Assert.Equal(VideoDecodeMode.Software, context.PreferencesStore.Load().PreviewDecodeMode);
        }
        finally
        {
            libraryCoordinator.Dispose();
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task BusyAndAppearanceNotificationsPreserveUnsavedStyleAndEffectDrafts()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new EffectLibraryWindowCoordinator(context);
        var owner = new Window();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await context.Initialization;
            var saved = new SubtitleStylePreset(Guid.NewGuid(), "Saved", new());
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(saved));
            owner.Show();
            await coordinator.OpenAsync(owner);
            var window = Assert.IsType<EffectLibraryWindow>(coordinator.Window);
            window.SelectPage(EffectLibraryPage.STYLES);
            UiTestActions.Find<TextBox>(window, "StyleNameInput").Text = "Unsaved 样式";
            window.ViewModel.Styles.FontSizeText = "invalid draft";
            var draftVersion = window.ViewModel.Styles.DraftVersion;
            window.ViewModel.Effects.AddCommand.Execute(null);
            window.ViewModel.Effects.Source = "effect broken\n";
            window.ViewModel.Effects.ValidateCommand.Execute(null);
            var effectId = window.ViewModel.Effects.Draft!.Id;
            var diagnosticLine = window.ViewModel.Effects.DiagnosticLine;

            var operation = context.RunEffectLibraryOperationAsync(() =>
            {
                started.TrySetResult();
                return release.Task;
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.ViewModel.Styles.IsBusy);
            Assert.True(window.ViewModel.Effects.IsBusy);
            context.UpdatePreferences(value => value with { Theme = WorkbenchTheme.DARK });
            release.SetResult();
            await operation;

            Assert.Equal(draftVersion, window.ViewModel.Styles.DraftVersion);
            Assert.Equal("Unsaved 样式", window.ViewModel.Styles.Name);
            Assert.Equal("invalid draft", window.ViewModel.Styles.FontSizeText);
            Assert.Equal(effectId, window.ViewModel.Effects.Draft!.Id);
            Assert.Equal("effect broken\n", window.ViewModel.Effects.Source);
            Assert.Equal(diagnosticLine, window.ViewModel.Effects.DiagnosticLine);
            Assert.False(window.ViewModel.Styles.IsBusy);
            Assert.False(window.ViewModel.Effects.IsBusy);
        }
        finally
        {
            release.TrySetResult();
            coordinator.Dispose();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task ClosingSettingsReleasesAllBindingsAndReopeningReadsTheLatestPreferences()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner);
            var closed = Assert.IsType<SettingsWindow>(coordinator.Window);
            coordinator.Close();
            Assert.Null(coordinator.Window);
            Assert.False(closed.IsVisible);

            closed.ViewModel.Appearance.ThemeIndex = (int)WorkbenchTheme.DARK;
            Assert.Equal(WorkbenchTheme.SYSTEM, context.Preferences.Theme);
            context.UpdatePreferences(value => value with { Theme = WorkbenchTheme.LIGHT });
            Assert.Equal((int)WorkbenchTheme.DARK, closed.ViewModel.Appearance.ThemeIndex);
            await coordinator.OpenAsync(owner, page: SettingsPage.COLORS);
            var reopened = Assert.IsType<SettingsWindow>(coordinator.Window);
            Assert.NotSame(closed, reopened);
            Assert.Equal(SettingsPage.COLORS, reopened.CurrentPage);
            Assert.Equal((int)WorkbenchTheme.LIGHT, reopened.ViewModel.Appearance.ThemeIndex);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task SharedLibraryFailureIsVisibleOnTheEffectPageAndDoesNotBreakFollowingOperations()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new EffectLibraryWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner, page: EffectLibraryPage.EFFECTS);
            var window = Assert.IsType<EffectLibraryWindow>(coordinator.Window);
            await Assert.ThrowsAsync<IOException>(() => context.RunEffectOperationAsync(() =>
                Task.FromException(new IOException("Storage unavailable"))));
            Assert.Equal("Storage unavailable", window.ViewModel.Error);
            Assert.True(window.ViewModel.HasError);
            Assert.False(window.ViewModel.Effects.IsBusy);

            UiTestActions.Click(window, "AddEffectScriptButton");
            UiTestActions.Click(window, "SaveEffectScriptButton");
            await context.Completion;
            Assert.Single(context.EffectScriptLibrary.Snapshot.Presets);
            Assert.False(window.ViewModel.HasError);
        }
        finally
        {
            coordinator.Dispose();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task WelcomeSettingsUseAModalLifetimeAndReuseTheExistingWindow()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            var modal = coordinator.OpenAsync(owner, page: SettingsPage.MEDIA, modal: true);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            Assert.False(modal.IsCompleted);
            await coordinator.OpenAsync(owner, page: SettingsPage.COLORS);
            Assert.Same(window, coordinator.Window);
            Assert.Equal(SettingsPage.COLORS, window.CurrentPage);
            Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            coordinator.Close();
            await modal;
            Assert.Null(coordinator.Window);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }
}
