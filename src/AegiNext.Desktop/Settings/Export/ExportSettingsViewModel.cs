using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Export;
using AegiNext.Export.Presets;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.Export;

/// <summary>压制预设的独立参数草稿、选择、验证和批量交换语义请求。</summary>
public sealed class ExportSettingsViewModel : VideoExportSettingsViewModel
{
    private ImmutableArray<VideoExportPreset> exportPresets = [];
    private ImmutableArray<Guid> selectedIds = [];
    private VideoExportPreset? selectedPreset;
    private VideoExportPreset? draft;
    internal event EventHandler? DraftChanging;
    private string name = string.Empty;
    private bool newDraft;
    private bool loading;
    private bool switching;
    private bool isBusy;
    private bool hasWorkspace;
    private string? error;
    private string? errorKey;
    private string? invalidFieldKey;

    /// <summary>创建预设草稿命令；存储和会话操作由组合根提供。</summary>
    public ExportSettingsViewModel()
    {
        AddCommand = new(() => SelectionCompletion = CreateDraftAsync(false), () => !IsBusy && !switching);
        DuplicateCommand = new(() => SelectionCompletion = CreateDraftAsync(true), () => CanEdit && !switching);
        SaveCommand = new(() => SelectionCompletion = SavePendingAsync(), () => CanEdit && !switching);
        DeleteCommand = new(Delete, () => CanDelete);
        CaptureCommand = new(() => SelectionCompletion = CaptureAsync(), () => HasWorkspace && !IsBusy && !switching);
        ImportCommand = new(() => ImportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy && !switching);
        ExportCommand = new(() => ExportRequested?.Invoke(this,
            new(exportPresets.Where(value => selectedIds.Contains(value.Id)).ToImmutableArray())),
            () => !selectedIds.IsEmpty && !IsBusy && !switching);
        PropertyChanged += OnParameterChanged;
    }

