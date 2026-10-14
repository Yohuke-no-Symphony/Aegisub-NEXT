using AegiNext.Application.Presets;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>效果库分享格式、导入预览与本地状态；存储、文件选择和任务由协调器拥有。</summary>
public sealed class EffectLibraryTransferViewModel : ObservableObject, IDisposable
{
    private IReadOnlyList<EffectLibraryExportFormatChoice> exportFormats = [];
    private EffectLibraryExportFormatChoice? selectedFormat;
    private IReadOnlyList<EffectLibraryImportRowViewModel> rows = [];
    private bool operationBusy;
    private bool externalBusy;
    private bool updatingDecisions;
    private bool disposed;
    private int styleCount;
    private int personalEffectCount;
    private int builtinEffectCount;
    private string? error;
    private string? statusKey;
    private object[] statusArguments = [];

    /// <summary>创建语义命令，默认使用完整独立素材格式。</summary>
    public EffectLibraryTransferViewModel()
    {
        ImportCommand = new(() => ImportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        ExportSelectionCommand = new(() => ExportSelectionRequested?.Invoke(this, EventArgs.Empty), () => CanExportSelection);
        ExportAllCommand = new(() => ExportAllRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
        CommitImportCommand = new(() => CommitImportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy && HasPreview && SelectedImportCount > 0);
        ClearPreviewCommand = new(ClearPreview, () => !IsBusy && HasPreview);
        UpdateSelection(0, 0, 0);
    }

    internal event EventHandler? ImportRequested;
    internal event EventHandler? ExportSelectionRequested;
    internal event EventHandler? ExportAllRequested;
    internal event EventHandler? CommitImportRequested;
    internal EffectLibraryImportPlan? ImportPlan { get; private set; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportSelectionCommand { get; }
    public RelayCommand ExportAllCommand { get; }
    public RelayCommand CommitImportCommand { get; }
    public RelayCommand ClearPreviewCommand { get; }
    public IReadOnlyList<EffectLibraryExportFormatChoice> ExportFormats => exportFormats;
    public IReadOnlyList<EffectLibraryImportRowViewModel> Rows => rows;
    public bool HasPreview => ImportPlan is not null;
    public bool IsBusy => operationBusy || externalBusy || disposed;
    public bool CanExportSelection => !IsBusy && selectedFormat is not null && styleCount + personalEffectCount + builtinEffectCount > 0;
    public bool HasBuiltinSelection => builtinEffectCount > 0;
    public bool IsSourceFormat => SelectedFormat == EffectLibraryExportFormat.SOURCE;
    public string SelectionSummary => Localization.Format("EffectLibrary.SelectionSummary", styleCount, personalEffectCount + builtinEffectCount);
    public string PreviewSummary => Localization.Format("EffectLibrary.PreviewSummary",
        Rows.Count(row => row.Row.Status == EffectLibraryImportStatus.NEW),
        Rows.Count(row => row.Row.Status == EffectLibraryImportStatus.IDENTICAL),
        Rows.Count(row => row.Row.Status == EffectLibraryImportStatus.CONFLICT));
    public int SelectedImportCount => Rows.Count(row => row.Decision != EffectLibraryImportDecision.SKIP);
    public string SelectedImportSummary => Localization.Format("EffectLibrary.SelectedImportSummary", SelectedImportCount);
    public string? Error => error is null ? null : Localization.Format("EffectLibrary.TransferFailed", error);
    public bool HasError => !string.IsNullOrWhiteSpace(error);
    public bool HasStatus => statusKey is not null;
    public string? Status => statusKey is null ? null : Localization.Format(statusKey, statusArguments);

    public bool ExternalBusy
    {
        get => externalBusy;
        internal set
        {
            if (SetProperty(ref externalBusy, value))
            {
                RefreshBusy();
            }
        }
    }

    public EffectLibraryExportFormatChoice? SelectedFormatChoice
    {
        get => selectedFormat;
        set
        {
            if (value is not null)
            {
                SelectedFormat = value.Format;
            }
        }
    }

    public EffectLibraryExportFormat SelectedFormat
    {
        get => selectedFormat?.Format ?? EffectLibraryExportFormat.INDEPENDENT;
        set
        {
            var next = ExportFormats.FirstOrDefault(choice => choice.Format == value) ??
                throw new ArgumentException("此格式不适用于当前效果库选择。", nameof(value));
            if (SetProperty(ref selectedFormat, next, nameof(SelectedFormatChoice)))
            {
                OnPropertyChanged(nameof(SelectedFormat));
                OnPropertyChanged(nameof(IsSourceFormat));
            }
        }
    }

    /// <summary>根据宿主已选条目重建可用格式，保留仍可用的格式身份。</summary>
    public void UpdateSelection(int styles, int personalEffects, int builtinEffects)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(styles);
        ArgumentOutOfRangeException.ThrowIfNegative(personalEffects);
        ArgumentOutOfRangeException.ThrowIfNegative(builtinEffects);
        var previous = SelectedFormat;
        styleCount = styles;
        personalEffectCount = personalEffects;
        builtinEffectCount = builtinEffects;
        var formats = new List<EffectLibraryExportFormatChoice>();
        if (builtinEffects == 0)
        {
            formats.Add(new(EffectLibraryExportFormat.INDEPENDENT));
            formats.Add(new(EffectLibraryExportFormat.PACKAGE));
        }
        if (styles == 0 && personalEffects + builtinEffects > 0)
        {
            formats.Add(new(EffectLibraryExportFormat.SOURCE));
        }
        exportFormats = formats;
        selectedFormat = formats.FirstOrDefault(choice => choice.Format == previous) ?? formats.FirstOrDefault();
        OnPropertyChanged(nameof(ExportFormats));
        OnPropertyChanged(nameof(SelectedFormatChoice));
        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(IsSourceFormat));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(HasBuiltinSelection));
        OnPropertyChanged(nameof(CanExportSelection));
        RefreshCommands();
    }

