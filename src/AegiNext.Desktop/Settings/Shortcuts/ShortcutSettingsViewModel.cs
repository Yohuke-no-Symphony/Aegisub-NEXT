using System.Collections.Immutable;
using System.ComponentModel;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Settings.Shortcuts;

/// <summary>快捷键编辑草稿与配置级验证；有效配置自动提交，按键录入由页面局部适配。</summary>
public sealed class ShortcutSettingsViewModel : ObservableObject
{
    private ShortcutSettingRow[] rows = [];
    private ShortcutSettingsListItem[] items = [];
    private ImmutableArray<ShortcutBinding> committedBindings = [];
    private ShortcutSettingRow? selectedRow;
    private bool recording;
    private bool waitingForKeyRelease;
    private string? error;

    /// <summary>创建完整命令草稿和有效配置的自动提交命令。</summary>
    public ShortcutSettingsViewModel(IEnumerable<ShortcutBinding> bindings)
    {
        ResetCommand = new(Reset);
        ClearCommand = new(() => Gesture = string.Empty, () => HasSelection);
        ToggleRecordingCommand = new(() => IsRecording = !IsRecording, () => HasSelection);
        UpdateBindings(bindings);
    }

    public event EventHandler<SettingsShortcutsChangedEventArgs>? Changed;
    public RelayCommand ResetCommand { get; }
    public RelayCommand ClearCommand { get; }
    public RelayCommand ToggleRecordingCommand { get; }
    public ShortcutSettingRow[] Rows => rows;
    public ShortcutSettingsListItem[] Items => items;
    public ShortcutSettingsListItem? SelectedItem
    {
        get => items.FirstOrDefault(item => ReferenceEquals(item.Row, SelectedRow) && item.IsCommand);
        set
        {
            if (value is null || value.IsCommand)
            {
                SelectedRow = value?.Row;
            }
        }
    }

    public bool HasSelection => SelectedRow is not null;
    public bool IsCaptureActive => IsRecording || IsWaitingForKeyRelease;

    public bool IsWaitingForKeyRelease
    {
        get => waitingForKeyRelease;
        internal set
        {
            if (SetProperty(ref waitingForKeyRelease, value))
            {
                OnPropertyChanged(nameof(IsCaptureActive));
            }
        }
    }

    public string? Error
    {
        get => error;
        private set => SetProperty(ref error, value);
    }

    public string RecordLabel => Localization.Get("Settings." + (IsRecording ? "Recording" : "Record"));

    public bool IsRecording
    {
        get => recording;
        set
        {
            if (SetProperty(ref recording, value))
            {
                OnPropertyChanged(nameof(RecordLabel));
                OnPropertyChanged(nameof(IsCaptureActive));
            }
        }
    }

    public ShortcutSettingRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (SetProperty(ref selectedRow, value))
            {
                IsRecording = false;
                OnPropertyChanged(nameof(SelectedItem));
                OnPropertyChanged(nameof(Gesture));
                OnPropertyChanged(nameof(HasSelection));
                ClearCommand.NotifyCanExecuteChanged();
                ToggleRecordingCommand.NotifyCanExecuteChanged();
                Validate();
            }
        }
    }

    public string Gesture
    {
        get => SelectedRow?.Gesture ?? string.Empty;
        set
        {
            if (SelectedRow is not null && SelectedRow.Gesture != value)
            {
                SelectedRow.Gesture = value;
                OnPropertyChanged();
            }

            Validate();
        }
    }

    /// <summary>替换已提交配置；保留选择命令。</summary>
    public void UpdateBindings(IEnumerable<ShortcutBinding> bindings)
    {
        var values = bindings.ToImmutableArray();
        ShortcutConfiguration.Validate(values);
        if (values.Length != Enum.GetValues<WorkbenchCommand>().Length)
        {
            throw new InvalidDataException("快捷键设置必须包含所有命令。");
        }

        committedBindings = values;
        ReplaceRows(values);
    }

    /// <summary>刷新命令名称，保留无效手势草稿与选择。</summary>
    public void RefreshLanguage()
    {
        ReplaceRows(rows.Select(value => new ShortcutBinding(value.Command, value.Gesture)).ToArray());
        OnPropertyChanged(nameof(RecordLabel));
    }

    /// <summary>录入适配器提交规范化后的语义手势。</summary>
    public void CaptureGesture(string gesture)
    {
        Gesture = gesture;
        IsRecording = false;
    }

    /// <summary>按键适配器拒绝不可表示的输入，保留原绑定。</summary>
    public void RejectGesture()
    {
        IsRecording = false;
        Error = Localization.Get("Settings.ShortcutFormatValidation");
    }

    /// <summary>宿主关闭或失焦时清理录制与等待释放状态。</summary>
    public void CancelCapture()
    {
        IsRecording = false;
        IsWaitingForKeyRelease = false;
    }

    private void ReplaceRows(IEnumerable<ShortcutBinding> bindings)
    {
        var selection = SelectedRow?.Command;
        foreach (var row in rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        rows = bindings.Select(value =>
            new ShortcutSettingRow(value.Command, Localization.Get("Settings." + value.Command.ToString()), value.Gesture)).ToArray();
        items = ShortcutSettingsSections.CreateItems(rows);
        foreach (var row in rows)
        {
            row.PropertyChanged += OnRowChanged;
        }

        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Items));
        SelectedRow = rows.FirstOrDefault(value => value.Command == selection) ?? rows.FirstOrDefault();
        Validate();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShortcutSettingRow.Gesture))
        {
            Validate();
            if (ReferenceEquals(sender, SelectedRow))
            {
                OnPropertyChanged(nameof(Gesture));
            }

            PublishValidBindings();
        }
    }

    private void Validate()
    {
        try
        {
            var conflict = ShortcutConfiguration.FindConflict(rows.Select(value => value.ToBinding()));
            if (conflict is null)
            {
                Error = null;
                return;
            }

            var otherCommand = conflict.FirstCommand == SelectedRow?.Command
                ? conflict.SecondCommand
                : conflict.FirstCommand;
            Error = Localization.Format("Settings.ShortcutConflictValidation", ShortcutGestureFormatter.Format(conflict.Gesture),
                Localization.Get("Settings." + otherCommand.ToString()));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or FormatException)
        {
            Error = Localization.Get("Settings.ShortcutFormatValidation");
        }
    }

    private void Reset()
    {
        ReplaceRows(ShortcutDefaults.CreateBindings());
        PublishValidBindings();
    }

    private void PublishValidBindings()
    {
        Validate();
        if (Error is not null)
        {
            return;
        }

        var values = rows.Select(value => value.ToBinding()).ToImmutableArray();
        if (committedBindings.SequenceEqual(values))
        {
            return;
        }

        committedBindings = values;
        Changed?.Invoke(this, new(values));
    }
}
