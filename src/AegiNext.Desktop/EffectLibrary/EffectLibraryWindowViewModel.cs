using System.ComponentModel;
using AegiNext.Desktop.EffectLibrary.Effects;
using AegiNext.Desktop.EffectLibrary.Styles;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.EffectLibrary;

/// <summary>效果库导航和草稿组合模型，不拥有工程与渲染资源。</summary>
public sealed class EffectLibraryWindowViewModel : ObservableObject
{
    private int pageIndex;
    private bool navigating;
    private string? externalError;
    private string title = Localization.Get("EffectLibrary.Title");

    /// <summary>创建独立的素材页面，偏好由窗口宿主呈现。</summary>
    public EffectLibraryWindowViewModel(WorkbenchPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.Validate();
        Styles.PropertyChanged += OnPageChanged;
        Effects.PropertyChanged += OnPageChanged;
        Transfer.PropertyChanged += OnPageChanged;
    }

    public StyleLibraryViewModel Styles { get; } = new();
    public EffectScriptLibraryViewModel Effects { get; } = new();
    public EffectLibraryTransferViewModel Transfer { get; } = new();
    public string Title => title;
    public bool IsNavigationAvailable => !navigating;
    public bool HasUnsavedTemplates => Styles.IsDirty || Effects.IsDirty;
    public bool IsStylesVisible => CurrentPage == EffectLibraryPage.STYLES;
    public bool IsEffectsVisible => CurrentPage == EffectLibraryPage.EFFECTS;
    public bool IsTransferVisible => CurrentPage == EffectLibraryPage.TRANSFER;
    public string PageTitle => Localization.Get("EffectLibrary." + (CurrentPage switch
    {
        EffectLibraryPage.STYLES => "Styles",
        EffectLibraryPage.EFFECTS => "Effects",
        _ => "Transfer"
    }));
    public string? Error => externalError ?? (CurrentPage switch
    {
        EffectLibraryPage.STYLES => Styles.Error,
        EffectLibraryPage.EFFECTS => Effects.Error,
        _ => null
    });
    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    internal Task NavigationCompletion { get; private set; } = Task.CompletedTask;

    public EffectLibraryPage CurrentPage
    {
        get => (EffectLibraryPage)pageIndex;
        set => PageIndex = (int)value;
    }

    public int PageIndex
    {
        get => pageIndex;
        set
        {
            if (!navigating)
            {
                NavigationCompletion = SelectPageAsync((EffectLibraryPage)value);
            }
        }
    }

    /// <summary>处理当前页未保存的草稿后选择页面。</summary>
    public async Task<bool> SelectPageAsync(EffectLibraryPage page)
    {
        if (!Enum.IsDefined(page) || navigating)
        {
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(PageIndex));
            return false;
        }
        if (CurrentPage == page)
        {
            return true;
        }
        navigating = true;
        OnPropertyChanged(nameof(IsNavigationAvailable));
        try
        {
            if (CurrentPage == EffectLibraryPage.STYLES)
            {
                await Styles.SelectionCompletion;
                if (Styles.SaveDraftAsync is not null && !await Styles.PrepareToLeaveAsync())
                {
                    return false;
                }
            }
            else if (CurrentPage == EffectLibraryPage.EFFECTS)
            {
                await Effects.SelectionCompletion;
                if (Effects.SaveDraftAsync is not null && !await Effects.PrepareToLeaveAsync())
                {
                    return false;
                }
            }
            pageIndex = (int)page;
            OnPropertyChanged(nameof(IsStylesVisible));
            OnPropertyChanged(nameof(IsEffectsVisible));
            OnPropertyChanged(nameof(IsTransferVisible));
            OnPropertyChanged(nameof(PageTitle));
            RefreshError();
            return true;
        }
        finally
        {
            navigating = false;
            OnPropertyChanged(nameof(IsNavigationAvailable));
            OnPropertyChanged(nameof(PageIndex));
            OnPropertyChanged(nameof(CurrentPage));
        }
    }

    /// <summary>显示当前库操作的错误。</summary>
    public void ShowError(string? message)
    {
        externalError = message;
        RefreshError();
    }

    /// <summary>刷新语言并保留编辑与导入选择。</summary>
    public void RefreshLanguage()
    {
        Styles.RefreshLanguage();
        Effects.RefreshLanguage();
        Transfer.RefreshLanguage();
        SetProperty(ref title, Localization.Get("EffectLibrary.Title"), nameof(Title));
        OnPropertyChanged(nameof(PageTitle));
        RefreshError();
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Error))
        {
            externalError = null;
            RefreshError();
        }
    }

    private void RefreshError()
    {
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
    }
}
