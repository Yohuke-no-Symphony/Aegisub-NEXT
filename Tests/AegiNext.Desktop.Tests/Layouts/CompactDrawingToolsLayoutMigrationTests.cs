using System.Text.Json;
using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Tests.Layouts;

public sealed class CompactDrawingToolsLayoutMigrationTests
{
    [Theory]
    [InlineData("standard")]
    [InlineData("effects")]
    [InlineData("timing")]
    [InlineData("encode")]
    public void UnmodifiedVersionSixBuiltInsUpgradeToTheCurrentPreset(string presetId)
    {
        var previous = VersionSix(presetId);
        var upgraded = WorkspaceLayoutMigration.Upgrade(new()
        {
            Version = 6, Current = previous, CurrentPresetId = presetId
        });
        var expected = WorkspaceLayoutPresets.BuiltIn.Single(preset => preset.Id == presetId).Layout;

        Assert.Equal(WorkspaceLayoutSnapshot.CURRENT_VERSION, upgraded.Version);
        Assert.Same(expected, upgraded.Current);
        Assert.Equal(previous.HiddenPanelIds, upgraded.Current.HiddenPanelIds.Where(id => id != WorkbenchPanelIds.EFFECT_PRESETS));
        Assert.Equal(previous.FocusedPanelId, upgraded.Current.FocusedPanelId);
        WorkspaceLayoutValidator.Validate(upgraded.Current);
        if (presetId is "standard" or "effects")
        {
            var top = upgraded.Current.Main.Children[0];
            var drawingAndPreview = top.Children[0];
            Assert.Equal("horizontal", top.Orientation);
            Assert.Equal(2, top.Children.Count);
            Assert.Equal("vertical", drawingAndPreview.Orientation);
            Assert.Equal(2, drawingAndPreview.Children.Count);
            Assert.Equal("drawingTools", Assert.Single(drawingAndPreview.Children[0].Children).PanelId);
            Assert.Equal("preview", Assert.Single(drawingAndPreview.Children[1].Children).PanelId);
            Assert.Equal(0.16, drawingAndPreview.Children[0].Proportion);
            Assert.Equal(0.84, drawingAndPreview.Children[1].Proportion);
            Assert.Equal(presetId == "standard" ? 0.75 : 0.66, drawingAndPreview.Proportion);
            var previousTabs = top.Children[1] with
            {
                Children = top.Children[1].Children.Where(node => node.PanelId != WorkbenchPanelIds.EFFECT_PRESETS).ToArray()
            };
            Assert.Equal(JsonSerializer.Serialize(previous.Main.Children[0].Children[2]), JsonSerializer.Serialize(previousTabs));
            Assert.Equal(previous.Main.Children.Select(node => node.Proportion), upgraded.Current.Main.Children.Select(node => node.Proportion));
            Assert.Equal(JsonSerializer.Serialize(previous.Main.Children.Skip(1)), JsonSerializer.Serialize(upgraded.Current.Main.Children.Skip(1)));
        }
    }

    [Theory]
    [InlineData("ratio")]
    [InlineData("activeTab")]
    [InlineData("focus")]
    public void ModifiedVersionSixBuiltInsPreserveTheExactLayout(string change)
    {
        var previous = VersionSix("standard");
        var top = previous.Main.Children[0];
        previous = change switch
        {
            "ratio" => previous with
            {
                Main = previous.Main with
                {
                    Children = [top with
                    {
                        Children = [top.Children[0] with { Proportion = 0.12 }, top.Children[1] with { Proportion = 0.60 },
                            top.Children[2] with { Proportion = 0.28 }]
                    }, previous.Main.Children[1], previous.Main.Children[2]]
                }
            },
            "activeTab" => previous with
            {
                Main = previous.Main with
                {
                    Children = [top with
                    {
                        Children = [top.Children[0], top.Children[1], top.Children[2] with { ActivePanelId = "effects" }]
                    }, previous.Main.Children[1], previous.Main.Children[2]]
                }
            },
            _ => previous with { FocusedPanelId = "drawingTools" }
        };

        var upgraded = WorkspaceLayoutMigration.Upgrade(new()
        {
            Version = 6, Current = previous, CurrentPresetId = "standard"
        });

        Assert.Equal(WorkspaceLayoutStore.Fingerprint(WithHiddenEffectPresets(previous)), WorkspaceLayoutStore.Fingerprint(upgraded.Current));
        Assert.Same(previous.Main, upgraded.Current.Main);
        Assert.Same(previous.Floating, upgraded.Current.Floating);
        WorkspaceLayoutValidator.Validate(upgraded.Current);
    }

