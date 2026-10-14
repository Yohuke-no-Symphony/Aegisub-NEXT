using System.Text;
using AegiNext.Desktop.Settings.Transfer;

namespace AegiNext.Desktop.Tests.Settings.Transfer;

/// <summary>验证下次启动恢复的原始备份、跨服务日志恢复与全量回滚。</summary>
[Collection("Workspace session")]
public sealed class UserSettingsRestoreServiceTests
{
    /// <summary>暂存和取消只改变待恢复包，取消操作保持全部原始配置字节。</summary>
    [Fact]
    public async Task StageAndCancelKeepLiveFilesAndNotifyOnlyCompletedChanges()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var original = WriteOriginalFiles(directory.Path);
        using var restore = new UserSettingsRestoreService(directory.Path);
        var changes = 0;
        restore.PendingChanged += () => changes++;

        await restore.StageAsync(UserSettingsTransferTestData.CreateBundle());
        Assert.True(restore.HasPending);
        Assert.False(restore.HasRecoveryJournal);
        AssertFiles(directory.Path, original);
        var pending = File.ReadAllBytes(PendingPath(directory.Path));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore.StageAsync(new(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore.CancelPendingAsync(cancellation.Token));
        Assert.Equal(pending, File.ReadAllBytes(PendingPath(directory.Path)));
        Assert.Equal(1, changes);

        await restore.CancelPendingAsync();
        await restore.CancelPendingAsync();
        Assert.False(restore.HasPending);
        Assert.Equal(2, changes);
        AssertFiles(directory.Path, original);
        Assert.Empty(Directory.GetFiles(RestorePath(directory.Path), "*.tmp"));
    }

    /// <summary>成功恢复提交完整配置，保留无法反序列化的原始字节和文件缺失记录。</summary>
    [Fact]
    public async Task SuccessfulRestoreKeepsRawBackupAndAbsenceMetadata()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var original = WriteOriginalFiles(directory.Path);
        var bundle = UserSettingsTransferTestData.CreateBundle();
        using var restore = new UserSettingsRestoreService(directory.Path);
        var changes = 0;
        restore.PendingChanged += () => changes++;
        await restore.StageAsync(bundle);

        restore.ApplyPending();

