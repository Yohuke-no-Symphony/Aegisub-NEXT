using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Panels.Styles;

/// <summary>维持枚举选择项身份，语言刷新只改变显示文字。</summary>
internal sealed class GraphicStyleChoice(string localizationKey) : ObservableObject
{
    private string label = Localization.Get(localizationKey);

    public string Label
    {
        get => label;
        private set => SetProperty(ref label, value);
    }

    internal void RefreshLanguage() => Label = Localization.Get(localizationKey);
}
