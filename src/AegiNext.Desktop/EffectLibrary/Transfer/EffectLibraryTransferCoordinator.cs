using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

internal sealed class EffectLibraryTransferCoordinator : IDisposable
{
    private readonly DesktopApplicationContext context;
    private readonly EffectLibraryTransferViewModel model;
    private readonly IWorkbenchDialogService dialogs;
    private readonly Func<Task<bool>> prepareImportAsync;
    private readonly CancellationTokenSource cancellation = new();
    private ImmutableArray<string> previewPaths = [];
    private AegiTaskHandle? activeTask;
    private bool activeOperation;
    private bool disposed;

    internal EffectLibraryTransferCoordinator(DesktopApplicationContext context, EffectLibraryTransferViewModel model,
        IWorkbenchDialogService dialogs, Func<Task<bool>> prepareImportAsync)
    {
        this.context = context;
        this.model = model;
        this.dialogs = dialogs;
        this.prepareImportAsync = prepareImportAsync;
        model.ImportRequested += OnImportRequested;
        model.ExportRequested += OnExportRequested;
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

    internal Task ExportDialogAsync(Action<string?>? reportError = null)
    {
        return RunAsync(async token =>
        {
            model.SetOperationBusy(true);
            await context.Initialization.WaitAsync(token);
            await context.Fonts.EnsureLoadedAsync().WaitAsync(token);
            var frozen = context.EffectLibrary.Snapshot;
            var frozenFonts = context.Fonts.Catalog;
            using var dialogCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var dialogToken = dialogCancellation.Token;
            var dialogOperation = dialogs.ShowEffectLibraryExportAsync(frozen, async (request, scopedDialogs, requestToken) =>
            {
                dialogToken.ThrowIfCancellationRequested();
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(dialogToken, requestToken);
                return await ExportRequestAsync(request, scopedDialogs, requestCancellation.Token, frozenFonts);
            }, dialogToken);
            bool exported;
            try
            {
                exported = await dialogOperation.WaitAsync(token);
            }
            finally
            {
                dialogCancellation.Cancel();
                if (activeTask is { } handle)
                {
                    await ObserveCompletionAsync(handle.Completion);
                }
                _ = ObserveCompletionAsync(dialogOperation);
            }
            token.ThrowIfCancellationRequested();
            if (exported)
            {
                model.ShowStatus("EffectLibrary.Exported");
            }
        }, reportError);
    }

    internal Task ExportSourceAsync(ImmutableArray<EffectScriptPreset> effects, Action<string?>? reportError = null)
    {
        return RunAsync(async token =>
        {
            if (effects.IsDefaultOrEmpty)
            {
                return;
            }
            model.SetOperationBusy(true);
            await context.Initialization.WaitAsync(token);
            var mode = effects.Length == 1 ? EffectLibraryExportMode.SCRIPT_SOURCE : EffectLibraryExportMode.SCRIPT_SOURCE_FILES;
            var destination = await ChooseExportDestinationAsync([], effects, mode, dialogs, token);
            token.ThrowIfCancellationRequested();
            if (destination is null)
            {
                return;
            }
            await SubmitAsync(new EffectLibraryExportTask(context, [], effects, destination, mode), token);
            token.ThrowIfCancellationRequested();
            model.ShowStatus("EffectLibrary.Exported");
        }, reportError);
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
            var fontsReady = plan.Rows.Any(row => row.Kind == EffectLibraryItemKind.STYLE &&
                decisions.GetValueOrDefault(row.Index) is EffectLibraryImportDecision.IMPORT or EffectLibraryImportDecision.REPLACE);
            model.ShowStatus(fontsReady ? "EffectLibrary.ImportedWithFonts" : "EffectLibrary.Imported", added, replaced, skipped);
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
        model.SetOperationBusy(false);
        model.ImportRequested -= OnImportRequested;
        model.ExportRequested -= OnExportRequested;
        model.CommitImportRequested -= OnCommitImportRequested;
        cancellation.Dispose();
    }

    private async Task<bool> ExportRequestAsync(EffectLibraryExportRequest request, IWorkbenchDialogService scopedDialogs,
        CancellationToken token, SystemFontCatalog? frozenFonts)
    {
        token.ThrowIfCancellationRequested();
        var count = request.Styles.Length + request.Effects.Length;
        var mode = request.Format switch
        {
            EffectLibraryExportFormat.PACKAGE => EffectLibraryExportMode.PACKAGE,
            EffectLibraryExportFormat.INDEPENDENT when count > 0 => count == 1
                ? EffectLibraryExportMode.SINGLE_FILE : EffectLibraryExportMode.INDEPENDENT_FILES,
            EffectLibraryExportFormat.SOURCE when request.Styles.IsEmpty && !request.Effects.IsEmpty => count == 1
                ? EffectLibraryExportMode.SCRIPT_SOURCE : EffectLibraryExportMode.SCRIPT_SOURCE_FILES,
            _ => throw new ArgumentException("导出格式不适用于当前素材选择。", nameof(request))
        };
        var destination = await ChooseExportDestinationAsync(request.Styles, request.Effects, mode, scopedDialogs, token);
        token.ThrowIfCancellationRequested();
        if (destination is null)
        {
            return false;
        }
        await SubmitAsync(new EffectLibraryExportTask(context, request.Styles, request.Effects, destination, mode, frozenFonts), token);
        token.ThrowIfCancellationRequested();
        return true;
    }

    private static async Task<string?> ChooseExportDestinationAsync(ImmutableArray<SubtitleStylePreset> styles,
        ImmutableArray<EffectScriptPreset> effects, EffectLibraryExportMode mode, IWorkbenchDialogService service,
        CancellationToken token)
    {
        if (mode is EffectLibraryExportMode.INDEPENDENT_FILES or EffectLibraryExportMode.SCRIPT_SOURCE_FILES)
        {
            return await service.OpenFolderAsync("ExportEffectLibraryFolder").WaitAsync(token);
        }
        if (mode == EffectLibraryExportMode.PACKAGE)
        {
            return await service.SaveFileAsync("ExportEffectLibrary", "EffectLibraryPackageFiles", ["*.aegilibrary"],
                ".aegilibrary", "effects.aegilibrary").WaitAsync(token);
        }
        if (styles.Length == 1)
        {
            return await service.SaveFileAsync("ExportStyles", "StyleFiles", ["*.aegistyles"], ".aegistyles", "style.aegistyles").WaitAsync(token);
        }
        var effect = effects.Single();
        var script = EffectScriptParser.Parse(effect.Source);
        return mode == EffectLibraryExportMode.SCRIPT_SOURCE
            ? await service.SaveFileAsync("ExportEffectScripts", "EffectScriptFiles", ["*.aegifx"], ".aegifx", script.Id + ".aegifx").WaitAsync(token)
            : await service.SaveFileAsync("ExportCompleteEffect", "CompleteEffectFiles", ["*.aegieffect"], ".aegieffect", script.Id + ".aegieffect").WaitAsync(token);
    }

    private void OnImportRequested(object? sender, EventArgs e)
    {
        _ = ImportAsync();
    }

    private void OnExportRequested(object? sender, EventArgs e)
    {
        _ = ExportDialogAsync();
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

    private static async Task ObserveCompletionAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private Task RunAsync(Func<CancellationToken, Task> operation, Action<string?>? reportError = null)
    {
        if (disposed || activeOperation || model.ExternalBusy)
        {
            return Task.CompletedTask;
        }
        activeOperation = true;
        model.ShowError(null);
        reportError?.Invoke(null);
        model.ShowStatus(null);
        Completion = ExecuteAsync(operation, reportError, cancellation.Token);
        return Completion;
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> operation, Action<string?>? reportError, CancellationToken token)
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
                if (reportError is null)
                {
                    model.ShowError(error.Message);
                }
                else
                {
                    reportError(Localization.Format("EffectLibrary.TransferFailed", error.Message));
                }
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
