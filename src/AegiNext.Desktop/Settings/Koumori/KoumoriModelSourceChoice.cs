using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Koumori;

/// <summary>模型来源的稳定身份及当前语言显示名称。</summary>
public sealed class KoumoriModelSourceChoice(KoumoriModelSource source) : ObservableObject
{
    private string label = string.Empty;

    public KoumoriModelSource Source { get; } = source;
    public string Label => label;

    internal void UpdateLabel(string value)
    {
        SetProperty(ref label, value, nameof(Label));
    }
}
