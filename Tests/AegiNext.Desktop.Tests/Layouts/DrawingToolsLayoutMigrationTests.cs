using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Tests.Layouts;

public sealed class DrawingToolsLayoutMigrationTests
{
    [Theory]
    [InlineData("standard", true)]
    [InlineData("effects", true)]
    [InlineData("timing", false)]
    [InlineData("encode", false)]
    public void UnmodifiedVersionFiveBuiltInsUpgradeToTheNewPreset(string presetId, bool drawingVisible)
    {
        var previous = VersionFive(presetId);
        var upgraded = WorkspaceLayoutMigration.Upgrade(new() { Version = 5, Current = previous, CurrentPresetId = presetId });
        var expected = WorkspaceLayoutPresets.BuiltIn.Single(preset => preset.Id == presetId).Layout;

        Assert.Equal(6, upgraded.Version);
        Assert.Same(expected, upgraded.Current);
        Assert.Equal(!drawingVisible, upgraded.Current.HiddenPanelIds.Contains(WorkbenchPanelIds.DRAWING_TOOLS));
        WorkspaceLayoutValidator.Validate(upgraded.Current);
        Assert.All(Nodes(upgraded.Current.Main).Where(node => node.Kind != "panel"), node => Assert.NotEmpty(node.Children));
        if (drawingVisible)
        {
            var top = upgraded.Current.Main.Children[0];
            Assert.Equal(3, top.Children.Count);
            Assert.Equal(WorkbenchPanelIds.DRAWING_TOOLS, Assert.Single(top.Children[0].Children).PanelId);
            Assert.Equal(WorkbenchPanelIds.PREVIEW, Assert.Single(top.Children[1].Children).PanelId);
            Assert.Equal(0.1, top.Children[0].Proportion);
            Assert.Equal(presetId == "standard" ? 0.65 : 0.56, top.Children[1].Proportion);
            Assert.Equal(presetId == "standard" ? 0.25 : 0.34, top.Children[2].Proportion);
        }
    }

    [Theory]
    [InlineData("ratio")]
    [InlineData("activeTab")]
    [InlineData("focus")]
    public void AVersionFiveLayoutModifiedFromABuiltInKeepsItsTopologyAndOnlyHidesTheNewPanel(string change)
    {
        var previous = VersionFive("standard");
        var top = previous.Main.Children[0];
        previous = change switch
        {
            "ratio" => previous with { Main = previous.Main with { Children =
                [top with { Children = [top.Children[0] with { Proportion = 0.72 }, top.Children[1] with { Proportion = 0.28 }] },
                    previous.Main.Children[1], previous.Main.Children[2]] } },
            "activeTab" => previous with { Main = previous.Main with { Children =
                [top with { Children = [top.Children[0], top.Children[1] with { ActivePanelId = "effects" }] },
                    previous.Main.Children[1], previous.Main.Children[2]] } },
            _ => previous with { FocusedPanelId = "timeline" }
        };

        var upgraded = WorkspaceLayoutMigration.Upgrade(new() { Version = 5, Current = previous, CurrentPresetId = "standard" });

        Assert.Same(previous.Main, upgraded.Current.Main);
        Assert.Same(previous.Floating, upgraded.Current.Floating);
        Assert.Equal(previous.FocusedPanelId, upgraded.Current.FocusedPanelId);
        Assert.Equal<string>(["subtitleDetails", "drawingTools"], upgraded.Current.HiddenPanelIds);
        WorkspaceLayoutValidator.Validate(upgraded.Current);
    }

    [Fact]
    public void PersonalVersionFiveLayoutsPreserveFloatingBoundsAndSavedPresetIdentity()
    {
        var previous = new WorkspaceLayoutSnapshot
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Tabs(1, "preview", "timeline", "subtitles", "styles", "masks", "assets"),
            Floating = [new() { X = -700, Y = 90, Width = 800, Height = 500, Scaling = 2,
                Content = WorkspaceLayoutPresets.Tabs(1, "effects", "export") }],
            HiddenPanelIds = ["log", "subtitleDetails"], FocusedPanelId = "effects"
        };
        var saved = VersionFive("standard");
        var file = new WorkspaceLayoutFile
        {
            Version = 5, Current = previous, CurrentPresetId = "personal",
            Presets = [new("personal", "个人空间", false, previous), new("saved-standard", "My standard", false, saved)]
        };

