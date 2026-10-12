using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Panels.Export;

internal sealed class ExportMethodChoice(ExportMethod method) : ObservableObject
{
    private string label = string.Empty;

    public ExportMethod Method { get; } = method;
    public string Label => label;

    internal void RefreshLanguage()
    {
        var key = Method switch
        {
            ExportMethod.BURN_IN => "Workbench.ExportBurnIn",
            ExportMethod.SUBTITLE_MUX => "Workbench.ExportSubtitleMux",
            _ => throw new InvalidOperationException("未知导出方式。")
        };
        SetProperty(ref label, Localization.Get(key), nameof(Label));
    }
}
