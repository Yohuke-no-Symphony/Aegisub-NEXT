using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Windowing;

internal sealed class WorkbenchWindowRegistry : IDisposable
{
    private readonly WorkbenchMenuCatalog catalog;
    private readonly Action invalidateTiming;
    private readonly Action? cancelGestures;
    private readonly WorkbenchApplicationMenu? applicationMenu;
    private readonly Dictionary<Window, WorkbenchWindowEntry> windows = [];
    private readonly Dictionary<WorkbenchCommand, Bitmap> icons = [];
    private WorkbenchPreferences preferences = new();
    private ShortcutRouter router = new(ShortcutDefaults.CreateBindings());
    private bool disposed;
    private bool focusRefreshPending;

    internal WorkbenchWindowRegistry(WorkbenchMenuCatalog catalog, Action invalidateTiming, Action? cancelGestures = null,
        bool includeApplicationMenu = true)
    {
        this.catalog = catalog;
        this.invalidateTiming = invalidateTiming;
        this.cancelGestures = cancelGestures;
        if (includeApplicationMenu && OperatingSystem.IsMacOS() && Avalonia.Application.Current is { } application)
        {
            applicationMenu = new(application, catalog);
        }
        catalog.Changed += OnCatalogChanged;
        Localization.LanguageChanged += OnLanguageChanged;
    }

    internal IReadOnlyCollection<Window> Windows => windows.Keys;
    internal event EventHandler? FocusCommandContextChanged;

    internal void Register(Window window, Func<string> titleProvider, WindowTitleBar? titleBar = null,
        WorkbenchWindowRole role = WorkbenchWindowRole.MAIN)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (windows.ContainsKey(window))
        {
            return;
        }

        if (!window.Classes.Contains("business-surface"))
        {
            window.Classes.Add("business-surface");
        }

        if (titleBar is null)
        {
            titleBar = new();
            var root = new DockPanel();
            DockPanel.SetDock(titleBar, Avalonia.Controls.Dock.Top);
            root.Children.Add(titleBar);
            if (window.Content is Control content)
            {
                window.Content = null;
                root.Children.Add(content);
            }

            window.Content = root;
        }