    public event EventHandler? CaptureRequested;
    public event EventHandler? ImportRequested;
    public event EventHandler<SettingsExportPresetDeleteEventArgs>? DeleteRequested;
    public event EventHandler<SettingsExportPresetsEventArgs>? ExportRequested;
    public RelayCommand AddCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand CaptureCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }
    internal Func<VideoExportPreset, Task<bool>>? SaveDraftAsync { get; set; }
    internal Func<Task<int>>? ConfirmLeaveAsync { get; set; }
    internal Task SelectionCompletion { get; private set; } = Task.CompletedTask;
    public ImmutableArray<VideoExportPreset> ExportPresets => exportPresets;
    public ImmutableArray<Guid> SelectedIds => selectedIds;
    public VideoExportPreset? SelectedPreset => selectedPreset;
    public VideoExportPreset? Draft => draft;
    public bool HasDraft => draft is not null;
    public bool IsEmpty => exportPresets.IsEmpty;
    public bool IsAvailable => !IsBusy && !switching;
    public bool CanEdit => HasDraft && selectedIds.Length <= 1 && !IsBusy;
    public bool CanDelete => !IsBusy && !switching && (!selectedIds.IsEmpty ||
        draft is not null && !exportPresets.Any(value => value.Id == draft.Id));
    public bool IsDirty
    {
        get
        {
            if (draft is null)
            {
                return false;
            }
            if (newDraft || Name != draft.Name || CrfText != draft.Settings.Crf.ToString(CultureInfo.CurrentCulture) ||
                VideoBitrateText != (draft.Settings.VideoBitrate / 1000000m).ToString(CultureInfo.CurrentCulture) ||
                AudioBitrateText != (draft.Settings.AudioBitrate / 1000m).ToString(CultureInfo.CurrentCulture))
            {
                return true;
            }
            try
            {
                return CaptureSettings(true) != draft.Settings;
            }
            catch (ExportSettingsValidationException)
            {
                return true;
            }
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value) && !loading)
            {
                RefreshActions();
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (SetProperty(ref isBusy, value))
            {
                RefreshActions();
            }
        }
    }

    public bool HasWorkspace
    {
        get => hasWorkspace;
        set
        {
            if (SetProperty(ref hasWorkspace, value))
            {
                RefreshActions();
            }
        }
    }

    public string? Error
    {
        get => error;
        private set => SetProperty(ref error, value);
    }

    public string? InvalidFieldKey
    {
        get => invalidFieldKey;
        private set => SetProperty(ref invalidFieldKey, value);
    }

    /// <summary>同步已保存预设，保留稳定选择和未保存的有效或无效输入。</summary>
    public void UpdatePresets(IEnumerable<VideoExportPreset> presets, Guid? selectedId = null)
    {
        ArgumentNullException.ThrowIfNull(presets);
        DraftChanging?.Invoke(this, EventArgs.Empty);
        var pending = draft;
        var dirty = IsDirty;
        var wasNew = newDraft;
        var selectionBefore = selectedIds;
        var captured = CreatePreset(false);
        exportPresets = presets.ToImmutableArray();
        var committed = captured is not null && exportPresets.FirstOrDefault(value => value.Id == captured.Id) == captured;
        var selection = selectedId ?? (committed ? captured?.Id : SelectedPreset?.Id);
        loading = true;
        try
        {
            OnPropertyChanged(nameof(ExportPresets));
            if (wasNew && !committed && selectedId is null)
            {
                SetSelection(null, []);
            }
            else
            {
                var ids = selectedId is not null || committed
                    ? (selection is { } id ? new[] { id } : [])
                    : selectionBefore.Where(id => exportPresets.Any(value => value.Id == id)).ToArray();
                if (!dirty && ids.Length == 0 && (pending is null || !selectionBefore.IsEmpty))
                {
                    ids = exportPresets.Take(1).Select(value => value.Id).ToArray();
                }
                SetSelection(selection, ids);
            }
        }
        finally
        {
            loading = false;
        }
        if (dirty && !committed && selectedId is null)
        {
            RefreshActions();
            return;
        }
        LoadDraft(SelectedPreset, false);
    }

    /// <summary>等待处理未保存草稿后切换单选或批量选择。</summary>
    public Task<bool> SelectPresetsAsync(Guid? primaryId, IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (loading || switching || IsBusy)
        {
            return Task.FromResult(false);
        }
        var values = ids.ToArray();
        var completion = SelectPresetsCoreAsync(primaryId, values);
        SelectionCompletion = completion;
        return completion;
    }

    /// <summary>等待保存、恢复或取消；失败和取消保留全部原始草稿。</summary>
    public async Task<bool> PrepareToLeaveAsync()
    {
        DraftChanging?.Invoke(this, EventArgs.Empty);
        if (!IsDirty)
        {
            return true;
        }
        var decision = ConfirmLeaveAsync is null ? 0 : await ConfirmLeaveAsync();
        if (decision == 1)
        {
            DiscardDraft();
            return true;
        }
        return decision == 0 && await SavePendingAsync();
    }

    /// <summary>完整验证并等待保存当前标识的参数，失败保留编辑草稿。</summary>
    public async Task<bool> SavePendingAsync()
    {
        if (!IsDirty)
        {
            return true;
        }
        var preset = CreatePreset(true);
        if (preset is null || SaveDraftAsync is null)
        {
            return false;
        }
        if (!await SaveDraftAsync(preset))
        {
            return false;
        }
        LoadDraft(exportPresets.FirstOrDefault(value => value.Id == preset.Id) ?? preset, false);
        return true;
    }

    /// <summary>恢复当前已保存参数，或丢弃尚未保存的新草稿。</summary>
    public void DiscardDraft()
    {
        LoadDraft(draft is null ? null : exportPresets.FirstOrDefault(value => value.Id == draft.Id), false);
    }

    /// <summary>用会话已捕获的配置创建未保存新草稿，不修改工作台参数或工程。</summary>
    public void CaptureFromSettings(VideoExportSettings settings)
    {
        VideoExportSettingsValidator.ValidatePreset(settings);
        SetSelection(null, []);
        LoadDraft(new(Guid.NewGuid(), UniqueName(Localization.Get("Settings.CapturedExportPreset")), settings), true);
    }

    /// <summary>显示存储或文件交换错误，保留草稿和字段定位。</summary>
    public void ShowError(string? message)
    {
        errorKey = null;
        Error = message;
    }

    /// <summary>更新语言和数值选择投影，保留草稿及选中的预设身份。</summary>
    public new void RefreshLanguage()
    {
        base.RefreshLanguage();
        if (errorKey is not null)
        {
            Error = Localization.Get(errorKey);
        }
    }

    private async Task<bool> SelectPresetsCoreAsync(Guid? primaryId, Guid[] ids)
    {
        if (SelectedPreset?.Id == primaryId && selectedIds.ToHashSet().SetEquals(ids))
        {
            return true;
        }
        switching = true;
        RefreshActions();
        try
        {
            if (!await PrepareToLeaveAsync())
            {
                return false;
            }
            SetSelection(primaryId, ids);
            LoadDraft(SelectedPreset, false);
            return true;
        }
        finally
        {
            switching = false;
            RefreshActions();
        }
    }

    private async Task CreateDraftAsync(bool duplicate)
    {
        switching = true;
        RefreshActions();
        try
        {
            if (!await PrepareToLeaveAsync())
            {
                return;
            }
            var source = duplicate ? CreatePreset(true) : null;
            if (duplicate && source is null)
            {
                return;
            }
            SetSelection(null, []);
            LoadDraft(new(Guid.NewGuid(), UniqueName(source is null ? Localization.Get("Settings.NewExportPreset") :
                source.Name + " " + Localization.Get("Settings.CopySuffix")), source?.Settings ?? new()), true);
        }
        finally
        {
            switching = false;
            RefreshActions();
        }
    }

    private async Task CaptureAsync()
    {
        switching = true;
        RefreshActions();
        try
        {
            if (await PrepareToLeaveAsync())
            {
                CaptureRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            switching = false;
            RefreshActions();
        }
    }

    private VideoExportPreset? CreatePreset(bool reportError)
    {
        if (draft is null)
        {
            return null;
        }
        string normalizedName;
        try
        {
            normalizedName = Name.Trim().Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return Reject("Settings.ExportPresetNameInvalid", "ExportPresetNameInput", reportError);
        }
        if (normalizedName.Length == 0)
        {
            return Reject("Settings.ExportPresetNameRequired", "ExportPresetNameInput", reportError);
        }
        if (normalizedName.Length > VideoExportPresetValidator.MAX_NAME_LENGTH || normalizedName.Any(char.IsControl))
        {
            return Reject("Settings.ExportPresetNameInvalid", "ExportPresetNameInput", reportError);
        }
        if (exportPresets.Any(value => value.Id != draft.Id &&
            string.Equals(value.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            return Reject("Settings.DuplicateExportPresetName", "ExportPresetNameInput", reportError);
        }
        try
        {
            var preset = new VideoExportPreset(draft.Id, normalizedName, CaptureSettings(true));
            VideoExportPresetValidator.Validate(preset);
            if (reportError)
            {
                errorKey = null;
                Error = null;
                InvalidFieldKey = null;
            }
            return preset;
        }
        catch (ExportSettingsValidationException failure)
        {
            return Reject(failure.LocalizationKey, failure.FieldKey, reportError);
        }
        catch (InvalidDataException)
        {
            return Reject("Settings.ExportPresetNameInvalid", "ExportPresetNameInput", reportError);
        }
    }

    private VideoExportPreset? Reject(string key, string fieldKey, bool reportError)
    {
        if (reportError)
        {
            errorKey = key;
            InvalidFieldKey = fieldKey;
            Error = Localization.Get(key);
        }
        return null;
    }

    private void SetSelection(Guid? primaryId, IEnumerable<Guid> ids)
    {
        var available = ids.ToHashSet();
        selectedIds = exportPresets.Where(value => available.Contains(value.Id)).Select(value => value.Id).ToImmutableArray();
        selectedPreset = exportPresets.FirstOrDefault(value => value.Id == primaryId && selectedIds.Contains(value.Id)) ??
                         exportPresets.FirstOrDefault(value => selectedIds.Contains(value.Id));
        OnPropertyChanged(nameof(SelectedIds));
        OnPropertyChanged(nameof(SelectedPreset));
    }

    private void LoadDraft(VideoExportPreset? preset, bool isNew)
    {
        DraftChanging?.Invoke(this, EventArgs.Empty);
        loading = true;
        try
        {
            draft = preset;
            newDraft = isNew;
            Name = preset?.Name ?? string.Empty;
            if (preset is not null)
            {
                ApplySettings(preset.Settings);
            }
            errorKey = null;
            Error = null;
            InvalidFieldKey = null;
            OnPropertyChanged(nameof(Draft));
        }
        finally
        {
            loading = false;
        }
        RefreshActions();
    }

    private void Delete()
    {
        if (!CanDelete)
        {
            return;
        }

        if (!selectedIds.IsEmpty)
        {
            DeleteRequested?.Invoke(this, new(selectedIds));
        }
        else if (draft is not null)
        {
            DeleteRequested?.Invoke(this, new([], true, draft.Id));
        }
    }

    private string UniqueName(string prefix)
    {
        var candidate = FitName(prefix, string.Empty);
        var suffix = 2;
        while (exportPresets.Any(value => string.Equals(value.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = FitName(prefix, " " + suffix++);
        }
        return candidate;
    }

    private static string FitName(string prefix, string suffix)
    {
        var length = Math.Min(prefix.Length, VideoExportPresetValidator.MAX_NAME_LENGTH - suffix.Length);
        if (length > 0 && char.IsHighSurrogate(prefix[length - 1]))
        {
            length--;
        }
        return prefix[..length].TrimEnd() + suffix;
    }

    private void OnParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!loading && e.PropertyName is nameof(Codec) or nameof(Speed) or nameof(UseHardwareEncoder) or
            nameof(QualityMode) or nameof(BitrateMode) or nameof(AudioMode) or nameof(CrfText) or
            nameof(VideoBitrateText) or nameof(AudioBitrateText))
        {
            RefreshActions();
        }
    }

    private void RefreshActions()
    {
        OnPropertyChanged(nameof(HasDraft));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanDelete));
        foreach (var command in new[] { AddCommand, DuplicateCommand, SaveCommand, DeleteCommand, CaptureCommand, ImportCommand, ExportCommand })
        {
            command.NotifyCanExecuteChanged();
        }
    }
}
