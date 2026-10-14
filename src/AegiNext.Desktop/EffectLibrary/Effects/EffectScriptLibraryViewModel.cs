using AegiNext.Desktop.EffectLibrary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.EffectLibrary.Effects;

/// <summary>个人脚本库的选择与独立编辑草稿；持久化由组合根提供。</summary>
public sealed class EffectScriptLibraryViewModel : ObservableObject
{
    private ImmutableArray<EffectScriptPreset> presets = [];
    private ImmutableArray<EffectScriptLibraryItem> items = [];
    private ImmutableArray<Guid> selectedIds = [];
    private readonly Dictionary<Guid, EffectScriptPreset> drafts = new();
    private EffectScriptPreset? newDraft;
    private EffectScriptLibraryItem? selectedEffect;
    private Guid? loadedDraftId;
    private string name = string.Empty;
    private string source = string.Empty;
    private string? error;
    private string? validationStatus;
    private int diagnosticLine;
    private int diagnosticColumn = 1;
    private bool loading;
    private bool busy;
    private bool changingSelection;
    private Task<bool>? pendingSave;

    /// <summary>创建页面命令；实际个人库读写由组合根承担。</summary>
    public EffectScriptLibraryViewModel()
    {
        AddCommand = new(() =>
        {
            var operation = AddAsync();
            SelectionCompletion = operation;
            return operation;
        }, () => !IsBusy && !changingSelection);
        DeleteCommand = new(Delete, () => CanDelete);
        SaveCommand = new(() =>
        {
            var operation = SaveAsync();
            SelectionCompletion = operation;
            return operation;
        }, () => CanEdit);
        ExportCommand = new(Export, () => !SelectedIds.IsEmpty && !IsBusy && !changingSelection);
        ImportCommand = new(() => ImportRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy && !changingSelection);
        ValidateCommand = new(() => ValidateDraft(false), () => HasDraft && SelectedIds.Length <= 1 && !IsBusy && !changingSelection);
        UpdateEffects([]);
    }

    public event EventHandler<LibraryEffectEventArgs>? SaveRequested;
    public event EventHandler<LibraryEffectDeleteEventArgs>? DeleteRequested;
    public event EventHandler<LibraryEffectsExportEventArgs>? ExportRequested;
    public event EventHandler<EffectScriptValidationFailedEventArgs>? ValidationFailed;
    public event EventHandler? ImportRequested;
    public AsyncRelayCommand AddCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ValidateCommand { get; }
    public ImmutableArray<EffectScriptLibraryItem> Effects => items;
    public ImmutableArray<Guid> SelectedIds => selectedIds;
    public EffectScriptPreset? Draft => newDraft ?? (SelectedIds.Length == 1 && selectedEffect is { } selected
        ? drafts.GetValueOrDefault(selected.Id) ?? selected.Preset : null);
    public bool HasDraft => Draft is not null;
    public bool HasSelection => !SelectedIds.IsEmpty;
    public bool IsReadOnly => !HasDraft || SelectedIds.Length > 1 || newDraft is null && selectedEffect?.IsBuiltin != false;
    public bool CanEdit => HasDraft && !IsReadOnly && !IsBusy && !changingSelection;
    public bool CanDelete => !IsBusy && !changingSelection && (newDraft is not null ||
        !SelectedIds.IsEmpty && SelectedIds.All(id => items.Any(item => item.Id == id && !item.IsBuiltin)));
    public bool IsDirty => newDraft is not null || SelectedIds.Length == 1 && selectedEffect is { } selected && drafts.ContainsKey(selected.Id);
    public string EditorStatus => SelectedIds.Length > 1 ? Localization.Format("Settings.MultipleScriptsSelected", SelectedIds.Length) :
        Localization.Get("Settings." + (IsReadOnly ? "BuiltinScriptReadOnly" : IsDirty ? "ScriptUnsaved" : "ScriptSaved"));
    internal Func<EffectScriptPreset, Task<bool>>? SaveDraftAsync { get; set; }
    internal Func<Task<int>>? ConfirmLeaveAsync { get; set; }
    internal Task SelectionCompletion { get; private set; } = Task.CompletedTask;

