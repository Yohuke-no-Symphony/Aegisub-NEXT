using System.Collections.ObjectModel;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Koumori;

internal sealed class AutoTimingViewModel : ObservableObject, IDisposable
{
    private readonly WorkbenchSession session;
    private KoumoriPreview? preview;
    private KoumoriPreviewRow? selectedRow;
    private bool isBusy;
    private bool disposed;
    private double progress;
    private string status = string.Empty;

    internal AutoTimingViewModel(WorkbenchSession session)
    {
        this.session = session;
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => RunCommand.Cancel(), () => IsBusy);
        ApplyCommand = new RelayCommand(Apply, () => !IsBusy && preview is not null);
        AuditionCommand = new AsyncRelayCommand(AuditionAsync, () => !IsBusy && preview is not null && SelectedRow is not null);
        SelectAllCommand = new RelayCommand(() => SelectAll(true));
        SelectNoneCommand = new RelayCommand(() => SelectAll(false));
    }

    public ObservableCollection<KoumoriPreviewRow> Rows { get; } = [];
    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand ApplyCommand { get; }
    public IAsyncRelayCommand AuditionCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand SelectNoneCommand { get; }
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                RefreshCommands();
            }
        }
    }
    public bool CanEdit => !IsBusy;
    public double Progress
    {
        get => progress;
        private set => SetProperty(ref progress, value);
    }
    public string Status
    {
        get => status;
        private set
        {
            if (SetProperty(ref status, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public KoumoriPreviewRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (SetProperty(ref selectedRow, value))
            {
                AuditionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        preview = null;
        Rows.Clear();
        Progress = 0;
        Status = Localization.Get("Koumori.Analyzing");
        try
        {
            var value = await session.AnalyzeKoumoriAsync(cancellationToken,
                new Progress<double>(fraction =>
                {
                    if (!disposed)
                    {
                        Progress = fraction * 100;
                    }
                }));
            if (disposed)
            {
                return;
            }
            preview = value;
            foreach (var segment in value.Result.Segments)
            {
                var start = segment.StartTime < MediaTime.Zero ? MediaTime.Zero : segment.StartTime;
                var end = segment.EndTime > value.Duration ? value.Duration : segment.EndTime;
                if (end > start)
                {
                    Rows.Add(new(start, end, segment.Score));
                }
            }
            SelectedRow = Rows.FirstOrDefault();
            Status = Localization.Format("Koumori.Result", Rows.Count, value.Result.ModelId);
        }
        catch (OperationCanceledException)
        {
            Status = Localization.Get("Koumori.Canceled");
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    private void Apply()
    {
        if (preview is not { } value)
        {
            return;
        }
        try
        {
            var intervals = new List<MediaTimeRange>();
            foreach (var row in Rows.Where(row => row.IsSelected))
            {
                if (!row.TryRead(value.Duration, out var start, out var end))
                {
                    Status = Localization.Get("Koumori.InvalidInterval");
                    return;
                }
                intervals.Add(new(start, end));
            }
            if (intervals.Count == 0)
            {
                Status = Localization.Get("Koumori.SelectRows");
                return;
            }
            var count = session.ApplyKoumoriPreview(value, intervals);
            preview = null;
            Status = Localization.Format("Koumori.Applied", count);
            RefreshCommands();
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    private async Task AuditionAsync(CancellationToken cancellationToken)
    {
        if (preview is not { } value || SelectedRow is not { } row)
        {
            return;
        }
        if (!row.TryRead(value.Duration, out var start, out var end))
        {
            Status = Localization.Get("Koumori.InvalidInterval");
            return;
        }
        try
        {
            await session.AuditionKoumoriAsync(value, new(start, end), cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    private void SelectAll(bool selected)
    {
        foreach (var row in Rows)
        {
            row.IsSelected = selected;
        }
    }

    private void RefreshCommands()
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ApplyCommand.NotifyCanExecuteChanged();
        AuditionCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        disposed = true;
        RunCommand.Cancel();
        AuditionCommand.Cancel();
    }
}
