using System.Collections.Immutable;
using AegiNext.Application.Presets;

namespace AegiNext.Desktop.EffectLibrary;

/// <summary>左侧所选脚本的已保存快照，包含内置模板。</summary>
public sealed class LibraryEffectsExportEventArgs(ImmutableArray<EffectScriptPreset> presets) : EventArgs
{
    public ImmutableArray<EffectScriptPreset> Presets { get; } = presets;
}