        AssertPersistedFiles(directory.Path, UserSettingsBundleStore.SerializeFiles(bundle));
        Assert.False(restore.HasPending);
        Assert.False(restore.HasRecoveryJournal);
        Assert.Equal(2, changes);
        Assert.NotNull(restore.RetentionBackupPath);
        var backup = ReadBackup(restore.RetentionBackupPath);
        foreach (var entry in backup.Files)
        {
            Assert.Equal(original[entry.Name] is not null, entry.Existed);
            if (entry.Existed)
            {
                Assert.Equal(original[entry.Name], File.ReadAllBytes(Path.Combine(restore.RetentionBackupPath, entry.Name)));
            }
            else
            {
                Assert.Equal(0, entry.Length);
                Assert.Null(entry.Sha256);
                Assert.False(File.Exists(Path.Combine(restore.RetentionBackupPath, entry.Name)));
            }
        }
        using var reopened = new UserSettingsRestoreService(directory.Path);
        Assert.Equal(restore.RetentionBackupPath, reopened.RetentionBackupPath);
    }

    /// <summary>任一文件替换失败都完整回滚，包括删除此前不存在的新文件；待恢复包可重试。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task FailureAtAnyReplacementRollsBackAndAllowsFreshServiceRetry(int failedIndex)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var original = WriteOriginalFiles(directory.Path);
        var bundle = UserSettingsTransferTestData.CreateBundle();
        using (var restore = new UserSettingsRestoreService(directory.Path, index =>
               {
                   if (index == failedIndex)
                   {
                       throw new IOException("Injected replacement failure.");
                   }
               }))
        {
            await restore.StageAsync(bundle);
            Assert.Throws<IOException>(restore.ApplyPending);
            AssertFiles(directory.Path, original);
            Assert.True(restore.HasPending);
            Assert.False(restore.HasRecoveryJournal);
        }
        using var reopened = new UserSettingsRestoreService(directory.Path);
        reopened.ApplyPending();
        AssertPersistedFiles(directory.Path, UserSettingsBundleStore.SerializeFiles(bundle));
        Assert.False(reopened.HasPending);
    }

    /// <summary>另一服务恢复中断日志时先回滚原文件，再决定是否重试仍保留的包。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentServiceRecoversInterruptedApplyingJournal(bool retainPending)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var original = WriteOriginalFiles(directory.Path);
        var bundle = UserSettingsTransferTestData.CreateBundle();
        var journal = await CaptureInterruptedJournalAsync(directory.Path, bundle);
        WriteFiles(directory.Path, UserSettingsBundleStore.SerializeFiles(bundle).Take(2));
        WriteJournal(directory.Path, journal);
        if (!retainPending)
        {
            File.Delete(PendingPath(directory.Path));
        }
        using var reopened = new UserSettingsRestoreService(directory.Path, _ =>
        {
            AssertFiles(directory.Path, original);
            throw new IOException("Stop after recovered rollback, before retry.");
        });

        if (retainPending)
        {
            Assert.Throws<IOException>(reopened.ApplyPending);
        }
        else
        {
            reopened.ApplyPending();
        }

        AssertFiles(directory.Path, original);
        Assert.Equal(retainPending, reopened.HasPending);
        Assert.False(reopened.HasRecoveryJournal);
    }

    /// <summary>已提交日志跨服务清理时不会再次替换或回滚已生效的新配置。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedJournalOnlyCompletesCleanup(bool pendingAlreadyRemoved)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        _ = WriteOriginalFiles(directory.Path);
        var bundle = UserSettingsTransferTestData.CreateBundle();
        var journal = await CaptureInterruptedJournalAsync(directory.Path, bundle);
        var expected = UserSettingsBundleStore.SerializeFiles(bundle);
        WriteFiles(directory.Path, expected);
        WriteJournal(directory.Path, journal with { Phase = "committed" });
        if (pendingAlreadyRemoved)
        {
            File.Delete(PendingPath(directory.Path));
        }
        using var reopened = new UserSettingsRestoreService(directory.Path, _ =>
            throw new InvalidOperationException("Committed cleanup must not replace files."));

        reopened.ApplyPending();

        AssertPersistedFiles(directory.Path, expected);
        Assert.False(reopened.HasPending);
        Assert.False(reopened.HasRecoveryJournal);
        Assert.Equal(UserSettingsTransferFiles.ResolvePhysicalPath(BackupPath(directory.Path, journal.BackupId)),
            reopened.RetentionBackupPath);
    }

    /// <summary>损坏备份在回滚首个文件前拒绝，日志与待恢复包保留供诊断。</summary>
    [Fact]
    public async Task DamagedBackupPreventsEveryRollbackWrite()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        _ = WriteOriginalFiles(directory.Path);
        var bundle = UserSettingsTransferTestData.CreateBundle();
        var journal = await CaptureInterruptedJournalAsync(directory.Path, bundle);
        WriteFiles(directory.Path, UserSettingsBundleStore.SerializeFiles(bundle).Take(2));
        WriteJournal(directory.Path, journal);
        var backupPath = BackupPath(directory.Path, journal.BackupId);
        var lastExisting = ReadBackup(backupPath).Files.Last(entry => entry.Existed);
        File.WriteAllBytes(Path.Combine(backupPath, lastExisting.Name), "corrupt backup"u8.ToArray());
        var before = CaptureFiles(directory.Path);
        using var reopened = new UserSettingsRestoreService(directory.Path);

        Assert.Throws<InvalidDataException>(reopened.ApplyPending);

        AssertFiles(directory.Path, before);
        Assert.True(reopened.HasPending);
        Assert.True(reopened.HasRecoveryJournal);
    }

    /// <summary>日志未知字段、备份路径记录和缺失文件的意外备份都不能驱动恢复写入。</summary>
    [Theory]
    [InlineData("unknownJournalField")]
    [InlineData("backupPath")]
    [InlineData("unexpectedAbsentBackup")]
    public async Task UntrustedRecoveryMetadataIsRejectedBeforeLiveWrites(string invalidCase)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        _ = WriteOriginalFiles(directory.Path);
        var journal = await CaptureInterruptedJournalAsync(directory.Path, UserSettingsTransferTestData.CreateBundle());
        WriteJournal(directory.Path, journal);
        var backupPath = BackupPath(directory.Path, journal.BackupId);
        if (invalidCase == "unknownJournalField")
        {
            var text = File.ReadAllText(JournalPath(directory.Path));
            File.WriteAllText(JournalPath(directory.Path), text[..^1] + ",\"Unexpected\":true}", Encoding.UTF8);
        }
        else if (invalidCase == "backupPath")
        {
            var backup = ReadBackup(backupPath);
            var altered = backup with { Files = backup.Files.SetItem(0, new("../preferences.json", false, 0, null)) };
            File.WriteAllBytes(Path.Combine(backupPath, "backup.json"), SettingsTransferJson.Serialize(altered, 65536));
        }
        else
        {
            File.WriteAllBytes(Path.Combine(backupPath, "preferences.json"), "unexpected"u8.ToArray());
        }
        var before = CaptureFiles(directory.Path);
        using var reopened = new UserSettingsRestoreService(directory.Path);

        Assert.Throws<InvalidDataException>(reopened.ApplyPending);

        AssertFiles(directory.Path, before);
        Assert.True(reopened.HasRecoveryJournal);
    }

    /// <summary>由用户指定的符号根目录及父目录别名映射至相同物理恢复目标。</summary>
    [Fact]
    public async Task ExplicitRootAliasResolvesTargetAncestorsAndAppliesToPhysicalFiles()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var physicalParent = Path.Combine(directory.Path, "physical");
        var physical = Path.Combine(physicalParent, "personal");
        Directory.CreateDirectory(physical);
        var alias = Path.Combine(directory.Path, "alias");
        Directory.CreateSymbolicLink(alias, physicalParent);
        try
        {
            var aliasRoot = Path.Combine(alias, "personal");
            Assert.Equal(UserSettingsTransferFiles.ResolvePhysicalPath(physical), UserSettingsTransferFiles.ResolvePhysicalPath(aliasRoot));
            var bundle = UserSettingsTransferTestData.CreateBundle();
            using var restore = new UserSettingsRestoreService(aliasRoot);
            await restore.StageAsync(bundle);
            restore.ApplyPending();
            AssertPersistedFiles(physical, UserSettingsBundleStore.SerializeFiles(bundle));
            Assert.False(restore.HasPending);
        }
        finally
        {
            Directory.Delete(alias);
        }
    }

    /// <summary>受控恢复子目录与实际配置文件的链接均拒绝，外部目标字节不受影响。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControlledDirectoryAndLiveFileLinksCannotRedirectRestore(bool liveFile)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var outside = Path.Combine(directory.Path, "outside");
        Directory.CreateDirectory(outside);
        var original = "outside original"u8.ToArray();
        var outsideFile = Path.Combine(outside, "preferences.json");
        File.WriteAllBytes(outsideFile, original);
        var link = liveFile ? Path.Combine(directory.Path, "preferences.json") : RestorePath(directory.Path);
        if (liveFile)
        {
            File.CreateSymbolicLink(link, outsideFile);
        }
        else
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        try
        {
            if (liveFile)
            {
                using var restore = new UserSettingsRestoreService(directory.Path);
                await restore.StageAsync(new());
                Assert.Throws<InvalidDataException>(restore.ApplyPending);
                Assert.False(restore.HasRecoveryJournal);
            }
            else
            {
                Assert.Throws<InvalidDataException>(() => new UserSettingsRestoreService(directory.Path));
            }
            Assert.Equal(original, File.ReadAllBytes(outsideFile));
        }
        finally
        {
            if (liveFile)
            {
                File.Delete(link);
            }
            else
            {
                Directory.Delete(link);
            }
        }
    }

    /// <summary>跨服务文件锁冲突和失效待恢复包都在日志创建前失败，不改变配置。</summary>
    [Fact]
    public async Task FileLockConflictAndInvalidPendingKeepLiveConfiguration()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var original = WriteOriginalFiles(directory.Path);
        using var restore = new UserSettingsRestoreService(directory.Path);
        await restore.StageAsync(new());
        var pending = File.ReadAllBytes(PendingPath(directory.Path));
        using (var fileLock = new FileStream(Path.Combine(RestorePath(directory.Path), "lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => restore.StageAsync(UserSettingsTransferTestData.CreateBundle()));
        }
        Assert.Equal(pending, File.ReadAllBytes(PendingPath(directory.Path)));
        File.WriteAllBytes(PendingPath(directory.Path), "invalid zip"u8.ToArray());

        Assert.Throws<InvalidDataException>(restore.ApplyPending);

        AssertFiles(directory.Path, original);
        Assert.True(restore.HasPending);
        Assert.False(restore.HasRecoveryJournal);
    }

    /// <summary>v3 省略标签时，本地标签文件字节或缺失状态不变。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OmittedTagStageAndRestoreLeavesTheTagLibraryBytesAndAbsenceUnchanged(bool tagFileExists)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "subtitle-color-tags.json");
        var original = "existing tag library raw bytes"u8.ToArray();
        if (tagFileExists)
        {
            await File.WriteAllBytesAsync(path, original);
        }
        var bundle = UserSettingsTransferTestData.CreateBundle() with { ColorTags = null };
        using (var restore = new UserSettingsRestoreService(directory.Path))
        {
            await restore.StageAsync(bundle);
            Assert.Null((await UserSettingsBundleStore.LoadAsync(PendingPath(directory.Path))).ColorTags);
        }
        using var reopened = new UserSettingsRestoreService(directory.Path);
        reopened.ApplyPending();

        AssertPersistedFiles(directory.Path, UserSettingsBundleStore.SerializeFiles(bundle));
        Assert.Equal(tagFileExists, File.Exists(path));
        if (tagFileExists)
        {
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
    }

    private static async Task<UserSettingsRestoreJournal> CaptureInterruptedJournalAsync(string directory, UserSettingsBundle bundle)
    {
        byte[]? journal = null;
        using var restore = new UserSettingsRestoreService(directory, index =>
        {
            if (index == 3)
            {
                journal = File.ReadAllBytes(JournalPath(directory));
                throw new IOException("Injected interrupted restore.");
            }
        });
        await restore.StageAsync(bundle);
        Assert.Throws<IOException>(restore.ApplyPending);
        Assert.NotNull(journal);
        return SettingsTransferJson.Deserialize<UserSettingsRestoreJournal>(journal, 65536);
    }

    private static Dictionary<string, byte[]?> WriteOriginalFiles(string directory)
    {
        var content = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        for (var index = 0; index < UserSettingsBundleStore.Files.Length; index++)
        {
            var file = UserSettingsBundleStore.Files[index];
            var bytes = file.Name is "preferences.json" or "subtitle-color-tags.json" ? null : Encoding.UTF8.GetBytes("original raw bytes: " + file.Name);
            content.Add(file.Name, bytes);
            if (bytes is not null)
            {
                File.WriteAllBytes(Path.Combine(directory, file.Name), bytes);
            }
        }
        return content;
    }

    private static Dictionary<string, byte[]?> CaptureFiles(string directory)
    {
        return UserSettingsBundleStore.Files.ToDictionary(file => file.Name, file =>
            File.Exists(Path.Combine(directory, file.Name)) ? File.ReadAllBytes(Path.Combine(directory, file.Name)) : null, StringComparer.Ordinal);
    }

    private static void WriteFiles(string directory, IEnumerable<KeyValuePair<string, byte[]>> content)
    {
        foreach (var (name, bytes) in content)
        {
            File.WriteAllBytes(Path.Combine(directory, name), bytes);
        }
    }

    private static void AssertFiles(string directory, IEnumerable<KeyValuePair<string, byte[]?>> expected)
    {
        foreach (var (name, bytes) in expected)
        {
            var path = Path.Combine(directory, name);
            if (bytes is null)
            {
                Assert.False(File.Exists(path));
            }
            else
            {
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
        }
    }

    private static void AssertPersistedFiles(string directory, IReadOnlyDictionary<string, byte[]> expected)
    {
        foreach (var (name, bytes) in expected)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, name)));
        }
    }

    private static UserSettingsRestoreBackup ReadBackup(string path)
    {
        return SettingsTransferJson.Deserialize<UserSettingsRestoreBackup>(File.ReadAllBytes(Path.Combine(path, "backup.json")), 65536);
    }

    private static void WriteJournal(string directory, UserSettingsRestoreJournal journal)
    {
        File.WriteAllBytes(JournalPath(directory), SettingsTransferJson.Serialize(journal, 65536));
    }

    private static string RestorePath(string directory)
    {
        return Path.Combine(directory, ".settings-restore");
    }

    private static string PendingPath(string directory)
    {
        return Path.Combine(RestorePath(directory), "pending.aegisettings");
    }

    private static string JournalPath(string directory)
    {
        return Path.Combine(RestorePath(directory), "journal.json");
    }

    private static string BackupPath(string directory, Guid id)
    {
        return Path.Combine(RestorePath(directory), "backups", id.ToString("N"));
    }
}
