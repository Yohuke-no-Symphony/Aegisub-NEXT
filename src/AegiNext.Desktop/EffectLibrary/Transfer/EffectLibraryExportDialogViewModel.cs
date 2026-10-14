using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>管理已保存个人快照的导出选择；文件选择和写入由注入的操作拥有。</summary>
internal sealed class EffectLibraryExportDialogViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private readonly Func<EffectLibraryExportRequest, CancellationToken, Task<bool>> exportAsync;
    private readonly CancellationTokenSource cancellation;
    private readonly ImmutableArray<EffectLibraryExportNode> leaves;
    private readonly EffectLibraryExportFormatChoice independent = new(EffectLibraryExportFormat.INDEPENDENT);
    private readonly EffectLibraryExportFormatChoice package = new(EffectLibraryExportFormat.PACKAGE);
    private readonly EffectLibraryExportFormatChoice source = new(EffectLibraryExportFormat.SOURCE);
    private IReadOnlyList<EffectLibraryExportFormatChoice> exportFormats;
    private EffectLibraryExportFormat selectedFormat = EffectLibraryExportFormat.PACKAGE;
    private TaskCompletionSource? operationCompletion;
    private Task? disposalCompletion;
    private string searchText = string.Empty;
    private Exception? error;
    private bool isBusy;
    private bool disposed;

    internal EffectLibraryExportDialogViewModel(EffectLibrarySnapshot snapshot,
        Func<EffectLibraryExportRequest, CancellationToken, Task<bool>> exportAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(exportAsync);
        this.exportAsync = exportAsync;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StyleRoot = new("EffectLibrary.StyleKind", [.. snapshot.Styles.Presets.Select(preset => new EffectLibraryExportNode(preset))]);
        EffectRoot = new("EffectLibrary.EffectKind", [.. snapshot.Effects.Presets.Select(preset => new EffectLibraryExportNode(preset))]);
        Roots = [StyleRoot, EffectRoot];
        leaves = [.. StyleRoot.AllChildren, .. EffectRoot.AllChildren];
        exportFormats = [package, independent];
        ExportSelectedCommand = new(ExportAsync, () => CanExportSelected);
        SelectAllCommand = new(SelectAll, () => CanInteract && SelectedCount < TotalCount);
        ClearSelectionCommand = new(ClearSelection, () => CanInteract && SelectedCount > 0);
        ClearSearchCommand = new(() => SearchText = string.Empty, () => CanInteract && HasSearchText);
        foreach (var node in Roots.Concat(leaves))
        {
            node.SelectionRequested += OnSelectionRequested;
            node.SetEnabled(CanInteract);
        }
        Localization.LanguageChanged += OnLanguageChanged;
    }

    internal event EventHandler? Exported;
    internal bool WasExported { get; private set; }
    internal Task Completion => operationCompletion?.Task ?? Task.CompletedTask;
    internal Task DisposalCompletion => disposalCompletion ?? Task.CompletedTask;
    public IReadOnlyList<EffectLibraryExportNode> Roots { get; }
    public EffectLibraryExportNode StyleRoot { get; }
    public EffectLibraryExportNode EffectRoot { get; }
    public AsyncRelayCommand ExportSelectedCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public IReadOnlyList<EffectLibraryExportFormatChoice> ExportFormats => exportFormats;
    public int TotalCount => leaves.Length;
    public int VisibleCount => leaves.Count(node => node.MatchesSearch);
    public int SelectedCount => leaves.Count(node => node.IsChecked == true);
    public int HiddenSelectedCount => leaves.Count(node => node.IsChecked == true && !node.MatchesSearch);
    public bool IsEmpty => TotalCount == 0;
    public bool HasNoMatches => !IsEmpty && VisibleCount == 0;
    public bool IsBusy => isBusy;
    public bool CanInteract => !isBusy && !disposed && !cancellation.IsCancellationRequested;
    public bool CanExportSelected => CanInteract && SelectedCount > 0;
    public bool HasSearchText => searchText.Length > 0;
    public bool IsSourceFormat => selectedFormat == EffectLibraryExportFormat.SOURCE;
    public bool HasError => error is not null;
    public string? Error => error is null ? null : Localization.Format("EffectLibrary.ExportDialogFailed", error.Message);

    public string SearchText
    {
        get => searchText;
        set
        {
            if (CanInteract && SetProperty(ref searchText, value ?? string.Empty))
            {
                foreach (var root in Roots)
                {
                    root.ApplySearch(searchText.Trim());
                }
                OnPropertyChanged(nameof(HasSearchText));
                NotifySelection();
            }
        }
    }

    public EffectLibraryExportFormat SelectedFormat
    {
        get => selectedFormat;
        set
        {
            if (!CanInteract || selectedFormat == value)
            {
                return;
            }
            if (!ExportFormats.Any(choice => choice.Format == value))
            {
                throw new ArgumentException("此导出格式不适用于当前素材选择。", nameof(value));
            }
            selectedFormat = value;
            NotifyFormat();
        }
    }

    public EffectLibraryExportFormatChoice SelectedFormatChoice
    {
        get => ExportFormats.Single(choice => choice.Format == selectedFormat);
        set
        {
            if (value is not null)
            {
                SelectedFormat = value.Format;
            }
        }
    }

    private void OnSelectionRequested(EffectLibraryExportNode node, bool selected)
    {
        if (!CanInteract)
        {
            return;
        }
        if (node.IsGroup)
        {
            foreach (var child in node.Children)
            {
                child.SetSelected(selected);
            }
        }
        else
        {
            node.SetSelected(selected);
        }
        NotifySelection();
    }

    private void SelectAll()
    {
        if (CanInteract)
        {
            foreach (var node in leaves)
            {
                node.SetSelected(true);
            }
            NotifySelection();
        }
    }

    private void ClearSelection()
    {
        if (CanInteract)
        {
            foreach (var node in leaves)
            {
                node.SetSelected(false);
            }
            NotifySelection();
        }
    }

    private void NotifySelection()
    {
        foreach (var root in Roots)
        {
            root.NotifySelection();
        }
        var allowSource = SelectedCount > 0 && !leaves.Any(node => node.Style is not null && node.IsChecked == true);
        var formatsChanged = ExportFormats.Contains(source) != allowSource;
        if (formatsChanged)
        {
            exportFormats = allowSource ? [package, independent, source] : [package, independent];
        }
        if (!allowSource && selectedFormat == EffectLibraryExportFormat.SOURCE)
        {
            selectedFormat = EffectLibraryExportFormat.PACKAGE;
        }
        if (formatsChanged)
        {
            OnPropertyChanged(nameof(ExportFormats));
        }
        NotifyFormat();
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HiddenSelectedCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(HasNoMatches));
        RefreshCommands();
    }

    private void NotifyFormat()
    {
        OnPropertyChanged(nameof(SelectedFormat));
        OnPropertyChanged(nameof(SelectedFormatChoice));
        OnPropertyChanged(nameof(IsSourceFormat));
    }

    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanExportSelected));
        ExportSelectedCommand.NotifyCanExecuteChanged();
        SelectAllCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
        ClearSearchCommand.NotifyCanExecuteChanged();
    }

    private void SetBusy(bool value)
    {
        isBusy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanInteract));
        foreach (var node in Roots.Concat(leaves))
        {
            node.SetEnabled(CanInteract);
        }
        RefreshCommands();
    }

    private async Task ExportAsync()
    {
        if (!CanExportSelected)
        {
            return;
        }
        var request = new EffectLibraryExportRequest(
            [.. leaves.Where(node => node.Style is not null && node.IsChecked == true).Select(node => node.Style!)],
            [.. leaves.Where(node => node.Effect is not null && node.IsChecked == true).Select(node => node.Effect!)], selectedFormat);
        var token = cancellation.Token;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        operationCompletion = completed;
        SetBusy(true);
        SetError(null);
        try
        {
            var succeeded = await exportAsync(request, token);
            if (!disposed && !token.IsCancellationRequested && succeeded)
            {
                WasExported = true;
                Exported?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            if (!disposed && !token.IsCancellationRequested)
            {
                SetError(failure);
            }
        }
        finally
        {
            if (disposed)
            {
                isBusy = false;
            }
            else
            {
                SetBusy(false);
            }
            completed.TrySetResult();
        }
    }

    private void SetError(Exception? value)
    {
        error = value;
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLanguage();

    /// <summary>刷新实时展示文案，保持搜索、勾选、展开与格式身份。</summary>
    public void RefreshLanguage()
    {
        foreach (var node in Roots.Concat(leaves))
        {
            node.RefreshLanguage();
        }
        foreach (var format in new[] { package, independent, source })
        {
            format.RefreshLanguage();
        }
        OnPropertyChanged(nameof(Error));
    }

    /// <summary>取消未完成操作并解除树和语言订阅；异步清理任务可由宿主等待。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Localization.LanguageChanged -= OnLanguageChanged;
        foreach (var node in Roots.Concat(leaves))
        {
            node.SelectionRequested -= OnSelectionRequested;
            node.SetEnabled(false);
        }
        Exported = null;
        cancellation.Cancel();
        OnPropertyChanged(nameof(CanInteract));
        RefreshCommands();
        disposalCompletion = CompleteDisposalAsync();
    }

    private async Task CompleteDisposalAsync()
    {
        await Completion.ConfigureAwait(false);
        cancellation.Dispose();
    }

    /// <summary>等待当前导出回调完成清理后释放取消源。</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await DisposalCompletion.ConfigureAwait(false);
    }
}
