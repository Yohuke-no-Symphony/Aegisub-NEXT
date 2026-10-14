using AegiNext.Application.ColorTags;
using AegiNext.Desktop.Layouts;
using AegiNext.Export.Presets;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed record UserSettingsBundle
{
    public int SourceVersion { get; init; } = UserSettingsBundleStore.CURRENT_VERSION;
    public WorkbenchPreferences Preferences { get; init; } = new();
    public VideoExportPresetCollection ExportPresets { get; init; } = new();
    /// <summary>完整个人标记库；旧版或省略标签的 v3 包为 null，恢复时保留本地文件或缺失状态。</summary>
    public SubtitleColorTagLibraryDocument? ColorTags { get; init; } = new();
    public WorkspaceLayoutFile Layouts { get; init; } = new();
}
