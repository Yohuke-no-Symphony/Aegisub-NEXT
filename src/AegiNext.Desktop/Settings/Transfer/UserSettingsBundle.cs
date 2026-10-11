using AegiNext.Application.ColorTags;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Layouts;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed record UserSettingsBundle
{
    public WorkbenchPreferences Preferences { get; init; } = new();
    public SubtitleStylePresetCollection Styles { get; init; } = new();
    public EffectScriptPresetDocument Effects { get; init; } = new();
    public VideoExportPresetCollection ExportPresets { get; init; } = new();
    /// <summary>完整个人标记库；旧版包为 null，恢复时保留本地库。</summary>
    public SubtitleColorTagLibraryDocument? ColorTags { get; init; } = new();
    public WorkspaceLayoutFile Layouts { get; init; } = new();
}
