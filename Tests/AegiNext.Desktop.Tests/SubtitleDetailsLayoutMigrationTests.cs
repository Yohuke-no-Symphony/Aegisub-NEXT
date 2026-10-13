using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleDetailsLayoutMigrationTests
{
    [Fact]
    public void VersionTwoPersonalLayoutRetainsTopologyAndAddsHiddenDetailsPanel()
    {
        var previous = VersionTwoStandard() with { FocusedPanelId = WorkbenchPanelIds.SUBTITLES };
        var upgraded = WorkspaceLayoutMigration.Upgrade(new() { Version = 2, Current = previous });
        Assert.Equal(WorkspaceLayoutSnapshot.CURRENT_VERSION, upgraded.Version);
        Assert.Same(previous.Main, upgraded.Current.Main);
        Assert.Equal(WorkbenchPanelIds.SUBTITLES, upgraded.Current.FocusedPanelId);
        Assert.Equal([WorkbenchPanelIds.SUBTITLE_DETAILS, WorkbenchPanelIds.MASKS, WorkbenchPanelIds.ASSETS,
            WorkbenchPanelIds.DRAWING_TOOLS, WorkbenchPanelIds.EFFECT_PRESETS], upgraded.Current.HiddenPanelIds);
        WorkspaceLayoutValidator.Validate(upgraded.Current);
    }

    private static WorkspaceLayoutSnapshot VersionTwoStandard()
    {
        return new()
        {
            Version = 2,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.58,
                    WorkspaceLayoutPresets.Tabs(0.75, "preview"),
                    WorkspaceLayoutPresets.Tabs(0.25, "styles", "effects", "export")),
                WorkspaceLayoutPresets.Tabs(0.19, "timeline"),
                WorkspaceLayoutPresets.Tabs(0.23, "subtitles", "log"))
        };
    }

    [Fact]
    public void IncompleteVersionTwoLayoutIsRejectedInsteadOfSilentlyReplaced()
    {
        var previous = VersionTwoStandard() with { HiddenPanelIds = [WorkbenchPanelIds.LOG] };
        Assert.Throws<InvalidDataException>(() => WorkspaceLayoutMigration.Upgrade(new() { Version = 2, Current = previous }));
    }
}
