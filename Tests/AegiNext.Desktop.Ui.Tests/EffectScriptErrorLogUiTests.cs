using AegiNext.Desktop.EffectLibrary;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectScriptErrorLogUiTests
{
    [AvaloniaTheory]
    [InlineData("ValidateEffectScriptButton", "docked")]
    [InlineData("ValidateEffectScriptButton", "hidden")]
    [InlineData("ValidateEffectScriptButton", "floating")]
    [InlineData("SaveEffectScriptButton", "docked")]
    [InlineData("SaveEffectScriptButton", "hidden")]
    [InlineData("SaveEffectScriptButton", "floating")]
    public async Task InvalidDraftRevealsOneCompleteErrorAndRetainsSettings(string button, string logState)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.EffectScripts.Completion;
        var settings = await OpenSettingsAsync(context);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        var model = settings.ViewModel.Effects;
        var draftId = model.Draft!.Id;
        model.Source = "effect broken\n";
        var log = context.Window.Panels[WorkbenchPanelIds.LOG];
        if (logState == "hidden")
        {
            context.Window.Layouts.Hide(WorkbenchPanelIds.LOG);
        }
        else if (logState == "floating")
        {
            context.Window.Layouts.Float(WorkbenchPanelIds.LOG);
        }
        var host = logState == "floating" ? Assert.Single(context.Window.Layouts.FloatingWindows) : context.Window;
        context.ViewModel.Log.FilterIndex = 1;
        context.ViewModel.Log.FilterText = "unmatched search";
        context.Session.Journal.Clear();
        var original = context.Session.DocumentSnapshot;
        UiTestActions.Click(settings, button);
        if (button == "SaveEffectScriptButton")
        {
            await model.SaveCommand.ExecutionTask!;
        }
        Flush(context.Window);
        Assert.True(settings.IsVisible);
        Assert.Equal(draftId, model.Draft!.Id);
        Assert.DoesNotContain(model.Effects, item => item.Id == draftId);
        Assert.Equal("effect broken\n", model.Source);
        Assert.True(model.IsDirty);
        Assert.Equal(1, model.DiagnosticLine);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.Empty(context.Session.EffectScriptLibrary.Snapshot.Presets);
        var entry = Assert.Single(context.Session.Journal.Entries);
        Assert.Equal(WorkbenchLogLevel.ERROR, entry.Level);
        Assert.Contains("EffectScriptException", entry.Details);
        Assert.Contains(model.Error!, entry.Details!);
        Assert.True(settings.ViewModel.HasError);
        Assert.Equal(model.Error, settings.ViewModel.Error);
        AssertErrorVisible(context, entry);
        Assert.Same(host, TopLevel.GetTopLevel(log));
        var expander = Assert.Single(log.GetVisualDescendants().OfType<Expander>(), control => control.IsVisible);
        Assert.True(expander.IsExpanded);
        if (button == "ValidateEffectScriptButton" && logState == "docked")
        {
            Capture(context.Window, "script-validation-error-log.png");
            Capture(settings, "script-validation-error-retained-draft.png");
        }
        settings.SelectPage(EffectLibraryPage.TRANSFER);
        UiTestActions.Click(Assert.Single(settings.OwnedWindows), "CancelButton");
        await settings.ViewModel.NavigationCompletion;
        Assert.Equal(EffectLibraryPage.EFFECTS, settings.CurrentPage);
        settings.RefreshLanguage();
        Assert.Equal("effect broken\n", model.Source);
        Assert.Single(context.Session.Journal.Entries);
    }

    [AvaloniaFact]
    public async Task FailedNewSaveRevealsTheRecordedIoErrorOnceAndRetryInsertsOnlyAfterCommit()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.EffectScripts.Completion;
        var settings = await OpenSettingsAsync(context);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        var model = settings.ViewModel.Effects;
        var draftId = model.Draft!.Id;
        var source = model.Source;
        var blocked = context.Session.ApplicationContext.EffectLibrary.ManifestPath;
        Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
        File.Delete(blocked);
        Directory.CreateDirectory(blocked);
        context.Window.Layouts.Hide(WorkbenchPanelIds.LOG);
        context.Session.Journal.Clear();
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await model.SaveCommand.ExecutionTask!;
        Flush(context.Window);
        var entry = Assert.Single(context.Session.Journal.Entries);
        Assert.Equal(context.Session.LastError!.ToString(), entry.Details);
        AssertErrorVisible(context, entry);
        Assert.True(settings.IsVisible);
        Assert.Equal(context.Session.LastError.Message, settings.ViewModel.Error);
        Assert.Equal(source, model.Source);
        Assert.True(model.IsDirty);
        Assert.DoesNotContain(model.Effects, item => item.Id == draftId);
        Assert.Empty(context.Session.EffectScriptLibrary.Snapshot.Presets);
        Directory.Delete(blocked);
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await model.SaveCommand.ExecutionTask!;
        Assert.False(model.IsDirty);
        Assert.Equal(draftId, model.SelectedEffect!.Id);
        Assert.Equal(source, model.Source);
        Assert.Equal(draftId, Assert.Single(context.Session.EffectScriptLibrary.Snapshot.Presets).Id);
        Assert.Contains(model.Effects, item => item.Id == draftId);
        Assert.Single(context.Session.Journal.Entries, item => item.Level == WorkbenchLogLevel.ERROR);
    }

    [AvaloniaFact]
    public async Task LibraryRefreshDoesNotRevealAnUnrelatedStaleError()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.EffectScripts.Completion;
        var settings = await OpenSettingsAsync(context);
        context.Session.ShowError(new IOException("Other workflow failed"));
        context.Window.Layouts.Hide(WorkbenchPanelIds.LOG);
        context.Session.NotifyEffectLibraryChanged();
        Flush(context.Window);
        Assert.False(context.Window.Layouts.IsVisible(WorkbenchPanelIds.LOG));
        Assert.True(settings.IsVisible);
        Assert.Null(settings.ViewModel.Error);
        Assert.False(settings.ViewModel.HasError);
        Assert.Single(context.Session.Journal.Entries, item => item.Level == WorkbenchLogLevel.ERROR);
    }

    [AvaloniaFact]
    public async Task ErrorBeyondTheCurrentViewportIsScrolledIntoViewAndExpanded()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.EffectScripts.Completion;
        var settings = await OpenSettingsAsync(context);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        settings.ViewModel.Effects.Source = "effect broken";
        context.Session.Journal.Clear();
        for (var index = 0; index < 150; index++)
        {
            context.Session.LogInfo("Fixture", $"Earlier entry {index}");
        }
        context.Window.Layouts.Hide(WorkbenchPanelIds.LOG);
        UiTestActions.Click(settings, "ValidateEffectScriptButton");
        Flush(context.Window);
        var entry = context.Session.Journal.Entries[^1];
        AssertErrorVisible(context, entry);
        var list = UiTestActions.Find<ListBox>(context.Window, "LogEntries");
        var container = Assert.IsAssignableFrom<ListBoxItem>(list.ContainerFromItem(entry));
        Assert.True(container.GetVisualDescendants().OfType<Expander>().Single().IsExpanded);
        var top = container.TranslatePoint(default, list)!.Value.Y;
        Assert.True(top < list.Bounds.Height && top + container.Bounds.Height > 0);
    }

    [AvaloniaFact]
    public async Task ClosedSettingsUnsubscribesItsValidationErrorForwarding()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.EffectScripts.Completion;
        var settings = await OpenSettingsAsync(context);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        var model = settings.ViewModel.Effects;
        model.Source = "effect broken";
        settings.Close();
        UiTestActions.Click(Assert.Single(settings.OwnedWindows), "DiscardButton");
        await settings.CloseCompletion;
        Assert.False(settings.IsVisible);
        await model.AddCommand.ExecuteAsync(null);
        model.Source = "effect broken";
        var failures = 0;
        model.ValidationFailed += (_, _) => failures++;
        context.Session.Journal.Clear();
        context.Window.Layouts.Hide(WorkbenchPanelIds.LOG);
        model.ValidateCommand.Execute(null);
        Flush(context.Window);
        Assert.Equal(1, failures);
        Assert.Empty(context.Session.Journal.Entries);
        Assert.False(context.Window.Layouts.IsVisible(WorkbenchPanelIds.LOG));
    }

    private static async Task<EffectLibraryWindow> OpenSettingsAsync(MainWindowTestContext context)
    {
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_EFFECT_SCRIPT_LIBRARY);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<EffectLibraryWindow>());
        settings.SelectPage(EffectLibraryPage.EFFECTS);
        return settings;
    }

    private static void AssertErrorVisible(MainWindowTestContext context, WorkbenchLogEntry entry)
    {
        Assert.Equal(0, context.ViewModel.Log.FilterIndex);
        Assert.Equal(string.Empty, context.ViewModel.Log.FilterText);
        Assert.Same(entry, context.ViewModel.Log.SelectedEntry);
        var log = context.Window.Panels[WorkbenchPanelIds.LOG];
        Assert.True(log.IsAttachedToVisualTree() && log.IsEffectivelyVisible);
        var panel = context.Window.Layouts.PanelAdapters[WorkbenchPanelIds.LOG];
        Assert.Same(panel, Assert.IsAssignableFrom<Dock.Model.Core.IDock>(panel.Owner).ActiveDockable);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
