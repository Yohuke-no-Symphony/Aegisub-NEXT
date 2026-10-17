using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Controls;
using AegiNext.Core.Timing;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MainWindowUiTests
{
    [AvaloniaFact]
    public async Task MenusAndButtonsShareCommandsAndRecordingTakesPriorityOverGlobalRoutes()
    {
        await using var context = new MainWindowTestContext();
        var window = context.Window;
        context.Session.UpdatePreferences(context.Session.Preferences with { WindowMenuOnMac = true });
        var menu = UiTestActions.Find<Menu>(window, "MainMenu");
        var items = menu.Items.OfType<MenuItem>().SelectMany(group => group.Items.OfType<MenuItem>()).ToArray();
        var openSettings = items.Single(item => Equals(item.Header, Localization.Get("Settings." + WorkbenchCommand.OPEN_SETTINGS.ToString())));
        Assert.Same(window.GetCommand(WorkbenchCommand.OPEN_SETTINGS), openSettings.Command);
        Assert.Null(window.FindControl<Button>("SettingsButton"));
        var exportVideo = items.Single(item => Equals(item.Header, Localization.Get("Settings." + WorkbenchCommand.EXPORT_VIDEO.ToString())));
        Assert.Same(UiTestActions.Find<Button>(window, "EncodeButton").Command, exportVideo.Command);
        Assert.True(openSettings.Command!.CanExecute(null));
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_ENTER).CanExecute(null));
        var undo = items.Single(item => Equals(item.Header, Localization.Get("Settings." + WorkbenchCommand.UNDO.ToString())));
        Assert.False(undo.Command!.CanExecute(null));

        await context.OpenMediaAsync();
        Assert.True(window.GetCommand(WorkbenchCommand.TIMING_ENTER).CanExecute(null));
        Assert.True(undo.Command.CanExecute(null));
        openSettings.Command.Execute(null);
        var settings = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.SHORTCUTS);
        UiTestActions.Click(settings, "RecordShortcutButton");
        var input = UiTestActions.Find<ShortcutGestureDisplay>(settings, "GestureInput");
        Assert.True(input.Focus());
        UiTestActions.Press(settings, Key.F8);
        Assert.Equal("F8", input.Gesture);
        Assert.False(settings.IsShortcutCaptureActive);
        Assert.Empty(UiTestActions.Find<ListBox>(window, "SubtitleList").Items);
        Assert.NotNull(settings.ViewModel.Shortcuts.Error);

        settings.SelectPage(SettingsPage.APPEARANCE);
        UiTestActions.Find<ComboBox>(settings, "ThemeCombo").SelectedIndex = 2;
        var accent = Color.Parse("#C54885");
        settings.SelectPage(SettingsPage.COLORS);
        UiTestActions.Find<ColorDraftInput>(settings, "AccentPicker").FindControl<ColorView>("Picker")!.Color = accent;
        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        var application = Assert.IsType<App>(Avalonia.Application.Current);
        var theme = Assert.Single(application.Styles.OfType<FluentTheme>());
        Assert.Equal(accent, theme.Palettes[ThemeVariant.Dark].Accent);
        Assert.Equal(accent, theme.Palettes[ThemeVariant.Light].Accent);
        Assert.True(application.TryGetResource("SystemAccentColor", ThemeVariant.Dark, out var resource));
        Assert.Equal(accent, Assert.IsType<Color>(resource));
    }

    [AvaloniaFact]
    public async Task TimingKeysReachTextBoxAndCompletedCycleAddsOnlyOnNextEnter()
    {
        await using var context = new MainWindowTestContext();
        var window = context.Window;
        await context.OpenMediaAsync();
        UiTestActions.Find<Button>(window, "AddCueButton").Command!.Execute(null);
        context.Session.Editor.SetSubtitleTiming(Assert.Single(window.DocumentSnapshot.Subtitles).Id,
            new(10), new(12), TimelineEditMode.CROP);
        var subtitles = UiTestActions.Find<ListBox>(window, "SubtitleList");
        var original = Assert.IsType<SubtitleRow>(Assert.Single(subtitles.Items));
        window.UpdateLayout();
        var textBox = subtitles.GetVisualDescendants().OfType<TextBox>().Single(input => input.AcceptsReturn);
        Assert.True(textBox.Focus());
        window.KeyTextInput("draft");
        UiTestActions.Press(window, Key.Space);
        window.KeyTextInput(" ");
        Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.Equal("draft ", textBox.Text);

        await context.Controller.SeekAsync(new(1, 3));
        UiTestActions.Press(window, Key.F8);
        Assert.Equal(2, subtitles.Items.Count);
        var entered = Assert.IsType<SubtitleRow>(subtitles.SelectedItem);
        Assert.NotEqual(original.Id, entered.Id);
        Assert.Equal("draft ", subtitles.Items.OfType<SubtitleRow>().Single(row => row.Id == original.Id).Text);
        Assert.Equal("00:00:00.333", entered.StartText);
        Assert.True(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        await context.Controller.PlayAsync();
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new MediaTime(4, 3), context.Controller.Snapshot.Position);
        window.UpdateLayout();
        textBox = subtitles.GetVisualDescendants().OfType<TextBox>().First(input => input.AcceptsReturn);
        Assert.True(textBox.Focus());
        UiTestActions.Press(window, Key.F9);
        var ended = Assert.IsType<SubtitleRow>(subtitles.SelectedItem);
        Assert.Equal(entered.Id, ended.Id);
        Assert.Equal("00:00:01.333", ended.EndText);
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        Assert.Equal(new MediaTime(4, 3), context.Controller.Snapshot.Position);
        Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
        UiTestActions.Press(window, Key.F9);
        Assert.Equal(2, subtitles.Items.Count);
        Assert.Equal(new MediaTime(4, 3), context.Controller.Snapshot.Position);
        UiTestActions.Press(window, Key.F8);
        Assert.Equal(3, subtitles.Items.Count);
        var next = Assert.IsType<SubtitleRow>(subtitles.SelectedItem);
        Assert.NotEqual(original.Id, next.Id);
        Assert.Equal("00:00:01.333", next.StartText);
        Assert.Equal(new MediaTime(4, 3), context.Controller.Snapshot.Position);
        UiTestCapture.CaptureWorkbench(window);
        Assert.Equal("00:00:01.333", Assert.IsType<SubtitleRow>(subtitles.SelectedItem).StartText);
        Assert.Equal(new MediaTime(4, 3), context.Controller.Snapshot.Position);
    }
}
