using System.Collections.Immutable;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Settings;

/// <summary>批量导出的已保存预设不可变快照，不包含编辑草稿。</summary>
public sealed class SettingsExportPresetsEventArgs(ImmutableArray<VideoExportPreset> presets) : EventArgs
{
    public ImmutableArray<VideoExportPreset> Presets { get; } = presets;
}
