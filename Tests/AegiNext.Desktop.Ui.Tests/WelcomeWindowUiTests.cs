using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WelcomeWindowUiTests
{
    [AvaloniaFact]
    public async Task SearchAndEnterOpenTheSelectedRecentProject()
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        var first = Path.Combine(environment.DirectoryPath, "中文 Project 123.aeginext");
        var second = Path.Combine(environment.DirectoryPath, "Other.aeginext");
        await File.WriteAllTextAsync(first, "first fixture", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "second fixture", TestContext.Current.CancellationToken);
        await history.RecordAsync(first);
        await history.RecordAsync(second);
        string? opened = null;
        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            path =>
            {
                opened = path;
                return Task.CompletedTask;
            }, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel);
        try
        {
            window.Show();
            UiTestActions.Find<TextBox>(window, "ProjectSearchInput").Text = "中文";
            var item = Assert.Single(viewModel.Projects);
            Assert.Equal(first, item.Path);
            var list = UiTestActions.Find<ListBox>(window, "RecentProjectList");
            Assert.Same(item, list.SelectedItem);
            Assert.True(list.Focus());
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(first, opened);
            UiTestActions.Find<TextBox>(window, "ProjectSearchInput").Text = "no match";
            Assert.Empty(viewModel.Projects);
            Assert.True(UiTestActions.Find<TextBlock>(window, "WelcomeEmptyState").IsVisible);
            Assert.Equal("No matching projects", viewModel.EmptyTitle);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BusyStateDisablesAllProjectActionsAndLanguageRefreshKeepsSearch()
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel);
        try
        {
            window.Show();
            UiTestActions.Find<TextBox>(window, "ProjectSearchInput").Text = "中文123";
            viewModel.IsBusy = true;
            Assert.False(UiTestActions.Find<Button>(window, "WelcomeNewProjectButton").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(window, "WelcomeOpenProjectButton").IsEffectivelyEnabled);
            Assert.False(UiTestActions.Find<Button>(window, "WelcomeSettingsButton").IsEffectivelyEnabled);
            Localization.SetLanguage("zh-CN");
            Assert.Equal("欢迎使用 AegiNext", window.Title);
            Assert.Equal("中文123", viewModel.SearchText);
            Assert.Equal("没有匹配的项目", viewModel.EmptyTitle);
            viewModel.IsBusy = false;
            Assert.True(UiTestActions.Find<Button>(window, "WelcomeOpenProjectButton").IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RightClickRemovesTheHistoryEntryWithoutOpeningOrDeletingTheProject(bool missing)
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        var path = Path.Combine(environment.DirectoryPath, "fixture.aeginext");
        if (!missing)
        {
            await File.WriteAllTextAsync(path, "fixture", TestContext.Current.CancellationToken);
        }
        await history.RecordAsync(path);
        var opened = new List<string?>();
        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            value =>
            {
                opened.Add(value);
                return Task.CompletedTask;
            }, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel);
        ContextMenu? menu = null;
        try
        {
            window.Show();
            var item = Assert.Single(viewModel.Projects);
            Assert.Equal(missing, item.IsUnavailable);
            var entry = ProjectEntry(window, item);
            menu = entry.ContextMenu;
            Assert.NotNull(menu);

            Click(window, UiTestActions.Find<Border>(entry, "ProjectIcon"), MouseButton.Right);

            Assert.True(menu.IsOpen);
            Assert.Empty(opened);
            var remove = Assert.Single(menu.Items.OfType<MenuItem>());
            Assert.Equal(Localization.Get("Welcome.RemoveFromRecent"), remove.Header);
            Assert.Same(item, remove.CommandParameter);
            var popup = TopLevel.GetTopLevel(remove);
            Assert.NotNull(popup);
            popup.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            popup.UpdateLayout();

            Click(popup, remove, MouseButton.Left);
            await (viewModel.RemoveProjectCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Empty(viewModel.Projects);
            Assert.Empty(history.Entries);
            Assert.Empty(opened);
            Assert.False(menu.IsOpen);
            Assert.Equal(!missing, File.Exists(path));
            if (!missing)
            {
                Assert.Equal("fixture", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
            }
            Assert.Equal("No recent projects", viewModel.EmptyTitle);
        }
        finally
        {
            menu?.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OnePointerClickOpensTheClickedProjectAndBindsItsStableInitialsIcon()
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        var first = Path.Combine(environment.DirectoryPath, "Alpha Beta.aeginext");
        var second = Path.Combine(environment.DirectoryPath, "Other Project.aeginext");
        await File.WriteAllTextAsync(first, "first fixture", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(second, "second fixture", TestContext.Current.CancellationToken);
        await history.RecordAsync(first);
        await history.RecordAsync(second);
        var opened = new List<string?>();
        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            path =>
            {
                opened.Add(path);
                return Task.CompletedTask;
            }, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel) { RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            window.Show();
            Assert.Equal(second, viewModel.SelectedProject!.Path);
            var item = Assert.Single(viewModel.Projects, project => project.Path == first);
            var entry = ProjectEntry(window, item);
            var icon = UiTestActions.Find<Border>(entry, "ProjectIcon");
            var initials = UiTestActions.Find<TextBlock>(entry, "ProjectIconInitials");
            Assert.Equal("AB", initials.Text);
            Assert.Same(item.IconBackground, icon.Background);
            Assert.Same(item.IconForeground, initials.Foreground);
            var rebuilt = new RecentProjectListItem(item.Entry);
            Assert.Equal(Assert.IsType<SolidColorBrush>(item.IconBackground).Color,
                Assert.IsType<SolidColorBrush>(rebuilt.IconBackground).Color);

            Click(window, icon, MouseButton.Left);

            Assert.Equal(first, Assert.Single(opened));
            Assert.Same(item, viewModel.SelectedProject);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MissingProjectIsMutedAndRejectsPointerAndEnterUntilTheFileReturns()
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        var path = Path.Combine(environment.DirectoryPath, "Missing Project.aeginext");
        await history.RecordAsync(path);
        var opened = new List<string?>();
        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            value =>
            {
                opened.Add(value);
                return Task.CompletedTask;
            }, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel) { RequestedThemeVariant = ThemeVariant.Light };
        try
        {
            window.Show();
            var item = Assert.Single(viewModel.Projects);
            var entry = ProjectEntry(window, item);
            var icon = UiTestActions.Find<Border>(entry, "ProjectIcon");
            Assert.True(item.IsUnavailable);
            Assert.Contains("unavailable", entry.Classes);
            Assert.InRange(entry.Opacity, 0.1, 0.9);
            Assert.Equal(Colors.Gainsboro, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Background).Color);
            Assert.Equal(Localization.Get("Welcome.ProjectUnavailable"), item.Availability);
            Assert.True(entry.IsEffectivelyEnabled);
            Assert.False(viewModel.OpenSelectedProjectCommand.CanExecute(null));

            Click(window, icon, MouseButton.Left);
            var list = UiTestActions.Find<ListBox>(window, "RecentProjectList");
            Assert.True(list.Focus());
            UiTestActions.Press(window, Key.Enter);

            Assert.Empty(opened);
            Assert.Single(history.Entries);
            await File.WriteAllTextAsync(path, "restored fixture", TestContext.Current.CancellationToken);
            viewModel.RefreshAvailability();
            UpdateLayout(window);

            Assert.False(item.IsUnavailable);
            Assert.DoesNotContain("unavailable", entry.Classes);
            Assert.Equal(1, entry.Opacity);
            Assert.Same(item.IconBackground, icon.Background);
            Assert.NotEqual(Colors.Gainsboro, Assert.IsAssignableFrom<ISolidColorBrush>(icon.Background).Color);
            Assert.Empty(item.Availability);
            Assert.True(viewModel.OpenSelectedProjectCommand.CanExecute(null));
            Assert.True(list.Focus());
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(path, Assert.Single(opened));

            Click(window, icon, MouseButton.Left);
            Assert.Equal(new[] { path, path }, opened);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(800, 520)]
    [InlineData(1200, 800)]
    public async Task TopToolbarKeepsSettingsOnTheRightAndRecentProjectsUseTheFullContentWidth(double width,
        double height)
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        var path = Path.Combine(environment.DirectoryPath, "中文 Project 123.aeginext");
        await File.WriteAllTextAsync(path, "fixture", TestContext.Current.CancellationToken);
        await history.RecordAsync(path);
        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel);
        try
        {
            Localization.SetLanguage("zh-CN");
            window.Width = width;
            window.Height = height;
            window.Show();
            UpdateLayout(window);
            foreach (var name in new[] { "ProjectSearchInput", "WelcomeNewProjectButton", "WelcomeOpenProjectButton", "WelcomeSettingsButton" })
            {
                var control = UiTestActions.Find<Control>(window, name);
                var origin = control.TranslatePoint(new(), window);
                Assert.NotNull(origin);
                Assert.True(control.Bounds.Width > 0 && control.Bounds.Height >= 32);
                Assert.True(origin.Value.X >= 0);
                Assert.True(origin.Value.X + control.Bounds.Width <= window.ClientSize.Width + 1);
                Assert.True(origin.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 1);
            }

            var search = WindowBounds(UiTestActions.Find<TextBox>(window, "ProjectSearchInput"), window);
            var create = WindowBounds(UiTestActions.Find<Button>(window, "WelcomeNewProjectButton"), window);
            var open = WindowBounds(UiTestActions.Find<Button>(window, "WelcomeOpenProjectButton"), window);
            var settings = WindowBounds(UiTestActions.Find<Button>(window, "WelcomeSettingsButton"), window);
            var projects = WindowBounds(UiTestActions.Find<ListBox>(window, "RecentProjectList"), window);
            Assert.InRange(search.Left, 16, 40);
            Assert.True(search.Width >= 100);
            Assert.True(search.Right < create.Left);
            Assert.True(create.Right < open.Left);
            Assert.True(open.Right < settings.Left);
            Assert.InRange(settings.Right, window.ClientSize.Width - 40, window.ClientSize.Width - 16);
            Assert.InRange(Math.Abs(create.Center.Y - search.Center.Y), 0, 1);
            Assert.InRange(Math.Abs(open.Center.Y - search.Center.Y), 0, 1);
            Assert.InRange(Math.Abs(settings.Center.Y - search.Center.Y), 0, 1);
            Assert.True(projects.Top > settings.Bottom + 8);
            Assert.InRange(Math.Abs(projects.Left - search.Left), 0, 1);
            Assert.InRange(Math.Abs(projects.Right - settings.Right), 0, 1);
        }
        finally
        {
            window.Close();
        }
    }

    private static Grid ProjectEntry(WelcomeWindow window, RecentProjectListItem item)
    {
        UpdateLayout(window);
        var list = UiTestActions.Find<ListBox>(window, "RecentProjectList");
        var container = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>(),
            candidate => ReferenceEquals(candidate.DataContext, item));
        return Assert.Single(container.GetVisualDescendants().OfType<Grid>(),
            candidate => candidate.Classes.Contains("project-entry"));
    }

    private static void Click(TopLevel host, Control target, MouseButton button)
    {
        var point = target.TranslatePoint(new(target.Bounds.Width / 2, target.Bounds.Height / 2), host);
        Assert.NotNull(point);
        Assert.True(target.Bounds.Width > 0 && target.Bounds.Height > 0);
        host.MouseDown(point.Value, button);
        host.MouseUp(point.Value, button);
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect WindowBounds(Control control, Window window)
    {
        var point = control.TranslatePoint(new(), window);
        Assert.NotNull(point);
        return new(point.Value, control.Bounds.Size);
    }

    private static void UpdateLayout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
