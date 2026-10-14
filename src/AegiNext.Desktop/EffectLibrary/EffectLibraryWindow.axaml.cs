using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.EffectLibrary.Effects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AegiNext.Desktop.EffectLibrary;

/// <summary>个人素材编辑与分享的独立非模态窗口。</summary>
public sealed partial class EffectLibraryWindow : Window
{
    private bool allowClose;
    private bool preparingClose;
    private Window? pendingClosingOwner;
    private readonly EffectScriptLibraryView effectsView;
    internal Task CloseCompletion { get; private set; } = Task.CompletedTask;
    internal Task DisposalCompletion { get; private set; } = Task.CompletedTask;

    /// <summary>供 XAML 构造效果库窗口。</summary>
    public EffectLibraryWindow() : this(new WorkbenchPreferences())
    {
    }

    /// <summary>使用显式偏好创建效果库。</summary>
    public EffectLibraryWindow(WorkbenchPreferences preferences) : this(new EffectLibraryWindowViewModel(preferences), preferences)
    {
    }

    /// <summary>由宿主注入页面组合模型。</summary>
    public EffectLibraryWindow(EffectLibraryWindowViewModel viewModel, WorkbenchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        TitleBar = this.FindControl<WindowTitleBar>("EffectLibraryTitleBar")!;
        effectsView = this.FindControl<EffectScriptLibraryView>("EffectsView")!;
        viewModel.Styles.UpsertRequested += OnUpsertStyleRequested;
        viewModel.Styles.DeleteRequested += OnDeleteStyleRequested;
        viewModel.Styles.ApplyRequested += OnApplyStyleRequested;
        viewModel.Styles.CaptureRequested += OnCaptureStyleRequested;
        viewModel.Styles.ImportRequested += OnImportStylesRequested;
        viewModel.Styles.ExportRequested += OnExportStylesRequested;
        viewModel.Effects.SaveRequested += OnUpsertEffectRequested;
        viewModel.Effects.DeleteRequested += OnDeleteEffectRequested;
        viewModel.Effects.ImportRequested += OnImportEffectRequested;
        viewModel.Effects.ExportRequested += OnExportEffectRequested;
        viewModel.Effects.ValidationFailed += OnEffectValidationFailed;
        Closed += OnClosed;
        Closing += OnClosing;
        Localization.LanguageChanged += OnLanguageChanged;
        UpdatePreferences(preferences);
    }

    public event EventHandler<LibraryStyleEventArgs>? UpsertStyleRequested;
    public event EventHandler<LibraryStyleDeleteEventArgs>? DeleteStyleRequested;
    public event EventHandler? CaptureStyleRequested;
    public event EventHandler<LibraryStyleEventArgs>? ApplyStyleRequested;
    public event EventHandler? ImportStylesRequested;
    public event EventHandler<LibraryStylesExportEventArgs>? ExportStylesRequested;
    public event EventHandler<LibraryEffectEventArgs>? UpsertEffectRequested;
    public event EventHandler<LibraryEffectDeleteEventArgs>? DeleteEffectRequested;
    public event EventHandler? ImportEffectRequested;
    public event EventHandler<LibraryEffectsExportEventArgs>? ExportEffectRequested;
    public event EventHandler<EffectScriptValidationFailedEventArgs>? EffectValidationFailed;
    public EffectLibraryWindowViewModel ViewModel { get; }
    public WindowTitleBar TitleBar { get; }
    public EffectLibraryPage CurrentPage => ViewModel.CurrentPage;

    /// <summary>处理未保存草稿后选择指定页面。</summary>
    public void SelectPage(EffectLibraryPage page)
    {
        if (!Enum.IsDefined(page))
        {
            throw new ArgumentOutOfRangeException(nameof(page));
        }

        ViewModel.PageIndex = (int)page;
    }

    /// <summary>刷新页面语言，保留编辑草稿。</summary>
    public void RefreshLanguage()
    {
        ViewModel.RefreshLanguage();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLanguage();
    }

    internal void CloseImmediately()
    {
        allowClose = true;
        DisposalCompletion = effectsView.DisposePreviewAsync();
        Close();
    }

    /// <summary>呈现当前主题，保留素材草稿。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        RequestedThemeVariant = value.Theme switch
        {
            WorkbenchTheme.LIGHT => ThemeVariant.Light,
            WorkbenchTheme.DARK => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        RefreshLanguage();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (allowClose)
        {
            return;
        }
        if (ViewModel.Styles.SaveDraftAsync is null && ViewModel.Effects.SaveDraftAsync is null)
        {
            DisposalCompletion = effectsView.DisposePreviewAsync();
            return;
        }
        e.Cancel = true;
        if (e.CloseReason == WindowCloseReason.OwnerWindowClosing)
        {
            pendingClosingOwner = Owner as Window;
        }
        if (!preparingClose)
        {
            CloseCompletion = PrepareCloseAsync();
        }
    }

