using System.Text.Json;
using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Tests.Layouts;

/// <summary>验证特效预设面板的历史布局识别、个人布局保留与持久化。</summary>
public sealed class EffectPresetsLayoutMigrationTests
{
    /// <summary>仅未改动的旧内置布局启用新版面板位置，并保留原活动标签。</summary>
    [Theory]
    [InlineData("standard")]
    [InlineData("effects")]
    [InlineData("timing")]
    [InlineData("encode")]
    public void UnmodifiedVersionSevenBuiltInsUpgradeAndOnlyEditingLayoutsShowPresets(string presetId)
    {
        var previous = VersionSeven(presetId);
        var upgraded = WorkspaceLayoutMigration.Upgrade(new()
        {
            Version = 7, Current = previous, CurrentPresetId = presetId
        });
        var expected = WorkspaceLayoutPresets.BuiltIn.Single(preset => preset.Id == presetId).Layout;

        Assert.Equal(8, upgraded.Version);
        Assert.Same(expected, upgraded.Current);
        WorkspaceLayoutValidator.Validate(upgraded.Current);
        if (presetId is "standard" or "effects")
        {
            var tabs = upgraded.Current.Main.Children[0].Children[1].Children.Select(node => node.PanelId).ToArray();
            Assert.Equal("effectPresets", tabs[Array.IndexOf(tabs, "effects") + 1]);
            Assert.Equal(previous.Main.Children[0].Children[1].ActivePanelId,
                upgraded.Current.Main.Children[0].Children[1].ActivePanelId);
            Assert.DoesNotContain("effectPresets", upgraded.Current.HiddenPanelIds);
        }
        else
        {
            Assert.Contains("effectPresets", upgraded.Current.HiddenPanelIds);
        }
    }

    /// <summary>比例、标签、焦点、隐藏和浮窗的修改均保持原几何。</summary>
    [Theory]
    [InlineData("ratio")]
    [InlineData("activeTab")]
    [InlineData("focus")]
    [InlineData("tabOrder")]
    [InlineData("floating")]
    [InlineData("hidden")]
    public void ModifiedVersionSevenLayoutsPreserveExactGeometryAndHideTheNewPanel(string change)
    {
        var previous = VersionSeven("standard");
        var top = previous.Main.Children[0];
        var right = top.Children[1];
        previous = change switch
        {
            "ratio" => previous with
            {
                Main = previous.Main with
                {
                    Children = [top with { Proportion = 0.6 }, previous.Main.Children[1], previous.Main.Children[2]]
                }
            },
            "activeTab" => previous with
            {
                Main = WithRightTabs(previous.Main, right with { ActivePanelId = "effects" })
            },
            "tabOrder" => previous with
            {
                Main = WithRightTabs(previous.Main, right with { Children = right.Children.Reverse().ToArray() })
            },
            "floating" => previous with
            {
                Main = WithRightTabs(previous.Main, WorkspaceLayoutPresets.Tabs(right.Proportion, "effects", "masks", "export", "assets")),
                Floating = [new()
                {
                    X = -800,
                    Y = 90,
                    Width = 800,
                    Height = 500,
                    Scaling = 2,
                    Content = WorkspaceLayoutPresets.Tabs(1, "styles")
                }]
            },
            "hidden" => previous with
            {
                Main = WithRightTabs(previous.Main, WorkspaceLayoutPresets.Tabs(right.Proportion, "effects", "masks", "export", "assets")),
                HiddenPanelIds = [.. previous.HiddenPanelIds, "styles"]
            },
            _ => previous with { FocusedPanelId = "timeline" }
        };
        var upgraded = WorkspaceLayoutMigration.Upgrade(new()
        {
            Version = 7, Current = previous, CurrentPresetId = "standard"
        });

        Assert.Same(previous.Main, upgraded.Current.Main);
        Assert.Same(previous.Floating, upgraded.Current.Floating);
        Assert.Equal(previous.FocusedPanelId, upgraded.Current.FocusedPanelId);
        Assert.Equal(previous.HiddenPanelIds.Concat(["effectPresets"]), upgraded.Current.HiddenPanelIds);
        WorkspaceLayoutValidator.Validate(upgraded.Current);
    }

    /// <summary>即便几何恰好等于内置布局，个人身份仍只添加隐藏面板。</summary>
    [Theory]
    [InlineData("standard")]
    [InlineData("effects")]
    [InlineData("timing")]
    [InlineData("encode")]
    public void APersonalIdentityWithUnmodifiedBuiltinGeometryRemainsPersonal(string presetId)
    {
        var previous = VersionSeven(presetId);
        var file = new WorkspaceLayoutFile
        {
            Version = 7,
            CurrentPresetId = "personal",
            Current = previous,
            Presets = [new("personal", "My saved layout", false, previous)]
        };
        var upgraded = WorkspaceLayoutMigration.Upgrade(file);

        Assert.Equal("personal", upgraded.CurrentPresetId);
        Assert.Same(previous.Main, upgraded.Current.Main);
        Assert.Same(previous.Floating, upgraded.Current.Floating);
        Assert.Equal(previous.HiddenPanelIds.Concat(["effectPresets"]), upgraded.Current.HiddenPanelIds);
        Assert.Equal(WorkspaceLayoutStore.Fingerprint(upgraded.Current),
            WorkspaceLayoutStore.Fingerprint(Assert.Single(upgraded.Presets).Layout));
        WorkspaceLayoutValidator.Validate(upgraded.Current);
    }

