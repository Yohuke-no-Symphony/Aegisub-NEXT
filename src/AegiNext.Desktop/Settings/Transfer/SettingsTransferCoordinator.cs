using AegiNext.Application.Tasks;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using Avalonia.Threading;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed class SettingsTransferCoordinator : IDisposable
{
    private readonly DesktopApplicationContext applicationContext;
    private readonly SettingsWindow window;
    private readonly IWorkbenchDialogService dialogs;
    private readonly Func<WorkspaceLayoutFile>? captureLayout;
    private readonly Action? requestApplicationExit;
    private readonly CancellationTokenSource cancellation = new();
    private UserSettingsBundle? preview;
    private AegiTaskHandle? activeTask;
    private bool activeOperation;
    private bool disposed;

    internal SettingsTransferCoordinator(DesktopApplicationContext applicationContext, SettingsWindow window,
        IWorkbenchDialogService dialogs, Func<WorkspaceLayoutFile>? captureLayout, Action? requestApplicationExit)
    {
        this.applicationContext = applicationContext;
        this.window = window;
        this.dialogs = dialogs;
        this.captureLayout = captureLayout;
        this.requestApplicationExit = requestApplicationExit;
        var viewModel = window.ViewModel.Transfer;
        viewModel.ExportRequested += OnExportRequested;
        viewModel.ImportRequested += OnImportRequested;
        viewModel.StageRestoreRequested += OnStageRestoreRequested;
        viewModel.CancelPendingRequested += OnCancelPendingRequested;
        viewModel.RestartRequested += OnRestartRequested;
        applicationContext.SettingsRestore.PendingChanged += OnPendingChanged;
        RefreshPending();
        RefreshAvailability();
    }

    internal Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>取消窗口请求并解除共享恢复状态订阅，阻止迟到的选择器结果。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellation.Cancel();
        activeTask?.RequestCancel();
        var viewModel = window.ViewModel.Transfer;
        viewModel.ExportRequested -= OnExportRequested;
        viewModel.ImportRequested -= OnImportRequested;
        viewModel.StageRestoreRequested -= OnStageRestoreRequested;
        viewModel.CancelPendingRequested -= OnCancelPendingRequested;
        viewModel.RestartRequested -= OnRestartRequested;
        applicationContext.SettingsRestore.PendingChanged -= OnPendingChanged;
        cancellation.Dispose();
    }

    private void OnExportRequested(object? sender, EventArgs e)
    {
        _ = RunAsync(async token =>
        {
            await applicationContext.Initialization.WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (applicationContext.SettingsLoadError is { } error)
            {
                throw new InvalidOperationException(Localization.Get("Settings.TransferExportUnavailable"), error);
            }

            var layouts = captureLayout?.Invoke();
            var snapshot = new UserSettingsBundle
            {
                Preferences = applicationContext.Preferences,
                ExportPresets = applicationContext.ExportPresetLibrary.Snapshot,
                ColorTags = applicationContext.ColorTagLibrary.Snapshot,
                Layouts = layouts ?? applicationContext.InitialLayout
            };
            if (layouts is null)
            {
                snapshot = snapshot with
                {
                    Layouts = await SubmitAsync(new UserSettingsLayoutCaptureTask(applicationContext), token)
                };
            }

            var path = await dialogs.SaveFileAsync("ExportUserSettings", "UserSettingsFiles", ["*.aegisettings"],
                ".aegisettings", "settings.aegisettings").WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (path is null)
            {
                return;
            }

            ValidateExportDestination(path);
            await SubmitAsync(new UserSettingsExportTask(applicationContext, snapshot, path), token);
            if (!disposed)
            {
                window.ViewModel.Transfer.ShowStatus("Settings.TransferExported");
            }
        });
    }

    private void OnImportRequested(object? sender, EventArgs e)
    {
        _ = RunAsync(async token =>
        {
            var path = await dialogs.OpenFileAsync("ImportUserSettings", "UserSettingsFiles", ["*.aegisettings"]).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            if (path is null)
            {
                return;
            }

            var imported = await SubmitAsync(new UserSettingsImportTask(path), token);
            if (!disposed)
            {
                preview = imported;
                window.ViewModel.Transfer.SetPreview(Path.GetFileName(path), imported.ExportPresets.Presets.Length,
                    imported.Layouts.Presets.Count, imported.ColorTags?.Tags.Length, imported.SourceVersion);
            }
        });
    }

    private void ValidateExportDestination(string path)
    {
        var directory = Path.GetFullPath(applicationContext.PreferencesStore.DirectoryPath);
        var target = Path.GetFullPath(path);
        if (IsWithinDirectory(target, directory) || IsWithinDirectory(UserSettingsTransferFiles.ResolvePhysicalPath(target),
                UserSettingsTransferFiles.ResolvePhysicalPath(directory)))
        {
            throw new InvalidOperationException(Localization.Get("Settings.TransferUnsafeDestination"));
        }
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(path, directory, comparison) ||
            path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, comparison);
    }

    private void OnStageRestoreRequested(object? sender, EventArgs e)
    {
        if (preview is not { } imported)
        {
            return;
        }

        var keepWorkspaceRoot = window.ViewModel.Transfer.KeepWorkspaceRoot;
        _ = RunAsync(async token =>
        {
            await applicationContext.Initialization.WaitAsync(token);
            var adapted = keepWorkspaceRoot ? imported with
            {
                Preferences = imported.Preferences with
                {
                    Projects = imported.Preferences.Projects with
                    {
                        WorkspaceRoot = applicationContext.Preferences.Projects.WorkspaceRoot
                    }
                }
            } : imported;
            adapted.Preferences.Validate();
            await SubmitAsync(new UserSettingsRestoreStageTask(applicationContext, adapted), token);
            if (!disposed)
            {
                preview = null;
                window.ViewModel.Transfer.ClearPreview();
                window.ViewModel.Transfer.ShowStatus(null);
                RefreshPending();
                await ConfirmRestartAsync(token);
            }
        });
    }

    private void OnCancelPendingRequested(object? sender, EventArgs e)
    {
        _ = RunAsync(async token =>
        {
            await SubmitAsync(new UserSettingsRestoreCancelTask(applicationContext), token);
            if (!disposed)
            {
                RefreshPending();
                window.ViewModel.Transfer.ShowStatus("Settings.TransferCancelled");
            }
        });
    }

    private void OnRestartRequested(object? sender, EventArgs e)
    {
        if (applicationContext.SettingsRestore.HasPending)
        {
            _ = RunAsync(ConfirmRestartAsync);
        }
    }

    private async Task ConfirmRestartAsync(CancellationToken token)
    {
        if (requestApplicationExit is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmSettingsRestartAsync(token).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if (confirmed && !disposed)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed && !token.IsCancellationRequested)
                {
                    requestApplicationExit();
                }
            });
        }
    }

    private void OnPendingChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshPending();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshPending);
        }
    }

    private void RefreshPending()
    {
        if (!disposed)
        {
            window.ViewModel.Transfer.UpdatePending(applicationContext.SettingsRestore.HasPending, requestApplicationExit is not null);
        }
    }

    private void RefreshAvailability()
    {
        if (!disposed)
        {
            window.ViewModel.Transfer.IsBusy = activeOperation;
        }
    }

    private async Task SubmitAsync(AegiTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        activeTask = applicationContext.Tasks.Submit(task);
        if (disposed || cancellationToken.IsCancellationRequested)
        {
            activeTask.RequestCancel();
        }

        await activeTask.Completion;
    }

    private async Task<TResult> SubmitAsync<TResult>(AegiTask<TResult> task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = applicationContext.Tasks.Submit(task);
        activeTask = handle;
        if (disposed || cancellationToken.IsCancellationRequested)
        {
            handle.RequestCancel();
        }

        return await handle.Completion;
    }

    private Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (disposed || activeOperation)
        {
            return Task.CompletedTask;
        }

        activeOperation = true;
        RefreshAvailability();
        window.ShowError(null);
        window.ViewModel.Transfer.ShowError(null);
        window.ViewModel.Transfer.ShowStatus(null);
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
                window.ViewModel.Transfer.ShowError(error.Message);
            }
        }
        finally
        {
            activeTask = null;
            activeOperation = false;
            RefreshAvailability();
        }
    }
}
