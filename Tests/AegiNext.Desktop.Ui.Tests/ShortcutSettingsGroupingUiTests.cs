using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ShortcutSettingsGroupingUiTests
{
    [AvaloniaTheory]
    [InlineData("en-US", false, 860)]
    [InlineData("zh-CN", true, 860)]
    [InlineData("en-US", true, 1100)]
    [InlineData("zh-CN", false, 1100)]
    public void SectionsAreReadableAndNonselectableAcrossLanguagesThemesAndWidths(string language, bool dark, int width)
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
            var model = window.ViewModel.Shortcuts;
            var list = UiTestActions.Find<ListBox>(window, "ShortcutList");
            foreach (var header in model.Items.Where(item => !item.IsCommand))
            {
                var index = Array.IndexOf(model.Items, header);
                list.ScrollIntoView(index);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var container = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(index));
                Assert.False(container.IsEffectivelyEnabled);
                Assert.False(container.Focusable);
                var title = Assert.Single(container.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible);
                Assert.Equal(header.SectionTitle, title.Text);
                Assert.True(title.Bounds.Height > 0);
                Assert.True(title.DesiredSize.Width <= container.Bounds.Width);
                var selected = model.SelectedRow;
                var point = container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.Same(selected, model.SelectedRow);
            }

            UiTestActions.SelectShortcut(window, WorkbenchCommand.AUDITION_SUBTITLE);
            var rowIndex = Array.IndexOf(model.Items, model.SelectedItem!);
            list.ScrollIntoView(rowIndex);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var rowContainer = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(rowIndex));
            var cells = rowContainer.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).ToArray();
            Assert.Equal(2, cells.Length);
            var action = cells[0].TranslatePoint(default, rowContainer)!.Value;
            var key = cells[1].TranslatePoint(default, rowContainer)!.Value;
            Assert.True(action.X + cells[0].Bounds.Width <= key.X);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyboardSkipsHeadersAndLanguageSwitchPreservesCrossSectionDraftAndSelection()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new() { Language = "en-US" });
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.SHORTCUTS);
            var model = window.ViewModel.Shortcuts;
            var list = UiTestActions.Find<ListBox>(window, "ShortcutList");
            UiTestActions.SelectShortcut(window, WorkbenchCommand.EXPORT_VIDEO);
            list.ScrollIntoView(list.SelectedIndex);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(list.SelectedIndex)).Focus());
            UiTestActions.Press(window, Key.Down);
            Assert.Equal(WorkbenchCommand.UNDO, model.SelectedRow!.Command);
            UiTestActions.Press(window, Key.Up);
            Assert.Equal(WorkbenchCommand.EXPORT_VIDEO, model.SelectedRow.Command);

            UiTestActions.SelectShortcut(window, WorkbenchCommand.AUDITION_SUBTITLE);
            model.Gesture = "Control+";
            var saves = new List<SettingsShortcutsChangedEventArgs>();
            window.ShortcutsChanged += (_, change) => saves.Add(change);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WorkbenchCommand.AUDITION_SUBTITLE, model.SelectedRow!.Command);
            Assert.Same(model.SelectedItem, list.SelectedItem);
            Assert.Equal("Control+", UiTestActions.Find<ShortcutGestureDisplay>(window, "GestureInput").Gesture);
            Assert.Empty(saves);
            Assert.Equal("播放与试听", model.Items.Where(item => !item.IsCommand).ElementAt(3).SectionTitle);
            model.Gesture = "CmdOrCtrl+S";
            Assert.Empty(saves);
            Assert.Contains(Localization.Get("Settings.SAVE_PROJECT"), model.Error);
            UiTestActions.Click(window, "ClearShortcutButton");
            Assert.Equal(string.Empty, Assert.Single(saves).Bindings.Single(binding => binding.Command == WorkbenchCommand.AUDITION_SUBTITLE).Gesture);
        }
        finally
        {
            window.Close();
        }
    }
}
