using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.Transfer;

/// <summary>呈现个人设置交换预览和待恢复状态，不拥有存储或工程。</summary>
public sealed class UserSettingsTransferViewModel : ObservableObject
{
    private bool isBusy;
    private bool keepWorkspaceRoot = true;
    private bool hasPreview;
    private bool hasPending;
    private bool canRequestExit;
    private string fileName = string.Empty;
    private int exportPresetCount;
    private int layoutCount;
    private int? colorTagCount;
    private int sourceVersion = UserSettingsBundleStore.CURRENT_VERSION;
    private string? statusKey;
    private string? error;

    /// <summary>创建由设置交换协调器处理的语义命令。</summary>
    public UserSettingsTransferViewModel()
    {
        ExportCommand = new(() => ExportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        ImportCommand = new(() => ImportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        StageRestoreCommand = new(() => StageRestoreRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy && HasPreview);
        CancelPendingCommand = new(() => CancelPendingRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy && HasPending);
        RestartCommand = new(() => RestartRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy && HasPending && CanRequestExit);
    }

    internal event EventHandler? ExportRequested;
    internal event EventHandler? ImportRequested;
    internal event EventHandler? StageRestoreRequested;
    internal event EventHandler? CancelPendingRequested;
    internal event EventHandler? RestartRequested;

    public RelayCommand ExportCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand StageRestoreCommand { get; }
    public RelayCommand CancelPendingCommand { get; }
    public RelayCommand RestartCommand { get; }
    public bool HasPreview => hasPreview;
    public bool HasPending => hasPending;
    public bool CanRequestExit => canRequestExit;
    public string FileName => fileName;
    public string PreviewSummary => Localization.Format("Settings.TransferPreviewSummary", exportPresetCount, layoutCount);
    public bool HasIgnoredLegacyMaterials => HasPreview && sourceVersion < UserSettingsBundleStore.CURRENT_VERSION;
    /// <summary>显示待导入标签数量，旧版包显示保留本地库的说明。</summary>
    public string ColorTagSummary => colorTagCount is { } count
        ? Localization.Format("Settings.TransferColorTagSummary", count)
        : Localization.Get("Settings.TransferColorTagsPreserved");
    public string? Status => statusKey is null ? null : Localization.Get(statusKey);
    public bool HasStatus => statusKey is not null;
    public string? Error => error;

    public bool IsBusy
    {
        get => isBusy;
        internal set
        {
            if (SetProperty(ref isBusy, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool KeepWorkspaceRoot
    {
        get => keepWorkspaceRoot;
        set => SetProperty(ref keepWorkspaceRoot, value);
    }

    internal void SetPreview(string name, int exportPresets, int layouts, int? colorTags = null,
        int version = UserSettingsBundleStore.CURRENT_VERSION)
    {
        fileName = name;
        exportPresetCount = exportPresets;
        layoutCount = layouts;
        colorTagCount = colorTags;
        sourceVersion = version;
        hasPreview = true;
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(ColorTagSummary));
        OnPropertyChanged(nameof(HasIgnoredLegacyMaterials));
        OnPropertyChanged(nameof(HasPreview));
        RefreshCommands();
    }

    internal void ClearPreview()
    {
        hasPreview = false;
        fileName = string.Empty;
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasIgnoredLegacyMaterials));
        RefreshCommands();
    }

    internal void UpdatePending(bool pending, bool allowExit)
    {
        hasPending = pending;
        canRequestExit = allowExit;
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(CanRequestExit));
        RefreshCommands();
    }

    internal void ShowStatus(string? key)
    {
        statusKey = key;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasStatus));
    }

    internal void ShowError(string? message)
    {
        SetProperty(ref error, message, nameof(Error));
    }

    /// <summary>刷新状态语言，保留文件预览、导入选择和待恢复状态。</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(ColorTagSummary));
        OnPropertyChanged(nameof(Status));
    }

    private void RefreshCommands()
    {
        ExportCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        StageRestoreCommand.NotifyCanExecuteChanged();
        CancelPendingCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
    }
}