    public EffectScriptLibraryItem? SelectedEffect
    {
        get => selectedEffect;
        set
        {
            if (!loading)
            {
                SelectEffects(value?.Id, value is null ? [] : [value.Id]);
            }
        }
    }

    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                RecordDraft(false);
            }
        }
    }

    public string Source
    {
        get => source;
        set
        {
            if (SetProperty(ref source, value))
            {
                RecordDraft(true);
            }
        }
    }

    public string? Error
    {
        get => error;
        private set => SetProperty(ref error, value);
    }

    public string? ValidationStatus
    {
        get => validationStatus;
        private set => SetProperty(ref validationStatus, value);
    }

    public int DiagnosticLine
    {
        get => diagnosticLine;
        private set => SetProperty(ref diagnosticLine, value);
    }

    public int DiagnosticColumn
    {
        get => diagnosticColumn;
        private set => SetProperty(ref diagnosticColumn, value);
    }

    public bool IsBusy
    {
        get => busy;
        set
        {
            if (SetProperty(ref busy, value))
            {
                RefreshActions();
            }
        }
    }

    /// <summary>按稳定标识同步列表选择；模型刷新期间忽略控件产生的临时清空。</summary>
    public void SelectEffects(Guid? primaryId, IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (loading)
        {
            return;
        }

        var selection = ids.ToHashSet();
        var nextIds = items.Where(item => selection.Contains(item.Id)).Select(item => item.Id).ToImmutableArray();
        var nextPrimary = items.FirstOrDefault(item => item.Id == primaryId && selection.Contains(item.Id)) ??
            items.FirstOrDefault(item => nextIds.Contains(item.Id));
        if (nextIds.SequenceEqual(SelectedIds) && nextPrimary?.Id == selectedEffect?.Id)
        {
            return;
        }

        newDraft = null;
        selectedIds = nextIds;
        selectedEffect = nextPrimary;
        NotifySelection();
        LoadDraft();
    }

    /// <summary>先处理未保存草稿，再切换列表选择；取消或保存失败时保留原选择。</summary>
    public Task<bool> SelectEffectsAsync(Guid? primaryId, IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (loading || changingSelection || IsBusy)
        {
            return Task.FromResult(false);
        }

        var selection = ids.ToImmutableArray();
        if (newDraft is null && primaryId == selectedEffect?.Id && SelectedIds.ToHashSet().SetEquals(selection))
        {
            return Task.FromResult(true);
        }

        var operation = ChangeSelectionAsync(primaryId, selection);
        SelectionCompletion = operation;
        return operation;
    }

    /// <summary>保存有效的可编辑脏草稿；没有持久化回调或提交失败时拒绝丢弃草稿。</summary>
    public Task<bool> SavePendingAsync()
    {
        if (pendingSave is { IsCompleted: false })
        {
            return pendingSave;
        }

        var operation = SavePendingCoreAsync();
        pendingSave = operation;
        return operation;
    }

    /// <summary>询问保存、不保存或取消；只有保存成功或明确丢弃后才能离开。</summary>
    public async Task<bool> PrepareToLeaveAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        if (ConfirmLeaveAsync is { } confirm)
        {
            var decision = await confirm();
            if (decision == 1)
            {
                DiscardDraft();
                return true;
            }
            if (decision != 0)
            {
                return false;
            }
        }

        return await SavePendingAsync();
    }

    /// <summary>恢复当前已保存脚本，或直接丢弃尚未入库的新草稿。</summary>
    public void DiscardDraft()
    {
        if (newDraft is not null)
        {
            newDraft = null;
        }
        else if (selectedEffect is { } selected)
        {
            drafts.Remove(selected.Id);
        }

        LoadDraft();
    }

    /// <summary>同步成功提交的个人库；保留其他草稿、多选标识和当前诊断。</summary>
    public void UpdateEffects(IEnumerable<EffectScriptPreset> values, Guid? selectedId = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        var updated = values.ToImmutableArray();
        foreach (var id in presets.Select(item => item.Id).Except(updated.Select(item => item.Id)))
        {
            drafts.Remove(id);
        }

        presets = updated;
        foreach (var preset in presets)
        {
            if (drafts.TryGetValue(preset.Id, out var pending) && Matches(pending, preset))
            {
                drafts.Remove(preset.Id);
            }
        }

        var savedNew = newDraft is { } created ? presets.FirstOrDefault(preset => Matches(created, preset)) : null;
        if (savedNew is not null)
        {
            newDraft = null;
            selectedId = savedNew.Id;
        }

        RebuildItems(selectedId);
    }

    /// <summary>仅刷新内置名称与提示，保留个人草稿、多选和诊断。</summary>
    public void RefreshLanguage()
    {
        RebuildItems(null);
        if (validationStatus is not null && Error is null)
        {
            ValidationStatus = Localization.Get("Settings.ScriptValid");
        }
    }

    private async Task<bool> ChangeSelectionAsync(Guid? primaryId, ImmutableArray<Guid> ids)
    {
        changingSelection = true;
        RefreshActions();
        try
        {
            if (!await PrepareToLeaveAsync())
            {
                return false;
            }

            SelectEffects(primaryId, ids);
            return true;
        }
        finally
        {
            changingSelection = false;
            RefreshActions();
        }
    }

    private async Task<bool> SavePendingCoreAsync()
    {
        if (!IsDirty || IsReadOnly)
        {
            return true;
        }

        if (!ValidateDraft(true) || SaveDraftAsync is not { } save || Draft is not { } pending)
        {
            return false;
        }

        return await save(pending with { Name = Name.Trim(), Source = Source });
    }

    private async Task SaveAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        if (SaveDraftAsync is not null)
        {
            await SavePendingAsync();
        }
        else if (ValidateDraft(true) && Draft is { } pending)
        {
            SaveRequested?.Invoke(this, new(pending with { Name = Name.Trim(), Source = Source }));
        }
    }

    private void RebuildItems(Guid? selectedId)
    {
        var wasLoading = loading;
        var hadSelection = !SelectedIds.IsEmpty;
        var initialize = items.IsEmpty;
        loading = true;
        try
        {
            var builtins = BuiltinEffectScripts.Templates.Select(template =>
            {
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(template.Script.Id)).AsSpan(0, 16));
                return new EffectScriptLibraryItem(new(id, Localization.Get("Settings.Effect_" + template.Script.Id), template.Source), true);
            });
            items = builtins.Concat(presets.Select(preset => new EffectScriptLibraryItem(preset, false))).ToImmutableArray();
            selectedIds = newDraft is not null ? [] :
                selectedId is { } id && items.Any(item => item.Id == id) ? [id] :
                items.Where(item => SelectedIds.Contains(item.Id)).Select(item => item.Id).ToImmutableArray();
            if (newDraft is null && selectedIds.IsEmpty && (hadSelection || initialize))
            {
                selectedIds = items.IsEmpty ? [] : [items[0].Id];
            }

            selectedEffect = items.FirstOrDefault(item => item.Id == selectedId && selectedIds.Contains(item.Id)) ??
                items.FirstOrDefault(item => item.Id == selectedEffect?.Id && selectedIds.Contains(item.Id)) ??
                items.FirstOrDefault(item => selectedIds.Contains(item.Id));
            OnPropertyChanged(nameof(Effects));
            NotifySelection();
            LoadDraft();
        }
        finally
        {
            loading = wasLoading;
        }
    }

    private void NotifySelection()
    {
        OnPropertyChanged(nameof(SelectedIds));
        OnPropertyChanged(nameof(SelectedEffect));
    }

    private void LoadDraft()
    {
        var wasLoading = loading;
        loading = true;
        var value = Draft;
        var preserveDiagnostics = loadedDraftId == value?.Id && Source == (value?.Source ?? string.Empty) &&
            Name == (value?.Name ?? string.Empty);
        Name = value?.Name ?? string.Empty;
        Source = value?.Source ?? string.Empty;
        loadedDraftId = value?.Id;
        loading = wasLoading;
        if (!preserveDiagnostics)
        {
            ClearDiagnostics();
        }
        RefreshActions();
    }

    private void RecordDraft(bool sourceChanged)
    {
        if (loading || IsReadOnly || Draft is not { } current)
        {
            return;
        }

        var value = current with { Name = Name, Source = Source };
        if (newDraft is not null)
        {
            newDraft = value;
        }
        else if (presets.FirstOrDefault(item => item.Id == value.Id) == value)
        {
            drafts.Remove(value.Id);
        }
        else
        {
            drafts[value.Id] = value;
        }

        if (sourceChanged || DiagnosticLine == 0)
        {
            ClearDiagnostics();
        }
        RefreshActions();
    }

    private async Task AddAsync()
    {
        changingSelection = true;
        RefreshActions();
        try
        {
            if (!await PrepareToLeaveAsync())
            {
                return;
            }

            var id = Guid.NewGuid();
            var scriptId = "effect-" + id.ToString("N");
            newDraft = new(id, UniqueName(Localization.Get("Settings.NewEffect")),
                $"effect \"{scriptId}\" version 1\nshort-clip compress\nsegment enter fixed 300ms\n    at 0 opacity 0 ease-out\n    at 1 opacity base\nend\nsegment stay flex 1\n    at 0 opacity base hold\n    at 1 opacity base\nend\n");
            selectedEffect = null;
            selectedIds = [];
            NotifySelection();
            LoadDraft();
        }
        finally
        {
            changingSelection = false;
            RefreshActions();
        }
    }

    private void Delete()
    {
        if (!CanDelete)
        {
            return;
        }

        if (newDraft is not null)
        {
            DeleteRequested?.Invoke(this, new([], true, newDraft.Id));
        }
        else
        {
            DeleteRequested?.Invoke(this, new(SelectedIds));
        }
    }

    private void Export()
    {
        if (!SelectedIds.IsEmpty)
        {
            ExportRequested?.Invoke(this, new(items.Where(item => SelectedIds.Contains(item.Id))
                .Select(item => item.Preset).ToImmutableArray()));
        }
    }

    private string UniqueName(string proposed)
    {
        var names = items.Select(item => drafts.GetValueOrDefault(item.Id)?.Name ?? item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = proposed;
        var index = 2;
        while (names.Contains(result))
        {
            result = $"{proposed} {index++}";
        }
        return result;
    }

    private bool ValidateDraft(bool saving)
    {
        if (Draft is not { } current)
        {
            return false;
        }

        try
        {
            var preset = current with { Name = Name.Trim(), Source = Source };
            if (saving)
            {
                var values = presets.Where(item => item.Id != preset.Id).Append(preset).ToImmutableArray();
                EffectScriptPresetService.Validate(new EffectScriptPresetDocument { Presets = values });
            }
            else
            {
                _ = EffectScriptParser.Parse(Source);
            }

            ClearDiagnostics();
            ValidationStatus = Localization.Get("Settings.ScriptValid");
            return true;
        }
        catch (EffectScriptException failure)
        {
            Error = failure.Message;
            DiagnosticLine = failure.Line;
            DiagnosticColumn = failure.Column;
            ValidationStatus = null;
            ValidationFailed?.Invoke(this, new(failure));
            return false;
        }
        catch (InvalidDataException failure)
        {
            Error = failure.Message;
            ValidationStatus = null;
            ValidationFailed?.Invoke(this, new(failure));
            return false;
        }
    }

    private static bool Matches(EffectScriptPreset pending, EffectScriptPreset saved)
    {
        return pending.Id == saved.Id && pending.Source == saved.Source &&
            string.Equals(pending.Name.Trim(), saved.Name, StringComparison.Ordinal);
    }

    private void ClearDiagnostics()
    {
        Error = null;
        ValidationStatus = null;
        DiagnosticLine = 0;
        DiagnosticColumn = 1;
    }

    private void RefreshActions()
    {
        foreach (var property in new[] { nameof(Draft), nameof(HasDraft), nameof(HasSelection), nameof(IsReadOnly), nameof(CanEdit), nameof(CanDelete), nameof(IsDirty), nameof(EditorStatus) })
        {
            OnPropertyChanged(property);
        }

        foreach (var command in new IRelayCommand[] { AddCommand, DeleteCommand, SaveCommand, ExportCommand, ImportCommand, ValidateCommand })
        {
            command.NotifyCanExecuteChanged();
        }
    }
}
