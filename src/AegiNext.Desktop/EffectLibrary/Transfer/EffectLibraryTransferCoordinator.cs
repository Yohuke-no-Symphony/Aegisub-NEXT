using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

internal sealed class EffectLibraryTransferCoordinator : IDisposable
{
    private readonly DesktopApplicationContext context;
    private readonly EffectLibraryTransferViewModel model;
    private readonly IWorkbenchDialogService dialogs;
    private readonly Func<Task<EffectLibraryTransferSelection?>> captureSelectionAsync;
    private readonly Func<Task<bool>> prepareImportAsync;
    private readonly CancellationTokenSource cancellation = new();
    private ImmutableArray<string> previewPaths = [];
    private AegiTaskHandle? activeTask;
    private bool activeOperation;
    private bool disposed;

    internal EffectLibraryTransferCoordinator(DesktopApplicationContext context, EffectLibraryTransferViewModel model,
        IWorkbenchDialogService dialogs, Func<Task<EffectLibraryTransferSelection?>> captureSelectionAsync,
        Func<Task<bool>> prepareImportAsync)
    {
        this.context = context;
        this.model = model;
        this.dialogs = dialogs;
        this.captureSelectionAsync = captureSelectionAsync;
        this.prepareImportAsync = prepareImportAsync;
        model.ImportRequested += OnImportRequested;
        model.ExportSelectionRequested += OnExportSelectionRequested;
        model.ExportAllRequested += OnExportAllRequested;
        model.CommitImportRequested += OnCommitImportRequested;
    }

    internal Task Completion { get; private set; } = Task.CompletedTask;

    internal Task ImportAsync()
    {
        return RunAsync(async token =>
        {
            if (!await prepareImportAsync().WaitAsync(token))
            {
                return;
            }
            token.ThrowIfCancellationRequested();
            model.SetOperationBusy(true);
            await context.Initialization.WaitAsync(token);
            var paths = await dialogs.OpenFilesAsync("ImportEffectLibrary", "EffectLibraryFiles",
                ["*.aegistyles", "*.aegieffect", "*.aegifx", "*.aegilibrary"]).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (paths.Count == 0)
            {
                return;
            }
            var inputs = paths.Select(Path.GetFullPath).ToImmutableArray();
            var plan = await SubmitAsync(new EffectLibraryImportAnalysisTask(context, inputs), token);
            token.ThrowIfCancellationRequested();
            previewPaths = inputs;
            model.SetPreview(plan);
        });
    }

    internal Task ExportSelectionAsync(EffectLibraryTransferSelection? frozen = null)
    {
        return RunAsync(async token =>
        {
            var selection = frozen ?? await captureSelectionAsync().WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (selection is null)
            {
                return;
            }
            if (selection.Styles.IsDefault || selection.Effects.IsDefault)
            {
                throw new ArgumentException("导出选择必须为已冻结集合。", nameof(frozen));
            }
            if (selection.Styles.Length + selection.Effects.Length == 0)
            {
                return;
            }
            var builtinCount = selection.ContainsBuiltinEffects
                ? selection.Effects.Count(preset =>
                {
                    var id = EffectScriptParser.Parse(preset.Source).Id;
                    return BuiltinEffectScripts.Templates.Any(template => template.Script.Id == id);
                }) : 0;
            model.UpdateSelection(selection.Styles.Length, selection.Effects.Length - builtinCount, builtinCount);
            var format = model.SelectedFormat;
            model.SetOperationBusy(true);
            await context.Initialization.WaitAsync(token);
            var count = selection.Styles.Length + selection.Effects.Length;
            var mode = format switch
            {
                EffectLibraryExportFormat.PACKAGE => EffectLibraryExportMode.PACKAGE,
                EffectLibraryExportFormat.SOURCE => count == 1 ? EffectLibraryExportMode.SCRIPT_SOURCE : EffectLibraryExportMode.SCRIPT_SOURCE_FILES,
                _ => count == 1 ? EffectLibraryExportMode.SINGLE_FILE : EffectLibraryExportMode.INDEPENDENT_FILES
            };
            var destination = await ChooseExportDestinationAsync(selection, mode, token);
            token.ThrowIfCancellationRequested();
            if (destination is null)
            {
                return;
            }
            await SubmitAsync(new EffectLibraryExportTask(context, selection.Styles, selection.Effects, destination, mode), token);
            token.ThrowIfCancellationRequested();
            model.ShowStatus("EffectLibrary.Exported");
        });
    }

