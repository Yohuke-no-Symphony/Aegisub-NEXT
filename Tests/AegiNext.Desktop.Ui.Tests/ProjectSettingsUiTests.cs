using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Projects;
using AegiNext.Desktop.Startup;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ProjectSettingsUiTests
{
    [AvaloniaFact]
    public void ImportCopyToggleUpdatesGlobalProjectPreferencesWithoutDiscardingInvalidDrafts()
    {
        using var environment = new UiTestEnvironment();
        var preferences = new WorkbenchPreferences { Projects = new() { WorkspaceRoot = environment.DirectoryPath } };
        var window = new SettingsWindow(preferences);
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.PROJECTS);
            var changes = new List<ProjectPreferences>();
            window.ProjectsChanged += (_, change) =>
            {
                changes.Add(change.Preferences);
                preferences = preferences with { Projects = change.Preferences };
                window.UpdatePreferences(preferences);
            };
            var interval = UiTestActions.Find<NumericDraftInput>(window, "AutoSaveIntervalInput");
            interval.RawText = "3e-";
            UiTestActions.Click(window, "CopyImportedAssetsToggle");

            Assert.False(Assert.Single(changes).CopyImportedAssetsToSources);
            Assert.Equal("3e-", interval.RawText);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            var toggle = UiTestActions.Find<CheckBox>(window, "CopyImportedAssetsToggle");
            Assert.False(toggle.IsChecked);
            Assert.Equal(Localization.Get("Settings.CopyImportedAssetsToSources"), toggle.Content);
            Assert.Single(changes);
            UiTestActions.Click(window, "CopyImportedAssetsToggle");
            Assert.True(preferences.Projects.CopyImportedAssetsToSources);
            Assert.Equal(2, changes.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ProjectAndThemePreferenceUpdatesKeepDirectLanguageSelection()
    {
        using var environment = new UiTestEnvironment();
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { Language = "en-US" });
        await context.Initialization;
        Localization.SetLanguage("zh-CN");

        context.UpdatePreferences(current => current with
        {
            Projects = current.Projects with { MaximumBackupCount = 6 }
        });
        context.UpdatePreferences(current => current with { Theme = WorkbenchTheme.DARK, Volume = 0.375f });

        Assert.Equal("zh-CN", Localization.SelectedLanguageID);
        Assert.Equal("en-US", context.Preferences.Language);
        context.UpdatePreferences(current => current with { Language = "zh-CN" });
        context.UpdatePreferences(current => current with { Language = "en-US" });
        Assert.Equal("en-US", Localization.SelectedLanguageID);
        await context.Completion;
        Assert.Equal(context.Preferences, context.PreferencesStore.Load());
    }

    [AvaloniaFact]
    public void ProjectPageCommitsOneFieldAndPreservesInvalidDraftAcrossLanguageAndTheme()
    {
        using var environment = new UiTestEnvironment();
        var preferences = new WorkbenchPreferences { Projects = new() { WorkspaceRoot = environment.DirectoryPath } };
        var window = new SettingsWindow(preferences);
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.PROJECTS);
            var changes = new List<ProjectPreferences>();
            window.ProjectsChanged += (_, change) =>
            {
                changes.Add(change.Preferences);
                preferences = preferences with { Projects = change.Preferences };
                window.UpdatePreferences(preferences);
            };
            var interval = UiTestActions.Find<NumericDraftInput>(window, "AutoSaveIntervalInput");
            Assert.True(Assert.Single(interval.GetVisualDescendants().OfType<TextBox>()).Focus());
            interval.RawText = "3";
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(3, Assert.Single(changes).AutoSaveIntervalMinutes);
            interval.RawText = "3.";
            UiTestActions.Press(window, Key.Enter);
            Assert.True(window.ViewModel.HasError);
            Localization.SetLanguage("zh-CN");
            window.UpdatePreferences(preferences with { Theme = WorkbenchTheme.DARK, Language = "zh-CN" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("3.", interval.RawText);
            Assert.Equal("项目", window.ViewModel.PageTitle);
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("3", interval.RawText);
            Assert.False(window.ViewModel.HasError);
            Assert.Single(changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WorkspaceImeConfirmationKeepsTheDraftUntilCompositionEnds()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new() { Projects = new() { WorkspaceRoot = environment.DirectoryPath } });
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.PROJECTS);
            var changes = 0;
            window.ProjectsChanged += (_, _) => changes++;
            var input = UiTestActions.Find<TextBox>(window, "WorkspaceRootInput");
            Assert.True(input.Focus());
            var next = Path.Combine(environment.DirectoryPath, "中文 ABC 123");
            input.Text = next;
            var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
            presenter.PreeditText = "目录候选";
            UiTestActions.Press(window, Key.Enter);
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal(0, changes);
            Assert.Equal(next, input.Text);
            presenter.PreeditText = null;

            UiTestActions.Press(window, Key.Enter);

            Assert.Equal(1, changes);
            Assert.Equal(next, window.ViewModel.Projects.Preferences.WorkspaceRoot);
            Assert.False(Directory.Exists(next));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ProtectionTogglesOnlyDisableTheirOwnInputsAndKeepValues()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new() { Projects = new() { WorkspaceRoot = environment.DirectoryPath } });
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.PROJECTS);
            window.ViewModel.Projects.AutoSaveEnabled = false;
            Assert.False(UiTestActions.Find<NumericDraftInput>(window, "AutoSaveIntervalInput").IsEffectivelyEnabled);
            Assert.True(UiTestActions.Find<NumericDraftInput>(window, "BackupIntervalInput").IsEffectivelyEnabled);
            window.ViewModel.Projects.BackupEnabled = false;
            Assert.False(UiTestActions.Find<NumericDraftInput>(window, "MaximumBackupCountInput").IsEffectivelyEnabled);
            Assert.Equal("2", window.ViewModel.Projects.AutoSaveIntervalText);
            Assert.Equal("5", window.ViewModel.Projects.BackupIntervalText);
            Assert.Equal("20", window.ViewModel.Projects.MaximumBackupCountText);
        }
        finally
        {
            window.Close();
        }
    }
}
