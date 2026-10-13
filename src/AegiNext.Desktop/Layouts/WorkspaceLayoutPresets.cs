namespace AegiNext.Desktop.Layouts;

internal static class WorkspaceLayoutPresets
{
    public const string STANDARD = "standard";
    public const string TIMING = "timing";
    public const string EFFECTS = "effects";
    public const string ENCODE = "encode";

    public static WorkspaceLayoutSnapshot Standard { get; } = new()
    {
        Main = Split("vertical", 1,
            Split("horizontal", 0.58,
                Split("vertical", 0.75,
                    Tabs(0.16, WorkbenchPanelIds.DRAWING_TOOLS),
                    Tabs(0.84, WorkbenchPanelIds.PREVIEW)),
                Tabs(0.25, WorkbenchPanelIds.STYLES, WorkbenchPanelIds.EFFECTS, WorkbenchPanelIds.EFFECT_PRESETS,
                    WorkbenchPanelIds.MASKS, WorkbenchPanelIds.EXPORT, WorkbenchPanelIds.ASSETS)),
            Tabs(0.19, WorkbenchPanelIds.TIMELINE),
            Tabs(0.23, WorkbenchPanelIds.SUBTITLES, WorkbenchPanelIds.LOG)),
        HiddenPanelIds = [WorkbenchPanelIds.SUBTITLE_DETAILS],
        FocusedPanelId = WorkbenchPanelIds.PREVIEW
    };

    public static IReadOnlyList<WorkspaceLayoutPreset> BuiltIn { get; } =
    [
        new(STANDARD, STANDARD, true, Standard),
        new(TIMING, TIMING, true, new()
        {
            Main = Split("vertical", 1,
                Split("horizontal", 0.7, Tabs(0.58, WorkbenchPanelIds.PREVIEW), Tabs(0.42, WorkbenchPanelIds.SUBTITLES)),
                Tabs(0.3, WorkbenchPanelIds.TIMELINE, WorkbenchPanelIds.LOG)),
            HiddenPanelIds = [WorkbenchPanelIds.STYLES, WorkbenchPanelIds.EFFECTS, WorkbenchPanelIds.MASKS,
                WorkbenchPanelIds.EXPORT, WorkbenchPanelIds.SUBTITLE_DETAILS, WorkbenchPanelIds.ASSETS,
                WorkbenchPanelIds.DRAWING_TOOLS, WorkbenchPanelIds.EFFECT_PRESETS],
            FocusedPanelId = WorkbenchPanelIds.SUBTITLES
        }),
        new(EFFECTS, EFFECTS, true, new()
        {
            Main = Split("vertical", 1,
                Split("horizontal", 0.72,
                    Split("vertical", 0.66,
                        Tabs(0.16, WorkbenchPanelIds.DRAWING_TOOLS),
                        Tabs(0.84, WorkbenchPanelIds.PREVIEW)),
                    Tabs(0.34, WorkbenchPanelIds.EFFECTS, WorkbenchPanelIds.EFFECT_PRESETS,
                        WorkbenchPanelIds.MASKS, WorkbenchPanelIds.STYLES, WorkbenchPanelIds.ASSETS)),
                Tabs(0.28, WorkbenchPanelIds.TIMELINE, WorkbenchPanelIds.LOG)),
            HiddenPanelIds = [WorkbenchPanelIds.SUBTITLES, WorkbenchPanelIds.EXPORT, WorkbenchPanelIds.SUBTITLE_DETAILS],
            FocusedPanelId = WorkbenchPanelIds.EFFECTS
        }),
        new(ENCODE, ENCODE, true, new()
        {
            Main = Split("horizontal", 1, Tabs(0.66, WorkbenchPanelIds.PREVIEW), Tabs(0.34, WorkbenchPanelIds.EXPORT, WorkbenchPanelIds.LOG)),
            HiddenPanelIds = [WorkbenchPanelIds.SUBTITLES, WorkbenchPanelIds.STYLES, WorkbenchPanelIds.EFFECTS,
                WorkbenchPanelIds.MASKS, WorkbenchPanelIds.TIMELINE, WorkbenchPanelIds.SUBTITLE_DETAILS,
                WorkbenchPanelIds.ASSETS, WorkbenchPanelIds.DRAWING_TOOLS, WorkbenchPanelIds.EFFECT_PRESETS],
            FocusedPanelId = WorkbenchPanelIds.EXPORT
        })
    ];

    internal static LayoutNodeSnapshot Tabs(double proportion, params string[] ids)
    {
        return new()
        {
            Kind = "tabs", Proportion = proportion, ActivePanelId = ids.FirstOrDefault(),
            Children = ids.Select(id => new LayoutNodeSnapshot { Kind = "panel", PanelId = id }).ToArray()
        };
    }

    internal static LayoutNodeSnapshot Split(string orientation, double proportion, params LayoutNodeSnapshot[] children)
    {
        return new() { Kind = "split", Orientation = orientation, Proportion = proportion, Children = children };
    }
}
