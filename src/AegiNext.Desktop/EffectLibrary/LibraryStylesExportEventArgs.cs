using System.Collections.Immutable;
using AegiNext.Core.Presets;

namespace AegiNext.Desktop.EffectLibrary;

/// <summary>左侧所选样式的已保存快照，不包含编辑草稿。</summary>
public sealed class LibraryStylesExportEventArgs(ImmutableArray<SubtitleStylePreset> presets) : EventArgs
{
    public ImmutableArray<SubtitleStylePreset> Presets { get; } = presets;
}