    [Fact]
    public void VersionSixPersonalLayoutsKeepFloatingDrawingToolsAndSavedPresetIdentityThroughPersistence()
    {
        var previous = new WorkspaceLayoutSnapshot
        {
            Version = 6,
            Main = WorkspaceLayoutPresets.Tabs(1, "preview", "timeline", "subtitles", "styles", "masks", "assets", "export"),
            Floating = [new()
            {
                X = -700, Y = 90, Width = 800, Height = 500, Scaling = 2,
                Content = WorkspaceLayoutPresets.Tabs(1, "drawingTools", "effects")
            }],
            HiddenPanelIds = ["log", "subtitleDetails"], FocusedPanelId = "drawingTools"
        };
        var file = new WorkspaceLayoutFile
        {
            Version = 6, Current = previous, CurrentPresetId = "personal",
            Presets = [new("personal", "个人空间", false, previous),
                new("saved-standard", "My standard", false, VersionSix("standard"))]
        };

        var upgraded = WorkspaceLayoutStore.Deserialize(JsonSerializer.SerializeToUtf8Bytes(file));
        var restored = WorkspaceLayoutStore.Deserialize(WorkspaceLayoutStore.Serialize(upgraded));

        Assert.Equal("personal", upgraded.CurrentPresetId);
        Assert.Equal(WorkspaceLayoutStore.Fingerprint(WithHiddenEffectPresets(previous)), WorkspaceLayoutStore.Fingerprint(upgraded.Current));
        Assert.Equal(file.Presets.Select(preset => (preset.Id, preset.Name, preset.IsReadOnly)),
            upgraded.Presets.Select(preset => (preset.Id, preset.Name, preset.IsReadOnly)));
        for (var index = 0; index < file.Presets.Count; index++)
        {
            Assert.Equal(WorkspaceLayoutStore.Fingerprint(WithHiddenEffectPresets(file.Presets[index].Layout)),
                WorkspaceLayoutStore.Fingerprint(upgraded.Presets[index].Layout));
            WorkspaceLayoutValidator.Validate(upgraded.Presets[index].Layout);
        }
        Assert.Equal(JsonSerializer.Serialize(upgraded), JsonSerializer.Serialize(restored));
        Assert.Same(upgraded, WorkspaceLayoutMigration.Upgrade(upgraded));
    }

    private static WorkspaceLayoutSnapshot WithHiddenEffectPresets(WorkspaceLayoutSnapshot previous)
    {
        return previous with
        {
            Version = WorkspaceLayoutSnapshot.CURRENT_VERSION,
            HiddenPanelIds = [.. previous.HiddenPanelIds, WorkbenchPanelIds.EFFECT_PRESETS]
        };
    }

    private static WorkspaceLayoutSnapshot VersionSix(string presetId) => presetId switch
    {
        "standard" => new()
        {
            Version = 6,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.58, WorkspaceLayoutPresets.Tabs(0.10, "drawingTools"),
                    WorkspaceLayoutPresets.Tabs(0.65, "preview"), WorkspaceLayoutPresets.Tabs(0.25, "styles", "effects", "masks", "export", "assets")),
                WorkspaceLayoutPresets.Tabs(0.19, "timeline"), WorkspaceLayoutPresets.Tabs(0.23, "subtitles", "log")),
            HiddenPanelIds = ["subtitleDetails"], FocusedPanelId = "preview"
        },
        "effects" => new()
        {
            Version = 6,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.72, WorkspaceLayoutPresets.Tabs(0.10, "drawingTools"),
                    WorkspaceLayoutPresets.Tabs(0.56, "preview"), WorkspaceLayoutPresets.Tabs(0.34, "effects", "masks", "styles", "assets")),
                WorkspaceLayoutPresets.Tabs(0.28, "timeline", "log")),
            HiddenPanelIds = ["subtitles", "export", "subtitleDetails"], FocusedPanelId = "effects"
        },
        "timing" => new()
        {
            Version = 6,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.7, WorkspaceLayoutPresets.Tabs(0.58, "preview"), WorkspaceLayoutPresets.Tabs(0.42, "subtitles")),
                WorkspaceLayoutPresets.Tabs(0.3, "timeline", "log")),
            HiddenPanelIds = ["styles", "effects", "masks", "export", "subtitleDetails", "assets", "drawingTools"], FocusedPanelId = "subtitles"
        },
        "encode" => new()
        {
            Version = 6,
            Main = WorkspaceLayoutPresets.Split("horizontal", 1, WorkspaceLayoutPresets.Tabs(0.66, "preview"), WorkspaceLayoutPresets.Tabs(0.34, "export", "log")),
            HiddenPanelIds = ["subtitles", "styles", "effects", "masks", "timeline", "subtitleDetails", "assets", "drawingTools"], FocusedPanelId = "export"
        },
        _ => throw new ArgumentOutOfRangeException(nameof(presetId))
    };
}
