using AegiNext.Application.Presets;
using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>保留导入决策身份和实时语言标签的行选项。</summary>
public sealed class EffectLibraryImportDecisionChoice : ObservableObject
{
    internal EffectLibraryImportDecisionChoice(EffectLibraryImportDecision decision)
    {
        Decision = decision;
    }

    public EffectLibraryImportDecision Decision { get; }
    public string Text => Localization.Get("EffectLibrary." + (Decision switch
    {
        EffectLibraryImportDecision.IMPORT => "ImportDecision",
        EffectLibraryImportDecision.REPLACE => "ReplaceDecision",
        _ => "SkipDecision"
    }));

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Text));
    }
}
