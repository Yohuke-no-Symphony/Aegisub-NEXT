using AegiNext.Desktop.EffectLibrary;
using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Settings;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class FontsReadyUiTests
{
    [AvaloniaFact]
    public async Task LateFontCatalogUpdatesSettingsCandidatesWithoutOverwritingUncommittedInputs()
    {
        using var environment = new UiTestEnvironment();
        await using var service = new AegiTaskService();
        var blocker = new TaskCenterTestTask("Font setup barrier", false, AegiTaskMode.Blocking);
        var barrier = service.Submit(blocker);
        await blocker.Started.Task;
        var fonts = new SubtitleFontSelectionService(service);
        var window = new EffectLibraryWindow(new());
        window.ViewModel.Styles.SetFonts(fonts);
        try
        {
            window.Show();
            window.UpdateStyles([new SubtitleStylePreset(Guid.NewGuid(), "Fixture", new())]);
            window.SelectPage(EffectLibraryPage.STYLES);
            var picker = UiTestActions.Find<FontFamilyPicker>(window, "FontInput");
            var box = Assert.Single(picker.GetVisualDescendants().OfType<TextBox>());
            Assert.True(box.Focus());
            box.SelectAll();
            window.KeyTextInput("Future Custom Face");
            window.ViewModel.Styles.FontSizeText = "7e-";
            Assert.Contains(service.GetSnapshots(), task => task.Name == "Tasks.SystemFonts" && task.State == AegiTaskState.Queued);
            var original = window.ViewModel.Styles.Draft;
            var commits = 0;
            picker.FamilyCommitted += (_, _) => commits++;

            blocker.Finish.TrySetResult();
            blocker.Cleanup.TrySetResult();
            await barrier.Completion;
            await fonts.EnsureLoadedAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.NotEmpty(fonts.Candidates);
            Assert.All(fonts.Candidates, candidate => Assert.Contains(candidate.Selection.FamilyName, picker.FontFamilies));
            Assert.Equal("Future Custom Face", picker.Text);
            Assert.Equal("7e-", window.ViewModel.Styles.FontSizeText);
            Assert.Same(original, window.ViewModel.Styles.Draft);
            Assert.Equal(0, commits);
        }
        finally
        {
            blocker.Finish.TrySetResult();
            blocker.Cleanup.TrySetResult();
            await barrier.Completion;
            window.Close();
            Assert.False(window.IsVisible);
        }
    }
}
