using System.Collections.Immutable;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Settings.Export;

internal sealed class SettingsExportPresetCoordinator : IDisposable
{
    private readonly DesktopApplicationContext applicationContext;
    private readonly SettingsWindow window;
    private readonly IWorkbenchDialogService dialogs;
    private readonly WorkbenchSession? session;
    private readonly CancellationTokenSource cancellation = new();
    private bool activeOperation;
    private bool disposed;

    internal SettingsExportPresetCoordinator(DesktopApplicationContext applicationContext,
        SettingsWindow window, IWorkbenchDialogService dialogs, WorkbenchSession? session)
    {
        this.applicationContext = applicationContext;
        this.window = window;
        this.dialogs = dialogs;
        this.session = session;
        var viewModel = window.ViewModel.ExportPresets;
        viewModel.SaveDraftAsync = SaveAsync;
        viewModel.ConfirmLeaveAsync = ConfirmLeaveAsync;
        viewModel.CaptureRequested += OnCaptureRequested;
        viewModel.ImportRequested += OnImportRequested;
        viewModel.ExportRequested += OnExportRequested;
        viewModel.DeleteRequested += OnDeleteRequested;
        applicationContext.ExportPresetsChanged += OnPresetsChanged;
        applicationContext.BusyChanged += OnBusyChanged;
        viewModel.UpdatePresets(applicationContext.ExportPresetLibrary.Snapshot.Presets);
        RefreshAvailability();
    }

    internal Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>取消当前窗口的交互等待并解除共享事件，已提交的原子库操作由任务服务收尾。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellation.Cancel();
        var viewModel = window.ViewModel.ExportPresets;
        viewModel.SaveDraftAsync = null;
        viewModel.ConfirmLeaveAsync = null;
        viewModel.CaptureRequested -= OnCaptureRequested;
        viewModel.ImportRequested -= OnImportRequested;
        viewModel.ExportRequested -= OnExportRequested;
        viewModel.DeleteRequested -= OnDeleteRequested;
        applicationContext.ExportPresetsChanged -= OnPresetsChanged;
        applicationContext.BusyChanged -= OnBusyChanged;
        cancellation.Dispose();
    }

    private Task<bool> SaveAsync(VideoExportPreset preset)
    {
        return RunAsync(async token =>
        {
            await applicationContext.RunExportPresetOperationAsync(() =>
                applicationContext.ExportPresetLibrary.UpsertAsync(preset, CancellationToken.None));
            if (!disposed)
            {
                window.ViewModel.ExportPresets.UpdatePresets(applicationContext.ExportPresetLibrary.Snapshot.Presets, preset.Id);
            }
        });
    }

    private async Task<int> ConfirmLeaveAsync()
    {
        if (disposed)
        {
            return 2;
        }

        var token = cancellation.Token;
        try
        {
            return await dialogs.ConfirmExportPresetChangesAsync().WaitAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!disposed)
            {
                window.ShowError(error.Message);
            }
        }
        return 2;
    }

    private void OnCaptureRequested(object? sender, EventArgs e)
    {
        if (disposed || session is not { IsClosing: false } active)
        {
            return;
        }

        window.ShowError(null);
        try
        {
            window.ViewModel.ExportPresets.CaptureFromSettings(active.ViewModel.Export.CaptureSettings(true));
        }
        catch (Exception error)
        {
            window.ShowError(error.Message);
        }
    }

    private void OnImportRequested(object? sender, EventArgs e)
    {
        _ = RunAsync(async token =>
        {
            var paths = await dialogs.OpenFilesAsync("ImportExportPresets", "ExportPresetFiles", ["*.aegiexports"]).WaitAsync(token);
            if (paths.Count > 0)
            {
                await applicationContext.RunExportPresetOperationAsync(() =>
                    applicationContext.ExportPresetLibrary.ImportAsync(paths, CancellationToken.None));
            }
        });
    }

    private void OnExportRequested(object? sender, SettingsExportPresetsEventArgs e)
    {
        var presets = e.Presets;
        if (presets.IsEmpty)
        {
            return;
        }

        _ = RunAsync(async token =>
        {
            var path = await dialogs.SaveFileAsync("ExportExportPreset", "ExportPresetFiles", ["*.aegiexports"],
                ".aegiexports", "export-presets.aegiexports").WaitAsync(token);
            if (path is not null)
            {
                await applicationContext.Tasks.Submit(new SettingsExportPresetExportTask(applicationContext, presets, path)).Completion;
            }
        });
    }

    private void OnDeleteRequested(object? sender, SettingsExportPresetDeleteEventArgs e)
    {
        _ = RunAsync(async token =>
        {
            var names = e.IsDraftOnly
                ? ImmutableArray.Create(window.ViewModel.ExportPresets.Name)
                : e.Ids.Select(id => applicationContext.ExportPresetLibrary.Snapshot.Presets.Single(preset => preset.Id == id).Name).ToImmutableArray();
            if (!await dialogs.ConfirmPresetDeletionAsync(new(names, e.IsDraftOnly), token).WaitAsync(token))
            {
                return;
            }
            token.ThrowIfCancellationRequested();
            if (disposed)
            {
                return;
            }
            if (!e.IsDraftOnly)
            {
                await applicationContext.RunExportPresetOperationAsync(() =>
                    applicationContext.ExportPresetLibrary.RemoveAsync(e.Ids, CancellationToken.None));
            }
            if (!disposed && window.ViewModel.ExportPresets.Draft?.Id is { } current
                && (e.IsDraftOnly ? current == e.DraftId : e.Ids.Contains(current)))
            {
                window.ViewModel.ExportPresets.DiscardDraft();
            }
        });
    }

    private void OnPresetsChanged(object? sender, EventArgs e)
    {
        if (!disposed)
        {
            window.ViewModel.ExportPresets.UpdatePresets(applicationContext.ExportPresetLibrary.Snapshot.Presets);
        }
    }

    private void OnBusyChanged(object? sender, EventArgs e)
    {
        RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        if (!disposed)
        {
            window.ViewModel.ExportPresets.IsBusy = activeOperation || applicationContext.ExportPresetsBusy;
            window.ViewModel.ExportPresets.HasWorkspace = session is { IsClosing: false };
        }
    }

    private Task<bool> RunAsync(Func<CancellationToken, Task> operation)
    {
        if (disposed || activeOperation || applicationContext.ExportPresetsBusy)
        {
            return Task.FromResult(false);
        }

        activeOperation = true;
        RefreshAvailability();
        window.ShowError(null);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Completion = completion.Task;
        return ExecuteAsync(operation, completion, cancellation.Token);
    }

    private async Task<bool> ExecuteAsync(Func<CancellationToken, Task> operation, TaskCompletionSource completion,
        CancellationToken token)
    {
        try
        {
            await operation(token);
            return !disposed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
        {
            if (!disposed)
            {
                window.ShowError(error.Message);
                if (session is { IsClosing: false } active)
                {
                    active.ShowError(error);
                }
            }
            return false;
        }
        finally
        {
            activeOperation = false;
            try
            {
                RefreshAvailability();
            }
            finally
            {
                completion.TrySetResult();
            }
        }
    }
}
