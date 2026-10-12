using System.Windows.Input;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;
using AegiNext.Export.Presets;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Panels.Export;

internal sealed class ExportPanelViewModel : VideoExportSettingsViewModel
{
    private readonly WorkbenchSession session;
    private readonly ExportMethodChoice[] methods = [new(ExportMethod.BURN_IN), new(ExportMethod.SUBTITLE_MUX)];
    private ExportMethod selectedMethod;
    private IReadOnlyList<ExportPresetListItem> presets = [];
    private ExportPresetListItem? selectedPreset;
    private bool refreshingPresets;
    private bool isRunning;
    private bool progressVisible;
    private bool progressIndeterminate;
    private double progress;
    private string status = string.Empty;

    internal ExportPanelViewModel(WorkbenchSession session)
    {
        this.session = session;
        CancelEncodeCommand = new AsyncRelayCommand(() => session.RunCommandAsync(session.CancelExportAsync));
        RefreshLanguage();
    }

    public IReadOnlyList<ExportMethodChoice> Methods => methods;
    public bool CanSelectMethod => !session.IsClosing && session.Export.CanStart && !IsRunning;
    public bool IsBurnIn => SelectedMethod == ExportMethod.BURN_IN;
    public bool IsSubtitleMux => SelectedMethod == ExportMethod.SUBTITLE_MUX;
    public string ExportButtonLabel => Localization.Get(IsSubtitleMux ? "Workbench.SubtitleMuxExport" : "Workbench.Export");

    public ExportMethod SelectedMethod
    {
        get => selectedMethod;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (!CanSelectMethod || !SetProperty(ref selectedMethod, value))
            {
                return;
            }
            OnPropertyChanged(nameof(SelectedMethodChoice));
            OnPropertyChanged(nameof(IsBurnIn));
            OnPropertyChanged(nameof(IsSubtitleMux));
            OnPropertyChanged(nameof(ExportButtonLabel));
            RefreshPresetCommands();
            session.ViewModel.RefreshCommands();
        }
    }

    public ExportMethodChoice? SelectedMethodChoice
    {
        get => methods.Single(value => value.Method == SelectedMethod);
        set
        {
            if (value is not null && methods.Contains(value))
            {
                SelectedMethod = value.Method;
            }
        }
    }

    /// <summary>刷新导出方式与编码选项文案，保留稳定选择和全部草稿。</summary>
    public new void RefreshLanguage()
    {
        base.RefreshLanguage();
        foreach (var method in methods)
        {
            method.RefreshLanguage();
        }
        OnPropertyChanged(nameof(ExportButtonLabel));
    }

    public IReadOnlyList<ExportPresetListItem> Presets
    {
        get => presets;
        private set => SetProperty(ref presets, value);
    }

    public bool CanManagePresets => IsBurnIn && !session.IsClosing && !session.ApplicationContext.ExportPresetsBusy;

    public ExportPresetListItem? SelectedPreset
    {
        get => selectedPreset;
        set
        {
            if (refreshingPresets || !CanManagePresets || !SetProperty(ref selectedPreset, value))
            {
                return;
            }
            if (value is not null)
            {
                session.ApplyExportPreset(value.Id);
            }
        }
    }

    public bool IsRunning
    {
        get => isRunning;
        set
        {
            if (SetProperty(ref isRunning, value))
            {
                OnPropertyChanged(nameof(CanSelectMethod));
            }
        }
    }

    public bool CanCancel => session.Export.CanCancel;

    public bool ProgressVisible
    {
        get => progressVisible;
        set => SetProperty(ref progressVisible, value);
    }

    public bool ProgressIndeterminate
    {
        get => progressIndeterminate;
        set => SetProperty(ref progressIndeterminate, value);
    }

    public double Progress
    {
        get => progress;
        set => SetProperty(ref progress, value);
    }

    public string Status
    {
        get => status;
        set => SetProperty(ref status, value);
    }

    public ICommand EncodeCommand => session.ViewModel.GetCommand(WorkbenchCommand.EXPORT_VIDEO);
    public ICommand CancelEncodeCommand { get; }

    internal void RefreshPresets(IReadOnlyList<VideoExportPreset> values, Guid? selectedId = null)
    {
        var selection = selectedId ?? SelectedPreset?.Id;
        refreshingPresets = true;
        try
        {
            Presets = values.Select(value => new ExportPresetListItem(value.Id, value.Name)).ToArray();
            selectedPreset = Presets.FirstOrDefault(value => value.Id == selection);
            OnPropertyChanged(nameof(SelectedPreset));
        }
        finally
        {
            refreshingPresets = false;
        }
        RefreshPresetCommands();
    }

    internal void RefreshPresetCommands()
    {
        OnPropertyChanged(nameof(CanManagePresets));
    }

    internal void RefreshTaskState()
    {
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanSelectMethod));
    }
}
