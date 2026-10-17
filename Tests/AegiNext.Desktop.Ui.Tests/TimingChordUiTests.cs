using AegiNext.Core.Timing;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimingChordUiTests
{
    [AvaloniaTheory]
    [InlineData("Control", Key.LeftCtrl, RawInputModifiers.Control)]
    [InlineData("Shift", Key.LeftShift, RawInputModifiers.Shift)]
    [InlineData("Alt", Key.LeftAlt, RawInputModifiers.Alt)]
    public async Task CustomTimingChordSurvivesModifierReleaseAndRepressBetweenEnterAndExit(
        string modifierName, Key modifierKey, RawInputModifiers modifiers)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        window.GetCommand(WorkbenchCommand.ADD_SUBTITLE).Execute(null);
        await WaitForCommandAsync(context, WorkbenchCommand.ADD_SUBTITLE);
        context.Session.Editor.SetSubtitleTiming(Assert.Single(window.DocumentSnapshot.Subtitles).Id,
            new(10), new(12), TimelineEditMode.CROP);
        var original = Assert.Single(window.DocumentSnapshot.Subtitles);
        window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.SHORTCUTS);
        SetGesture(settings, WorkbenchCommand.TIMING_ENTER, modifierName + "+F8");
        SetGesture(settings, WorkbenchCommand.TIMING_EXIT, modifierName + "+F9");
        settings.Close();
        window.Activate();
        window.UpdateLayout();
        var list = UiTestActions.Find<ListBox>(window, "SubtitleList");
        var input = Assert.Single(list.GetVisualDescendants().OfType<TextBox>(), value => value.AcceptsReturn);
        Assert.True(input.Focus());
        await context.Controller.PlayAsync();

        window.KeyPress(modifierKey, modifiers, PhysicalKey.None, null);
        try
        {
            Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
            UiTestActions.Press(window, Key.F8, modifiers);
            await WaitForCommandAsync(context, WorkbenchCommand.TIMING_ENTER);
            Assert.Equal(2, window.DocumentSnapshot.Subtitles.Length);
            Assert.True(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
            UiTestActions.Press(window, Key.F8, modifiers);
            await WaitForCommandAsync(context, WorkbenchCommand.TIMING_ENTER);
            Assert.Equal(2, window.DocumentSnapshot.Subtitles.Length);
        }
        finally
        {
            window.KeyRelease(modifierKey, RawInputModifiers.None, PhysicalKey.None, null);
        }

        var active = window.DocumentSnapshot.Subtitles.Single(line => line.Id != original.Id);
        Assert.True(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        window.KeyPress(modifierKey, modifiers, PhysicalKey.None, null);
        try
        {
            Assert.True(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
            UiTestActions.Press(window, Key.F9, modifiers);
            await WaitForCommandAsync(context, WorkbenchCommand.TIMING_EXIT);
        }
        finally
        {
            window.KeyRelease(modifierKey, RawInputModifiers.None, PhysicalKey.None, null);
        }

        var ended = window.DocumentSnapshot.Subtitles.Single(line => line.Id == active.Id);
        Assert.Equal(new MediaTime(1), ended.End - ended.Start);
        Assert.Equal(original, window.DocumentSnapshot.Subtitles.Single(line => line.Id == original.Id));
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        Assert.Equal(2, window.DocumentSnapshot.Subtitles.Length);
    }

    private static Task WaitForCommandAsync(MainWindowTestContext context, WorkbenchCommand command)
    {
        var task = Assert.IsType<AsyncRelayCommand>(context.Window.GetCommand(command)).ExecutionTask;
        Assert.NotNull(task);
        return task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static void SetGesture(SettingsWindow settings, WorkbenchCommand command, string gesture)
    {
        UiTestActions.SelectShortcut(settings, command);
        var parsed = KeyGesture.Parse(gesture);
        UiTestActions.Click(settings, "RecordShortcutButton");
        UiTestActions.Find<Control>(settings, "GestureInput").Focus();
        UiTestActions.Press(settings, parsed.Key, parsed.KeyModifiers switch
        {
            KeyModifiers.Control => RawInputModifiers.Control,
            KeyModifiers.Shift => RawInputModifiers.Shift,
            KeyModifiers.Alt => RawInputModifiers.Alt,
            _ => throw new ArgumentOutOfRangeException(nameof(gesture))
        });
        Assert.Equal(ShortcutConfiguration.NormalizeGesture(gesture), settings.ViewModel.Shortcuts.Gesture);
    }
}
