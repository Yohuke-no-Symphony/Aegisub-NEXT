using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Views;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WelcomeProjectIconUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameNameInDifferentDirectoriesKeepsColorsWhenTheThemeAndListChange(bool startDark)
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        foreach (var folder in new[] { "first", "second" })
        {
            var directory = Directory.CreateDirectory(Path.Combine(environment.DirectoryPath, folder));
            var path = Path.Combine(directory.FullName, "Alpha Beta.aeginext");
            await File.WriteAllTextAsync(path, "fixture", TestContext.Current.CancellationToken);
            await history.RecordAsync(path);
        }

        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel);
        try
        {
            window.RequestedThemeVariant = startDark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Show();
            Flush(window);
            var original = AssertMatchingIcons(window, startDark);

            window.RequestedThemeVariant = startDark ? ThemeVariant.Light : ThemeVariant.Dark;
            Flush(window);
            Assert.NotEqual(original, AssertMatchingIcons(window, !startDark));

            viewModel.SearchText = "Alpha";
            Flush(window);
            AssertMatchingIcons(window, !startDark);

            window.RequestedThemeVariant = startDark ? ThemeVariant.Dark : ThemeVariant.Light;
            Flush(window);
            Assert.Equal(original, AssertMatchingIcons(window, startDark));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableIconUsesThemeNeutralColorsAndRestoresItsNameColor(bool dark)
    {
        using var environment = new UiTestEnvironment();
        await using var history = new RecentProjectService(environment.DirectoryPath);
        var path = Path.Combine(environment.DirectoryPath, "Alpha Beta.aeginext");
        await history.RecordAsync(path);
        var viewModel = new WelcomeViewModel(history, () => Task.CompletedTask,
            _ => Task.CompletedTask, () => Task.CompletedTask);
        var window = new WelcomeWindow(viewModel);
        try
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.Show();
            Flush(window);
            var icon = Assert.Single(ProjectIcons(window));
            var initials = Assert.Single(icon.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal(dark ? Colors.DimGray : Colors.Gainsboro, BrushColor(icon.Background));
            Assert.Equal(dark ? Colors.White : Colors.Black, BrushColor(initials.Foreground));

            await File.WriteAllTextAsync(path, "fixture", TestContext.Current.CancellationToken);
            viewModel.RefreshAvailability();
            Flush(window);

            var background = BrushColor(icon.Background);
            Assert.InRange(background.ToHsl().H, 298, 300);
            Assert.InRange(background.ToHsl().L, dark ? 0.30 : 0.80, dark ? 0.34 : 0.84);
        }
        finally
        {
            window.Close();
        }
    }

    private static Color AssertMatchingIcons(WelcomeWindow window, bool dark)
    {
        var icons = ProjectIcons(window);
        Assert.Equal(2, icons.Length);
        var first = BrushColor(icons[0].Background);
        Assert.Equal(first, BrushColor(icons[1].Background));
        Assert.InRange(first.ToHsl().H, 298, 300);
        Assert.InRange(first.ToHsl().L, dark ? 0.30 : 0.80, dark ? 0.34 : 0.84);
        foreach (var icon in icons)
        {
            var initials = Assert.Single(icon.GetVisualDescendants().OfType<TextBlock>());
            Assert.Equal("AB", initials.Text);
            Assert.Equal(dark ? Colors.White : Colors.Black, BrushColor(initials.Foreground));
        }
        return first;
    }

    private static Border[] ProjectIcons(WelcomeWindow window) =>
        window.GetVisualDescendants().OfType<Border>().Where(border => border.Name == "ProjectIcon").ToArray();

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