    /// <summary>个人预设的身份、只读标记和窗口信息可完整持久化往返。</summary>
    [Fact]
    public void PersonalVersionSevenLayoutsAndTheirSavedPresetsKeepIdentityThroughPersistence()
    {
        var previous = VersionSeven("effects") with
        {
            Floating = [new()
            {
                X = -700,
                Y = 100,
                Width = 700,
                Height = 400,
                Scaling = 2,
                Content = WorkspaceLayoutPresets.Tabs(1, "subtitles")
            }],
            HiddenPanelIds = ["export", "subtitleDetails"]
        };
        var file = new WorkspaceLayoutFile
        {
            Version = 7, CurrentPresetId = "personal", Current = previous,
            Presets = [new("personal", "个人空间", false, previous),
                new("saved-standard", "Saved standard", false, VersionSeven("standard"))]
        };
        var upgraded = WorkspaceLayoutStore.Deserialize(JsonSerializer.SerializeToUtf8Bytes(file));
        var restored = WorkspaceLayoutStore.Deserialize(WorkspaceLayoutStore.Serialize(upgraded));

        Assert.Equal(file.CurrentPresetId, upgraded.CurrentPresetId);
        Assert.Equal(file.Presets.Select(preset => (preset.Id, preset.Name, preset.IsReadOnly)),
            upgraded.Presets.Select(preset => (preset.Id, preset.Name, preset.IsReadOnly)));
        Assert.Equal(JsonSerializer.Serialize(previous.Main), JsonSerializer.Serialize(upgraded.Current.Main));
        Assert.Equal(JsonSerializer.Serialize(previous.Floating), JsonSerializer.Serialize(upgraded.Current.Floating));
        Assert.All(upgraded.Presets, preset =>
        {
            Assert.Contains("effectPresets", preset.Layout.HiddenPanelIds);
            WorkspaceLayoutValidator.Validate(preset.Layout);
        });
        Assert.Equal(JsonSerializer.Serialize(upgraded), JsonSerializer.Serialize(restored));
        Assert.Same(upgraded, WorkspaceLayoutMigration.Upgrade(upgraded));
    }

    private static LayoutNodeSnapshot WithRightTabs(LayoutNodeSnapshot main, LayoutNodeSnapshot right)
    {
        var top = main.Children[0];
        return main with
        {
            Children = [top with { Children = [top.Children[0], right] }, main.Children[1], main.Children[2]]
        };
    }

    private static WorkspaceLayoutSnapshot VersionSeven(string id) => id switch
    {
        "standard" => new()
        {
            Version = 7,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.58,
                    WorkspaceLayoutPresets.Split("vertical", 0.75, WorkspaceLayoutPresets.Tabs(0.16, "drawingTools"),
                        WorkspaceLayoutPresets.Tabs(0.84, "preview")),
                    WorkspaceLayoutPresets.Tabs(0.25, "styles", "effects", "masks", "export", "assets")),
                WorkspaceLayoutPresets.Tabs(0.19, "timeline"), WorkspaceLayoutPresets.Tabs(0.23, "subtitles", "log")),
            HiddenPanelIds = ["subtitleDetails"], FocusedPanelId = "preview"
        },
        "effects" => new()
        {
            Version = 7,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.72,
                    WorkspaceLayoutPresets.Split("vertical", 0.66, WorkspaceLayoutPresets.Tabs(0.16, "drawingTools"),
                        WorkspaceLayoutPresets.Tabs(0.84, "preview")),
                    WorkspaceLayoutPresets.Tabs(0.34, "effects", "masks", "styles", "assets")),
                WorkspaceLayoutPresets.Tabs(0.28, "timeline", "log")),
            HiddenPanelIds = ["subtitles", "export", "subtitleDetails"], FocusedPanelId = "effects"
        },
        "timing" => new()
        {
            Version = 7,
            Main = WorkspaceLayoutPresets.Split("vertical", 1,
                WorkspaceLayoutPresets.Split("horizontal", 0.7, WorkspaceLayoutPresets.Tabs(0.58, "preview"),
                    WorkspaceLayoutPresets.Tabs(0.42, "subtitles")), WorkspaceLayoutPresets.Tabs(0.3, "timeline", "log")),
            HiddenPanelIds = ["styles", "effects", "masks", "export", "subtitleDetails", "assets", "drawingTools"],
            FocusedPanelId = "subtitles"
        },
        "encode" => new()
        {
            Version = 7,
            Main = WorkspaceLayoutPresets.Split("horizontal", 1, WorkspaceLayoutPresets.Tabs(0.66, "preview"),
                WorkspaceLayoutPresets.Tabs(0.34, "export", "log")),
            HiddenPanelIds = ["subtitles", "styles", "effects", "masks", "timeline", "subtitleDetails", "assets", "drawingTools"],
            FocusedPanelId = "export"
        },
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
}