    internal void SetPreview(EffectLibraryImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        UnsubscribeRows();
        ImportPlan = plan;
        rows = plan.Rows.Select(row => new EffectLibraryImportRowViewModel(row)).ToArray();
        foreach (var row in rows)
        {
            row.DecisionChanged += OnDecisionChanged;
            row.SetBusy(IsBusy);
        }
        NotifyPreview();
    }

    internal IReadOnlyDictionary<int, EffectLibraryImportDecision> CreateDecisions()
    {
        return Rows.ToDictionary(row => row.Row.Index, row => row.Decision);
    }

    internal void SetOperationBusy(bool value)
    {
        if (operationBusy != value)
        {
            operationBusy = value;
            RefreshBusy();
        }
    }

    internal void ShowError(string? message)
    {
        if (SetProperty(ref error, message, nameof(Error)))
        {
            OnPropertyChanged(nameof(HasError));
        }
    }

    internal void ShowStatus(string? key, params object[] arguments)
    {
        statusKey = key;
        statusArguments = arguments.ToArray();
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasStatus));
    }

    internal void ClearPreview()
    {
        UnsubscribeRows();
        ImportPlan = null;
        rows = [];
        NotifyPreview();
    }

    /// <summary>刷新所有展示标签，保留格式、计划、输入名称与行决策。</summary>
    public void RefreshLanguage()
    {
        foreach (var format in ExportFormats)
        {
            format.RefreshLanguage();
        }
        foreach (var row in Rows)
        {
            row.RefreshLanguage();
        }
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(SelectedImportSummary));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>解除本地行和命令请求订阅；不影响应用共享仓库。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        UnsubscribeRows();
        ImportRequested = null;
        ExportSelectionRequested = null;
        ExportAllRequested = null;
        CommitImportRequested = null;
        RefreshBusy();
    }

    private void OnDecisionChanged(object? sender, EventArgs e)
    {
        if (!updatingDecisions && sender is EffectLibraryImportRowViewModel changed &&
            changed.Decision != EffectLibraryImportDecision.SKIP && changed.Row.ConflictGroupId is { } group)
        {
            updatingDecisions = true;
            try
            {
                foreach (var row in Rows.Where(row => !ReferenceEquals(row, changed) && row.Row.ConflictGroupId == group))
                {
                    row.Decision = EffectLibraryImportDecision.SKIP;
                }
            }
            finally
            {
                updatingDecisions = false;
            }
        }
        OnPropertyChanged(nameof(SelectedImportCount));
        OnPropertyChanged(nameof(SelectedImportSummary));
        CommitImportCommand.NotifyCanExecuteChanged();
    }

    private void UnsubscribeRows()
    {
        foreach (var row in Rows)
        {
            row.DecisionChanged -= OnDecisionChanged;
        }
    }

    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(PreviewSummary));
        OnPropertyChanged(nameof(SelectedImportCount));
        OnPropertyChanged(nameof(SelectedImportSummary));
        RefreshCommands();
    }

    private void RefreshBusy()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanExportSelection));
        foreach (var row in Rows)
        {
            row.SetBusy(IsBusy);
        }
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        ImportCommand.NotifyCanExecuteChanged();
        ExportSelectionCommand.NotifyCanExecuteChanged();
        ExportAllCommand.NotifyCanExecuteChanged();
        CommitImportCommand.NotifyCanExecuteChanged();
        ClearPreviewCommand.NotifyCanExecuteChanged();
    }
}
