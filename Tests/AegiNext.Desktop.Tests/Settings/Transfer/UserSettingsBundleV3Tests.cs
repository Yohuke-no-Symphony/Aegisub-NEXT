using System.Text.Json.Nodes;
using AegiNext.Desktop.Settings.Transfer;

namespace AegiNext.Desktop.Tests.Settings.Transfer;

[Collection("Workspace session")]
public sealed class UserSettingsBundleV3Tests
{
    [Fact]
    public void NewExportContainsOnlySettingsAndVersionThreeManifest()
    {
        var files = UserSettingsTransferTestData.ReadArchive(
            UserSettingsBundleStore.Serialize(UserSettingsTransferTestData.CreateBundle()));

        Assert.Equal(3, JsonNode.Parse(files["manifest.json"])!["version"]!.GetValue<int>());
        Assert.Equal(["export-presets.aegiexports", "layouts.json", "manifest.json", "preferences.json", "subtitle-color-tags.json"],
            files.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ManualLegacyImportIgnoresMaterialDocumentsAndRestagesAsVersionThree(int version)
    {
        var files = UserSettingsTransferTestData.ReadArchive(UserSettingsTransferTestData.CreateLegacyArchive(version));
        files["subtitle-styles.aegistyles"] = "not a style document"u8.ToArray();
        files["effect-scripts.json"] = [0xFF];

        var imported = UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(files));
        var restaged = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(imported));

        Assert.Equal(3, JsonNode.Parse(restaged["manifest.json"])!["version"]!.GetValue<int>());
        Assert.Equal(version, imported.SourceVersion);
        Assert.Equal(3, UserSettingsBundleStore.Deserialize(UserSettingsBundleStore.Serialize(imported)).SourceVersion);
        Assert.DoesNotContain("subtitle-styles.aegistyles", restaged.Keys);
        Assert.DoesNotContain("effect-scripts.json", restaged.Keys);
        Assert.Equal(version == 2, restaged.ContainsKey("subtitle-color-tags.json"));
        Assert.Equal("zh-CN", imported.Preferences.Language);
        Assert.Single(imported.ExportPresets.Presets);
        Assert.Single(imported.Layouts.Presets);
    }

    [Fact]
    public void OmittedTagLibraryStillUsesVersionThreeAndPreservesItsOmission()
    {
        var bundle = UserSettingsTransferTestData.CreateBundle() with { ColorTags = null };
        var bytes = UserSettingsBundleStore.Serialize(bundle);
        var files = UserSettingsTransferTestData.ReadArchive(bytes);

        Assert.Equal(3, JsonNode.Parse(files["manifest.json"])!["version"]!.GetValue<int>());
        Assert.Equal(4, files.Count);
        Assert.False(files.ContainsKey("subtitle-color-tags.json"));
        Assert.Null(UserSettingsBundleStore.Deserialize(bytes).ColorTags);
    }

    [Theory]
    [InlineData("preferences.json", false)]
    [InlineData("preferences.json", true)]
    [InlineData("export-presets.aegiexports", false)]
    [InlineData("export-presets.aegiexports", true)]
    [InlineData("layouts.json", false)]
    [InlineData("layouts.json", true)]
    public void OptionalTagsDoNotMakeAnyOtherSettingOptional(string missingName, bool includeTags)
    {
        var settings = UserSettingsTransferTestData.CreateBundle();
        var bytes = UserSettingsBundleStore.Serialize(includeTags ? settings : settings with { ColorTags = null });
        var files = UserSettingsTransferTestData.ReadArchive(bytes);
        files.Remove(missingName);

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(files)));
    }

    [Fact]
    public void VersionThreeRejectsLegacyMaterialEntries()
    {
        var files = UserSettingsTransferTestData.ReadArchive(UserSettingsBundleStore.Serialize(new()));
        files.Add("effect-scripts.json", "ignored content"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.Deserialize(UserSettingsTransferTestData.WriteArchive(files)));
    }

    [Theory]
    [InlineData("subtitle-styles.aegistyles")]
    [InlineData("effect-scripts.json")]
    public void LegacyPendingReaderStillRequiresCompleteValidMaterialDocuments(string name)
    {
        var files = UserSettingsTransferTestData.ReadArchive(UserSettingsTransferTestData.CreateLegacyArchive(2));
        files[name] = "{}"u8.ToArray();
        var bytes = UserSettingsTransferTestData.WriteArchive(files);

        Assert.Equal(2, UserSettingsBundleStore.Deserialize(bytes).SourceVersion);
        Assert.Throws<InvalidDataException>(() => UserSettingsBundleStore.DeserializeForRestore(bytes));
    }
}
