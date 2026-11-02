using AegiNext.Desktop.I18n;
using AegiNext.SubtitleFormats;

namespace AegiNext.Desktop.EffectLibrary.Transfer;

internal static class EffectLibraryImportDiagnosticLocalization
{
    internal static string Get(SubtitleFormatDiagnostic diagnostic)
    {
        var key = diagnostic.Code switch
        {
            "Ass.StyleScale" => "EffectLibrary.AssStyleScale",
            "Ass.StyleRotation" => "EffectLibrary.AssStyleRotation",
            "Ass.StyleGeometry" => "EffectLibrary.AssStyleGeometry",
            "Ass.StyleSecondaryColor" => "EffectLibrary.AssStyleSecondaryColor",
            "Ass.LetterSpacingRange" => "EffectLibrary.AssLetterSpacingRange",
            "Ass.BorderResampling" => "EffectLibrary.AssBorderResampling",
            "Ass.YCbCrMatrix" => "EffectLibrary.AssYCbCrMatrix",
            "Ass.WrapStyle" => "EffectLibrary.AssWrapStyle",
            "Ass.BorderLayoutResolution" => "EffectLibrary.AssBorderLayoutResolution",
            "Ass.PlayRes" => "EffectLibrary.AssPlayRes",
            "Ass.WrapModeApproximation" => "EffectLibrary.AssWrapModeApproximation",
            _ => null
        };
        return key is null ? diagnostic.Message : Localization.Get(key);
    }
}