    private async Task PrepareCloseAsync()
    {
        preparingClose = true;
        try
        {
            await ViewModel.NavigationCompletion;
            await ViewModel.Styles.SelectionCompletion;
            await ViewModel.Effects.SelectionCompletion;
            if (allowClose || await ViewModel.Styles.PrepareToLeaveAsync() && await ViewModel.Effects.PrepareToLeaveAsync())
            {
                DisposalCompletion = effectsView.DisposePreviewAsync();
                await DisposalCompletion;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    allowClose = true;
                    Close();
                });
                if (pendingClosingOwner is { } closingOwner)
                {
                    Dispatcher.UIThread.Post(closingOwner.Close);
                }
            }
        }
        finally
        {
            pendingClosingOwner = null;
            preparingClose = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        DisposalCompletion = effectsView.DisposePreviewAsync();
        this.FindControl<Styles.StyleLibraryView>("StylesView")!.Dispose();
        Localization.LanguageChanged -= OnLanguageChanged;
        ViewModel.Styles.UpsertRequested -= OnUpsertStyleRequested;
        ViewModel.Styles.DeleteRequested -= OnDeleteStyleRequested;
        ViewModel.Styles.ApplyRequested -= OnApplyStyleRequested;
        ViewModel.Styles.CaptureRequested -= OnCaptureStyleRequested;
        ViewModel.Styles.ImportRequested -= OnImportStylesRequested;
        ViewModel.Styles.ExportRequested -= OnExportStylesRequested;
        ViewModel.Effects.SaveRequested -= OnUpsertEffectRequested;
        ViewModel.Effects.DeleteRequested -= OnDeleteEffectRequested;
        ViewModel.Effects.ImportRequested -= OnImportEffectRequested;
        ViewModel.Effects.ExportRequested -= OnExportEffectRequested;
        ViewModel.Effects.ValidationFailed -= OnEffectValidationFailed;
        Closed -= OnClosed;
        Closing -= OnClosing;
    }

    private void OnUpsertStyleRequested(object? sender, LibraryStyleEventArgs e)
    {
        UpsertStyleRequested?.Invoke(this, e);
    }

    private void OnDeleteStyleRequested(object? sender, LibraryStyleDeleteEventArgs e)
    {
        DeleteStyleRequested?.Invoke(this, e);
    }

    private void OnApplyStyleRequested(object? sender, LibraryStyleEventArgs e)
    {
        ApplyStyleRequested?.Invoke(this, e);
    }

    private void OnCaptureStyleRequested(object? sender, EventArgs e)
    {
        CaptureStyleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnImportStylesRequested(object? sender, EventArgs e)
    {
        ImportStylesRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExportStylesRequested(object? sender, LibraryStylesExportEventArgs e)
    {
        ExportStylesRequested?.Invoke(this, e);
    }

    private void OnUpsertEffectRequested(object? sender, LibraryEffectEventArgs e)
    {
        UpsertEffectRequested?.Invoke(this, e);
    }

    private void OnDeleteEffectRequested(object? sender, LibraryEffectDeleteEventArgs e)
    {
        DeleteEffectRequested?.Invoke(this, e);
    }

    private void OnImportEffectRequested(object? sender, EventArgs e)
    {
        ImportEffectRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnExportEffectRequested(object? sender, LibraryEffectsExportEventArgs e)
    {
        ExportEffectRequested?.Invoke(this, e);
    }

    private void OnEffectValidationFailed(object? sender, EffectScriptValidationFailedEventArgs e)
    {
        EffectValidationFailed?.Invoke(this, e);
    }

    /// <summary>同步效果库页面的宿主状态。</summary>
    public void UpdateStyles(IEnumerable<SubtitleStylePreset> presets, Guid? selectedId = null)
    {
        ViewModel.Styles.UpdateStyles(presets, selectedId);
    }

    /// <summary>同步效果库页面的宿主状态。</summary>
    public void UpdateEffects(IEnumerable<EffectScriptPreset> presets, Guid? selectedId = null)
    {
        ViewModel.Effects.UpdateEffects(presets, selectedId);
    }

    /// <summary>同步效果库页面的宿主状态。</summary>
    public void SetEffectOperationBusy(bool busy)
    {
        ViewModel.Effects.IsBusy = busy;
    }

    /// <summary>同步效果库页面的宿主状态。</summary>
    public void SetSubtitlePositionMeasurement(Func<SubtitleStylePreset, SubtitlePositionMeasurement> measure)
    {
        ViewModel.Styles.SetPositionMeasurement(measure);
    }

    /// <summary>同步效果库页面的宿主状态。</summary>
    public void SetStyleOperationBusy(bool busy)
    {
        ViewModel.Styles.IsBusy = busy;
    }

    /// <summary>同步效果库页面的宿主状态。</summary>
    public void UpdateSelectionAvailability(bool available)
    {
        ViewModel.Styles.HasSelectedSubtitle = available;
    }

    /// <summary>同步效果库页面的宿主状态。</summary>
    public void ShowError(string? message)
    {
        ViewModel.ShowError(message);
    }

}
