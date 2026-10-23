using System.Collections.Immutable;
using AegiNext.Desktop.Settings.Koumori;
using System.ComponentModel;
using AegiNext.Application.ColorTags;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings.Export;
using AegiNext.Desktop.Settings.Projects;
using AegiNext.Desktop.Settings.Preview;
using AegiNext.Desktop.Settings.Tasks;
using AegiNext.Desktop.Settings.AudioAnalysis;
using AegiNext.Desktop.Settings.Updates;
using AegiNext.Desktop.Settings.TimingPostProcessor;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Workspace.Diagnostics;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;

namespace AegiNext.Desktop.Settings;

internal sealed class SettingsWindowCoordinator(DesktopApplicationContext applicationContext,
    Func<Window, IWorkbenchDialogService>? createDialogs = null, Func<WorkspaceLayoutFile>? captureLayout = null,
    Action? requestApplicationExit = null) : IDisposable
{
    private WorkbenchSession? session;
    private IWorkbenchDialogService? dialogs;
    private WorkbenchPreferences? presentedPreferences;
    private bool disposed;
    private IWindowChrome? chrome;
    private SettingsExportPresetCoordinator? exportPresets;
    private SettingsTransferCoordinator? transfer;
    private Task closedTransferCompletion = Task.CompletedTask;
    internal Task ExportCompletion => exportPresets?.Completion ?? Task.CompletedTask;
    internal Task TransferCompletion => Task.WhenAll(closedTransferCompletion, transfer?.Completion ?? Task.CompletedTask);
    internal Task TimingCompletion { get; private set; } = Task.CompletedTask;

    internal SettingsWindow? Window { get; private set; }

    internal Task OpenAsync(Window owner, WorkbenchSession? session = null, SettingsPage? page = null,
        Action<Window>? registerWindow = null, bool modal = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(owner);
        if (Window is { } existing)
        {
            if (page is { } requested)
            {
                existing.SelectPage(requested);
            }

            existing.Activate();
            return Task.CompletedTask;
        }

        var window = new SettingsWindow(applicationContext.Preferences);
        Window = window;
        this.session = session;
        presentedPreferences = applicationContext.Preferences;
        dialogs = createDialogs?.Invoke(window) ?? new WindowWorkbenchDialogService(window);
        try
        {
            Subscribe(window);
            exportPresets = new(applicationContext, window, dialogs, session);
            transfer = new(applicationContext, window, dialogs, captureLayout, requestApplicationExit);
            window.UpdateShortcuts(applicationContext.Preferences.ShortcutBindings);
            window.UpdateStyles(applicationContext.StyleLibrary.Snapshot.Presets);
            window.ViewModel.ColorTags.UpdateLibrary(applicationContext.ColorTagLibrary.Snapshot);
            RefreshAvailability();
            RefreshMediaSettings();
            window.ShowError(applicationContext.LastError?.Message);
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
            if (modal)
            {
                return window.ShowDialog(owner);
            }

            window.Show(owner);
            return Task.CompletedTask;
        }
        catch
        {
            DisposeTransfer();
            exportPresets?.Dispose();
            exportPresets = null;
            Unsubscribe(window);
            Window = null;
            this.session = null;
            dialogs = null;
            presentedPreferences = null;
            window.Close();
            chrome?.Dispose();
            chrome = null;
            throw;
        }
    }

    internal void Close()
    {
        Window?.Close();
    }

    /// <summary>关闭设置并解除窗口、共享资源与工程会话事件。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeTransfer();
        exportPresets?.Dispose();
        Window?.CloseImmediately();
    }

    private void Subscribe(SettingsWindow window)
    {
        window.ViewModel.ColorTags.SaveDraftAsync = async (document, expected) =>
            await RunAsync(() => SaveColorTagsAsync(document, expected, window))
                ? applicationContext.ColorTagLibrary.Snapshot : null;
        window.ViewModel.ColorTags.ConfirmLeaveAsync = () => dialogs!.ConfirmColorTagChangesAsync();
        applicationContext.PreferencesChanged += OnPreferencesChanged;
        applicationContext.StylesChanged += OnStylesChanged;
        applicationContext.ColorTagsChanged += OnColorTagsChanged;
        applicationContext.BusyChanged += OnBusyChanged;
        applicationContext.ErrorChanged += OnErrorChanged;
        window.AppearanceChanged += OnAppearanceChanged;
        window.ColorsChanged += OnColorsChanged;
        window.ShortcutsChanged += OnShortcutsChanged;
        window.PreviewDecodeModeChanged += OnPreviewDecodeModeChanged;
        window.ViewModel.Media.AudioCalibrationChanged += OnAudioCalibrationChanged;
        window.ProjectsChanged += OnProjectsChanged;
        window.PreviewChanged += OnPreviewChanged;
        window.TasksChanged += OnTasksChanged;
        window.UpdatesChanged += OnUpdatesChanged;
        window.AudioAnalysisChanged += OnAudioAnalysisChanged;
        window.KoumoriChanged += OnKoumoriChanged;
        window.AudioAnalysisRebuildRequested += OnAudioAnalysisRebuildRequested;
        window.TimingPreferencesChanged += OnTimingPreferencesChanged;
        window.TimingAssociateRequested += OnTimingAssociationRequested;
        window.TimingUnlinkRequested += OnTimingAssociationRequested;
        window.Closed += OnWindowClosed;
        if (session is { } active)
        {
            active.PreviewDecodeModeChanged += OnSessionPreviewDecodeModeChanged;
            active.AudioClockChanged += OnSessionPreviewDecodeModeChanged;
        }
    }

    private void Unsubscribe(SettingsWindow window)
    {
        window.ViewModel.ColorTags.SaveDraftAsync = null;
        window.ViewModel.ColorTags.ConfirmLeaveAsync = null;
        applicationContext.PreferencesChanged -= OnPreferencesChanged;
        applicationContext.StylesChanged -= OnStylesChanged;
        applicationContext.ColorTagsChanged -= OnColorTagsChanged;
        applicationContext.BusyChanged -= OnBusyChanged;
        applicationContext.ErrorChanged -= OnErrorChanged;
        window.AppearanceChanged -= OnAppearanceChanged;
        window.ColorsChanged -= OnColorsChanged;
        window.ShortcutsChanged -= OnShortcutsChanged;
        window.PreviewDecodeModeChanged -= OnPreviewDecodeModeChanged;
        window.ViewModel.Media.AudioCalibrationChanged -= OnAudioCalibrationChanged;
        window.ProjectsChanged -= OnProjectsChanged;
        window.PreviewChanged -= OnPreviewChanged;
        window.TasksChanged -= OnTasksChanged;
        window.UpdatesChanged -= OnUpdatesChanged;
        window.AudioAnalysisChanged -= OnAudioAnalysisChanged;
        window.KoumoriChanged -= OnKoumoriChanged;
        window.AudioAnalysisRebuildRequested -= OnAudioAnalysisRebuildRequested;
        window.TimingPreferencesChanged -= OnTimingPreferencesChanged;
        window.TimingAssociateRequested -= OnTimingAssociationRequested;
        window.TimingUnlinkRequested -= OnTimingAssociationRequested;
        window.Closed -= OnWindowClosed;
        if (session is { } active)
        {
            active.PreviewDecodeModeChanged -= OnSessionPreviewDecodeModeChanged;
            active.AudioClockChanged -= OnSessionPreviewDecodeModeChanged;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window && ReferenceEquals(Window, window))
        {
            DisposeTransfer();
            exportPresets?.Dispose();
            exportPresets = null;
            Unsubscribe(window);
            chrome?.Dispose();
            chrome = null;
            Window = null;
            session = null;
            dialogs = null;
            presentedPreferences = null;
        }
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

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        var current = applicationContext.Preferences;
        var previous = presentedPreferences;
        presentedPreferences = current;
        Window?.UpdatePreferences(current);
        if (previous is null || !previous.ShortcutBindings.SequenceEqual(current.ShortcutBindings))
        {
            Window?.UpdateShortcuts(current.ShortcutBindings);
        }
    }

    private void OnStylesChanged(object? sender, EventArgs e)
    {
        Window?.UpdateStyles(applicationContext.StyleLibrary.Snapshot.Presets);
        RefreshTimingStyles();
    }

    private void OnColorTagsChanged(object? sender, EventArgs e) =>
        Window?.ViewModel.ColorTags.UpdateLibrary(applicationContext.ColorTagLibrary.Snapshot);

    private Task SaveColorTagsAsync(SubtitleColorTagLibraryDocument document, SubtitleColorTagLibraryDocument expected,
        SettingsWindow window) => applicationContext.RunColorTagOperationAsync(() =>
        applicationContext.ColorTagLibrary.ReplaceAsync(document, () =>
        {
            if (disposed || !ReferenceEquals(Window, window))
            {
                throw new OperationCanceledException();
            }
            if (!ReferenceEquals(applicationContext.ColorTagLibrary.Snapshot, expected))
            {
                throw new InvalidDataException(Localization.Get("Settings.ColorTagLibraryChanged"));
            }
        }));

    private void OnBusyChanged(object? sender, EventArgs e)
    {
        RefreshAvailability();
    }

    private void OnErrorChanged(object? sender, EventArgs e)
    {
        Window?.ShowError(applicationContext.LastError?.Message);
    }

    private void RefreshAvailability()
    {
        if (Window is { } window)
        {
            window.ViewModel.ColorTags.IsBusy = applicationContext.ColorTagsBusy;
        }
        RefreshTimingStyles();
    }

    private void OnSessionPreviewDecodeModeChanged(object? sender, EventArgs e)
    {
        RefreshMediaSettings();
    }

    private void RefreshMediaSettings()
    {
        if (Window is not { } window)
        {
            return;
        }

        window.ViewModel.Media.IsBusy = session?.IsPreviewDecodeModeSwitching == true || session?.IsSwitchingAudioDevice == true;
        window.ViewModel.Media.UpdatePreferences(applicationContext.Preferences);
        var info = session?.PreviewDecodeSessionInfo;
        window.ViewModel.Media.UpdateDecodeStatus(info?.ActiveBackend.ToString(), info?.HardwareConfirmed == true,
            info?.FallbackReason);
        window.ViewModel.Media.UpdateAudioStatus(session?.AudioClock);
    }

    private void OnAppearanceChanged(object? sender, SettingsAppearanceChangedEventArgs e)
    {
        UpdatePreferences(value => value with
        {
            Theme = e.Theme, Language = e.Language, WindowMenuOnMac = e.WindowMenuOnMac
        });
    }

    private void OnColorsChanged(object? sender, SettingsColorsChangedEventArgs e)
    {
        UpdatePreferences(value => value with { AccentColor = e.AccentColor, AudioGraph = e.AudioGraph, TimelineClips = e.TimelineClips });
    }

    private void OnProjectsChanged(object? sender, ProjectPreferencesChangedEventArgs e)
    {
        UpdatePreferences(value => value with { Projects = e.Preferences });
    }

    private void OnTasksChanged(object? sender, TaskSettingsChangedEventArgs e)
    {
        UpdatePreferences(value => value with { MaximumConcurrentTasks = e.MaximumConcurrentTasks });
    }

    private void OnUpdatesChanged(object? sender, UpdateSettingsChangedEventArgs e)
    {
        UpdatePreferences(value => value with { AutoCheckUpdates = e.AutoCheckUpdates, UpdateChannel = e.UpdateChannel });
    }

    private void OnKoumoriChanged(object? sender, KoumoriPreferencesChangedEventArgs e)
    {
        UpdatePreferences(value => value with { Koumori = e.Preferences });
    }

    private void OnAudioAnalysisChanged(object? sender, AudioAnalysisPreferencesChangedEventArgs e)
    {
        UpdatePreferences(value => value with { AudioAnalysis = e.Preferences });
    }

    private void OnAudioAnalysisRebuildRequested(object? sender, AudioAnalysisRebuildRequestedEventArgs e)
    {
        _ = RunAsync(() => applicationContext.RebuildAudioAnalysisAsync(e.Preferences));
    }

    private void OnPreviewChanged(object? sender, PreviewSettingsChangedEventArgs e)
    {
        UpdatePreferences(value => value with { SubtitleAuditionMilliseconds = e.SubtitleAuditionMilliseconds });
    }

    private void RefreshTimingStyles()
    {
        if (Window is { } window)
        {
            window.ViewModel.TimingPostProcessor.UpdateStyles(applicationContext.StyleLibrary.Snapshot.Presets);
            window.ViewModel.TimingPostProcessor.IsBusy = applicationContext.StylesBusy;
        }
    }

    private void OnTimingPreferencesChanged(object? sender, TimingPostProcessorPreferencesChangedEventArgs e)
    {
        UpdatePreferences(value => value with { TimingPostProcessor = e.Preferences });
    }

    private void OnTimingAssociationRequested(object? sender, TimingPostProcessorAssociationEventArgs e)
    {
        if (Window is not { } target || applicationContext.StylesBusy || !TimingCompletion.IsCompleted)
        {
            return;
        }

        TimingCompletion = SaveTimingAssociationAsync(target, e);
    }

    private async Task SaveTimingAssociationAsync(SettingsWindow target, TimingPostProcessorAssociationEventArgs request)
    {
        var model = target.ViewModel.TimingPostProcessor;
        model.IsBusy = true;
        target.ShowError(null);
        var saved = false;
        try
        {
            await applicationContext.RunStyleOperationAsync(() =>
                applicationContext.StyleLibrary.SetTimingPostProcessorAsync(request.StyleIds, request.Options));
            saved = true;
        }
        catch (Exception error)
        {
            if (ReferenceEquals(Window, target))
            {
                target.ShowError(Localization.Format("Settings.TimingAssociationFailed", error.Message));
            }
        }
        finally
        {
            model.IsBusy = false;
            if (ReferenceEquals(Window, target))
            {
                RefreshTimingStyles();
                if (saved)
                {
                    if (request.Options is null)
                    {
                        model.ShowUnlinked(request.StyleIds.Count);
                    }
                    else
                    {
                        model.ShowResult(request.StyleIds.Count);
                    }
                }
            }
        }
    }

    private void OnShortcutsChanged(object? sender, SettingsShortcutsChangedEventArgs e)
    {
        UpdatePreferences(value => value with { ShortcutBindings = e.Bindings });
    }

    private void UpdatePreferences(Func<WorkbenchPreferences, WorkbenchPreferences> update)
    {
        _ = RunAsync(() =>
        {
            applicationContext.UpdatePreferences(update);
            return Task.CompletedTask;
        });
    }

    private void OnPreviewDecodeModeChanged(object? sender, SettingsPreviewDecodeModeChangedEventArgs e)
    {
        var active = session;
        var targetWindow = Window;
        _ = RunAsync(async () =>
        {
            if (active is null)
            {
                applicationContext.UpdatePreferences(value => value with { PreviewDecodeMode = e.Mode });
            }
            else if (!await active.SetPreviewDecodeModeAsync(e.Mode) && active.LastError is { } error)
            {
                if (ReferenceEquals(Window, targetWindow))
                {
                    targetWindow?.ShowError(error.Message);
                }
            }

            RefreshMediaSettings();
        });
    }

    private void OnAudioCalibrationChanged(object? sender, SettingsAudioCalibrationChangedEventArgs e)
    {
        var active = session;
        _ = RunAsync(async () =>
        {
            if (active is null)
            {
                throw new InvalidOperationException("音频校准需要已打开媒体的工作台。");
            }
            await active.SetAudioCalibrationAsync(e.Calibration);
            RefreshMediaSettings();
        });
    }

    private async Task<bool> RunAsync(Func<Task> operation)
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

            if (targetSession is { IsClosing: false })
            {
                targetSession.ShowError(error);
            }
        }
        return false;
    }

}
