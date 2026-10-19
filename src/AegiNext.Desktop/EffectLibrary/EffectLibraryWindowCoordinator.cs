using System.Collections.Immutable;
using System.ComponentModel;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.EffectLibrary.Effects;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Windowing;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Workspace.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AegiNext.Desktop.EffectLibrary;

internal sealed class EffectLibraryWindowCoordinator(DesktopApplicationContext applicationContext,
    Func<Window, IWorkbenchDialogService>? createDialogs = null) : IDisposable
{
    private WorkbenchSession? session;
    private IWorkbenchDialogService? dialogs;
    private bool disposed;
    private IWindowChrome? chrome;
    private CancellationTokenSource? deletionCancellation;
    private bool deletionActive;
    private EffectLibraryTransferCoordinator? transfer;
    private Task closedTransferCompletion = Task.CompletedTask;
    private Task closedDisposalCompletion = Task.CompletedTask;
    internal Task TransferCompletion => Task.WhenAll(closedTransferCompletion, transfer?.Completion ?? Task.CompletedTask);
    internal Task DisposalCompletion => Task.WhenAll(closedDisposalCompletion, Window?.DisposalCompletion ?? Task.CompletedTask);
    internal Task DeletionCompletion { get; private set; } = Task.CompletedTask;
    internal event Action<WorkbenchLogEntry>? EffectScriptErrorReported;
    internal EffectLibraryWindow? Window { get; private set; }

    internal async Task OpenAsync(Window owner, WorkbenchSession? session = null, EffectLibraryPage? page = null,
        Action<Window>? registerWindow = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(owner);
        if (Window is { CloseCompletion.IsCompleted: false } closingWindow)
        {
            await closingWindow.CloseCompletion;
            ObjectDisposedException.ThrowIf(disposed, this);
        }
        if (Window is { } existing)
        {
            if (page is { } requested)
            {
                existing.SelectPage(requested);
            }
            existing.Activate();
            return;
        }
        var window = new EffectLibraryWindow(applicationContext.Preferences);
        Window = window;
        this.session = session;
        dialogs = createDialogs?.Invoke(window) ?? new WindowWorkbenchDialogService(window);
        deletionCancellation = new();
        try
        {
            window.ViewModel.Styles.SetFonts(applicationContext.Fonts);
            window.ViewModel.Effects.PreviewFonts = applicationContext.Fonts.Catalog;
            window.ViewModel.Effects.UpdatePreviewStyles(applicationContext.StyleLibrary.Snapshot.Presets);
            window.ViewModel.Styles.SetPositionMeasurement((preset, text) => session is null
                ? MeasureDefaultPosition(preset, text) : session.MeasureStylePosition(preset, text));
            transfer = new(applicationContext, window.ViewModel.Transfer, dialogs, PrepareImportAsync);
            Subscribe(window);
            window.UpdateStyles(applicationContext.StyleLibrary.Snapshot.Presets);
            window.UpdateEffects(applicationContext.EffectScriptLibrary.Snapshot.Presets);
            RefreshAvailability();
            window.ShowError(applicationContext.EffectLibraryLoadError?.Message);
            if (page is { } selected)
            {
                window.SelectPage(selected);
            }
            if (registerWindow is null)
            {
                chrome = WindowChrome.Attach(window, window.TitleBar);
            }
            else
            {
                registerWindow(window);
            }
            window.Show(owner);
            _ = LoadPreviewFontsAsync(window);
        }
        catch
        {
            CancelDeletion();
            DisposeTransfer();
            Unsubscribe(window);
            Window = null;
            this.session = null;
            dialogs = null;
            window.CloseImmediately();
            closedDisposalCompletion = Task.WhenAll(closedDisposalCompletion, window.DisposalCompletion);
            chrome?.Dispose();
            chrome = null;
            throw;
        }
    }

    internal void Close() => Window?.Close();

    /// <summary>结束窗口与后台分享操作的生命周期。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        CancelDeletion();
        DisposeTransfer();
        Window?.CloseImmediately();
        EffectScriptErrorReported = null;
    }

    private void Subscribe(EffectLibraryWindow window)
    {
        window.ViewModel.Styles.SaveDraftAsync = preset => RunAsync(() => SaveStylePresetAsync(preset));
        window.ViewModel.Effects.SaveDraftAsync = preset => RunAsync(() =>
            applicationContext.RunEffectOperationAsync(() => applicationContext.EffectScriptLibrary.UpsertAsync(preset)), true);
        window.ViewModel.Styles.ConfirmLeaveAsync = () => dialogs!.ConfirmPresetChangesAsync(false);
        window.ViewModel.Effects.ConfirmLeaveAsync = () => dialogs!.ConfirmPresetChangesAsync(true);
        applicationContext.PreferencesChanged += OnPreferencesChanged;
        applicationContext.StylesChanged += OnStylesChanged;
        applicationContext.EffectsChanged += OnEffectsChanged;
        applicationContext.BusyChanged += OnBusyChanged;
        applicationContext.LibraryOperationFailed += OnLibraryOperationFailed;
        applicationContext.Fonts.Changed += OnFontsChanged;
        window.UpsertStyleRequested += OnUpsertStyleRequested;
        window.DeleteStyleRequested += OnDeleteStyleRequested;
        window.CaptureStyleRequested += OnCaptureStyleRequested;
        window.ApplyStyleRequested += OnApplyStyleRequested;
        window.ImportStylesRequested += OnImportRequested;
        window.ImportEffectRequested += OnImportRequested;
        window.ExportStylesRequested += OnExportRequested;
        window.ExportEffectRequested += OnExportRequested;
        window.ExportEffectSourceRequested += OnExportSourceRequested;
        window.EffectValidationFailed += OnEffectValidationFailed;
        window.UpsertEffectRequested += OnUpsertEffectRequested;
        window.DeleteEffectRequested += OnDeleteEffectRequested;
        window.Closed += OnWindowClosed;
        if (session is { } active)
        {
            active.SelectionChanged += OnBusyChanged;
            active.Styles.BusyChanged += OnBusyChanged;
            active.StyleLibraryChanged += OnBusyChanged;
            active.EffectLibraryChanged += OnBusyChanged;
            active.ViewModel.PropertyChanged += OnSessionStateChanged;
        }
    }

    private void Unsubscribe(EffectLibraryWindow window)
    {
        window.ViewModel.Styles.SaveDraftAsync = null;
        window.ViewModel.Effects.SaveDraftAsync = null;
        window.ViewModel.Styles.ConfirmLeaveAsync = null;
        window.ViewModel.Effects.ConfirmLeaveAsync = null;
        applicationContext.PreferencesChanged -= OnPreferencesChanged;
        applicationContext.StylesChanged -= OnStylesChanged;
        applicationContext.EffectsChanged -= OnEffectsChanged;
        applicationContext.BusyChanged -= OnBusyChanged;
        applicationContext.LibraryOperationFailed -= OnLibraryOperationFailed;
        applicationContext.Fonts.Changed -= OnFontsChanged;
        window.UpsertStyleRequested -= OnUpsertStyleRequested;
        window.DeleteStyleRequested -= OnDeleteStyleRequested;
        window.CaptureStyleRequested -= OnCaptureStyleRequested;
        window.ApplyStyleRequested -= OnApplyStyleRequested;
        window.ImportStylesRequested -= OnImportRequested;
        window.ImportEffectRequested -= OnImportRequested;
        window.ExportStylesRequested -= OnExportRequested;
        window.ExportEffectRequested -= OnExportRequested;
        window.ExportEffectSourceRequested -= OnExportSourceRequested;
        window.EffectValidationFailed -= OnEffectValidationFailed;
        window.UpsertEffectRequested -= OnUpsertEffectRequested;
        window.DeleteEffectRequested -= OnDeleteEffectRequested;
        window.Closed -= OnWindowClosed;
        if (session is { } active)
        {
            active.SelectionChanged -= OnBusyChanged;
            active.Styles.BusyChanged -= OnBusyChanged;
            active.StyleLibraryChanged -= OnBusyChanged;
            active.EffectLibraryChanged -= OnBusyChanged;
            active.ViewModel.PropertyChanged -= OnSessionStateChanged;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is EffectLibraryWindow window && ReferenceEquals(Window, window))
        {
            CancelDeletion();
            DisposeTransfer();
            closedDisposalCompletion = Task.WhenAll(closedDisposalCompletion, window.DisposalCompletion);
            Unsubscribe(window);
            chrome?.Dispose();
            chrome = null;
            Window = null;
            session = null;
            dialogs = null;
        }
    }

    private void CancelDeletion()
    {
        deletionCancellation?.Cancel();
        deletionCancellation?.Dispose();
        deletionCancellation = null;
        deletionActive = false;
    }

    private void DisposeTransfer()
    {
        if (transfer is { } current)
        {
            current.Dispose();
            closedTransferCompletion = Task.WhenAll(closedTransferCompletion, current.Completion);
            transfer = null;
        }
    }

    private void OnPreferencesChanged(object? sender, EventArgs e) => Window?.UpdatePreferences(applicationContext.Preferences);
    private void OnStylesChanged(object? sender, EventArgs e)
    {
        Window?.UpdateStyles(applicationContext.StyleLibrary.Snapshot.Presets);
        Window?.ViewModel.Effects.UpdatePreviewStyles(applicationContext.StyleLibrary.Snapshot.Presets);
    }
    private void OnEffectsChanged(object? sender, EventArgs e)
    {
        Window?.UpdateEffects(applicationContext.EffectScriptLibrary.Snapshot.Presets);
    }
    private void OnBusyChanged(object? sender, EventArgs e) => RefreshAvailability();
    private void OnLibraryOperationFailed(PersonalLibraryKind kind, Exception error)
    {
        if (kind is PersonalLibraryKind.STYLE or PersonalLibraryKind.EFFECT or PersonalLibraryKind.EFFECT_LIBRARY)
        {
            Window?.ShowError(error.Message);
        }
    }
    private void OnSessionStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkbenchViewModel.IsBusy))
        {
            RefreshAvailability();
        }
    }
    private void RefreshAvailability()
    {
        var projectBusy = session is { } active && (active.IsProjectBusy || active.IsClosing);
        Window?.SetStyleOperationBusy(deletionActive || applicationContext.StylesBusy || projectBusy || session?.Styles.IsBusy == true);
        Window?.SetEffectOperationBusy(deletionActive || applicationContext.EffectsBusy || projectBusy || session?.EffectScripts.IsBusy == true);
        Window?.UpdateSelectionAvailability(session is { HasSelectedCue: true, IsProjectBusy: false, IsClosing: false });
        if (Window is { } window)
        {
            window.ViewModel.Transfer.ExternalBusy = deletionActive || applicationContext.StylesBusy || applicationContext.EffectsBusy;
        }
    }

    private async Task<bool> PrepareImportAsync()
    {
        if (Window is not { } window || !await window.ViewModel.SelectPageAsync(EffectLibraryPage.TRANSFER))
        {
            return false;
        }
        await window.ViewModel.Styles.SelectionCompletion;
        await window.ViewModel.Effects.SelectionCompletion;
        if (disposed || !ReferenceEquals(Window, window) || !await window.ViewModel.Styles.PrepareToLeaveAsync())
        {
            return false;
        }
        return !disposed && ReferenceEquals(Window, window) && await window.ViewModel.Effects.PrepareToLeaveAsync()
            && !disposed && ReferenceEquals(Window, window);
    }

    private void OnFontsChanged(object? sender, EventArgs e)
    {
        if (Window is { } window)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => RefreshPreviewFonts(window));
                return;
            }
            RefreshPreviewFonts(window);
        }
    }

    private void RefreshPreviewFonts(EffectLibraryWindow target)
    {
        if (!disposed && ReferenceEquals(Window, target))
        {
            target.ViewModel.Effects.PreviewFonts = applicationContext.Fonts.Catalog;
        }
    }

    private async Task LoadPreviewFontsAsync(EffectLibraryWindow target)
    {
        try
        {
            await applicationContext.Fonts.EnsureLoadedAsync();
        }
        catch (Exception error)
        {
            if (!disposed && ReferenceEquals(Window, target))
            {
                target.ViewModel.Effects.SetPreviewError(error.Message);
            }
        }
    }

    private void OnImportRequested(object? sender, EventArgs e)
    {
        if (transfer is { } current)
        {
            _ = current.ImportAsync();
        }
    }

    private void OnExportRequested(object? sender, EventArgs e)
    {
        if (transfer is { } current && Window is { } target)
        {
            _ = current.ExportDialogAsync(message =>
            {
                if (ReferenceEquals(Window, target))
                {
                    target.ShowError(message);
                }
            });
        }
    }

    private void OnExportSourceRequested(object? sender, LibraryEffectsExportEventArgs e)
    {
        if (transfer is { } current && Window is { } target)
        {
            _ = current.ExportSourceAsync(e.Presets, message =>
            {
                if (ReferenceEquals(Window, target))
                {
                    target.ShowError(message);
                }
            });
        }
    }

    private Task SaveStylePresetAsync(SubtitleStylePreset preset)
    {
        return applicationContext.RunStyleOperationAsync(() =>
        {
            var current = applicationContext.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == preset.Id);
            var updated = current is null ? preset : preset with { TimingPostProcessor = current.TimingPostProcessor };
            return applicationContext.StyleLibrary.UpsertAsync(updated);
        });
    }

    private void OnUpsertStyleRequested(object? sender, LibraryStyleEventArgs e)
    {
        _ = RunAsync(() => SaveStylePresetAsync(e.Preset));
    }

    private void OnDeleteStyleRequested(object? sender, LibraryStyleDeleteEventArgs e)
    {
        RequestPresetDeletion(e.Ids, e.IsDraftOnly, e.DraftId, false);
    }

    private void RequestPresetDeletion(ImmutableArray<Guid> ids, bool isDraftOnly, Guid? draftId, bool effects)
    {
        if (disposed || deletionActive || Window is not { } target || dialogs is not { } service
            || deletionCancellation is not { } cancellation || (effects ? applicationContext.EffectsBusy : applicationContext.StylesBusy))
        {
            return;
        }

        deletionActive = true;
        RefreshAvailability();
        var token = cancellation.Token;
        DeletionCompletion = RunAsync(async () =>
        {
            try
            {
                var names = isDraftOnly
                    ? ImmutableArray.Create(effects ? target.ViewModel.Effects.Name : target.ViewModel.Styles.Name)
                    : effects
                        ? ids.Select(id => applicationContext.EffectScriptLibrary.Snapshot.Presets.Single(preset => preset.Id == id).Name).ToImmutableArray()
                        : ids.Select(id => applicationContext.StyleLibrary.Snapshot.Presets.Single(preset => preset.Id == id).Name).ToImmutableArray();
                var accepted = await service.ConfirmPresetDeletionAsync(new(names, isDraftOnly), token).WaitAsync(token);
                token.ThrowIfCancellationRequested();
                if (!accepted || disposed || !ReferenceEquals(Window, target))
                {
                    return;
                }

                if (!isDraftOnly)
                {
                    if (effects)
                    {
                        await applicationContext.RunEffectOperationAsync(() => applicationContext.EffectScriptLibrary.RemoveAsync(ids, token));
                    }
                    else
                    {
                        await applicationContext.RunStyleOperationAsync(() => applicationContext.StyleLibrary.RemoveAsync(ids, token));
                    }
                }
                if (disposed || !ReferenceEquals(Window, target))
                {
                    return;
                }

                var currentDraftId = effects ? target.ViewModel.Effects.Draft?.Id : target.ViewModel.Styles.Draft?.Id;
                if (currentDraftId is { } current && (isDraftOnly ? current == draftId : ids.Contains(current)))
                {
                    if (effects)
                    {
                        target.ViewModel.Effects.DiscardDraft();
                    }
                    else
                    {
                        target.ViewModel.Styles.DiscardDraft();
                    }
                }
            }
            finally
            {
                if (ReferenceEquals(Window, target))
                {
                    deletionActive = false;
                    RefreshAvailability();
                }
            }
        }, effects);
    }

    private void OnCaptureStyleRequested(object? sender, LibraryStyleEventArgs e)
    {
        if (session is { } active && Window is { } target)
        {
            _ = RunAsync(async () =>
            {
                if (!await target.ViewModel.Styles.PrepareToLeaveAsync() || disposed || active.IsClosing ||
                    !ReferenceEquals(Window, target) || !ReferenceEquals(session, active) ||
                    target.ViewModel.Styles.SelectedStyle?.Id != e.Preset.Id || target.ViewModel.Styles.SelectedIds.Length != 1)
                {
                    return;
                }
                await active.Styles.QueueAsync(() => active.Styles.CaptureAsync(e.Preset.Id));
                if (!disposed && !active.IsClosing && ReferenceEquals(Window, target))
                {
                    target.UpdateStyles(active.StyleLibrary.Snapshot.Presets, e.Preset.Id);
                }
            });
        }
    }

    private void OnApplyStyleRequested(object? sender, LibraryStyleEventArgs e)
    {
        if (session is { } active)
        {
            _ = RunAsync(() =>
            {
                active.Styles.Queue(() => active.Styles.ApplyAsync(e.Preset));
                return active.Styles.Completion;
            });
        }
    }

    private void OnEffectValidationFailed(object? sender, EffectScriptValidationFailedEventArgs e)
    {
        if (session is { IsClosing: false } active)
        {
            EffectScriptErrorReported?.Invoke(active.LogError("Effects", e.Error));
        }
    }

    private void OnUpsertEffectRequested(object? sender, LibraryEffectEventArgs e)
    {
        _ = RunAsync(() => applicationContext.RunEffectOperationAsync(() => applicationContext.EffectScriptLibrary.UpsertAsync(e.Preset)), true);
    }

    private void OnDeleteEffectRequested(object? sender, LibraryEffectDeleteEventArgs e)
    {
        RequestPresetDeletion(e.Ids, e.IsDraftOnly, e.DraftId, true);
    }

    private async Task<bool> RunAsync(Func<Task> operation, bool effectOperation = false)
    {
        if (disposed || Window is not { } targetWindow)
        {
            return false;
        }

        var targetSession = session;
        targetWindow.ShowError(null);
        try
        {
            await operation();
            return true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (ReferenceEquals(Window, targetWindow))
            {
                targetWindow.ShowError(error.Message);
            }

            if (effectOperation)
            {
                ReportEffectError(targetSession, error);
            }
            else if (targetSession is { IsClosing: false })
            {
                targetSession.ShowError(error);
            }
        }
        return false;
    }

    private void ReportEffectError(WorkbenchSession? targetSession, Exception error)
    {
        if (targetSession is { IsClosing: false })
        {
            targetSession.ShowError(error, false);
            EffectScriptErrorReported?.Invoke(targetSession.LogError("Effects", error));
        }
    }

    private SubtitlePositionMeasurement MeasureDefaultPosition(SubtitleStylePreset preset, string text)
    {
        var document = new ProjectDocument();
        return Rendering.SubtitleStylePositionMeasurer.Measure(preset, document.Width, document.Height,
            text, applicationContext.Fonts.Catalog);
    }

}
