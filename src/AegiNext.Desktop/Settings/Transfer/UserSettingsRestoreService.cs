using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed class UserSettingsRestoreService : IDisposable
{
    private const int MAXIMUM_METADATA_BYTES = 65536;
    private readonly string directory;
    private readonly string restoreDirectory;
    private readonly string pendingPath;
    private readonly string journalPath;
    private readonly string backupsDirectory;
    private readonly string retentionPath;
    private readonly string effectLibraryManifestPath;
    private readonly Action<int>? beforeReplace;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private int operations;
    private bool disposed;

    internal UserSettingsRestoreService(string directory, Action<int>? beforeReplace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("用户设置目录必须是绝对路径。", nameof(directory));
        }
        this.directory = UserSettingsTransferFiles.ResolvePhysicalPath(directory);
        restoreDirectory = Path.Combine(this.directory, ".settings-restore");
        pendingPath = Path.Combine(restoreDirectory, "pending.aegisettings");
        journalPath = Path.Combine(restoreDirectory, "journal.json");
        backupsDirectory = Path.Combine(restoreDirectory, "backups");
        retentionPath = Path.Combine(restoreDirectory, "last-backup");
        effectLibraryManifestPath = new PersonalLibraryPaths(this.directory).EffectLibraryManifestFile;
        this.beforeReplace = beforeReplace;
        EnsureControlledDirectories();
        if (RegularFileExists(retentionPath))
        {
            var text = Encoding.ASCII.GetString(UserSettingsTransferFiles.Read(retentionPath, 32));
            if (!Guid.TryParseExact(text, "N", out var id) || id == Guid.Empty)
            {
                throw new InvalidDataException("用户设置备份标识无效。");
            }
            RetentionBackupPath = BackupDirectory(id);
        }
    }

    internal event Action? PendingChanged;
    internal bool HasPending => RegularFileExists(pendingPath);
    internal bool HasRecoveryJournal => RegularFileExists(journalPath);
    internal string? RetentionBackupPath { get; private set; }

    internal Task StageAsync(UserSettingsBundle bundle, CancellationToken cancellationToken = default)
    {
        return StageAsync(bundle, null, cancellationToken);
    }

    internal async Task StageAsync(UserSettingsBundle bundle, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        cancellationToken.ThrowIfCancellationRequested();
        bundle.Preferences.Validate();
        var bytes = UserSettingsBundleStore.Serialize(bundle, cancellationToken);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureControlledDirectories();
            using var fileLock = AcquireFileLock();
            EnsureNoJournal();
            _ = RegularFileExists(pendingPath);
            await UserSettingsTransferFiles.WriteAtomicAsync(pendingPath, bytes, beforeCommit, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
        PendingChanged?.Invoke();
    }

    internal Task CancelPendingAsync(CancellationToken cancellationToken = default)
    {
        return CancelPendingAsync(null, cancellationToken);
    }

    internal async Task CancelPendingAsync(Action? beforeCommit, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        var changed = false;
        try
        {
            EnsureControlledDirectories();
            using var fileLock = AcquireFileLock();
            EnsureNoJournal();
            cancellationToken.ThrowIfCancellationRequested();
            if (RegularFileExists(pendingPath))
            {
                beforeCommit?.Invoke();
                File.Delete(pendingPath);
                changed = true;
            }
        }
        finally
        {
            Exit();
        }
        if (changed)
        {
            PendingChanged?.Invoke();
        }
    }

    internal void ApplyPending()
    {
        Enter();
        var changed = false;
        try
        {
            EnsureControlledDirectories();
            if (!RegularFileExists(pendingPath) && !RegularFileExists(journalPath))
            {
                return;
            }
            using var fileLock = AcquireFileLock();
            var hadPending = RegularFileExists(pendingPath);
            RecoverInterruptedRestore();
            if (!RegularFileExists(pendingPath))
            {
                changed = hadPending;
                return;
            }
            var pendingBytes = UserSettingsTransferFiles.Read(pendingPath, UserSettingsBundleStore.MAXIMUM_FILE_BYTES);
            var payload = UserSettingsBundleStore.DeserializeForRestore(pendingBytes);
            payload.Settings.Preferences.Validate();
            if (payload.Settings.SourceVersion < UserSettingsBundleStore.CURRENT_VERSION && Attributes(effectLibraryManifestPath) is not null)
            {
                throw new InvalidOperationException(Localization.Get("Settings.TransferLegacyPendingConflict"));
            }
            var content = payload.Content;
            var files = payload.Files;
            var backup = CreateBackup(payload.Settings.SourceVersion);
            var journal = new UserSettingsRestoreJournal
            {
                Version = backup.Version,
                BackupId = backup.Id,
                PendingSha256 = Hash(pendingBytes)
            };
            WriteJournal(journal);
            try
            {
                for (var index = 0; index < files.Length; index++)
                {
                    var file = files[index];
                    beforeReplace?.Invoke(index + 1);
                    _ = RegularFileExists(Path.Combine(directory, file.Name));
                    UserSettingsTransferFiles.WriteAtomic(Path.Combine(directory, file.Name), content[file.Name]);
                }
                journal = journal with { Phase = "committed" };
                WriteJournal(journal);
            }
            catch (Exception failure)
            {
                try
                {
                    RestoreBackup(backup);
                    File.Delete(journalPath);
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("用户设置恢复失败，且无法完整回滚；已保留恢复日志和原始备份。", failure, rollbackFailure);
                }
                throw;
            }
            FinishCommittedRestore(journal);
            changed = true;
        }
        finally
        {
            Exit();
            if (changed)
            {
                PendingChanged?.Invoke();
            }
        }
    }

    /// <summary>宿主先排空暂存操作；释放后拒绝继续修改恢复状态。</summary>
    public void Dispose()
    {
        lock (lifetime)
        {
            if (disposed)
            {
                return;
            }
            if (operations != 0)
            {
                throw new InvalidOperationException("请先等待用户设置暂存操作完成。");
            }
            disposed = true;
            PendingChanged = null;
            gate.Dispose();
        }
    }

    private void RecoverInterruptedRestore()
    {
        if (!RegularFileExists(journalPath))
        {
            return;
        }
        var journal = SettingsTransferJson.Deserialize<UserSettingsRestoreJournal>(
            UserSettingsTransferFiles.Read(journalPath, MAXIMUM_METADATA_BYTES), MAXIMUM_METADATA_BYTES);
        if (journal.Version is not (1 or 2 or UserSettingsBundleStore.CURRENT_VERSION) || journal.BackupId == Guid.Empty || !ValidHash(journal.PendingSha256) ||
            journal.Phase is not ("applying" or "committed"))
        {
            throw new InvalidDataException("用户设置恢复日志无效。");
        }
        var backup = ReadBackup(journal.BackupId);
        if (backup.Version != journal.Version)
        {
            throw new InvalidDataException("用户设置恢复日志与原始备份版本不一致。");
        }
        if (journal.Phase == "committed")
        {
            FinishCommittedRestore(journal);
        }
        else
        {
            RestoreBackup(backup);
            File.Delete(journalPath);
        }
    }

    private UserSettingsRestoreBackup CreateBackup(int version)
    {
        var id = Guid.NewGuid();
        var path = BackupDirectory(id);
        Directory.CreateDirectory(path);
        var files = ImmutableArray.CreateBuilder<UserSettingsRestoreEntry>();
        foreach (var file in UserSettingsBundleStore.GetFiles(version))
        {
            var source = Path.Combine(directory, file.Name);
            if (RegularFileExists(source))
            {
                var bytes = UserSettingsTransferFiles.Read(source, file.MaximumBytes);
                UserSettingsTransferFiles.WriteAtomic(Path.Combine(path, file.Name), bytes);
                files.Add(new(file.Name, true, bytes.Length, Hash(bytes)));
            }
            else
            {
                files.Add(new(file.Name, false, 0, null));
            }
        }
        var backup = new UserSettingsRestoreBackup { Version = version, Id = id, Files = files.ToImmutable() };
        UserSettingsTransferFiles.WriteAtomic(Path.Combine(path, "backup.json"),
            SettingsTransferJson.Serialize(backup, MAXIMUM_METADATA_BYTES));
        RetentionBackupPath = path;
        UserSettingsTransferFiles.WriteAtomic(retentionPath, Encoding.ASCII.GetBytes(id.ToString("N")));
        return backup;
    }

    private UserSettingsRestoreBackup ReadBackup(Guid id)
    {
        var path = BackupDirectory(id);
        var metadataPath = Path.Combine(path, "backup.json");
        _ = RegularFileExists(metadataPath);
        var backup = SettingsTransferJson.Deserialize<UserSettingsRestoreBackup>(
            UserSettingsTransferFiles.Read(metadataPath, MAXIMUM_METADATA_BYTES), MAXIMUM_METADATA_BYTES);
        var expectedFiles = UserSettingsBundleStore.GetFiles(backup.Version);
        if (backup.Id != id || backup.Files.IsDefault || backup.Files.Length != expectedFiles.Length)
        {
            throw new InvalidDataException("用户设置原始备份元数据无效。");
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in backup.Files)
        {
            var file = entry is null ? null : expectedFiles.FirstOrDefault(value => value.Name == entry.Name);
            if (file is null || entry is null || !names.Add(entry.Name) || entry.Length < 0 || entry.Length > file.MaximumBytes ||
                entry.Existed && !ValidHash(entry.Sha256) || !entry.Existed && (entry.Length != 0 || entry.Sha256 is not null))
            {
                throw new InvalidDataException("用户设置原始备份包含未知、重复或无效文件记录。");
            }
        }
        RetentionBackupPath = path;
        return backup;
    }

    private void RestoreBackup(UserSettingsRestoreBackup backup)
    {
        var validated = ReadBackup(backup.Id);
        var content = ReadBackupContent(validated);
        foreach (var entry in validated.Files)
        {
            var target = Path.Combine(directory, entry.Name);
            if (entry.Existed)
            {
                UserSettingsTransferFiles.WriteAtomic(target, content[entry.Name]);
            }
            else
            {
                File.Delete(target);
            }
        }
    }

    private Dictionary<string, byte[]> ReadBackupContent(UserSettingsRestoreBackup validated)
    {
        var path = BackupDirectory(validated.Id);
        var content = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in validated.Files)
        {
            _ = RegularFileExists(Path.Combine(directory, entry.Name));
            var backupFile = Path.Combine(path, entry.Name);
            if (entry.Existed)
            {
                _ = RegularFileExists(backupFile);
                var bytes = UserSettingsTransferFiles.Read(backupFile, entry.Length);
                if (bytes.Length != entry.Length || !string.Equals(Hash(bytes), entry.Sha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("用户设置原始备份内容与校验值不一致。");
                }
                content.Add(entry.Name, bytes);
            }
            else if (RegularFileExists(backupFile))
            {
                throw new InvalidDataException("不存在的原始文件出现了意外备份内容。");
            }
        }
        return content;
    }

    private void FinishCommittedRestore(UserSettingsRestoreJournal journal)
    {
        var backup = ReadBackup(journal.BackupId);
        _ = ReadBackupContent(backup);
        UserSettingsTransferFiles.WriteAtomic(retentionPath, Encoding.ASCII.GetBytes(backup.Id.ToString("N")));
        if (RegularFileExists(pendingPath))
        {
            var bytes = UserSettingsTransferFiles.Read(pendingPath, UserSettingsBundleStore.MAXIMUM_FILE_BYTES);
            if (!string.Equals(Hash(bytes), journal.PendingSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("已提交恢复的待导入设置包发生变化，已保留日志供诊断。");
            }
            File.Delete(pendingPath);
        }
        File.Delete(journalPath);
    }

    private void WriteJournal(UserSettingsRestoreJournal journal)
    {
        _ = RegularFileExists(journalPath);
        UserSettingsTransferFiles.WriteAtomic(journalPath, SettingsTransferJson.Serialize(journal, MAXIMUM_METADATA_BYTES));
    }

    private void EnsureNoJournal()
    {
        if (RegularFileExists(journalPath))
        {
            throw new InvalidOperationException("请先重新启动并完成中断的用户设置恢复。");
        }
    }

    private string BackupDirectory(Guid id)
    {
        var path = Path.Combine(backupsDirectory, id.ToString("N"));
        RejectLink(path);
        return path;
    }

    private void EnsureControlledDirectories()
    {
        RejectLink(directory);
        RejectLink(restoreDirectory);
        RejectLink(backupsDirectory);
        _ = RegularFileExists(journalPath);
        _ = RegularFileExists(retentionPath);
    }

    private FileStream AcquireFileLock()
    {
        Directory.CreateDirectory(restoreDirectory);
        var lockPath = Path.Combine(restoreDirectory, "lock");
        _ = RegularFileExists(lockPath);
        return new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static bool RegularFileExists(string path)
    {
        var attributes = Attributes(path);
        if (attributes is null)
        {
            return false;
        }
        if ((attributes.Value & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
        {
            throw new InvalidDataException("用户设置恢复路径不能是链接或目录文件。");
        }
        return true;
    }

    private static void RejectLink(string path)
    {
        if (Attributes(path) is { } attributes && (attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("用户设置恢复目录不能是文件系统链接。");
        }
    }

    private static FileAttributes? Attributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static bool ValidHash(string? value)
    {
        return value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    }

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        AddOperation();
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RemoveOperation();
            throw;
        }
    }

    private void Enter()
    {
        AddOperation();
        try
        {
            gate.Wait();
        }
        catch
        {
            RemoveOperation();
            throw;
        }
    }

    private void AddOperation()
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            operations++;
        }
    }

    private void Exit()
    {
        gate.Release();
        RemoveOperation();
    }

    private void RemoveOperation()
    {
        lock (lifetime)
        {
            operations--;
        }
    }
}
