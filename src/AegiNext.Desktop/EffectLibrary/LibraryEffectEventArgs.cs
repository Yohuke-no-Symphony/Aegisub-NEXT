using AegiNext.Application.Presets;

namespace AegiNext.Desktop.EffectLibrary;

/// <summary>设置页面向宿主提交已验证脚本源及稳定个人身份。</summary>
public sealed class LibraryEffectEventArgs(EffectScriptPreset preset) : EventArgs
{
    public EffectScriptPreset Preset { get; } = preset;
}
