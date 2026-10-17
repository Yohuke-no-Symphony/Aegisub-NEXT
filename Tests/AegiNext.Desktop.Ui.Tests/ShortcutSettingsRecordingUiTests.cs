using AegiNext.Desktop.Controls.Common;
using System.Globalization;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ShortcutSettingsRecordingUiTests
{
    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Control, KeyModifiers.Control)]
    [InlineData(RawInputModifiers.Meta, KeyModifiers.Meta)]
    public async Task CharacterChordUsesSemanticKeyAndRejectsTrailingTextAndRepeats(
        RawInputModifiers inputModifiers, KeyModifiers keyModifiers)
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.SHORTCUTS);
            var input = UiTestActions.Find<ShortcutGestureDisplay>(window, "GestureInput");
            var command = window.ViewModel.Shortcuts.SelectedRow!.Command;
            var original = input.Gesture;
            Assert.True(input.Focusable);
            input.Focus();
            window.KeyTextInput("Control+K\n");
            Assert.Equal(original, input.Gesture);
            Assert.Equal(original, window.ViewModel.Shortcuts.Gesture);
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            await clipboard.SetTextAsync("CmdOrCtrl+S\n");
            UiTestActions.Press(window, Key.V,
                OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(original, input.Gesture);
            Assert.Equal(original, window.ViewModel.Shortcuts.Gesture);

            UiTestActions.Click(window, "RecordShortcutButton");
            Assert.True(input.Focus());
            window.KeyPress(Key.K, inputModifiers, PhysicalKey.None, null);
            Assert.False(window.ViewModel.Shortcuts.IsRecording);
            Assert.True(window.IsShortcutCaptureActive);
            window.KeyTextInput("k");
            window.KeyPress(Key.K, inputModifiers, PhysicalKey.None, null);
            window.KeyTextInput("k");
            window.KeyRelease(Key.K, inputModifiers, PhysicalKey.None, null);
            window.KeyTextInput("\n");
            var expected = ShortcutConfiguration.FormatGesture(Key.K, keyModifiers);
            Assert.Equal(expected, input.Gesture);
            Assert.Equal(expected, window.ViewModel.Shortcuts.Gesture);
            Assert.False(window.IsShortcutCaptureActive);
            Assert.Null(window.ViewModel.Shortcuts.Error);

            UiTestActions.SelectShortcut(window, WorkbenchCommand.SAVE_PROJECT);
            Assert.Equal("CmdOrCtrl+S", input.Gesture);
            Localization.SetLanguage("zh-CN");
            UiTestActions.SelectShortcut(window, command);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected, input.Gesture);
            Assert.Null(window.ViewModel.Shortcuts.Error);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RecordingOriginalChordIsValidAndRealConflictNamesItsOwner()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new() { Language = "en-US" });
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.SHORTCUTS);
            var input = UiTestActions.Find<ShortcutGestureDisplay>(window, "GestureInput");
            var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            UiTestActions.Click(window, "RecordShortcutButton");
            input.Focus();
            UiTestActions.Press(window, Key.N, modifiers);
            window.KeyTextInput("n");
            Assert.Equal("CmdOrCtrl+N", input.Gesture);
            Assert.Null(window.ViewModel.Shortcuts.Error);

            UiTestActions.Click(window, "RecordShortcutButton");
            input.Focus();
            UiTestActions.Press(window, Key.S, modifiers);
            Assert.Contains(Localization.Get("Settings." + nameof(WorkbenchCommand.SAVE_PROJECT)), window.ViewModel.Shortcuts.Error);
            var saves = new List<SettingsShortcutsChangedEventArgs>();
            window.ShortcutsChanged += (_, value) => saves.Add(value);
            Assert.Empty(saves);
            UiTestActions.Click(window, "ClearShortcutButton");
            Assert.Equal(string.Empty, input.Gesture);
            Assert.Null(window.ViewModel.Shortcuts.Error);
            Assert.Equal(string.Empty, Assert.Single(saves).Bindings.Single(row => row.Command == WorkbenchCommand.NEW_PROJECT).Gesture);

            UiTestActions.Click(window, "RecordShortcutButton");
            input.Focus();
            UiTestActions.Press(window, Key.Escape);
            window.KeyTextInput("escape");
            Assert.False(window.IsShortcutCaptureActive);
            Assert.Equal(string.Empty, input.Gesture);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RecordedCharacterChordAutomaticallyRoutesThroughRealMainWorkbenchAndPersistsOnReopen()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var main = context.Window;
        main.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.SHORTCUTS);
        UiTestActions.SelectShortcut(settings, WorkbenchCommand.ADD_SUBTITLE);
        UiTestActions.Click(settings, "RecordShortcutButton");
        var input = UiTestActions.Find<ShortcutGestureDisplay>(settings, "GestureInput");
        input.Focus();
        var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        settings.KeyPress(Key.K, modifiers, PhysicalKey.None, null);
        settings.KeyTextInput("k");
        settings.KeyPress(Key.K, modifiers, PhysicalKey.None, null);
        settings.KeyRelease(Key.K, modifiers, PhysicalKey.None, null);
        Assert.Empty(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal("CmdOrCtrl+K", input.Gesture);
        Assert.Equal("CmdOrCtrl+K", context.Session.Preferences.ShortcutBindings.Single(row => row.Command == WorkbenchCommand.ADD_SUBTITLE).Gesture);
        await context.Session.ApplicationContext.Completion;
        var persisted = context.Session.ApplicationContext.PreferencesStore.Load();
        Assert.Equal("CmdOrCtrl+K", persisted.ShortcutBindings.Single(row => row.Command == WorkbenchCommand.ADD_SUBTITLE).Gesture);
        settings.Close();
        main.Activate();
        Assert.True(UiTestActions.Find<Button>(main, "PlayButton").Focus());
        UiTestActions.Press(main, Key.K, modifiers);
        await context.Session.ApplicationContext.Completion.WaitAsync(TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Single(context.Session.DocumentSnapshot.Subtitles);

        main.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var reopened = Assert.Single(main.OwnedWindows.OfType<SettingsWindow>());
        reopened.SelectPage(SettingsPage.SHORTCUTS);
        UiTestActions.SelectShortcut(reopened, WorkbenchCommand.ADD_SUBTITLE);
        Assert.Equal("CmdOrCtrl+K", UiTestActions.Find<ShortcutGestureDisplay>(reopened, "GestureInput").Gesture);
        reopened.Close();
    }
}
