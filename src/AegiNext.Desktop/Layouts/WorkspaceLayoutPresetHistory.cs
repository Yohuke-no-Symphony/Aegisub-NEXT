namespace AegiNext.Desktop.Layouts;

/// <summary>固定历史内置布局结构，避免新预设的分栏和比例变化影响旧布局识别。</summary>
internal static class WorkspaceLayoutPresetHistory
{
    internal static WorkspaceLayoutSnapshot? Find(string presetId, int version, IReadOnlyList<string> panelIds)
    {
        var previous = VersionFive(presetId);
        if (previous is null)
        {
            return null;
        }
        var included = panelIds.ToHashSet(StringComparer.Ordinal);
        return previous with
        {
            Version = version,
            Main = IncludePanels(previous.Main, included),
            HiddenPanelIds = previous.HiddenPanelIds.Where(included.Contains).ToArray()
        };
    }

    private static WorkspaceLayoutSnapshot? VersionFive(string presetId) => presetId switch
    {
        WorkspaceLayoutPresets.STANDARD => new()
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.58,
                    WorkspaceLayoutPresets.Tabs(0.75, "preview"),
                    WorkspaceLayoutPresets.Tabs(0.25, "styles", "effects", "masks", "export", "assets")),
                WorkspaceLayoutPresets.Tabs(0.19, "timeline"),
                WorkspaceLayoutPresets.Tabs(0.23, "subtitles", "log")),
            HiddenPanelIds = ["subtitleDetails"], FocusedPanelId = "preview"
        },
        WorkspaceLayoutPresets.TIMING => new()
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.7,
                    WorkspaceLayoutPresets.Tabs(0.58, "preview"), WorkspaceLayoutPresets.Tabs(0.42, "subtitles")),
                WorkspaceLayoutPresets.Tabs(0.3, "timeline", "log")),
            HiddenPanelIds = ["styles", "effects", "masks", "export", "subtitleDetails", "assets"],
            FocusedPanelId = "subtitles"
        },
        WorkspaceLayoutPresets.EFFECTS => new()
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.72,
                    WorkspaceLayoutPresets.Tabs(0.66, "preview"), WorkspaceLayoutPresets.Tabs(0.34, "effects", "masks", "styles", "assets")),
                WorkspaceLayoutPresets.Tabs(0.28, "timeline", "log")),
            HiddenPanelIds = ["subtitles", "export", "subtitleDetails"], FocusedPanelId = "effects"
        },
        WorkspaceLayoutPresets.ENCODE => new()
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Split("horizontal", 1,
                WorkspaceLayoutPresets.Tabs(0.66, "preview"), WorkspaceLayoutPresets.Tabs(0.34, "export", "log")),
            HiddenPanelIds = ["subtitles", "styles", "effects", "masks", "timeline", "subtitleDetails", "assets"],
            FocusedPanelId = "export"
        },
        _ => null
    };

    private static LayoutNodeSnapshot IncludePanels(LayoutNodeSnapshot node, HashSet<string> included) => node with
    {
        Children = node.Children.Where(child => child.PanelId is null || included.Contains(child.PanelId))
            .Select(child => IncludePanels(child, included)).ToArray()
    };
}
