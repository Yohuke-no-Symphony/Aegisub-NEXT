using System.Globalization;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.Koumori;

/// <summary>自动打轴设置草稿，非法距离不改变已经生效的偏好。</summary>
public sealed class KoumoriSettingsViewModel : ObservableObject
{
    private readonly KoumoriModelSourceChoice[] modelSources =
    [
        new(KoumoriModelSource.BUNDLED), new(KoumoriModelSource.MANUAL)
    ];
    private KoumoriPreferences preferences;
    private string maximumSnapMillisecondsText;
    private string modelDirectoryText;
    private bool invalidDistance;
    private bool invalidDirectory;
    private bool refreshingSources;

    /// <summary>从持久化快照构造独立页面。</summary>
    public KoumoriSettingsViewModel(WorkbenchPreferences value)
    {
        preferences = value.Koumori;
        maximumSnapMillisecondsText = Format(preferences.MaximumSnapMilliseconds);
        modelDirectoryText = preferences.ModelDirectory;
        ApplyModelDirectoryCommand = new RelayCommand(ApplyModelDirectory, () => IsManualModel);
        RefreshLanguage();
    }

    public event EventHandler<KoumoriPreferencesChangedEventArgs>? Changed;
    public IRelayCommand ApplyModelDirectoryCommand { get; }
    public IReadOnlyList<KoumoriModelSourceChoice> ModelSources => modelSources;
    public bool IsManualModel => preferences.ModelSource == KoumoriModelSource.MANUAL;
    public string? Error => invalidDistance ? Localization.Get("Koumori.InvalidDistance")
        : IsManualModel && (invalidDirectory || string.IsNullOrWhiteSpace(preferences.ModelDirectory))
            ? Localization.Get("Koumori.ManualDirectoryRequired") : null;
    public KoumoriModelSourceChoice? SelectedModelSource
    {
        get => modelSources.FirstOrDefault(choice => choice.Source == preferences.ModelSource);
        set
        {
            if (!refreshingSources && value is not null && Enum.IsDefined(value.Source))
            {
                Apply(preferences with { ModelSource = value.Source });
            }
        }
    }
    public bool Enabled
    {
        get => preferences.Enabled;
        set => Apply(preferences with { Enabled = value });
    }
    public string ModelDirectoryText
    {
        get => modelDirectoryText;
        set => SetProperty(ref modelDirectoryText, value);
    }
    public string MaximumSnapMillisecondsText
    {
        get => maximumSnapMillisecondsText;
        set => SetProperty(ref maximumSnapMillisecondsText, value);
    }

    /// <summary>确认距离；无效内容保留供用户修正。</summary>
    public bool CommitMaximumSnapMilliseconds()
    {
        if (!int.TryParse(MaximumSnapMillisecondsText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ||
            value is < 1 or > 1000)
        {
            invalidDistance = true;
            OnPropertyChanged(nameof(Error));
            return false;
        }
        Apply(preferences with { MaximumSnapMilliseconds = value });
        RestoreMaximumSnapMilliseconds();
        return true;
    }

    /// <summary>撤回距离草稿。</summary>
    public void RestoreMaximumSnapMilliseconds()
    {
        MaximumSnapMillisecondsText = Format(preferences.MaximumSnapMilliseconds);
        invalidDistance = false;
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>同步已保存快照，同时保留正在编辑的草稿。</summary>
    public void UpdatePreferences(WorkbenchPreferences value)
    {
        if (MaximumSnapMillisecondsText == Format(preferences.MaximumSnapMilliseconds))
        {
            MaximumSnapMillisecondsText = Format(value.Koumori.MaximumSnapMilliseconds);
        }
        if (ModelDirectoryText == preferences.ModelDirectory)
        {
            ModelDirectoryText = value.Koumori.ModelDirectory;
            invalidDirectory = false;
        }
        preferences = value.Koumori;
        RefreshPreferences();
    }

    /// <summary>刷新动态验证信息。</summary>
    public void RefreshLanguage()
    {
        refreshingSources = true;
        try
        {
            modelSources[0].UpdateLabel(Localization.Get("Koumori.BundledModel"));
            modelSources[1].UpdateLabel(Localization.Get("Koumori.ManualModel"));
            OnPropertyChanged(nameof(ModelSources));
            OnPropertyChanged(nameof(SelectedModelSource));
            OnPropertyChanged(nameof(Error));
        }
        finally
        {
            refreshingSources = false;
        }
    }

    private void ApplyModelDirectory()
    {
        var directory = ModelDirectoryText.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            invalidDirectory = true;
            OnPropertyChanged(nameof(Error));
            return;
        }
        invalidDirectory = false;
        Apply(preferences with { ModelDirectory = directory });
        ModelDirectoryText = preferences.ModelDirectory;
        OnPropertyChanged(nameof(Error));
    }

    private void RefreshPreferences()
    {
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(SelectedModelSource));
        OnPropertyChanged(nameof(IsManualModel));
        OnPropertyChanged(nameof(Error));
        ApplyModelDirectoryCommand.NotifyCanExecuteChanged();
    }

    private void Apply(KoumoriPreferences value)
    {
        value.Validate();
        if (preferences == value)
        {
            return;
        }
        preferences = value;
        RefreshPreferences();
        Changed?.Invoke(this, new(value));
    }

    private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);
}
