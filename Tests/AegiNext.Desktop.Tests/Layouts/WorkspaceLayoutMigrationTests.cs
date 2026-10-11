using System.Text.Json;
using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Tests.Layouts;

public sealed class WorkspaceLayoutMigrationTests
{
    [Fact]
    public async Task LegacyCurrentAndAllPersonalPresetsKeepTopologyBoundsNamesAndSelection()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-layout-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var legacy = new WorkspaceLayoutSnapshot
            {
                Version = 1,
                Main = WorkspaceLayoutPresets.Split("vertical", 1,
                    WorkspaceLayoutPresets.Tabs(0.65, "styles", "preview") with { ActivePanelId = "preview" },
                    WorkspaceLayoutPresets.Tabs(0.35, "timeline", "subtitles")),
                Floating = [new() { X = -800, Y = 123, Width = 710, Height = 480, Scaling = 2,
                    Content = WorkspaceLayoutPresets.Tabs(1, "effects") }],
                HiddenPanelIds = ["export"], FocusedPanelId = "effects"
            };
            var file = new WorkspaceLayoutFile
            {
                Version = 1, Current = legacy, CurrentPresetId = "user-one",
                Presets = [new("user-one", "My custom name", false, legacy),
                    new("user-two", "Second", false, legacy with { FocusedPanelId = "preview" })]
            };
            var path = Path.Combine(directory, "layouts.json");
            File.WriteAllText(path, JsonSerializer.Serialize(file));
            var store = new WorkspaceLayoutStore(directory);
            var restored = store.Load();
            Assert.Null(store.LoadError);
            Assert.Null(store.DiagnosticPath);
            Assert.Equal(WorkspaceLayoutSnapshot.CURRENT_VERSION, restored.Version);
            Assert.Equal(file.CurrentPresetId, restored.CurrentPresetId);
            Assert.Equal(JsonSerializer.Serialize(legacy.Main), JsonSerializer.Serialize(restored.Current.Main));
            Assert.Equal(JsonSerializer.Serialize(legacy.Floating), JsonSerializer.Serialize(restored.Current.Floating));
            Assert.Equal<string>(["export", "log", "subtitleDetails", "masks", "assets"], restored.Current.HiddenPanelIds);
            Assert.Equal("effects", restored.Current.FocusedPanelId);
            Assert.Equal(file.Presets.Select(preset => preset.Name), restored.Presets.Select(preset => preset.Name));
            Assert.All(restored.Presets, preset =>
            {
                Assert.Contains("log", preset.Layout.HiddenPanelIds);
                WorkspaceLayoutValidator.Validate(preset.Layout);
            });
            await store.SaveAsync(restored);
            Assert.Equal(WorkspaceLayoutStore.Fingerprint(restored.Current), WorkspaceLayoutStore.Fingerprint(store.Load().Current));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void VersionThreeCustomDockLayoutAddsMasksHiddenWithoutChangingExistingTopologyOrBounds()
    {
        var legacy = new WorkspaceLayoutSnapshot
        {
            Version = 3,
            Main = WorkspaceLayoutPresets.Tabs(1, "preview", "timeline", "styles", "subtitles"),
            Floating = [new() { X = -300, Y = 80, Width = 700, Height = 500, Scaling = 2,
                Content = WorkspaceLayoutPresets.Tabs(1, "effects", "export") }],
            HiddenPanelIds = ["log", "subtitleDetails"], FocusedPanelId = "effects"
        };
        var file = new WorkspaceLayoutFile { Version = 3, Current = legacy, CurrentPresetId = "personal",
            Presets = [new("personal", "布局 123", false, legacy)] };
        var restored = WorkspaceLayoutMigration.Upgrade(file);
        Assert.Equal(WorkspaceLayoutSnapshot.CURRENT_VERSION, restored.Version);
        Assert.Equal(JsonSerializer.Serialize(legacy.Main), JsonSerializer.Serialize(restored.Current.Main));
        Assert.Equal(JsonSerializer.Serialize(legacy.Floating), JsonSerializer.Serialize(restored.Current.Floating));
        Assert.Equal<string>(["log", "subtitleDetails", "masks", "assets"], restored.Current.HiddenPanelIds);
        Assert.Equal("effects", restored.Current.FocusedPanelId);
        Assert.Equal("布局 123", Assert.Single(restored.Presets).Name);
        WorkspaceLayoutValidator.Validate(restored.Current);
        Assert.Contains("masks", restored.Presets[0].Layout.HiddenPanelIds);
    }

    [Fact]
    public void VersionFourCustomLayoutKeepsItsDockingAndAddsOnlyHiddenAssets()
    {
        var legacy = new WorkspaceLayoutSnapshot
        {
            Version = 4,
            Main = WorkspaceLayoutPresets.Tabs(1, "preview", "timeline", "styles", "subtitles", "masks"),
            Floating = [new() { X = -300, Y = 80, Width = 700, Height = 500, Scaling = 2,
                Content = WorkspaceLayoutPresets.Tabs(1, "effects", "export") }],
            HiddenPanelIds = ["log", "subtitleDetails"], FocusedPanelId = "masks"
        };
        var restored = WorkspaceLayoutMigration.Upgrade(new() { Version = 4, Current = legacy, CurrentPresetId = "personal",
            Presets = [new("personal", "Personal", false, legacy)] });
        Assert.Same(legacy.Main, restored.Current.Main);
        Assert.Same(legacy.Floating, restored.Current.Floating);
        Assert.Equal<string>(["log", "subtitleDetails", "assets"], restored.Current.HiddenPanelIds);
        Assert.Equal("masks", restored.Current.FocusedPanelId);
        WorkspaceLayoutValidator.Validate(restored.Current);
        WorkspaceLayoutValidator.Validate(restored.Presets[0].Layout);
    }

    [Fact]
    public void InvalidLegacyPanelSetsAreRejected()
    {
        var missing = new WorkspaceLayoutSnapshot { Version = 1, Main = WorkspaceLayoutPresets.Tabs(1, "preview") };
        Assert.Throws<InvalidDataException>(() => WorkspaceLayoutMigration.Upgrade(new() { Version = 1, Current = missing }));
    }
}