        var upgraded = WorkspaceLayoutMigration.Upgrade(file);

        Assert.Equal("personal", upgraded.CurrentPresetId);
        Assert.Same(previous.Main, upgraded.Current.Main);
        Assert.Same(previous.Floating, upgraded.Current.Floating);
        Assert.Equal<string>(["log", "subtitleDetails", "drawingTools"], upgraded.Current.HiddenPanelIds);
        Assert.Equal(file.Presets.Select(preset => (preset.Id, preset.Name)), upgraded.Presets.Select(preset => (preset.Id, preset.Name)));
        Assert.Same(saved.Main, upgraded.Presets[1].Layout.Main);
        Assert.All(upgraded.Presets, preset =>
        {
            Assert.Contains(WorkbenchPanelIds.DRAWING_TOOLS, preset.Layout.HiddenPanelIds);
            WorkspaceLayoutValidator.Validate(preset.Layout);
        });
        WorkspaceLayoutValidator.Validate(upgraded.Current);
        Assert.Same(upgraded, WorkspaceLayoutMigration.Upgrade(upgraded));
    }

    [Fact]
    public void UnmodifiedVersionFourStandardStillMatchesTheHistoricalPreset()
    {
        var previous = new WorkspaceLayoutSnapshot
        {
            Version = 4,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.58, WorkspaceLayoutPresets.Tabs(0.75, "preview"),
                    WorkspaceLayoutPresets.Tabs(0.25, "styles", "effects", "masks", "export")),
                WorkspaceLayoutPresets.Tabs(0.19, "timeline"), WorkspaceLayoutPresets.Tabs(0.23, "subtitles", "log")),
            HiddenPanelIds = ["subtitleDetails"], FocusedPanelId = "preview"
        };
        var upgraded = WorkspaceLayoutMigration.Upgrade(new() { Version = 4, Current = previous });
        Assert.Same(WorkspaceLayoutPresets.Standard, upgraded.Current);
        WorkspaceLayoutValidator.Validate(upgraded.Current);
    }

    private static IEnumerable<LayoutNodeSnapshot> Nodes(LayoutNodeSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Nodes))
        {
            yield return child;
        }
    }

    private static WorkspaceLayoutSnapshot VersionFive(string presetId) => presetId switch
    {
        "standard" => new()
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.58, WorkspaceLayoutPresets.Tabs(0.75, "preview"),
                    WorkspaceLayoutPresets.Tabs(0.25, "styles", "effects", "masks", "export", "assets")),
                WorkspaceLayoutPresets.Tabs(0.19, "timeline"), WorkspaceLayoutPresets.Tabs(0.23, "subtitles", "log")),
            HiddenPanelIds = ["subtitleDetails"], FocusedPanelId = "preview"
        },
        "effects" => new()
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.72, WorkspaceLayoutPresets.Tabs(0.66, "preview"),
                    WorkspaceLayoutPresets.Tabs(0.34, "effects", "masks", "styles", "assets")),
                WorkspaceLayoutPresets.Tabs(0.28, "timeline", "log")),
            HiddenPanelIds = ["subtitles", "export", "subtitleDetails"], FocusedPanelId = "effects"
        },
        "timing" => new()
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.7, WorkspaceLayoutPresets.Tabs(0.58, "preview"), WorkspaceLayoutPresets.Tabs(0.42, "subtitles")),
                WorkspaceLayoutPresets.Tabs(0.3, "timeline", "log")),
            HiddenPanelIds = ["styles", "effects", "masks", "export", "subtitleDetails", "assets"], FocusedPanelId = "subtitles"
        },
        "encode" => new()
        {
            Version = 5,
            Main = WorkspaceLayoutPresets.Split("horizontal", 1, WorkspaceLayoutPresets.Tabs(0.66, "preview"), WorkspaceLayoutPresets.Tabs(0.34, "export", "log")),
            HiddenPanelIds = ["subtitles", "styles", "effects", "masks", "timeline", "subtitleDetails", "assets"], FocusedPanelId = "export"
        },
        _ => throw new ArgumentOutOfRangeException(nameof(presetId))
    };
}
