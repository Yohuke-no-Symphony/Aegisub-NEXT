using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Settings.Transfer;

[Collection("Workspace session")]
public sealed class UserSettingsRestoreV3Tests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ExistingNewManifestProtectsAllLiveFilesFromLegacyPending(int version)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var paths = new PersonalLibraryPaths(directory.Path);
        Directory.CreateDirectory(paths.EffectLibraryDirectory);
        var manifestPath = paths.EffectLibraryManifestFile;
        File.WriteAllText(manifestPath, "damaged new manifest");
        var preferencesPath = Path.Combine(directory.Path, "preferences.json");
        File.WriteAllText(preferencesPath, "original preferences");
        var restoreDirectory = Path.Combine(directory.Path, ".settings-restore");
        Directory.CreateDirectory(restoreDirectory);
        var pendingPath = Path.Combine(restoreDirectory, "pending.aegisettings");
        var pending = UserSettingsTransferTestData.CreateLegacyArchive(version);
        File.WriteAllBytes(pendingPath, pending);
        using var restore = new UserSettingsRestoreService(directory.Path);

        Assert.Throws<InvalidOperationException>(restore.ApplyPending);

        Assert.Equal("original preferences", File.ReadAllText(preferencesPath));
        Assert.Equal("damaged new manifest", File.ReadAllText(manifestPath));
        Assert.Equal(pending, File.ReadAllBytes(pendingPath));
        Assert.True(restore.HasPending);
        Assert.False(restore.HasRecoveryJournal);
        Assert.Null(restore.RetentionBackupPath);
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("danglingLink")]
    public void NonRegularManifestAlsoProtectsLegacyPending(string kind)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var paths = new PersonalLibraryPaths(directory.Path);
        Directory.CreateDirectory(paths.EffectLibraryDirectory);
        if (kind == "directory")
        {
            Directory.CreateDirectory(paths.EffectLibraryManifestFile);
        }
        else
        {
            File.CreateSymbolicLink(paths.EffectLibraryManifestFile, Path.Combine(directory.Path, "missing-manifest"));
        }
        var restoreDirectory = Path.Combine(directory.Path, ".settings-restore");
        Directory.CreateDirectory(restoreDirectory);
        var pending = UserSettingsTransferTestData.CreateLegacyArchive(2);
        var pendingPath = Path.Combine(restoreDirectory, "pending.aegisettings");
        File.WriteAllBytes(pendingPath, pending);
        using var restore = new UserSettingsRestoreService(directory.Path);

        Assert.Throws<InvalidOperationException>(restore.ApplyPending);

        Assert.Equal(pending, File.ReadAllBytes(pendingPath));
        Assert.False(File.Exists(Path.Combine(directory.Path, "preferences.json")));
        Assert.False(restore.HasRecoveryJournal);
    }

    [Fact]
    public async Task NewSettingsRestoreLeavesAllEffectLibraryFilesUntouched()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var paths = new PersonalLibraryPaths(directory.Path);
        Directory.CreateDirectory(paths.EffectLibraryDirectory);
        File.WriteAllText(paths.EffectLibraryManifestFile, "new library manifest");
        File.WriteAllText(paths.LegacyStyleFile, "legacy style bytes");
        File.WriteAllText(paths.LegacyEffectFile, "legacy effect bytes");
        using var restore = new UserSettingsRestoreService(directory.Path);
        var bundle = UserSettingsTransferTestData.CreateBundle();
        await restore.StageAsync(bundle);

        restore.ApplyPending();

        Assert.Equal("new library manifest", File.ReadAllText(paths.EffectLibraryManifestFile));
        Assert.Equal("legacy style bytes", File.ReadAllText(paths.LegacyStyleFile));
        Assert.Equal("legacy effect bytes", File.ReadAllText(paths.LegacyEffectFile));
        Assert.False(restore.HasPending);
        var backup = SettingsTransferJson.Deserialize<UserSettingsRestoreBackup>(
            File.ReadAllBytes(Path.Combine(restore.RetentionBackupPath!, "backup.json")), 65536);
        Assert.Equal(3, backup.Version);
        Assert.Equal(4, backup.Files.Length);
        Assert.DoesNotContain(backup.Files, file => file.Name is "subtitle-styles.aegistyles" or "effect-scripts.json");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacyPendingWithoutNewManifestKeepsItsOriginalLibraryRestorePromise(int version)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var restoreDirectory = Path.Combine(directory.Path, ".settings-restore");
        Directory.CreateDirectory(restoreDirectory);
        var bytes = UserSettingsTransferTestData.CreateLegacyArchive(version);
        File.WriteAllBytes(Path.Combine(restoreDirectory, "pending.aegisettings"), bytes);
        var expected = UserSettingsTransferTestData.ReadArchive(bytes);
        expected.Remove("manifest.json");
        using var restore = new UserSettingsRestoreService(directory.Path);

        restore.ApplyPending();

        foreach (var (name, content) in expected)
        {
            Assert.Equal(content, File.ReadAllBytes(Path.Combine(directory.Path, name)));
        }
        Assert.False(restore.HasPending);
        var backup = SettingsTransferJson.Deserialize<UserSettingsRestoreBackup>(
            File.ReadAllBytes(Path.Combine(restore.RetentionBackupPath!, "backup.json")), 65536);
        Assert.Equal(version, backup.Version);
        Assert.Equal(version == 1 ? 5 : 6, backup.Files.Length);
    }

    [Theory]
    [InlineData(1, "applying", false)]
    [InlineData(1, "applying", true)]
    [InlineData(1, "committed", false)]
    [InlineData(1, "committed", true)]
    [InlineData(2, "applying", false)]
    [InlineData(2, "applying", true)]
    [InlineData(2, "committed", false)]
    [InlineData(2, "committed", true)]
    public void LegacyJournalRecoversItsOwnFileSetWithoutTouchingTheNewLibrary(int version, string phase, bool retainPending)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var paths = new PersonalLibraryPaths(directory.Path);
        Directory.CreateDirectory(paths.EffectLibraryDirectory);
        File.WriteAllText(paths.EffectLibraryManifestFile, "active new manifest");
        var pending = UserSettingsTransferTestData.CreateLegacyArchive(version);
        var expected = UserSettingsTransferTestData.ReadArchive(pending);
        expected.Remove("manifest.json");
        var restoreDirectory = Path.Combine(directory.Path, ".settings-restore");
        var id = Guid.NewGuid();
        var backupDirectory = Path.Combine(restoreDirectory, "backups", id.ToString("N"));
        Directory.CreateDirectory(backupDirectory);
        var originals = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var entries = ImmutableArray.CreateBuilder<UserSettingsRestoreEntry>();
        foreach (var name in expected.Keys)
        {
            var original = name == "preferences.json" ? null : Encoding.UTF8.GetBytes("old raw bytes: " + name);
            originals.Add(name, original);
            if (original is not null)
            {
                File.WriteAllBytes(Path.Combine(directory.Path, name), original);
                File.WriteAllBytes(Path.Combine(backupDirectory, name), original);
            }
            entries.Add(new(name, original is not null, original?.Length ?? 0,
                original is null ? null : Convert.ToHexString(SHA256.HashData(original))));
        }
        var tagsBefore = "tag library outside legacy transaction"u8.ToArray();
        if (version == 1)
        {
            File.WriteAllBytes(paths.ColorTagFile, tagsBefore);
        }
        var backup = new UserSettingsRestoreBackup { Version = version, Id = id, Files = entries.ToImmutable() };
        File.WriteAllBytes(Path.Combine(backupDirectory, "backup.json"), SettingsTransferJson.Serialize(backup, 65536));
        var journal = new UserSettingsRestoreJournal
        {
            Version = version, BackupId = id, PendingSha256 = Convert.ToHexString(SHA256.HashData(pending)), Phase = phase
        };
        File.WriteAllBytes(Path.Combine(restoreDirectory, "journal.json"), SettingsTransferJson.Serialize(journal, 65536));
        foreach (var (name, content) in phase == "committed" ? expected : expected.Take(2))
        {
            File.WriteAllBytes(Path.Combine(directory.Path, name), content);
        }
        if (retainPending)
        {
            File.WriteAllBytes(Path.Combine(restoreDirectory, "pending.aegisettings"), pending);
        }
        using var restore = new UserSettingsRestoreService(directory.Path);
        if (phase == "applying" && retainPending)
        {
            Assert.Throws<InvalidOperationException>(restore.ApplyPending);
        }
        else
        {
            restore.ApplyPending();
        }

        Assert.Equal("active new manifest", File.ReadAllText(paths.EffectLibraryManifestFile));
        Assert.False(restore.HasRecoveryJournal);
        Assert.Equal(phase == "applying" && retainPending, restore.HasPending);
        foreach (var name in expected.Keys)
        {
            var wanted = phase == "committed" ? expected[name] : originals[name];
            Assert.Equal(wanted is not null, File.Exists(Path.Combine(directory.Path, name)));
            if (wanted is not null)
            {
                Assert.Equal(wanted, File.ReadAllBytes(Path.Combine(directory.Path, name)));
            }
        }
        if (version == 1)
        {
            Assert.Equal(tagsBefore, File.ReadAllBytes(paths.ColorTagFile));
        }
    }
}
