using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Export;
using AegiNext.Desktop.Settings.Tasks;
using AegiNext.Desktop.Settings.TimingPostProcessor;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using AegiNext.Export.Presets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SettingsNumericDraggingUiTests
{
    [AvaloniaFact]
    public void TaskSettingTitleUpdatesOnlyItsDraftDuringDragAndCommitsOnceOnRelease()
    {
        using var environment = new UiTestEnvironment();
        var model = new TaskSettingsViewModel(new() { MaximumConcurrentTasks = 4 });
        var view = new TaskSettingsView { DataContext = model };
        var host = new Window { Width = 600, Height = 240, Content = view };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "MaximumConcurrentTasksInput");
            input.FocusInput();
            var commits = 0;
            model.PropertyChanged += (_, args) => commits += args.PropertyName == nameof(TaskSettingsViewModel.MaximumConcurrentTasks) ? 1 : 0;
            var changed = new List<int>();
            model.Changed += (_, args) => changed.Add(args.MaximumConcurrentTasks);
            var end = BeginDrag(host, "MaximumConcurrentTasksInputTitle");

            Assert.True(input.IsTitleDragging);
            Assert.NotEqual("4", input.RawText);
            Assert.Equal(4, model.MaximumConcurrentTasks);
            Assert.Empty(changed);
            Assert.Equal(0, commits);
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.False(input.IsTitleDragging);
            Assert.Equal(model.MaximumConcurrentTasks, Assert.Single(changed));
            Assert.Equal(1, commits);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsCancellationRestoresTheExactOriginalDraftWithoutSubmitting()
    {
        using var environment = new UiTestEnvironment();
        var model = new TaskSettingsViewModel(new() { MaximumConcurrentTasks = 4 }) { MaximumConcurrentTasksText = "04" };
        var host = new Window { Width = 600, Height = 240, Content = new TaskSettingsView { DataContext = model } };
        try
        {
            host.Show();
            var input = UiTestActions.Find<NumericDraftInput>(host, "MaximumConcurrentTasksInput");
            var changed = 0;
            model.Changed += (_, _) => changed++;
            var end = BeginDrag(host, "MaximumConcurrentTasksInputTitle");
            UiTestActions.Find<NumericDragLabel>(host, "MaximumConcurrentTasksInputTitle").CancelDrag();
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("04", input.RawText);
            Assert.Equal("04", model.MaximumConcurrentTasksText);
            Assert.Equal(4, model.MaximumConcurrentTasks);
            Assert.Equal(0, changed);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public void ReplacingASettingsDataContextCancelsTheOldGestureWithoutWritingIntoTheNewModel()
    {
        using var environment = new UiTestEnvironment();
        var original = new TaskSettingsViewModel(new() { MaximumConcurrentTasks = 4 });
        var replacement = new TaskSettingsViewModel(new() { MaximumConcurrentTasks = 10 });
        var view = new TaskSettingsView { DataContext = original };
        var host = new Window { Width = 600, Height = 240, Content = view };
        try
        {
            host.Show();
            var oldCommits = 0;
            var newCommits = 0;
            original.Changed += (_, _) => oldCommits++;
            replacement.Changed += (_, _) => newCommits++;
            var end = BeginDrag(host, "MaximumConcurrentTasksInputTitle");
            view.DataContext = replacement;
            Dispatcher.UIThread.RunJobs();
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var input = UiTestActions.Find<NumericDraftInput>(host, "MaximumConcurrentTasksInput");
            Assert.False(input.IsTitleDragging);
            Assert.Equal("10", input.RawText);
            Assert.Equal("10", replacement.MaximumConcurrentTasksText);
            Assert.Equal(10, replacement.MaximumConcurrentTasks);
            Assert.Equal(0, oldCommits);
            Assert.Equal(0, newCommits);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public void StylePresetReplacementCancelsTheDragBeforeLoadingTheNewPresetsDraft()
    {
        using var environment = new UiTestEnvironment();
        var host = new SettingsWindow(new());
        try
        {
            host.Show();
            var first = new SubtitleStylePreset(Guid.NewGuid(), "First", new() { FontSize = 40 });
            var second = new SubtitleStylePreset(Guid.NewGuid(), "Second", new() { FontSize = 80 });
            host.UpdateStyles([first, second], first.Id);
            host.SelectPage(SettingsPage.STYLES);
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            var end = BeginDrag(host, "FontSizeInputTitle");
            Assert.NotEqual(40m, input.Value);
            host.ViewModel.Styles.SelectedStyle = second;
            Dispatcher.UIThread.RunJobs();
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.False(input.IsTitleDragging);
            Assert.Equal("80", input.RawText);
            Assert.Equal(80m, input.Value);
            Assert.Equal(second.Id, host.ViewModel.Styles.Draft!.Id);
            Assert.Equal(80, host.ViewModel.Styles.Draft.Style.FontSize);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public void DuplicatingAStyleCancelsTheGestureBeforeCopyingTheDraft()
    {
        using var environment = new UiTestEnvironment();
        var host = new SettingsWindow(new());
        try
        {
            host.Show();
            var preset = new SubtitleStylePreset(Guid.NewGuid(), "Original", new() { FontSize = 40 });
            host.UpdateStyles([preset], preset.Id);
            host.SelectPage(SettingsPage.STYLES);
            var input = UiTestActions.Find<NumericDraftInput>(host, "FontSizeInput");
            var end = BeginDrag(host, "FontSizeInputTitle");
            Assert.NotEqual(40m, input.Value);
            host.ViewModel.Styles.DuplicateCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            host.MouseUp(end, MouseButton.Left);

            Assert.False(input.IsTitleDragging);
            Assert.NotEqual(preset.Id, host.ViewModel.Styles.Draft!.Id);
            Assert.Equal(40, host.ViewModel.Styles.Draft.Style.FontSize);
            Assert.Equal("40", input.RawText);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public async Task IntegerDialogDraggingChangesTheDraftAndEscapeCancelsOnlyTheGesture()
    {
        using var environment = new UiTestEnvironment();
        var owner = new Window();
        owner.Show();
        var result = new WindowWorkbenchDialogService(owner).ShowIntegerInputAsync(
            new("Workbench.Move", "Workbench.MoveMilliseconds", "Workbench.MoveMillisecondsHint", 10, -100, 100));
        var dialog = Assert.Single(owner.OwnedWindows.OfType<IntegerInputDialog>());
        try
        {
            var input = UiTestActions.Find<NumericDraftInput>(dialog, "IntegerInput");
            var end = BeginDrag(dialog, "IntegerInputLabel");
            Assert.NotEqual("10", input.RawText);
            Assert.False(result.IsCompleted);
            UiTestActions.Press(dialog, Key.Escape);
            dialog.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.True(dialog.IsVisible);
            Assert.False(result.IsCompleted);
            Assert.Equal("10", input.RawText);
            UiTestActions.Click(dialog, "ConfirmButton");
            Assert.Equal(10, await result);
        }
        finally
        {
            dialog.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public void UpdatingTheSavedExportPresetCancelsTheGestureBeforeDetectingPendingDraftChanges()
    {
        using var environment = new UiTestEnvironment();
        var original = new VideoExportPreset(Guid.NewGuid(), "Preset", new() { Crf = 20 });
        var model = new ExportSettingsViewModel();
        model.UpdatePresets([original], original.Id);
        var view = new ExportSettingsView { DataContext = model };
        var host = new Window { Width = 720, Height = 850, Content = view };
        try
        {
            host.Show();
            var end = BeginDrag(host, "CrfInputTitle");
            var updated = original with { Settings = original.Settings with { Crf = 30 } };
            model.UpdatePresets([updated], updated.Id);
            Dispatcher.UIThread.RunJobs();
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var input = UiTestActions.Find<NumericDraftInput>(host, "CrfInput");
            Assert.False(input.IsTitleDragging);
            Assert.Equal("30", input.RawText);
            Assert.Equal(30m, input.Value);
            Assert.False(model.IsDirty);
            Assert.Equal(updated, model.Draft);
        }
        finally
        {
            view.DataContext = null;
            host.Close();
        }
    }

    [AvaloniaFact]
    public void TimingStyleReplacementCancelsBeforeCommitAllCanSubmitTheDraggingField()
    {
        using var environment = new UiTestEnvironment();
        var model = new TimingPostProcessorSettingsViewModel(new()
        {
            TimingPostProcessor = new() { Options = new() { LeadInEnabled = true, LeadInMilliseconds = 100 } }
        });
        var first = new SubtitleStylePreset(Guid.NewGuid(), "A", new(), TimingPostProcessor: new() { LeadInEnabled = true, LeadInMilliseconds = 100 });
        var second = new SubtitleStylePreset(Guid.NewGuid(), "B", new(), TimingPostProcessor: new() { LeadInEnabled = true, LeadInMilliseconds = 200 });
        model.UpdateStyles([first, second]);
        model.SelectedStyle = model.Styles.Single(style => style.Id == first.Id);
        var view = new TimingPostProcessorSettingsView { DataContext = model };
        var host = new Window { Width = 750, Height = 800, Content = view };
        try
        {
            host.Show();
            var values = new List<int>();
            model.Changed += (_, args) => values.Add(args.Preferences.Options.LeadInMilliseconds);
            var end = BeginDrag(host, "LeadInMillisecondsInputTitle");
            model.SelectedStyle = model.Styles.Single(style => style.Id == second.Id);
            Dispatcher.UIThread.RunJobs();
            host.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            var input = UiTestActions.Find<NumericDraftInput>(host, "LeadInMillisecondsInput");
            Assert.False(input.IsTitleDragging);
            Assert.Equal("200", input.RawText);
            Assert.Equal(200, Assert.Single(values));
            Assert.Equal(second.Id, model.SelectedStyle!.Id);
        }
        finally
        {
            view.DataContext = null;
            host.Close();
        }
    }

    private static Point BeginDrag(Window host, string titleName)
    {
        var title = UiTestActions.Find<NumericDragLabel>(host, titleName);
        title.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
        var start = title.TranslatePoint(new(title.Bounds.Width / 2, title.Bounds.Height / 2), host)!.Value;
        var end = start + new Vector(40, 0);
        host.MouseDown(start, MouseButton.Left);
        host.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        return end;
    }
}
