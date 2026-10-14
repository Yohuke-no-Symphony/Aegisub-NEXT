using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Startup;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectLibraryTransferUiTests
{
    [AvaloniaFact]
    public async Task MixedImportShowsPreviewAndOnlyCommitsAfterConfirmation()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var input = Path.Combine(environment.DirectoryPath, "incoming.aegilibrary");
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Shared style", new());
        await EffectLibraryPackageStore.SaveAsync(new() { Styles = new() { Presets = [style] } }, input);
        var effect = new EffectScriptPreset(Guid.NewGuid(), "Shared effect",
            BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", "\"mixed-import\"", StringComparison.Ordinal));
        var effectPath = Path.Combine(environment.DirectoryPath, "incoming.aegieffect");
        var builtinPath = Path.Combine(environment.DirectoryPath, "builtin.aegifx");
        await EffectScriptPresetStore.WritePresetAsync(effect, effectPath);
        await EffectScriptPresetStore.WriteScriptAsync(BuiltinEffectScripts.Get("fade-in").Source, builtinPath);
        var dialogs = new StartupTestDialogService { OpenPaths = [input, effectPath, builtinPath] };
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => Task.FromResult<EffectLibraryTransferSelection?>(new([], [])), () => Task.FromResult(true));
        var host = new Window { Width = 900, Height = 700, Content = new EffectLibraryTransferView { DataContext = model } };
        host.Show();
        try
        {
            UiTestActions.Click(host, "ImportEffectLibraryButton");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(model.HasPreview);
            Assert.Equal(3, model.Rows.Count);
            Assert.Equal(2, model.SelectedImportCount);
            Assert.Equal(EffectLibraryImportDecision.SKIP,
                Assert.Single(model.Rows, row => row.Row.Status == EffectLibraryImportStatus.IDENTICAL).Decision);
            Assert.Empty(context.StyleLibrary.Snapshot.Presets);
            Assert.Empty(context.EffectScriptLibrary.Snapshot.Presets);

            UiTestActions.Click(host, "CommitEffectLibraryImportButton");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(style, Assert.Single(context.StyleLibrary.Snapshot.Presets));
            Assert.Equal(effect, Assert.Single(context.EffectScriptLibrary.Snapshot.Presets));
            Assert.False(model.HasPreview);
            Assert.Null(model.Error);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClearingPreviewDuringDraftPreparationPreventsThePendingCommit()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var input = Path.Combine(environment.DirectoryPath, "style.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [new(Guid.NewGuid(), "Imported", new())] }, input);
        var dialogs = new StartupTestDialogService { OpenPaths = [input] };
        TaskCompletionSource<bool>? preparation = null;
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => Task.FromResult<EffectLibraryTransferSelection?>(new([], [])),
            () => preparation?.Task ?? Task.FromResult(true));
        await coordinator.ImportAsync();
        preparation = new(TaskCreationOptions.RunContinuationsAsynchronously);

        var pendingCommit = coordinator.CommitImportAsync();
        model.ClearPreviewCommand.Execute(null);
        preparation.TrySetResult(true);
        await pendingCommit.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(context.StyleLibrary.Snapshot.Presets);
        Assert.False(model.HasPreview);
        Assert.Null(model.Error);
    }

    [AvaloniaFact]
    public async Task ClosingDuringDraftPreparationIgnoresTheLatePreparationResult()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var preparation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, new StartupTestDialogService(),
            () => Task.FromResult<EffectLibraryTransferSelection?>(new([], [])), () => preparation.Task);

        var pendingImport = coordinator.ImportAsync();
        coordinator.Dispose();
        preparation.TrySetResult(true);
        await pendingImport.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(model.IsBusy);
        Assert.False(model.HasPreview);
        Assert.Null(model.Error);
    }

    [AvaloniaFact]
    public async Task StalePreviewIsReanalyzedLocallyAndRequiresAnotherConfirmation()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var input = Path.Combine(environment.DirectoryPath, "style.aegistyles");
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Imported", new());
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [style] }, input);
        var dialogs = new StartupTestDialogService { OpenPaths = [input] };
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => Task.FromResult<EffectLibraryTransferSelection?>(new([], [])), () => Task.FromResult(true));
        await coordinator.ImportAsync();
        var previous = model.ImportPlan;
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(new(Guid.NewGuid(), "Intervening", new())));

        await coordinator.CommitImportAsync();

        Assert.True(model.HasPreview);
        Assert.NotSame(previous, model.ImportPlan);
        Assert.Equal(context.EffectLibrary.Snapshot.Revision, model.ImportPlan!.ExpectedRevision);
        Assert.Equal(Localization.Get("EffectLibrary.PreviewUpdated"), model.Status);
        Assert.Null(model.Error);
        Assert.Null(context.LastError);
        Assert.Single(context.StyleLibrary.Snapshot.Presets);
        await coordinator.CommitImportAsync();
        Assert.Equal(2, context.StyleLibrary.Snapshot.Presets.Length);
    }

    [AvaloniaFact]
    public async Task ExportFreezesSelectionBeforePickerAndClosingIgnoresLatePickerResult()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Original", new());
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style));
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new StartupTestDialogService { PendingSaveSelection = pending };
        using var model = new EffectLibraryTransferViewModel();
        model.UpdateSelection(1, 0, 0);
        model.SelectedFormat = EffectLibraryExportFormat.PACKAGE;
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => Task.FromResult<EffectLibraryTransferSelection?>(new([style], [])), () => Task.FromResult(true));
        var operation = coordinator.ExportSelectionAsync();
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style with { Name = "Changed" }));
        var output = Path.Combine(environment.DirectoryPath, "selected.aegilibrary");
        pending.TrySetResult(output);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(style, Assert.Single((await EffectLibraryPackageStore.LoadAsync(output)).Styles.Presets));
        var late = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialogs.PendingSaveSelection = late;
        var lateOperation = coordinator.ExportSelectionAsync();
        coordinator.Dispose();
        var blocked = Path.Combine(environment.DirectoryPath, "late.aegilibrary");
        late.TrySetResult(blocked);
        await lateOperation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(File.Exists(blocked));
    }

    [AvaloniaFact]
    public void LabelsSwitchLanguageWhileFormatAndBusinessSelectionRemainStable()
    {
        using var environment = new UiTestEnvironment();
        using var model = new EffectLibraryTransferViewModel();
        model.UpdateSelection(1, 0, 0);
        model.SelectedFormat = EffectLibraryExportFormat.PACKAGE;
        var host = new Window { Width = 900, Height = 700, Content = new EffectLibraryTransferView { DataContext = model } };
        host.Show();
        try
        {
            var button = UiTestActions.Find<Button>(host, "ExportAllEffectLibraryButton");
            Localization.SetLanguage("zh-CN");
            model.RefreshLanguage();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Localization.Get("EffectLibrary.ExportAll"), button.Content);
            Assert.Equal(EffectLibraryExportFormat.PACKAGE, model.SelectedFormat);
            Assert.True(model.ExportSelectionCommand.CanExecute(null));
        }
        finally
        {
            host.Close();
        }
    }
}
