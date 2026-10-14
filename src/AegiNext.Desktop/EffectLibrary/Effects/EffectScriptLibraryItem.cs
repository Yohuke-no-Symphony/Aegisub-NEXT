using AegiNext.Application.Presets;

namespace AegiNext.Desktop.EffectLibrary.Effects;

/// <summary>内置只读脚本及个人模板在设置列表中的统一投影。</summary>
public sealed record EffectScriptLibraryItem(EffectScriptPreset Preset, bool IsBuiltin)
{
    public Guid Id => Preset.Id;
    public string Name => Preset.Name;
}