        var native = new WorkbenchNativeMenu(WorkbenchMenuCatalog.Groups, catalog.GetCommand, GetIcon);
        NativeMenu.SetMenu(window, native.Menu);
        var entry = new WorkbenchWindowEntry(window, titleBar, WindowChrome.Attach(window, titleBar),
            new(catalog), native, titleProvider, [], role);
        windows.Add(window, entry);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.TextInputEvent, OnText, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.GotFocusEvent, OnFocusChanged, RoutingStrategies.Bubble, true);
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;
        window.Closed += OnClosed;
        window.PropertyChanged += OnWindowChanged;
        titleBar.SizeChanged += OnTitleBarSizeChanged;
        titleBar.PropertyChanged += OnTitleBarPropertyChanged;
        Apply(entry);
    }

    internal void UpdatePreferences(WorkbenchPreferences value)
    {
        preferences = value;
        router = new(value.ShortcutBindings);
        catalog.Update(value);
        foreach (var entry in windows.Values)
        {
            Apply(entry);
        }
    }

    internal void RegisterAuxiliary(Window window)
    {
        var host = window as IWindowTitleBarHost;
        host?.ReleaseStandaloneChrome();
        Register(window, () => window.Title ?? string.Empty, host?.TitleBar, WorkbenchWindowRole.AUXILIARY);
    }

    internal void RefreshTitles()
    {
        foreach (var entry in windows.Values)
        {
            entry.Window.SetCurrentValue(Window.TitleProperty, entry.TitleProvider());
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        catalog.Changed -= OnCatalogChanged;
        applicationMenu?.Dispose();
        foreach (var window in windows.Keys.ToArray())
        {
            Unregister(window);
        }

        foreach (var icon in icons.Values)
        {
            icon.Dispose();
        }

        icons.Clear();
    }

    private void Apply(WorkbenchWindowEntry entry)
    {
        entry.Window.SetCurrentValue(Window.TitleProperty, entry.TitleProvider());
        entry.Window.RequestedThemeVariant = preferences.Theme switch
        {
            WorkbenchTheme.LIGHT => ThemeVariant.Light, WorkbenchTheme.DARK => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        var prefersWindowMenu = !OperatingSystem.IsMacOS() || preferences.WindowMenuOnMac;
        entry.TitleBar.MenuContent = prefersWindowMenu && entry.Role == WorkbenchWindowRole.MAIN ? entry.MenuBar : null;
        entry.NativeMenu.SetEnabled(!OperatingSystem.IsMacOS() || !prefersWindowMenu);
        entry.NativeMenu.Update(key => Localization.Get("Workbench." + key), catalog.GetDisplayLabel, catalog.GetGestureLabel);
        entry.NativeMenu.UpdateLayouts(catalog.LayoutChoices);
        UpdateMenuWidth(entry);
    }

    private static void UpdateMenuWidth(WorkbenchWindowEntry entry)
    {
        entry.MenuBar.SetAvailableWidth(entry.TitleBar.AvailableMenuWidth);
    }

    private Bitmap GetIcon(WorkbenchCommand command)
    {
        if (!icons.TryGetValue(command, out var value))
        {
            value = WorkbenchIcon.CreateNative(command.ToString());
            icons.Add(command, value);
        }

        return value;
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        foreach (var entry in windows.Values)
        {
            Apply(entry);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        catalog.RefreshLanguage();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Window window || !windows.TryGetValue(window, out var entry))
        {
            return;
        }

        if (entry.PressedKeys.Contains(e.Key))
        {
            e.Handled = true;
            return;
        }

        var visual = e.Source as Visual;
        if (e.Handled || window.IsDialog || window is SettingsWindow { IsShortcutCaptureActive: true } ||
            HasOpenKeyboardSurface(window, visual))
        {
            return;
        }

        var focusedElement = window.FocusManager.GetFocusedElement();
        if (focusedElement is Visual focused && focused.GetSelfAndVisualAncestors().OfType<IWorkbenchLocalKeyboardTarget>()
            .Any(target => target.OwnsLocalKey(e.Key, e.KeyModifiers, focusedElement)))
        {
            return;
        }

        var textInput = visual is TextBox or AegiNext.Desktop.Controls.RichSubtitleEditor || visual?.GetVisualAncestors().Any(value => value is TextBox or AegiNext.Desktop.Controls.RichSubtitleEditor) == true;
        if (!router.TryResolve(e.Key, e.KeyModifiers, textInput, out var id))
        {
            if (e.Key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
                Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin))
            {
                invalidateTiming();
            }

            return;
        }

        if (id is WorkbenchCommand.AUDITION_BEFORE_SUBTITLE or WorkbenchCommand.AUDITION_AFTER_SUBTITLE or
            WorkbenchCommand.AUDITION_SUBTITLE_BEGIN or WorkbenchCommand.AUDITION_SUBTITLE or
            WorkbenchCommand.ADVANCE_SUBTITLE_ROW or WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK)
        {
            invalidateTiming();
            if (TryExecuteFocusCommand(id, window))
            {
                e.Handled = true;
                entry.PressedKeys.Add(e.Key);
            }
            return;
        }

        if (id is WorkbenchCommand.COPY_CLIPS or WorkbenchCommand.PASTE_CLIPS or WorkbenchCommand.DELETE_SUBTITLE or WorkbenchCommand.MERGE_SUBTITLE)
        {
            if (TryExecuteFocusCommand(id, window))
            {
                invalidateTiming();
                e.Handled = true;
                entry.PressedKeys.Add(e.Key);
                return;
            }
            if (id is WorkbenchCommand.COPY_CLIPS or WorkbenchCommand.PASTE_CLIPS)
            {
                return;
            }
        }

        if (id == WorkbenchCommand.END_TEXT_INPUT)
        {
            if (TryExecuteFocusCommand(id, window))
            {
                invalidateTiming();
                e.Handled = true;
                entry.PressedKeys.Add(e.Key);
            }
            return;
        }

        if (id is not (WorkbenchCommand.TIMING_ENTER or WorkbenchCommand.TIMING_EXIT))
        {
            invalidateTiming();
        }

        if (e.Key is Key.Left or Key.Right && visual is Slider)
        {
            return;
        }

        e.Handled = true;
        entry.PressedKeys.Add(e.Key);
        var command = catalog.GetCommand(id);
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is Window window && windows.TryGetValue(window, out var entry))
        {
            if (entry.PressedKeys.Remove(e.Key))
            {
                e.Handled = true;
            }
        }
    }

    internal bool TryExecuteFocusCommand(WorkbenchCommand command, Window? window = null)
    {
        window ??= windows.Keys.FirstOrDefault(candidate => candidate.IsActive);
        if (window is null || !windows.ContainsKey(window) || window.IsDialog ||
            window is SettingsWindow { IsShortcutCaptureActive: true } ||
            window.FocusManager.GetFocusedElement() is not Visual focused || HasOpenKeyboardSurface(window, focused))
        {
            return false;
        }
        return TryExecuteOwnedFocusCommand(command, focused);
    }

    internal bool TryExecuteContextCommand(WorkbenchCommand command)
    {
        var focused = GetCommandFocus();
        return focused is not null && TryExecuteOwnedFocusCommand(command, focused);
    }

    private static bool TryExecuteOwnedFocusCommand(WorkbenchCommand command, Visual focused)
    {
        var target = focused.GetSelfAndVisualAncestors().OfType<IWorkbenchFocusCommandTarget>().FirstOrDefault();
        if (target is null || !target.OwnsFocusCommand(command, (IInputElement)focused))
        {
            return false;
        }
        if (!HasActiveComposition(focused) && target.CanExecuteFocusCommand(command, (IInputElement)focused))
        {
            target.TryExecuteFocusCommand(command, (IInputElement)focused);
        }
        return true;
    }

    internal bool? GetFocusCommandAvailability(WorkbenchCommand command)
    {
        var focused = GetCommandFocus();
        if (focused is null)
        {
            return null;
        }
        var target = focused.GetSelfAndVisualAncestors().OfType<IWorkbenchFocusCommandTarget>().FirstOrDefault();
        return target is not null && target.OwnsFocusCommand(command, (IInputElement)focused)
            ? !HasActiveComposition(focused) && target.CanExecuteFocusCommand(command, (IInputElement)focused) : null;
    }

    private Visual? GetCommandFocus()
    {
        var window = windows.Keys.FirstOrDefault(candidate => candidate.IsActive);
        if (window is null || window.IsDialog || !windows.TryGetValue(window, out var entry) ||
            window.FocusManager.GetFocusedElement() is not Visual focused)
        {
            return null;
        }
        return IsCommandMenuSurface(focused)
            ? entry.CommandFocus is { } previous && TopLevel.GetTopLevel(previous) == window ? previous : null
            : focused;
    }

    private static bool IsCommandMenuSurface(Visual focused) => focused.GetSelfAndVisualAncestors()
        .Any(visual => visual is MenuBase or MenuItem or WindowTitleBar or PopupRoot);

    private void OnFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        if (sender is Window window && windows.TryGetValue(window, out var entry) && e.Source is Visual focused)
        {
            if (!IsCommandMenuSurface(focused))
            {
                entry.CommandFocus = focused;
            }
            ScheduleFocusCommandRefresh();
        }
    }

    private void OnActivated(object? sender, EventArgs e) => ScheduleFocusCommandRefresh();

    private void ScheduleFocusCommandRefresh()
    {
        if (focusRefreshPending || disposed)
        {
            return;
        }
        focusRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            focusRefreshPending = false;
            if (!disposed)
            {
                FocusCommandContextChanged?.Invoke(this, EventArgs.Empty);
            }
        }, DispatcherPriority.Input);
    }

    private static bool HasActiveComposition(Visual focused)
    {
        if (focused is AegiNext.Desktop.Controls.RichSubtitleEditor rich)
        {
            return rich.Preedit.Length > 0;
        }
        return focused is TextBox text && text.GetVisualDescendants().OfType<TextPresenter>()
            .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
    }

    private static bool HasOpenKeyboardSurface(Window window, Visual? source)
    {
        if (source is PopupRoot ||
            source?.GetVisualAncestors().Any(value => value is PopupRoot) == true)
        {
            return true;
        }

        return window.GetVisualDescendants().OfType<Control>().Any(control => control switch
        {
            ComboBox { IsDropDownOpen: true } => true,
            AutoCompleteBox { IsDropDownOpen: true } => true,
            MenuBase { IsOpen: true } => true,
            Popup { IsOpen: true } => true,
            Button { Flyout.IsOpen: true } => true,
            _ => control.ContextMenu?.IsOpen == true || control.ContextFlyout?.IsOpen == true
        });
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (sender is Window window && windows.TryGetValue(window, out var entry))
        {
            entry.PressedKeys.Clear();
        }

        invalidateTiming();
    }

    private void OnPointer(object? sender, PointerPressedEventArgs e)
    {
        invalidateTiming();
        if (e.Source is Visual visual && (visual is WindowTitleBar || visual.GetVisualAncestors().Any(ancestor => ancestor is WindowTitleBar)))
        {
            cancelGestures?.Invoke();
        }
    }
    private void OnWheel(object? sender, PointerWheelEventArgs e) => invalidateTiming();
    private void OnText(object? sender, TextInputEventArgs e) => invalidateTiming();
    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            Unregister(window);
        }
    }

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Window window || !windows.TryGetValue(window, out var entry))
        {
            return;
        }

        if (e.Property == Window.ClientSizeProperty)
        {
            UpdateMenuWidth(entry);
        }

    }

    private void OnTitleBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if ((e.Property == WindowTitleBar.CaptionInsetsProperty || e.Property == WindowTitleBar.AvailableMenuWidthProperty) &&
            windows.Values.FirstOrDefault(value => ReferenceEquals(value.TitleBar, sender)) is { } entry)
        {
            UpdateMenuWidth(entry);
        }
    }

    private void OnTitleBarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (windows.Values.FirstOrDefault(value => ReferenceEquals(value.TitleBar, sender)) is { } entry)
        {
            UpdateMenuWidth(entry);
        }
    }

    private void Unregister(Window window)
    {
        if (!windows.Remove(window, out var entry))
        {
            return;
        }

        window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        window.RemoveHandler(InputElement.PointerPressedEvent, OnPointer);
        window.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
        window.RemoveHandler(InputElement.TextInputEvent, OnText);
        window.RemoveHandler(InputElement.GotFocusEvent, OnFocusChanged);
        window.Activated -= OnActivated;
        window.Deactivated -= OnDeactivated;
        window.Closed -= OnClosed;
        window.PropertyChanged -= OnWindowChanged;
        entry.TitleBar.SizeChanged -= OnTitleBarSizeChanged;
        entry.TitleBar.PropertyChanged -= OnTitleBarPropertyChanged;
        entry.MenuBar.Dispose();
        entry.Chrome.Dispose();
    }
}
