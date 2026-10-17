using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;
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

public sealed class ShortcutBadgeUiTests
{
    [AvaloniaFact]
    public void FocusedRecorderOwnsLocalKeysUntilFocusLeaves()
    {
        using var environment = new UiTestEnvironment();
        var executed = new List<WorkbenchCommand>();
        var commands = Enum.GetValues<WorkbenchCommand>().ToDictionary(id => id,
            id => new WorkbenchCommandAdapter(() => executed.Add(id), () => true));
        using var registry = new WorkbenchWindowRegistry(new(id => commands[id]), () => { },
            includeApplicationMenu: false);
        var window = new SettingsWindow(new());
        try
        {
            registry.RegisterAuxiliary(window);
            window.Show();
            window.SelectPage(SettingsPage.SHORTCUTS);
            var input = UiTestActions.Find<Control>(window, "GestureInput");
            Assert.True(input.Focus());
            Assert.False(window.IsShortcutCaptureActive);
            UiTestActions.Press(window, Key.Space);
            UiTestActions.Press(window, Key.F8);
            UiTestActions.Press(window, Key.N,
                OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
            Assert.Empty(executed);

            Assert.True(UiTestActions.Find<Button>(window, "ClearShortcutButton").Focus());
            UiTestActions.Press(window, Key.F8);
            Assert.Equal(WorkbenchCommand.TIMING_ENTER, Assert.Single(executed));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US", false, 860)]
    [InlineData("zh-CN", true, 860)]
    [InlineData("ja-JP", false, 1100)]
    [InlineData("en-US", true, 1100)]
    public void SettingsShowPlatformKeyBadgesInRowsAndRecorder(string language, bool dark, int width)
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new() { Language = language })
        {
            Width = width,
            Height = 580,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.SHORTCUTS);
            UiTestActions.SelectShortcut(window, WorkbenchCommand.SAVE_PROJECT_AS);
            var list = UiTestActions.Find<ListBox>(window, "ShortcutList");
            list.ScrollIntoView(list.SelectedIndex);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(list.SelectedIndex));
            var labels = row.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible).ToArray();
            var expected = new[] { OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl", "Shift", "S" };
            Assert.Equal(expected, labels.Skip(1).Select(text => text.Text));
            var recorder = UiTestActions.Find<Control>(window, "GestureInput");
            AssertBadges(recorder, expected);
            foreach (var label in labels.Skip(1))
            {
                AssertBadge(label, row);
            }

            var action = BoundsIn(labels[0], window);
            var firstKey = BoundsIn(labels[1], window);
            Assert.True(action.Right <= firstKey.Left);
            Assert.True(recorder.Focus());
            window.ViewModel.Shortcuts.ClearCommand.Execute(null);
            window.UpdateLayout();
            Assert.DoesNotContain(recorder.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible);
            window.ViewModel.Shortcuts.ResetCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AssertBadges(recorder, expected);
            Assert.Equal("CmdOrCtrl+Shift+S", window.ViewModel.Shortcuts.Gesture);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertBadges(Control control, string[] expected)
    {
        var labels = control.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible).ToArray();
        Assert.Equal(expected, labels.Select(text => text.Text));
        foreach (var label in labels)
        {
            AssertBadge(label, control);
        }
    }

    private static void AssertBadge(TextBlock label, Control container)
    {
        var border = Assert.IsType<Border>(label.GetVisualParent());
        Assert.True(border.BorderThickness.Left > 0);
        Assert.True(border.CornerRadius.TopLeft > 0);
        Assert.NotNull(border.Background);
        Assert.Equal(20, label.LineHeight);
        Assert.Contains("PingFang SC", label.FontFamily.Name, StringComparison.Ordinal);
        Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
        var bounds = BoundsIn(border, container);
        Assert.True(new Rect(container.Bounds.Size).Contains(bounds));
        var textBounds = BoundsIn(label, border);
        Assert.InRange(Math.Abs(textBounds.Center.Y - border.Bounds.Height / 2), 0, 0.5);
    }

    private static Rect BoundsIn(Control control, Control relativeTo)
    {
        var position = control.TranslatePoint(default, relativeTo);
        Assert.NotNull(position);
        return new(position.Value, control.Bounds.Size);
    }
}
