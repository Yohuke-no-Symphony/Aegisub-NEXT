using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Koumori;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests;

public sealed class KoumoriPreferencesMigrationTests
{
    [Fact]
    public void NewAndMissingPreferencesUseTheBundledModel()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(KoumoriModelSource.BUNDLED, new KoumoriPreferences().ModelSource);
        Assert.Equal(KoumoriModelSource.BUNDLED, preferences.Koumori.ModelSource);
        Assert.Equal(string.Empty, preferences.Koumori.ModelDirectory);
    }

    [Theory]
    [InlineData("", KoumoriModelSource.BUNDLED)]
    [InlineData("  ", KoumoriModelSource.BUNDLED)]
    [InlineData("models/manual-koumori", KoumoriModelSource.MANUAL)]
    public void LegacyMissingSourceMigratesForLocalLoadAndStrictImport(string modelDirectory,
        KoumoriModelSource expectedSource)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var original = new WorkbenchPreferences
        {
            Language = "zh-CN",
            Koumori = new()
            {
                Enabled = true,
                SnapEnabled = true,
                MaximumSnapMilliseconds = 125,
                ModelDirectory = modelDirectory
            }
        };
        var bytes = LegacyJson(original);
        var path = Path.Combine(directory.Path, "preferences.json");
        File.WriteAllBytes(path, bytes);

        var loaded = store.Load();
        var imported = WorkbenchPreferencesStore.Deserialize(bytes);

        Assert.Null(store.LoadError);
        Assert.Equal(original with { Koumori = original.Koumori with { ModelSource = expectedSource } }, loaded);
        Assert.Equal(loaded, imported);
        Assert.Equal(modelDirectory, loaded.Koumori.ModelDirectory);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void LegacyLocalPreferencesWithoutKoumoriKeepBundledDefaults()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        const string JSON = "{\"Version\":1,\"Language\":\"zh-CN\"}";
        var path = Path.Combine(directory.Path, "preferences.json");
        File.WriteAllText(path, JSON);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(KoumoriModelSource.BUNDLED, preferences.Koumori.ModelSource);
        Assert.Equal(string.Empty, preferences.Koumori.ModelDirectory);
        Assert.Equal("zh-CN", preferences.Language);
        Assert.Equal(JSON, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(KoumoriModelSource.BUNDLED)]
    [InlineData(KoumoriModelSource.MANUAL)]
    public async Task ExplicitSourceAndSavedManualDirectorySurviveLocalAndPortableRoundTrips(KoumoriModelSource source)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var expected = new WorkbenchPreferences
        {
            Language = "zh-CN",
            Koumori = new()
            {
                Enabled = true,
                SnapEnabled = true,
                MaximumSnapMilliseconds = 125,
                ModelSource = source,
                ModelDirectory = Path.Combine(directory.Path, "saved-manual-model")
            }
        };

        await store.SaveAsync(expected, CancellationToken.None);
        var loaded = store.Load();
        var bytes = WorkbenchPreferencesStore.Serialize(loaded);
        var imported = WorkbenchPreferencesStore.Deserialize(bytes);

        Assert.Null(store.LoadError);
        Assert.Equal(expected, loaded);
        Assert.Equal(expected, imported);
        Assert.False(Directory.Exists(expected.Koumori.ModelDirectory));
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal((int)source, document.RootElement.GetProperty(nameof(WorkbenchPreferences.Koumori))
            .GetProperty(nameof(KoumoriPreferences.ModelSource)).GetInt32());
    }

    [Theory]
    [InlineData(KoumoriModelSource.BUNDLED, "", true)]
    [InlineData(KoumoriModelSource.BUNDLED, "  ", true)]
    [InlineData(KoumoriModelSource.BUNDLED, "saved-manual-model", false)]
    [InlineData(KoumoriModelSource.MANUAL, "saved-manual-model", false)]
    public void MigrationPreservesSnapshotIdentityWhenNoUpgradeIsNeeded(KoumoriModelSource source,
        string modelDirectory, bool omitSource)
    {
        var preferences = new WorkbenchPreferences
        {
            Koumori = new() { ModelSource = source, ModelDirectory = modelDirectory }
        };
        var bytes = omitSource ? LegacyJson(preferences) : WorkbenchPreferencesStore.Serialize(preferences);

        Assert.Same(preferences, WorkbenchPreferencesMigration.Upgrade(preferences, bytes));
    }

    [Fact]
    public void LegacyModelMigrationAlsoRetainsShortcutUpgradeAndBecomesIdempotent()
    {
        var previousBindings = ShortcutDefaults.CreateBindings()
            .Where(binding => binding.Command < WorkbenchCommand.AUTO_TIMING).ToImmutableArray();
        var preferences = new WorkbenchPreferences
        {
            ShortcutBindings = previousBindings,
            Koumori = new() { ModelDirectory = "saved-manual-model" }
        };

        var bytes = LegacyJson(preferences);
        var upgraded = WorkbenchPreferencesMigration.Upgrade(preferences, bytes);

        upgraded.Validate();
        Assert.Equal(KoumoriModelSource.MANUAL, upgraded.Koumori.ModelSource);
        Assert.Equal(previousBindings, upgraded.ShortcutBindings.Where(binding => binding.Command < WorkbenchCommand.AUTO_TIMING));
        Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.AUTO_TIMING);
        Assert.Same(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded, bytes));
        Assert.Same(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded, WorkbenchPreferencesStore.Serialize(upgraded)));
    }

    [Fact]
    public void LegacySourceCompatibilityDoesNotMakeOtherKoumoriFieldsOptional()
    {
        var json = JsonNode.Parse(LegacyJson(new()))!.AsObject();
        Assert.True(json[nameof(WorkbenchPreferences.Koumori)]!.AsObject().Remove(nameof(KoumoriPreferences.Enabled)));

        Assert.Throws<InvalidDataException>(() => WorkbenchPreferencesStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    private static byte[] LegacyJson(WorkbenchPreferences preferences)
    {
        var json = JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(preferences))!.AsObject();
        Assert.True(json[nameof(WorkbenchPreferences.Koumori)]!.AsObject().Remove(nameof(KoumoriPreferences.ModelSource)));
        return Encoding.UTF8.GetBytes(json.ToJsonString());
    }
}
