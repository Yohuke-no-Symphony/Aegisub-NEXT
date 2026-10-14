using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsStyleCaptureUiTests
{
    [AvaloniaFact]
    public async Task CapturedSubtitleStyleRemainsEditableAndCanBeSaved()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var cueId = await UiTestActions.CreateSubtitleAsync(context);
        var document = context.Session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_STYLE_LIBRARY);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        settings.SelectPage(EffectLibraryPage.STYLES);

        UiTestActions.Click(settings, "CaptureStyleButton");
        await context.Session.Styles.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();
        settings.UpdateLayout();

        var captured = Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
        Assert.Equal(document.Subtitles.Single(cue => cue.Id == cueId).Style, captured.Style);
        Assert.False(context.Session.Styles.IsBusy);
        Assert.False(settings.ViewModel.Styles.IsBusy);
        Assert.True(UiTestActions.Find<StackPanel>(settings, "StyleEditor").IsEffectivelyEnabled);
        Assert.True(UiTestActions.Find<Button>(settings, "SaveStyleButton").IsEffectivelyEnabled);
        var nameInput = UiTestActions.Find<TextBox>(settings, "StyleNameInput");
        Assert.True(nameInput.IsEffectivelyEnabled);
        nameInput.Focus();
        nameInput.SelectAll();
        settings.KeyTextInput("Edited captured style");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Edited captured style", settings.ViewModel.Styles.Name);

        UiTestActions.Click(settings, "SaveStyleButton");
        await settings.ViewModel.Styles.SelectionCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Dispatcher.UIThread.RunJobs();

        var saved = Assert.Single(context.Session.StyleLibrary.Snapshot.Presets);
        Assert.Equal(captured.Id, saved.Id);
        Assert.Equal("Edited captured style", saved.Name);
        Assert.False(settings.ViewModel.Styles.IsBusy);
        Assert.True(UiTestActions.Find<Button>(settings, "DuplicateStyleButton").IsEffectivelyEnabled);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }
}
