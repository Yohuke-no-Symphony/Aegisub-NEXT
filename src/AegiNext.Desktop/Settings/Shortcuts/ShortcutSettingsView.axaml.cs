using System.ComponentModel;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Shortcuts;

/// <summary>按键录入的局部适配，不持有工程或全局输入路由。</summary>
public sealed partial class ShortcutSettingsView : UserControl, IWorkbenchLocalKeyboardTarget
{
    private readonly HashSet<Key> capturedKeys = [];
    private readonly ShortcutGestureDisplay gestureInput;
    private ShortcutSettingsViewModel? model;
    private Window? window;

    /// <summary>先隔离父级上下文，再加载编译绑定并接线局部键盘输入。</summary>
    public ShortcutSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        gestureInput = this.FindControl<ShortcutGestureDisplay>("GestureInput")!;
        DataContextChanged += (_, _) => ChangeModel();
        AddHandler(KeyDownEvent, RecordShortcut, RoutingStrategies.Tunnel);
        gestureInput.AddHandler(TextInputEvent, RejectTextInput, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => AttachWindow();
        DetachedFromVisualTree += (_, _) => DetachWindow();
    }

    bool IWorkbenchLocalKeyboardTarget.OwnsLocalKey(Key key, KeyModifiers modifiers, IInputElement focusedElement)
    {
        return ReferenceEquals(focusedElement, gestureInput);
    }

    private void ChangeModel()
    {
        if (model is not null)
        {
            model.PropertyChanged -= ModelChanged;
            model.IsWaitingForKeyRelease = false;
        }

        capturedKeys.Clear();
        model = DataContext as ShortcutSettingsViewModel;
        if (model is not null)
        {
            model.PropertyChanged += ModelChanged;
        }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutSettingsViewModel.IsRecording) && model?.IsRecording == true)
        {
            gestureInput.Focus();
        }
    }

    private void FocusGesture(object? sender, RoutedEventArgs e)
    {
        if (model?.IsRecording == true)
        {
            gestureInput.Focus();
        }
    }

    private void RecordShortcut(object? sender, KeyEventArgs e)
    {
        if (capturedKeys.Contains(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (model?.IsRecording != true)
        {
            return;
        }

        e.Handled = true;
        capturedKeys.Add(e.Key);
        model.IsWaitingForKeyRelease = true;
        if (e.Key == Key.Escape)
        {
            model.IsRecording = false;
            return;
        }

        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        try
        {
            model.CaptureGesture(ShortcutConfiguration.FormatGesture(e.Key, e.KeyModifiers));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or FormatException)
        {
            model.RejectGesture();
        }
    }

    private void ReleaseShortcut(object? sender, KeyEventArgs e)
    {
        if (capturedKeys.Remove(e.Key))
        {
            e.Handled = true;
            if (model is not null)
            {
                model.IsWaitingForKeyRelease = capturedKeys.Count != 0;
            }
        }
    }

    private static void RejectTextInput(object? sender, TextInputEventArgs e)
    {
        e.Handled = true;
    }

    private void AttachWindow()
    {
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is not null)
        {
            window.Deactivated += OnWindowDeactivated;
            window.AddHandler(KeyUpEvent, ReleaseShortcut, RoutingStrategies.Tunnel, true);
        }
    }

    private void DetachWindow()
    {
        if (window is not null)
        {
            window.Deactivated -= OnWindowDeactivated;
            window.RemoveHandler(KeyUpEvent, ReleaseShortcut);
            window = null;
        }

        ResetCapture();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        ResetCapture();
    }

    private void ResetCapture()
    {
        capturedKeys.Clear();
        if (model is not null)
        {
            model.IsRecording = false;
            model.IsWaitingForKeyRelease = false;
        }
    }
}
