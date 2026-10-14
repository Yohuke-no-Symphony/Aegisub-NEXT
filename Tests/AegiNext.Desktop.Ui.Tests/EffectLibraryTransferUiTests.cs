using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.Controls.Common;
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
            () => Task.FromResult(true));
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
            () => preparation.Task);

        var pendingImport = coordinator.ImportAsync();
        coordinator.Dispose();
        preparation.TrySetResult(true);
        await pendingImport.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(model.IsBusy);
        Assert.False(model.HasPreview);
        Assert.Null(model.Error);
    }

    [AvaloniaFact]
    public async Task ClosingCancelsUnresolvedPreparationAndIgnoresItsLaterCompletion()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var input = Path.Combine(environment.DirectoryPath, "style.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [new(Guid.NewGuid(), "Imported", new())] }, input);
        var preparation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new StartupTestDialogService { OpenPaths = [input] };
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => preparation.Task);
        var changes = 0;
        model.PropertyChanged += (_, _) => changes++;

        var pendingImport = coordinator.ImportAsync();
        Assert.False(pendingImport.IsCompleted);
        coordinator.Dispose();
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(preparation.Task.IsCompleted);
        Assert.True(pendingImport.IsCompleted);
        Assert.False(model.IsBusy);
        var beforeLateCompletion = changes;
        preparation.TrySetResult(true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(beforeLateCompletion, changes);
        Assert.Empty(context.StyleLibrary.Snapshot.Presets);
        Assert.False(model.HasPreview);
        Assert.Null(model.Error);
    }

    [AvaloniaFact]
    public async Task ClosingCancelsUnresolvedDialogSelectionAndNeverExportsItsLateResult()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Original", new());
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style));
        var selection = new TaskCompletionSource<EffectLibraryExportRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = Path.Combine(environment.DirectoryPath, "late.aegistyles");
        var dialogs = new StartupTestDialogService { SavePath = output };
        dialogs.ExportDialogHandler = async (_, exportAsync, token) =>
        {
            var request = await selection.Task.WaitAsync(token);
            return request is not null && await exportAsync(request, dialogs, token);
        };
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => throw new InvalidOperationException("导出不应处理草稿。"));
        var changes = 0;
        model.PropertyChanged += (_, _) => changes++;

        var pendingExport = coordinator.ExportDialogAsync();
        Assert.False(pendingExport.IsCompleted);
        coordinator.Dispose();
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(selection.Task.IsCompleted);
        Assert.True(pendingExport.IsCompleted);
        Assert.False(model.IsBusy);
        var beforeLateCompletion = changes;
        selection.TrySetResult(new([style], [], EffectLibraryExportFormat.INDEPENDENT));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(beforeLateCompletion, changes);
        Assert.False(File.Exists(output));
        Assert.Equal(0, dialogs.SaveCount);
        Assert.Equal(style, Assert.Single(context.StyleLibrary.Snapshot.Presets));
        Assert.Null(model.Error);
    }

    [AvaloniaFact]
    public async Task ClosingCancelsAnUnresponsiveDialogServiceAndRejectsItsLateExportCallback()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Saved", new());
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style));
        var before = context.EffectLibrary.Snapshot;
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<EffectLibraryExportRequest, AegiNext.Desktop.Workspace.IWorkbenchDialogService,
            CancellationToken, Task<bool>>? lateExport = null;
        var output = Path.Combine(environment.DirectoryPath, "late.aegistyles");
        var dialogs = new StartupTestDialogService { SavePath = output };
        dialogs.ExportDialogHandler = (_, callback, _) =>
        {
            lateExport = callback;
            return answer.Task;
        };
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => throw new InvalidOperationException("导出不应处理草稿。"));
        var changes = 0;
        model.PropertyChanged += (_, _) => changes++;

        var operation = coordinator.ExportDialogAsync();
        Assert.False(operation.IsCompleted);
        coordinator.Dispose();
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(answer.Task.IsCompleted);
        var beforeLateCompletion = changes;
        Assert.NotNull(lateExport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lateExport(
            new([style], [], EffectLibraryExportFormat.INDEPENDENT), dialogs, CancellationToken.None));
        answer.TrySetResult(true);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(beforeLateCompletion, changes);
        Assert.True(operation.IsCompleted);
        Assert.False(File.Exists(output));
        Assert.Equal(0, dialogs.SaveCount);
        Assert.Same(before, context.EffectLibrary.Snapshot);
        Assert.Null(model.Error);
    }

    [AvaloniaFact]
    public async Task ClosingAnUnresponsiveDialogWaitsForTheCommittedExportAndItsStagingCleanup()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Saved style", new());
        var effect = new EffectScriptPreset(Guid.NewGuid(), "Saved effect",
            BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", "\"committed-export\"", StringComparison.Ordinal));
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style));
        await context.RunEffectOperationAsync(() => context.EffectScriptLibrary.UpsertAsync(effect));
        var before = context.EffectLibrary.Snapshot;
        var output = Path.Combine(environment.DirectoryPath, "committed-output");
        Directory.CreateDirectory(output);
        var enteredCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCommit = new ManualResetEventSlim();
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? export = null;
        var dialogs = new StartupTestDialogService { FolderPath = output };
        dialogs.ExportDialogHandler = (snapshot, callback, token) =>
        {
            export = callback(new(snapshot.Styles.Presets, snapshot.Effects.Presets, EffectLibraryExportFormat.INDEPENDENT), dialogs, token);
            return answer.Task;
        };
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs, () => Task.FromResult(true));
        var changes = 0;
        model.PropertyChanged += (_, _) => changes++;
        context.Tasks.Changed += HoldCommit;
        var operation = coordinator.ExportDialogAsync();
        try
        {
            await enteredCommit.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(export);
            Assert.False(export.IsCompleted);
            Assert.Single(Directory.GetDirectories(output, ".aeginext-export-*"));
            coordinator.Dispose();
            Dispatcher.UIThread.RunJobs();

            Assert.False(coordinator.Completion.IsCompleted);
            Assert.False(model.IsBusy);
            var beforeCleanup = changes;
            releaseCommit.Set();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);

            Assert.Equal(style, Assert.Single((await SubtitleStylePresetStore.LoadAsync(
                Assert.Single(Directory.GetFiles(output, "*.aegistyles")))).Presets));
            Assert.Equal(effect, await EffectScriptPresetStore.ReadPresetAsync(Assert.Single(Directory.GetFiles(output, "*.aegieffect"))));
            Assert.Empty(Directory.GetDirectories(output));
            Assert.False(answer.Task.IsCompleted);
            Assert.Equal(beforeCleanup, changes);
            Assert.Same(before, context.EffectLibrary.Snapshot);
            Assert.Null(context.LastError);
            Assert.Null(model.Error);
        }
        finally
        {
            context.Tasks.Changed -= HoldCommit;
            releaseCommit.Set();
            answer.TrySetResult(false);
            coordinator.Dispose();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
        }

        void HoldCommit(object? sender, EventArgs e)
        {
            if (context.Tasks.GetSnapshots().Any(snapshot => snapshot.Name == "Tasks.ExportEffectLibrary"
                && snapshot.State == AegiTaskState.Committing))
            {
                enteredCommit.TrySetResult();
                releaseCommit.Wait();
            }
        }
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
            () => Task.FromResult(true));
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
    public async Task ExportFreezesTheCommittedLibraryBeforeDialogAndClosingIgnoresLatePickerResult()
    {
        using var environment = new UiTestEnvironment(disableAutomaticUpdates: true);
        await using var context = new DesktopApplicationContext(new(environment.DirectoryPath), new() { AutoCheckUpdates = false });
        await context.Initialization;
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Original", new());
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style));
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialogs = new StartupTestDialogService { PendingSaveSelection = pending };
        dialogs.ExportDialogHandler = (snapshot, exportAsync, token) => exportAsync(new(
            snapshot.Styles.Presets, snapshot.Effects.Presets, EffectLibraryExportFormat.PACKAGE), dialogs, token);
        using var model = new EffectLibraryTransferViewModel();
        using var coordinator = new EffectLibraryTransferCoordinator(context, model, dialogs,
            () => throw new InvalidOperationException("导出不应处理草稿。"));
        var operation = coordinator.ExportDialogAsync();
        await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(style with { Name = "Changed" }));
        var output = Path.Combine(environment.DirectoryPath, "selected.aegilibrary");
        pending.TrySetResult(output);
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(style, Assert.Single((await EffectLibraryPackageStore.LoadAsync(output)).Styles.Presets));
        var late = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialogs.PendingSaveSelection = late;
        var lateOperation = coordinator.ExportDialogAsync();
        coordinator.Dispose();
        var blocked = Path.Combine(environment.DirectoryPath, "late.aegilibrary");
        late.TrySetResult(blocked);
        await lateOperation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(File.Exists(blocked));
    }

    [AvaloniaFact]
    public void ExportEntrySwitchesLanguageWithoutRequiringLibrarySelection()
    {
        using var environment = new UiTestEnvironment();
        using var model = new EffectLibraryTransferViewModel();
        var host = new Window { Width = 900, Height = 700, Content = new EffectLibraryTransferView { DataContext = model } };
        host.Show();
        try
        {
            var button = UiTestActions.Find<Button>(host, "ExportEffectLibraryButton");
            Localization.SetLanguage("zh-CN");
            model.RefreshLanguage();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Localization.Get("EffectLibrary.OpenExportDialog"), Assert.IsType<IconText>(button.Content).Text);
            Assert.True(model.ExportCommand.CanExecute(null));
        }
        finally
        {
            host.Close();
        }
    }
}