    internal Task CommitImportAsync()
    {
        return RunAsync(async token =>
        {
            if (!model.HasPreview || !await prepareImportAsync().WaitAsync(token) || model.ImportPlan is not { } plan)
            {
                return;
            }
            token.ThrowIfCancellationRequested();
            model.SetOperationBusy(true);
            var decisions = model.CreateDecisions();
            try
            {
                await SubmitAsync(new EffectLibraryImportCommitTask(context, plan, decisions), token);
            }
            catch (InvalidOperationException) when (context.EffectLibrary.Snapshot.Revision != plan.ExpectedRevision)
            {
                var refreshed = await SubmitAsync(new EffectLibraryImportAnalysisTask(context, previewPaths), token);
                token.ThrowIfCancellationRequested();
                model.SetPreview(refreshed);
                model.ShowStatus("EffectLibrary.PreviewUpdated");
                return;
            }
            token.ThrowIfCancellationRequested();
            var added = decisions.Values.Count(decision => decision == EffectLibraryImportDecision.IMPORT);
            var replaced = decisions.Values.Count(decision => decision == EffectLibraryImportDecision.REPLACE);
            var skipped = decisions.Values.Count(decision => decision == EffectLibraryImportDecision.SKIP);
            model.ClearPreview();
            previewPaths = [];
            model.ShowStatus("EffectLibrary.Imported", added, replaced, skipped);
        });
    }

    /// <summary>取消文件选择等待与尚未提交的任务，解除请求订阅，阻止关闭后更新页面。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        cancellation.Cancel();
        activeTask?.RequestCancel();
        model.ImportRequested -= OnImportRequested;
        model.ExportSelectionRequested -= OnExportSelectionRequested;
        model.ExportAllRequested -= OnExportAllRequested;
        model.CommitImportRequested -= OnCommitImportRequested;
        cancellation.Dispose();
    }

    private Task ExportAllAsync()
    {
        return RunAsync(async token =>
        {
            if (!await prepareImportAsync().WaitAsync(token))
            {
                return;
            }
            token.ThrowIfCancellationRequested();
            model.SetOperationBusy(true);
            await context.Initialization.WaitAsync(token);
            var frozen = context.EffectLibrary.Snapshot;
            var path = await dialogs.SaveFileAsync("ExportEffectLibrary", "EffectLibraryPackageFiles", ["*.aegilibrary"],
                ".aegilibrary", "effects.aegilibrary").WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (path is null)
            {
                return;
            }
            await SubmitAsync(new EffectLibraryExportTask(context, frozen.Styles.Presets, frozen.Effects.Presets,
                path, EffectLibraryExportMode.PACKAGE), token);
            token.ThrowIfCancellationRequested();
            model.ShowStatus("EffectLibrary.Exported");
        });
    }

    private async Task<string?> ChooseExportDestinationAsync(EffectLibraryTransferSelection selection,
        EffectLibraryExportMode mode, CancellationToken token)
    {
        if (mode is EffectLibraryExportMode.INDEPENDENT_FILES or EffectLibraryExportMode.SCRIPT_SOURCE_FILES)
        {
            return await dialogs.OpenFolderAsync("ExportEffectLibraryFolder").WaitAsync(token);
        }
        if (mode == EffectLibraryExportMode.PACKAGE)
        {
            return await dialogs.SaveFileAsync("ExportEffectLibrary", "EffectLibraryPackageFiles", ["*.aegilibrary"],
                ".aegilibrary", "effects.aegilibrary").WaitAsync(token);
        }
        if (selection.Styles.Length == 1)
        {
            return await dialogs.SaveFileAsync("ExportStyles", "StyleFiles", ["*.aegistyles"], ".aegistyles", "style.aegistyles").WaitAsync(token);
        }
        var effect = selection.Effects.Single();
        var script = EffectScriptParser.Parse(effect.Source);
        return mode == EffectLibraryExportMode.SCRIPT_SOURCE
            ? await dialogs.SaveFileAsync("ExportEffectScripts", "EffectScriptFiles", ["*.aegifx"], ".aegifx", script.Id + ".aegifx").WaitAsync(token)
            : await dialogs.SaveFileAsync("ExportCompleteEffect", "CompleteEffectFiles", ["*.aegieffect"], ".aegieffect", script.Id + ".aegieffect").WaitAsync(token);
    }

    private void OnImportRequested(object? sender, EventArgs e)
    {
        _ = ImportAsync();
    }

    private void OnExportSelectionRequested(object? sender, EventArgs e)
    {
        _ = ExportSelectionAsync();
    }

    private void OnExportAllRequested(object? sender, EventArgs e)
    {
        _ = ExportAllAsync();
    }

    private void OnCommitImportRequested(object? sender, EventArgs e)
    {
        _ = CommitImportAsync();
    }

    private async Task SubmitAsync(AegiTask task, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var handle = context.Tasks.Submit(task);
        activeTask = handle;
        using var registration = token.Register(() => handle.RequestCancel());
        await handle.Completion;
    }

    private async Task<TResult> SubmitAsync<TResult>(AegiTask<TResult> task, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var handle = context.Tasks.Submit(task);
        activeTask = handle;
        using var registration = token.Register(() => handle.RequestCancel());
        return await handle.Completion;
    }

    private Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (disposed || activeOperation || model.ExternalBusy)
        {
            return Task.CompletedTask;
        }
        activeOperation = true;
        model.ShowError(null);
        model.ShowStatus(null);
        Completion = ExecuteAsync(operation, cancellation.Token);
        return Completion;
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken token)
    {
        try
        {
            await operation(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!disposed)
            {
                model.ShowError(error.Message);
            }
        }
        finally
        {
            activeTask = null;
            activeOperation = false;
            if (!disposed)
            {
                model.SetOperationBusy(false);
            }
        }
    }
}
