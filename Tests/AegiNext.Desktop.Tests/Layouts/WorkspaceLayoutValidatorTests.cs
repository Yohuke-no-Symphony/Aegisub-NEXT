using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Tests.Layouts;

public sealed class WorkspaceLayoutValidatorTests
{
    private static readonly string[] reorderedPanelIds = ["styles", "preview"];

    [Fact]
    public void FourBuiltInLayoutsAreReadOnlyAndAccountForEveryPanel()
    {
        Assert.Equal(4, WorkspaceLayoutPresets.BuiltIn.Count);
        Assert.All(WorkspaceLayoutPresets.BuiltIn, preset =>
        {
            Assert.True(preset.IsReadOnly);
            WorkspaceLayoutValidator.Validate(preset.Layout);
        });
    }

    [Fact]
    public void SplitRatiosTabOrderActivePanelHiddenPanelsAndFloatingBoundsSurviveSerialization()
    {
        var layout = new WorkspaceLayoutSnapshot
        {
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Tabs(0.72, WorkbenchPanelIds.STYLES, WorkbenchPanelIds.PREVIEW) with { ActivePanelId = WorkbenchPanelIds.PREVIEW },
                WorkspaceLayoutPresets.Tabs(0.28, WorkbenchPanelIds.TIMELINE)),
            Floating = [new() { X = -1440, Y = 200, Width = 800, Height = 560, Scaling = 2,
                Content = WorkspaceLayoutPresets.Tabs(1, WorkbenchPanelIds.EFFECTS, WorkbenchPanelIds.EXPORT) }],
            HiddenPanelIds = [WorkbenchPanelIds.SUBTITLES, WorkbenchPanelIds.LOG, WorkbenchPanelIds.SUBTITLE_DETAILS, WorkbenchPanelIds.MASKS, WorkbenchPanelIds.ASSETS, WorkbenchPanelIds.DRAWING_TOOLS], FocusedPanelId = WorkbenchPanelIds.EFFECTS
        };
        WorkspaceLayoutValidator.Validate(layout);
        var copy = System.Text.Json.JsonSerializer.Deserialize<WorkspaceLayoutSnapshot>(WorkspaceLayoutStore.Fingerprint(layout));

        Assert.NotNull(copy);
        Assert.Equal(0.72, copy.Main.Children[0].Proportion);
        Assert.Equal(reorderedPanelIds, copy.Main.Children[0].Children.Select(child => child.PanelId));
        Assert.Equal("preview", copy.Main.Children[0].ActivePanelId);
        Assert.Equal(-1440, Assert.Single(copy.Floating).X);
        Assert.Equal<string>(["subtitles", "log", "subtitleDetails", "masks", "assets", "drawingTools"], copy.HiddenPanelIds);
        Assert.Equal("effects", copy.FocusedPanelId);
        WorkspaceLayoutValidator.Validate(copy);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("ratio")]
    [InlineData("active")]
    [InlineData("bounds")]
    [InlineData("version")]
    public void InvalidSnapshotsAreRejectedBeforeAffectingHosts(string damage)
    {
        var source = WorkspaceLayoutPresets.BuiltIn.Single(preset => preset.Id == WorkspaceLayoutPresets.TIMING).Layout;
        var invalid = damage switch
        {
            "missing" => source with { HiddenPanelIds = [] },
            "duplicate" => source with { HiddenPanelIds = ["styles", "effects", "export", "preview"] },
            "unknown" => source with { HiddenPanelIds = ["styles", "effects", "other"] },
            "ratio" => source with { Main = source.Main with { Proportion = double.NaN } },
            "active" => source with { Main = WorkspaceLayoutPresets.Tabs(1, "preview") with { ActivePanelId = "effects" } },
            "bounds" => source with { Floating = [new() { Width = -1 }] },
            "version" => source with { Version = 99 },
            _ => throw new ArgumentException("Unknown damage.", nameof(damage))
        };
        Assert.Throws<InvalidDataException>(() => WorkspaceLayoutValidator.Validate(invalid));
    }
}
