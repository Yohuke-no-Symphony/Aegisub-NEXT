using AegiNext.Application.Presets;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>一个已冻结导入行的本地化展示和允许决策，不修改原始计划。</summary>
public sealed class EffectLibraryImportRowViewModel : ObservableObject
{
    private EffectLibraryImportDecisionChoice selectedDecision;
    private bool isBusy;

    /// <summary>由计划行生成有限决策列表；冲突默认跳过，新项默认导入。</summary>
    public EffectLibraryImportRowViewModel(EffectLibraryImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Row = row;
        var choices = new List<EffectLibraryImportDecisionChoice> { new(EffectLibraryImportDecision.SKIP) };
        if (row.CanImport)
        {
            choices.Add(new(EffectLibraryImportDecision.IMPORT));
        }
        if (row.CanReplace)
        {
            choices.Add(new(EffectLibraryImportDecision.REPLACE));
        }
        DecisionChoices = choices;
        selectedDecision = row.Status == EffectLibraryImportStatus.NEW && row.ConflictGroupId is null
            ? choices.Single(choice => choice.Decision == EffectLibraryImportDecision.IMPORT) : choices[0];
    }

    internal event EventHandler? DecisionChanged;
    public EffectLibraryImportRow Row { get; }
    public string Name => Row.Name;
    public string SourceName => Row.SourceName;
    public string KindText => Localization.Get(Row.Kind == EffectLibraryItemKind.STYLE ? "EffectLibrary.StyleKind" : "EffectLibrary.EffectKind");
    public string StatusText => Localization.Get("EffectLibrary." + (Row.Status switch
    {
        EffectLibraryImportStatus.NEW => "ImportNew",
        EffectLibraryImportStatus.IDENTICAL => "ImportIdentical",
        _ => "ImportConflict"
    }));
    public IReadOnlyList<EffectLibraryImportDecisionChoice> DecisionChoices { get; }
    public bool IsDecisionAvailable => !isBusy && DecisionChoices.Count > 1;
    public bool HasDslIdChange => Row.DslIdChanges;
    public string DslIdChangeText => Localization.Format("EffectLibrary.DslIdChange", Row.PreviousDslId ?? string.Empty, Row.DslId ?? string.Empty);
    public bool HasConflictDescription => Row.ConflictReason != EffectLibraryImportConflictReason.NONE;
    public string ConflictDescription => Row.ConflictReason switch
    {
        EffectLibraryImportConflictReason.DIFFERENT_CONTENT => Localization.Get("EffectLibrary.ContentConflict"),
        EffectLibraryImportConflictReason.MULTIPLE_TARGETS => Localization.Format("EffectLibrary.IdentityConflict", string.Join(", ", Row.ConflictingNames)),
        EffectLibraryImportConflictReason.MULTIPLE_INPUT_VERSIONS => Localization.Get("EffectLibrary.InputVersionsConflict"),
        EffectLibraryImportConflictReason.BUILTIN_READ_ONLY => Localization.Get("EffectLibrary.BuiltinConflict"),
        _ => string.Empty
    };

    public EffectLibraryImportDecisionChoice? SelectedDecisionChoice
    {
        get => selectedDecision;
        set
        {
            if (value is not null)
            {
                Decision = value.Decision;
            }
        }
    }

    public EffectLibraryImportDecision Decision
    {
        get => selectedDecision.Decision;
        set
        {
            var next = DecisionChoices.FirstOrDefault(choice => choice.Decision == value) ??
                throw new ArgumentException("该行不允许此导入决策。", nameof(value));
            if (SetProperty(ref selectedDecision, next, nameof(SelectedDecisionChoice)))
            {
                OnPropertyChanged(nameof(Decision));
                DecisionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    internal void SetBusy(bool value)
    {
        if (isBusy != value)
        {
            isBusy = value;
            OnPropertyChanged(nameof(IsDecisionAvailable));
        }
    }

    /// <summary>刷新展示文字，保留行身份、输入名称和当前决策。</summary>
    public void RefreshLanguage()
    {
        foreach (var choice in DecisionChoices)
        {
            choice.RefreshLanguage();
        }
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DslIdChangeText));
        OnPropertyChanged(nameof(ConflictDescription));
    }
}
