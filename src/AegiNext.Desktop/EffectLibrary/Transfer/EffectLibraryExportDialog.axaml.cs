using System.ComponentModel;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>呈现保存个人素材的可搜索勾选树，并在成功导出后结束模态操作。</summary>
public sealed partial class EffectLibraryExportDialog : Window, IWindowTitleBarHost
{
    private readonly EffectLibraryExportDialogViewModel? model;
    private readonly WindowTitleBar titleBar;
    private readonly ComboBox formatControl;
    private readonly HashSet<Key> pressedKeys = [];
    private IWindowChrome? chrome;
    private bool closed;
    private bool updatingFormat;

    /// <summary>为 XAML 加载器构造展示宿主。</summary>
    public EffectLibraryExportDialog() : this(null)
    {
    }

    internal EffectLibraryExportDialog(EffectLibraryExportDialogViewModel? model)
    {
        this.model = model;
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        titleBar = this.FindControl<WindowTitleBar>("EffectLibraryExportTitleBar")!;
        formatControl = this.FindControl<ComboBox>("EffectLibraryExportDialogFormat")!;
        formatControl.SelectionChanged += OnFormatSelectionChanged;
        chrome = WindowChrome.Attach(this, titleBar);
        if (model is not null)
        {
            model.Exported += OnExported;
            model.PropertyChanged += OnModelChanged;
            UpdateFormatControl();
        }
        Opened += OnOpened;
        Closed += OnClosed;
        Deactivated += OnDeactivated;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, true);
    }

    internal Task Completion => model?.Completion ?? Task.CompletedTask;
    internal Task DisposalCompletion => model?.DisposalCompletion ?? Task.CompletedTask;
    WindowTitleBar IWindowTitleBarHost.TitleBar => titleBar;

    void IWindowTitleBarHost.ReleaseStandaloneChrome()
    {
        chrome?.Dispose();
        chrome = null;
    }

    internal void CloseImmediately()
    {
        model?.Dispose();
        if (!closed)
        {
            Close(false);
        }
    }

    private void OnOpened(object? sender, EventArgs e) => this.FindControl<TextBox>("EffectLibraryExportSearch")!.Focus();

    private void OnExported(object? sender, EventArgs e)
    {
        if (!closed)
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => CloseImmediately();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EffectLibraryExportDialogViewModel.ExportFormats) or nameof(EffectLibraryExportDialogViewModel.SelectedFormat))
        {
            UpdateFormatControl();
        }
    }

    private void UpdateFormatControl()
    {
        if (model is null || closed)
        {
            return;
        }
        updatingFormat = true;
        try
        {
            if (!ReferenceEquals(formatControl.ItemsSource, model.ExportFormats))
            {
                formatControl.ItemsSource = model.ExportFormats;
            }
            formatControl.SelectedItem = model.ExportFormats.Single(choice => choice.Format == model.SelectedFormat);
        }
        finally
        {
            updatingFormat = false;
        }
    }

    private void OnFormatSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!updatingFormat && !closed && model?.CanInteract == true &&
            formatControl.SelectedItem is EffectLibraryExportFormatChoice choice)
        {
            model.SelectedFormat = choice.Format;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (pressedKeys.Contains(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (e.Handled || e.KeyModifiers != KeyModifiers.None || e.Key != Key.Escape || HasActiveComposition())
        {
            return;
        }
        if (formatControl.IsDropDownOpen)
        {
            pressedKeys.Add(e.Key);
            return;
        }
        pressedKeys.Add(e.Key);
        e.Handled = true;
        CloseImmediately();
    }

    private bool HasActiveComposition()
    {
        return FocusManager.GetFocusedElement() is Avalonia.Visual focused &&
            focused.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault() is { } input &&
            input.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (pressedKeys.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e) => pressedKeys.Clear();

    private void OnClosed(object? sender, EventArgs e)
    {
        closed = true;
        Opened -= OnOpened;
        Closed -= OnClosed;
        Deactivated -= OnDeactivated;
        RemoveHandler(KeyDownEvent, OnKeyDown);
        RemoveHandler(KeyUpEvent, OnKeyUp);
        formatControl.SelectionChanged -= OnFormatSelectionChanged;
        pressedKeys.Clear();
        if (model is not null)
        {
            model.Exported -= OnExported;
            model.PropertyChanged -= OnModelChanged;
            model.Dispose();
        }
        chrome?.Dispose();
        chrome = null;
    }
}
