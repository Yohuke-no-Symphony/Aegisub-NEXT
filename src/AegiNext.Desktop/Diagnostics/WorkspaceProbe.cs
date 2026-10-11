using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Controls;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class WorkspaceProbe
{
    private const int MAC_OS_FOCUS_TIMEOUT_MILLISECONDS = 5000;
    private const int MAC_OS_FOCUS_POLL_MILLISECONDS = 25;
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private readonly WorkspaceProbeOptions options;
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly WorkspaceProbeReport report = new();
    private readonly EventHandler<FirstChanceExceptionEventArgs> captureBindingException;

    internal WorkspaceProbe(WorkspaceProbeOptions options, IClassicDesktopStyleApplicationLifetime desktop)
    {
        this.options = options;
        this.desktop = desktop;
        WorkspaceProbeProfile.Prepare(options);
        report.SourceProfileCopied = options.SourceProfileDirectory is not null;
        captureBindingException = OnBindingException;
        AppDomain.CurrentDomain.FirstChanceException += captureBindingException;
        var previous = Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY", options.ProfileDirectory);
            MainWindow = new();
        }
        catch
        {
            AppDomain.CurrentDomain.FirstChanceException -= captureBindingException;
            throw;
        }
        finally
        {
            Environment.SetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY", previous);
        }
        MainWindow.Opened += OnOpened;
    }

    internal MainWindow MainWindow { get; }

    private async void OnOpened(object? sender, EventArgs args)
    {
        try
        {
            await SettleAsync();
            await MainWindow.Session.Styles.Completion;
            report.LoadedStylePresetCount = MainWindow.Session.StyleLibrary.Snapshot.Presets.Length;
            await MainWindow.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SETTINGS);
            await SettleAsync();
            var settings = MainWindow.WindowRegistry.Windows.OfType<SettingsWindow>().SingleOrDefault();
            if (settings is null)
            {
                throw new InvalidOperationException("Settings did not open: " +
                    (MainWindow.Session.LastError?.ToString() ?? MainWindow.ViewModel.Error ?? "No settings host was registered."));
            }
            Verify(settings.IsVisible, "Settings opened after loading the isolated preferences and style library");
            foreach (var page in Enum.GetValues<SettingsPage>())
            {
                settings.SelectPage(page);
                await SettleAsync();
                Verify(settings.CurrentPage == page && settings.IsVisible, $"Settings page displayed: {page}");
            }
            settings.Close();
            await SettleAsync();
            Verify(report.SettingsBindingExceptionCount == 0, "Settings compiled bindings raised no InvalidCastException");
            Capture("standard");
            foreach (var id in new[] { WorkbenchPanelIds.PREVIEW, WorkbenchPanelIds.TIMELINE, WorkbenchPanelIds.SUBTITLES, WorkbenchPanelIds.STYLES })
            {
                Verify(MainWindow.Panels[id].IsAttachedToVisualTree(), $"Standard panel attached: {id}");
            }
            var log = MainWindow.Panels[WorkbenchPanelIds.LOG];
            var logOwner = (Dock.Model.Core.IDock)MainWindow.Layouts.PanelAdapters[WorkbenchPanelIds.LOG].Owner!;
            var subtitlePanel = MainWindow.Layouts.PanelAdapters[WorkbenchPanelIds.SUBTITLES];
            Verify(MainWindow.Panels.Count == WorkbenchPanelIds.All.Count && WorkbenchPanelIds.All.Count == 8,
                "All eight stable panel views were registered");
            Verify(MainWindow.Layouts.IsVisible(WorkbenchPanelIds.LOG) && !IsDisplayed(log) &&
                   ReferenceEquals(logOwner.ActiveDockable, subtitlePanel),
                "Standard layout kept the Log tab inactive");
            MainWindow.Session.Journal.Clear();
            MainWindow.Session.LogInfo("Workspace probe", "Native Log activation check");
            MainWindow.Session.LogError("Workspace probe", new InvalidOperationException("Native unread error check"));
            await SettleAsync();
            Verify(!IsDisplayed(log) && ReferenceEquals(logOwner.ActiveDockable, subtitlePanel) &&
                   MainWindow.ViewModel.Log.UnreadErrorCount == 1,
                "New log entries retained the active work tab and marked one unread error");
            await MainWindow.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_LOG);
            await SettleAsync();
            Verify(IsDisplayed(log) && ReferenceEquals(logOwner.ActiveDockable, MainWindow.Layouts.PanelAdapters[WorkbenchPanelIds.LOG]) &&
                   MainWindow.ViewModel.Log.UnreadErrorCount == 0,
                "The shared Log command activated its existing view and marked visible errors read");
            Capture("log-active");
            MainWindow.Session.Journal.Clear();
            Verify(await MainWindow.Layouts.ApplyPresetAsync(WorkspaceLayoutPresets.STANDARD), "Restored Standard after the Log activation check");
            await SettleAsync();
            var panelInstances = MainWindow.Panels.ToDictionary(pair => pair.Key, pair => pair.Value);
            var controller = MainWindow.Session.Controller;
            foreach (var id in new[] { WorkspaceLayoutPresets.TIMING, WorkspaceLayoutPresets.EFFECTS, WorkspaceLayoutPresets.ENCODE, WorkspaceLayoutPresets.STANDARD })
            {
                Verify(await MainWindow.Layouts.ApplyPresetAsync(id), $"Applied preset: {id}");
                await SettleAsync();
                if (id == WorkspaceLayoutPresets.ENCODE)
                {
                    CaptureExportChoices();
                }
                Capture(id);
            }
            Verify(panelInstances.All(pair => ReferenceEquals(pair.Value, MainWindow.Panels[pair.Key])) &&
                   ReferenceEquals(controller, MainWindow.Session.Controller), "All eight views and the media controller retained their identity");
            Verify(!MainWindow.Session.Editor.CanUndo, "Layout changes did not add an undo entry");
            await VerifySubtitleDetailsAsync();
            var preset = await MainWindow.Layouts.SaveAsAsync("Runtime verification");
            Verify(preset is not null, "Saved a personal preset");
            MainWindow.Layouts.Hide(WorkbenchPanelIds.EXPORT);
            await MainWindow.Layouts.FlushAsync();
            var stored = new WorkspaceLayoutStore(options.ProfileDirectory).Load();
            Verify(stored.Current.HiddenPanelIds.Contains(WorkbenchPanelIds.EXPORT) &&
                   !stored.Presets.Single().Layout.HiddenPanelIds.Contains(WorkbenchPanelIds.EXPORT), "Automatic layout memory did not overwrite the named preset");
            MainWindow.Layouts.Float(WorkbenchPanelIds.PREVIEW);
            await SettleAsync();
            var floating = MainWindow.Layouts.FloatingWindows.Single();
            Verify(MainWindow.Panels[WorkbenchPanelIds.PREVIEW].IsAttachedToVisualTree(), "Preview view attached to its real floating host");
            await MainWindow.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SETTINGS);
            await SettleAsync();
            Capture("system-menu-three-hosts");
            MainWindow.Session.UpdatePreferences(MainWindow.Session.Preferences with { WindowMenuOnMac = true, Language = "en-US" });
            await SettleAsync();
            Capture("window-menu-three-hosts");
            Verify(MainWindow.WindowRegistry.Windows.All(window => (ReferenceEquals(window, MainWindow) && FindTitleBar(window).MenuContent is not null) || (!ReferenceEquals(window, MainWindow) && FindTitleBar(window).MenuContent is null)), "Window menu belongs only to main workbench");
            await VerifyActualMacOsMenusAsync(true);
            MainWindow.Session.UpdatePreferences(MainWindow.Session.Preferences with { WindowMenuOnMac = false, Language = "zh-CN" });
            await SettleAsync();
            Capture("system-menu-restored");
            if (OperatingSystem.IsMacOS())
            {
                Verify(MainWindow.WindowRegistry.Windows.All(window => NativeMenu.GetIsNativeMenuExported(window) && FindTitleBar(window).MenuContent is null), "Native system menus restored on all macOS hosts");
            }
            await VerifyActualMacOsMenusAsync(false);
            floating.Close();
            await SettleAsync();
            Verify(MainWindow.IsVisible && !MainWindow.Layouts.IsVisible(WorkbenchPanelIds.PREVIEW), "Closing the floating host only hid its panel");
            await MainWindow.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_PREVIEW);
            await SettleAsync();
            Verify(MainWindow.Layouts.IsVisible(WorkbenchPanelIds.PREVIEW), "Shared view command reopened the preview panel");
            Verify(await MainWindow.Layouts.RenameAsync(preset!, "Renamed verification") && await MainWindow.Layouts.DeleteAsync(preset!), "Renamed and deleted the personal preset");
            await MainWindow.Layouts.RestoreDefaultAsync();
            await MainWindow.Layouts.FlushAsync();
            Capture("final-standard");
            Verify(report.ExportBindingExceptionCount == 0, "Export compiled bindings raised no InvalidCastException");
            Verify(report.GetBindingExceptionCount == 0, "Binding evaluation raised no GetBinding exception");
            report.Completed = true;
        }
        catch (Exception error)
        {
            report.Failures.Add(error.ToString());
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= captureBindingException;
            try
            {
                await MainWindow.DisposeAsync();
                MainWindow.Close();
                Verify(!MainWindow.IsVisible && MainWindow.WindowRegistry.Windows.Count == 0, "All registered windows were closed and released");
            }
            catch (Exception error)
            {
                report.Failures.Add(error.ToString());
            }
            Directory.CreateDirectory(Path.GetDirectoryName(options.ReportPath)!);
            await File.WriteAllBytesAsync(options.ReportPath, JsonSerializer.SerializeToUtf8Bytes(report, jsonOptions));
            Directory.Delete(options.ProfileDirectory, true);
            desktop.Shutdown(report.Completed && report.Failures.Count == 0 ? 0 : 1);
        }
    }

    private async Task VerifySubtitleDetailsAsync()
    {
        var previous = MainWindow.Session.Editor.Snapshot;
        var cue = new SubtitleLine { Text = "Native detail", End = new(4), Style = new() { FontSize = 32 } };
        var document = previous with
        {
            Subtitles = [cue],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End }]
        };
        try
        {
            MainWindow.Session.Editor.Reset(document);
            MainWindow.Session.SelectCue(cue.Id);
            await MainWindow.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
            await SettleAsync();
            var panel = MainWindow.Panels[WorkbenchPanelIds.SUBTITLE_DETAILS];
            var host = MainWindow.Layouts.FloatingWindows.Single();
            Verify(panel.IsAttachedToVisualTree() && ReferenceEquals(TopLevel.GetTopLevel(panel), host),
                "Subtitle detail command opened one real floating child window");
            var rich = panel.GetVisualDescendants().OfType<RichSubtitleEditor>().Single(control => control.Name == "RichSubtitleInput");
            rich.Focus();
            rich.SetSelection(0, cue.Text.Length);
            rich.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "你好👩‍💻" });
            var state = panel.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "SubtitleVisualStateInput");
            state.SelectedIndex = 1;
            await SettleAsync();
            Verify(MainWindow.Session.Editor.Snapshot.Subtitles[0].Text == "你好👩‍💻" && rich.RenderDiagnostic is null,
                "Native detail rich input rendered and committed Chinese with a ZWJ emoji");
            var committed = MainWindow.Session.Editor.Snapshot;
            Verify(!panel.GetVisualDescendants().OfType<Control>().Any(control => control.Name == "SubtitleCodeInput"),
                "Subtitle details exposes native visual editing without an ASS source page");
            state.SelectedIndex = 2;
            await SettleAsync();
            state.SelectedIndex = 0;
            await SettleAsync();
            Verify(ReferenceEquals(committed, MainWindow.Session.Editor.Snapshot) && state.SelectedIndex == 0,
                "Visual state previews preserved content without new transactions");
            Capture("subtitle-details-floating");
            var source = MainWindow.Layouts.PanelAdapters[WorkbenchPanelIds.SUBTITLE_DETAILS];
            var target = MainWindow.Layouts.PanelAdapters[WorkbenchPanelIds.SUBTITLES];
            var factory = ((DockControl)MainWindow.Layouts.Host).Factory!;
            factory.MoveDockable((Dock.Model.Core.IDock)source.Owner!, (Dock.Model.Core.IDock)target.Owner!, source, target);
            MainWindow.Layouts.Activate(WorkbenchPanelIds.SUBTITLE_DETAILS);
            await SettleAsync();
            Verify(ReferenceEquals(TopLevel.GetTopLevel(panel), MainWindow),
                "Floating subtitle detail docked into the main workspace with its original view");
            await MainWindow.Layouts.FlushAsync();
            var stored = new WorkspaceLayoutStore(options.ProfileDirectory).Load();
            Verify(ContainsPanel(stored.Current.Main, WorkbenchPanelIds.SUBTITLE_DETAILS) &&
                !stored.Current.Floating.Any(window => ContainsPanel(window.Content, WorkbenchPanelIds.SUBTITLE_DETAILS)),
                "Layout persistence retained subtitle detail as a docked child panel");
            Capture("subtitle-details-docked");
            MainWindow.Layouts.Hide(WorkbenchPanelIds.SUBTITLE_DETAILS);
            await MainWindow.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
            await SettleAsync();
            Verify(ReferenceEquals(panel, MainWindow.Panels[WorkbenchPanelIds.SUBTITLE_DETAILS]) && MainWindow.Layouts.IsVisible(WorkbenchPanelIds.SUBTITLE_DETAILS),
                "Closing and reopening subtitle detail reused its controls and editor state");
        }
        finally
        {
            MainWindow.Session.Details.Restore("All");
            MainWindow.Session.Details.Restore("Duration");
            MainWindow.Session.Details.Restore("LeadingDelay");
            MainWindow.Session.Editor.Reset(previous);
            await MainWindow.Layouts.RestoreDefaultAsync();
            await SettleAsync();
        }
    }

    private void OnBindingException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return;
        }

        var error = args.Exception;
        var settingsBinding = error is InvalidCastException && error.Message.Contains("AegiNext.Desktop.Settings.", StringComparison.Ordinal);
        var exportBinding = error is InvalidCastException && error.Message.Contains("AegiNext.Desktop.Panels.Export.", StringComparison.Ordinal);
        var getBinding = error.StackTrace?.Contains("GetBinding", StringComparison.Ordinal) == true;
        if (settingsBinding)
        {
            report.SettingsBindingExceptionCount++;
        }
        if (exportBinding)
        {
            report.ExportBindingExceptionCount++;
        }
        if (getBinding)
        {
            report.GetBindingExceptionCount++;
        }
        if (settingsBinding || exportBinding || getBinding)
        {
            report.Failures.Add(error.ToString());
        }
    }

    private void CaptureExportChoices()
    {
        var export = MainWindow.Panels[WorkbenchPanelIds.EXPORT];
        report.ExportCodecChoiceCount = export.FindControl<ComboBox>("CodecCombo")!.ItemCount;
        report.ExportSpeedChoiceCount = export.FindControl<ComboBox>("SpeedCombo")!.ItemCount;
        report.ExportAudioModeChoiceCount = export.FindControl<ComboBox>("AudioModeCombo")!.ItemCount;
        Verify(report.ExportCodecChoiceCount == 6 && report.ExportSpeedChoiceCount == 9 && report.ExportAudioModeChoiceCount == 4,
            $"Export choices were populated: codec={report.ExportCodecChoiceCount}, speed={report.ExportSpeedChoiceCount}, audio={report.ExportAudioModeChoiceCount}");
    }

    private void Capture(string action)
    {
        var host = (DockControl)MainWindow.Layouts.Host;
        var visuals = host.GetVisualDescendants().OfType<Control>().ToArray();
        report.DockSamples.Add(new(action, host.IsAttachedToVisualTree(), host.IsEffectivelyVisible,
            host.Bounds.ToString(), host.Layout?.GetType().Name, host.Layout?.ActiveDockable?.GetType().Name,
            host.DataContext?.GetType().Name, visuals.OfType<RootDockControl>().Count(),
            visuals.OfType<ToolDockControl>().Count(),
            host.GetVisualAncestors().OfType<Control>().Select(control =>
                $"{control.GetType().Name}/{control.Name}: visible={control.IsVisible}, bounds={control.Bounds}").ToArray(),
            visuals.Take(80).Select(control =>
                $"{control.GetType().Name}/{control.Name}: visible={control.IsVisible}, bounds={control.Bounds}, data={control.DataContext?.GetType().Name}").ToArray(),
            MainWindow.Panels.Select(pair =>
                $"{pair.Key}: attached={pair.Value.IsAttachedToVisualTree()}, visible={pair.Value.IsEffectivelyVisible}, bounds={pair.Value.Bounds}, parent={pair.Value.GetVisualParent()?.GetType().Name}, data={pair.Value.DataContext?.GetType().Name}").ToArray()));
        foreach (var window in MainWindow.WindowRegistry.Windows.Where(window => window.IsVisible))
        {
            var titleBar = FindTitleBar(window);
            int? buttons = null;
            if (OperatingSystem.IsMacOS() && window.TryGetPlatformHandle() is IPlatformHandle { HandleDescriptor: "NSWindow", Handle: not 0 } handle)
            {
                buttons = MacOsCaptionButtons.CountVisible(handle.Handle);
                Verify(buttons == 3, $"Three native traffic lights visible: {action}/{window.GetType().Name}");
            }
            report.Samples.Add(new(action, window.GetType().Name, window.Title ?? string.Empty, window.ClientSize.Width,
                window.ClientSize.Height, window.IsExtendedIntoWindowDecorations, window.WindowDecorations.ToString(),
                titleBar.MenuContent is not null, NativeMenu.GetIsNativeMenuExported(window), titleBar.CaptionInsets.Left,
                titleBar.CaptionInsets.Right, buttons, MainWindow.Layouts.CurrentPresetId, MainWindow.Layouts.IsModified));
        }
    }

    private void Verify(bool condition, string description)
    {
        (condition ? report.Checks : report.Failures).Add(description);
    }

    private async Task VerifyActualMacOsMenusAsync(bool windowMenuMode)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var hosts = MainWindow.WindowRegistry.Windows.Where(window => window.IsVisible).ToArray();
        var roots = hosts.ToDictionary(window => window, NativeMenu.GetMenu);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            foreach (var host in hosts)
            {
                host.Activate();
                var action = $"focus-{cycle + 1}";
                await WaitForMacOsKeyWindowAsync(action, host, windowMenuMode, roots);
                CaptureActualMacOsMenu(action, host, windowMenuMode, roots);
                if (cycle == 2)
                {
                    await Task.Delay(1000);
                    CaptureActualMacOsMenu(action + "-idle", host, windowMenuMode, roots);
                }
            }
        }

        var dialog = new UnsavedProjectDialog();
        Task<int>? dialogCompletion = null;
        try
        {
            MainWindow.WindowRegistry.RegisterAuxiliary(dialog);
            roots.Add(dialog, NativeMenu.GetMenu(dialog));
            dialogCompletion = dialog.ShowDialog<int>(MainWindow);
            await WaitForMacOsKeyWindowAsync("modal-focused", dialog, windowMenuMode, roots);
            Verify(dialog.IsDialog && dialog.IsVisible, "Actual unsaved-project modal dialog opened");
            CaptureActualMacOsMenu("modal-focused", dialog, windowMenuMode, roots);
            await Task.Delay(1000);
            CaptureActualMacOsMenu("modal-focused-idle", dialog, windowMenuMode, roots);
        }
        finally
        {
            if (dialog.IsVisible)
            {
                dialog.Close(0);
            }

            if (dialogCompletion is not null)
            {
                await dialogCompletion;
            }
        }

        roots.Remove(dialog);
        MainWindow.Activate();
        await WaitForMacOsKeyWindowAsync("main-after-modal", MainWindow, windowMenuMode, roots);
        await Task.Delay(1000);
        CaptureActualMacOsMenu("main-idle-after-modal", MainWindow, windowMenuMode, roots);
        Verify(!MainWindow.WindowRegistry.Windows.Contains(dialog), "Modal host was unregistered after close");
    }

    private async Task WaitForMacOsKeyWindowAsync(string action, Window host, bool windowMenuMode,
        IReadOnlyDictionary<Window, NativeMenu?> roots)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (host.TryGetPlatformHandle() is not IPlatformHandle { HandleDescriptor: "NSWindow", Handle: not 0 } handle)
        {
            throw new InvalidOperationException("The focus probe requires a real macOS NSWindow.");
        }

        var timer = Stopwatch.StartNew();
        nint observed = 0;
        var polls = 0;
        while (timer.ElapsedMilliseconds < MAC_OS_FOCUS_TIMEOUT_MILLISECONDS)
        {
            observed = MacOsActualMenuSnapshot.ReadKeyWindowHandle();
            polls++;
            if (observed == handle.Handle)
            {
                break;
            }

            await Task.Delay(MAC_OS_FOCUS_POLL_MILLISECONDS);
        }

        var matched = observed == handle.Handle;
        report.MacOsFocusWaits.Add(new(action, host.GetType().Name, windowMenuMode, (long)handle.Handle,
            (long)observed, timer.ElapsedMilliseconds, polls, matched));
        if (!matched)
        {
            var rootsPreserved = roots.All(pair => ReferenceEquals(pair.Value, NativeMenu.GetMenu(pair.Key)));
            var sample = MacOsActualMenuSnapshot.Capture(action + "-focus-timeout", host.GetType().Name,
                windowMenuMode, rootsPreserved) with { RequestedKeyWindowHandle = (long)handle.Handle };
            report.ActualMacOsMenus.Add(sample);
            throw new TimeoutException($"Native keyWindow did not reach {host.GetType().Name} within " +
                                       $"{MAC_OS_FOCUS_TIMEOUT_MILLISECONDS} ms: expected {handle.Handle}, observed {observed}.");
        }

        await Dispatcher.UIThread.InvokeAsync(host.UpdateLayout, DispatcherPriority.Background);
    }

    private void CaptureActualMacOsMenu(string action, Window host, bool windowMenuMode,
        IReadOnlyDictionary<Window, NativeMenu?> roots)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var rootsPreserved = roots.All(pair => ReferenceEquals(pair.Value, NativeMenu.GetMenu(pair.Key)));
        if (host.TryGetPlatformHandle() is not IPlatformHandle { HandleDescriptor: "NSWindow", Handle: not 0 } handle)
        {
            throw new InvalidOperationException("The menu probe requires a real macOS NSWindow.");
        }

        var sample = MacOsActualMenuSnapshot.Capture(action, host.GetType().Name, windowMenuMode, rootsPreserved)
            with { RequestedKeyWindowHandle = (long)handle.Handle };
        report.ActualMacOsMenus.Add(sample);
        var description = $"{action}/{host.GetType().Name}/window-menu={windowMenuMode}";
        Verify(sample.MainMenuHandle != 0 && sample.Items.Length > 0,
            $"NSApplication actual mainMenu retained application items: {description}");
        Verify(sample.KeyWindowHandle == sample.RequestedKeyWindowHandle && sample.KeyWindowTitle == host.Title,
            $"Native keyWindow matched the requested focus host: {description}");
        Verify(sample.ManagedRootsPreserved, $"Managed native menu roots survived focus: {description}");
        var titles = sample.Items.Select(item => item.Title).ToArray();
        foreach (var group in WorkbenchMenuCatalog.Groups)
        {
            var label = Localization.Get("Workbench." + (group.Key));
            Verify(titles.Contains(label, StringComparer.Ordinal) != windowMenuMode,
                $"Actual native workbench group '{label}' matched the selected menu mode: {description}");
        }
    }

    private static bool IsDisplayed(Control control) => control.IsAttachedToVisualTree() && control.IsEffectivelyVisible &&
        control.Bounds.Width > 0 && control.Bounds.Height > 0;

    private static bool ContainsPanel(LayoutNodeSnapshot node, string panelId)
    {
        return node.PanelId == panelId || node.Children.Any(child => ContainsPanel(child, panelId));
    }

    private async Task SettleAsync()
    {
        await Task.Delay(200);
        await Dispatcher.UIThread.InvokeAsync(MainWindow.UpdateLayout, DispatcherPriority.Render);
    }

    private static WindowTitleBar FindTitleBar(Window window) => window.GetVisualDescendants().OfType<WindowTitleBar>().Single();
}
