using AegiNext.Desktop.I18n;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

/// <summary>保留格式身份和实时语言标签的分享选项。</summary>
public sealed class EffectLibraryExportFormatChoice : ObservableObject
{
    internal EffectLibraryExportFormatChoice(EffectLibraryExportFormat format)
    {
        Format = format;
    }

    public EffectLibraryExportFormat Format { get; }
    public string Text => Localization.Get("EffectLibrary." + (Format switch
    {
        EffectLibraryExportFormat.INDEPENDENT => "ExportIndependent",
        EffectLibraryExportFormat.PACKAGE => "ExportPackage",
        _ => "ExportSource"
    }));

    internal void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Text));
    }
}
