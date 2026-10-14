using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>导出树的保存素材或可见结果分组；分组勾选状态由当前子项派生。</summary>
internal sealed class EffectLibraryExportNode : ObservableObject
{
    private readonly string? titleKey;
    private readonly ImmutableArray<EffectLibraryExportNode> allChildren = [];
    private IReadOnlyList<EffectLibraryExportNode> children = [];
    private bool selected;
    private bool enabled = true;
    private bool matchesSearch = true;
    private bool expanded;

    internal EffectLibraryExportNode(string titleKey, ImmutableArray<EffectLibraryExportNode> children)
    {
        this.titleKey = titleKey;
        allChildren = children;
        this.children = children;
        expanded = true;
    }

    internal EffectLibraryExportNode(SubtitleStylePreset style)
    {
        Style = style;
    }

    internal EffectLibraryExportNode(EffectScriptPreset effect)
    {
        Effect = effect;
        DslId = EffectScriptParser.Parse(effect.Source).Id;
    }

    internal event Action<EffectLibraryExportNode, bool>? SelectionRequested;
    internal ImmutableArray<EffectLibraryExportNode> AllChildren => allChildren;
    public SubtitleStylePreset? Style { get; }
    public EffectScriptPreset? Effect { get; }
    public string? DslId { get; }
    public bool IsGroup => titleKey is not null;
    public string Name => titleKey is not null ? Localization.Get(titleKey) : Style?.Name ?? Effect!.Name;
    public IReadOnlyList<EffectLibraryExportNode> Children => children;
    public bool MatchesSearch => matchesSearch;
    public bool CanSelect => enabled && (!IsGroup || Children.Count > 0);
    public bool HasDetails => IsGroup || DslId is not null;
    public string Details => IsGroup
        ? Localization.Format("EffectLibrary.ExportVisibleGroupSummary", Children.Count(child => child.selected), Children.Count)
        : DslId is not null ? Localization.Format("EffectLibrary.ExportDslId", DslId) : string.Empty;

    public bool IsExpanded
    {
        get => expanded;
        set => SetProperty(ref expanded, value);
    }

    public bool? IsChecked
    {
        get
        {
            if (!IsGroup)
            {
                return selected;
            }
            var count = Children.Count(child => child.selected);
            return count == 0 ? false : count == Children.Count ? true : null;
        }
        set
        {
            if (!CanSelect || value is null)
            {
                return;
            }
            var next = IsGroup && IsChecked is null || value.Value;
            if (!IsGroup && selected == next)
            {
                return;
            }
            SelectionRequested?.Invoke(this, next);
        }
    }

    internal void SetSelected(bool value)
    {
        if (selected != value)
        {
            selected = value;
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    internal void SetEnabled(bool value)
    {
        if (enabled != value)
        {
            enabled = value;
            OnPropertyChanged(nameof(CanSelect));
        }
    }

    internal void ApplySearch(string query)
    {
        foreach (var child in allChildren)
        {
            child.matchesSearch = child.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                child.DslId?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
            child.OnPropertyChanged(nameof(MatchesSearch));
        }
        children = allChildren.Where(child => child.matchesSearch).ToArray();
        OnPropertyChanged(nameof(Children));
        OnPropertyChanged(nameof(CanSelect));
        NotifySelection();
    }

    internal void NotifySelection()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(Details));
    }

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Details));
    }
}
