using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Projects;
using System.Text;
using System.Text.Json.Nodes;

namespace AegiNext.Desktop.Tests;

public sealed class ProjectPreferencesTests
{
    [Fact]
    public void PreviousSettingsExportUsesDefaultsForNewAssetPreferences()
    {
        var preferences = new WorkbenchPreferences { Projects = new() { AutoSaveIntervalMinutes = 9 } };
        var json = JsonNode.Parse(WorkbenchPreferencesStore.Serialize(preferences))!;
        Assert.True(json.AsObject().Remove(nameof(WorkbenchPreferences.AssetBrowser)));
        Assert.True(json[nameof(WorkbenchPreferences.Projects)]!.AsObject().Remove(nameof(ProjectPreferences.CopyImportedAssetsToSources)));

        var restored = WorkbenchPreferencesStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal(preferences, restored);
        Assert.True(restored.Projects.CopyImportedAssetsToSources);
        Assert.Equal(new(), restored.AssetBrowser);
    }

    [Fact]
    public async Task LegacyPreferencesUseProtectionDefaultsWithoutRewritingTheFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var path = Path.Combine(directory.Path, "preferences.json");
        const string JSON = "{\"Language\":\"zh-CN\",\"Volume\":0.375}";
        await File.WriteAllTextAsync(path, JSON, CancellationToken.None);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal("zh-CN", preferences.Language);
        Assert.Equal(0.375f, preferences.Volume);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Path.IsPathFullyQualified(documents))
        {
            Assert.Equal(Path.Combine(documents, "AegiNext", "Workspace"), preferences.Projects.WorkspaceRoot);
        }
        Assert.True(Path.IsPathFullyQualified(preferences.Projects.WorkspaceRoot));
        preferences.Projects.Validate();
        Assert.True(preferences.Projects.AutoSaveEnabled);
        Assert.True(preferences.Projects.CopyImportedAssetsToSources);
        Assert.Equal(2, preferences.Projects.AutoSaveIntervalMinutes);
        Assert.True(preferences.Projects.BackupEnabled);
        Assert.Equal(5, preferences.Projects.BackupIntervalMinutes);
        Assert.Equal(20, preferences.Projects.MaximumBackupCount);
        Assert.Equal(JSON, await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task ProjectPreferencesRoundTripAndParticipateInSnapshotEquality()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var expected = new WorkbenchPreferences
        {
            Language = "zh-CN", Volume = 0.25f,
            Projects = new()
            {
                WorkspaceRoot = Path.Combine(directory.Path, "项目 空间"), AutoSaveEnabled = false,
                AutoSaveIntervalMinutes = 3, BackupIntervalMinutes = 7, MaximumBackupCount = 4,
                CopyImportedAssetsToSources = false
            }
        };

        await store.SaveAsync(expected, CancellationToken.None);

        Assert.Equal(expected, store.Load());
        Assert.NotEqual(expected, expected with { Projects = expected.Projects with { BackupEnabled = false } });
        Assert.NotEqual(expected, expected with { Projects = expected.Projects with { CopyImportedAssetsToSources = true } });
        Assert.NotEqual(expected.GetHashCode(), (expected with { Projects = expected.Projects with { MaximumBackupCount = 9 } }).GetHashCode());
    }

    [Theory]
    [InlineData(0, 5, 20)]
    [InlineData(1441, 5, 20)]
    [InlineData(2, 0, 20)]
    [InlineData(2, 1441, 20)]
    [InlineData(2, 5, 0)]
    [InlineData(2, 5, 1001)]
    public void InvalidProtectionValuesAreRejected(int autoSave, int backup, int count)
    {
        var preferences = new WorkbenchPreferences
        {
            Projects = new() { AutoSaveIntervalMinutes = autoSave, BackupIntervalMinutes = backup, MaximumBackupCount = count }
        };

        Assert.Throws<InvalidDataException>(preferences.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/workspace")]
    public void WorkspaceMustBeAnAbsoluteLocalPath(string path)
    {
        Assert.Throws<InvalidDataException>(() => new ProjectPreferences { WorkspaceRoot = path }.Validate());
    }

    /// <summary>无文档目录的账号仍获得绝对默认位置，路径选择不会创建目录。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void DefaultWorkspaceUsesAnAbsoluteAvailableFolderWithoutCreatingIt(bool hasDocuments, bool hasProfile)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var documents = Path.Combine(directory.Path, "Documents");
        var profile = Path.Combine(directory.Path, "Profile");
        var temporary = Path.Combine(directory.Path, "Temporary");
        var root = ProjectPreferences.ResolveDefaultWorkspaceRoot(hasDocuments ? documents : string.Empty,
            hasProfile ? profile : string.Empty, temporary);
        var expected = Path.Combine(hasDocuments ? documents : hasProfile ? profile : temporary, "AegiNext", "Workspace");
        Assert.Equal(expected, root);
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.False(Directory.Exists(root));
        new ProjectPreferences { WorkspaceRoot = root }.Validate();
    }
}
