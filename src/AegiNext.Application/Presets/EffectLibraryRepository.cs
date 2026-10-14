using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Presets;

/// <summary>以不可变逐项文件和原子清单维护共享效果库；只在提交成功后发布完整快照。</summary>
public sealed class EffectLibraryRepository : IDisposable
{
    private readonly string? legacyStyleFilePath;
    private readonly string? legacyEffectFilePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private EffectLibrarySnapshot snapshot = new();
    private EffectLibraryManifest? manifest;
    private ImmutableArray<Exception> maintenanceErrors = [];
    private bool loaded;
    private bool disposed;
    private int operations;

    /// <summary>由宿主指定效果库绝对目录及需要一次性迁移的旧库路径；不依赖工作目录。</summary>
    public EffectLibraryRepository(string absoluteDirectoryPath, string? legacyStyleFilePath = null, string? legacyEffectFilePath = null)
    {
        DirectoryPath = AbsolutePath(absoluteDirectoryPath, nameof(absoluteDirectoryPath));
        ManifestPath = Path.Combine(DirectoryPath, "manifest.json");
        this.legacyStyleFilePath = legacyStyleFilePath is null ? null : AbsolutePath(legacyStyleFilePath, nameof(legacyStyleFilePath));
        this.legacyEffectFilePath = legacyEffectFilePath is null ? null : AbsolutePath(legacyEffectFilePath, nameof(legacyEffectFilePath));
        if (this.legacyStyleFilePath is { } styles && IsWithinDirectory(styles) ||
            this.legacyEffectFilePath is { } effects && IsWithinDirectory(effects))
        {
            throw new ArgumentException("旧库迁移来源不能位于新效果库目录内。");
        }
    }

    public string DirectoryPath { get; }
    public string ManifestPath { get; }
    public EffectLibrarySnapshot Snapshot => Volatile.Read(ref snapshot);
    public event EventHandler<EffectLibraryChangedEventArgs>? Changed;

    /// <summary>已完成提交后的归档源清理或旧条目清理错误；不会把成功提交降级为失败。</summary>
    public ImmutableArray<Exception> MaintenanceErrors
    {
        get
        {
            lock (lifetime)
            {
                return maintenanceErrors;
            }
        }
    }

    /// <summary>完整校验清单和全部条目；首次没有清单时迁移旧库，已有清单绝不回退到旧数据。</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(() => ReadAsync(cancellationToken), cancellationToken);
    }

    /// <summary>在共享提交锁内根据当前样式集合构建修改，保留同期个人特效更新。</summary>
    public Task UpdateStylesAsync(Func<SubtitleStylePresetCollection, SubtitleStylePresetCollection> update,
        Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return UpdateCoreAsync(null, current => current with { Styles = update(current.Styles) }, beforeCommit, cancellationToken);
    }

    /// <summary>在共享提交锁内根据当前个人特效集合构建修改，保留同期样式更新。</summary>
    public Task UpdateEffectsAsync(Func<EffectScriptPresetDocument, EffectScriptPresetDocument> update,
        Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return UpdateCoreAsync(null, current => current with { Effects = update(current.Effects) }, beforeCommit, cancellationToken);
    }

    /// <summary>仅在预览修订仍为当前修订时提交跨库修改；过期时在求值修改委托前拒绝。</summary>
    public Task UpdateAsync(Guid expectedRevision, Func<EffectLibrarySnapshot, EffectLibrarySnapshot> update,
        Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return UpdateCoreAsync(expectedRevision, update, beforeCommit, cancellationToken);
    }

    /// <summary>判断导出位置是否会覆盖应用管理的库内容或尚待迁移的旧库文件。</summary>
    public bool IsManagedPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return IsWithinDirectory(fullPath) || PathsEqual(fullPath, legacyStyleFilePath) || PathsEqual(fullPath, legacyEffectFilePath);
    }

    /// <summary>宿主需先等待全部操作结束；门面不拥有此仓库，释放后拒绝新操作。</summary>
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
                throw new InvalidOperationException("请先等待效果库异步操作完成，再释放仓库。");
            }
            disposed = true;
            gate.Dispose();
            Changed = null;
        }
    }

    internal Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => EnsureLoadedAsync(cancellationToken), cancellationToken);
    }

    private Task UpdateCoreAsync(Guid? expectedRevision, Func<EffectLibrarySnapshot, EffectLibrarySnapshot> update,
        Action? beforeCommit, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var current = Snapshot;
            if (expectedRevision is { } expected && current.Revision != expected)
            {
                throw new InvalidOperationException("效果库已发生变化，请重新检查导入预览。");
            }
            var updated = update(current) ?? throw new InvalidDataException("效果库修改不能返回 null。");
            await CommitAsync(updated, null, beforeCommit, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    private async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            operations++;
        }
        var acquired = false;
        EffectLibrarySnapshot? previous = null;
        EffectLibrarySnapshot? current = null;
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            previous = Snapshot;
            await action().ConfigureAwait(false);
            current = Snapshot;
        }
        finally
        {
            if (acquired)
            {
                gate.Release();
            }
            try
            {
                if (previous is not null && current is not null && !ReferenceEquals(previous, current))
                {
                    Changed?.Invoke(this, new(previous, current));
                }
            }
            finally
            {
                lock (lifetime)
                {
                    operations--;
                }
            }
        }
    }

    private Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        return loaded ? Task.CompletedTask : ReadAsync(cancellationToken);
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        var bytes = await ReadOptionalAsync(ManifestPath, EffectLibraryManifestStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
        if (bytes is not null)
        {
            var nextManifest = EffectLibraryManifestStore.Deserialize(bytes);
            var styles = ImmutableArray.CreateBuilder<SubtitleStylePreset>();
            foreach (var name in nextManifest.Styles)
            {
                var content = await ReadEntryAsync("styles", name, ".aegistyles", SubtitleStylePresetStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
                var collection = SubtitleStylePresetStore.Deserialize(content);
                if (collection.Presets.Length != 1 || collection.Presets[0].Id != EffectLibraryManifestStore.EntryIdentity(name, ".aegistyles"))
                {
                    throw new InvalidDataException("本地样式条目必须包含与文件名对应的一个样式。");
                }
                styles.Add(collection.Presets[0]);
            }
            var effects = ImmutableArray.CreateBuilder<EffectScriptPreset>();
            foreach (var name in nextManifest.Effects)
            {
                var content = await ReadEntryAsync("effects", name, ".aegieffect", EffectScriptPresetStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
                var preset = EffectScriptPresetStore.DeserializePreset(content);
                if (preset.Id != EffectLibraryManifestStore.EntryIdentity(name, ".aegieffect"))
                {
                    throw new InvalidDataException("本地特效条目身份与文件名不一致。");
                }
                effects.Add(preset);
            }
            var value = new EffectLibrarySnapshot
            {
                Revision = nextManifest.Revision,
                Styles = new() { Presets = styles.ToImmutable() },
                Effects = new() { Presets = effects.ToImmutable() }
            };
            ValidateSnapshot(value);
            cancellationToken.ThrowIfCancellationRequested();
            if (manifest is not null && manifest.Revision == nextManifest.Revision)
            {
                if (!SameEntries(manifest, nextManifest))
                {
                    throw new InvalidDataException("同一效果库修订不能引用不同条目。");
                }
            }
            else
            {
                Volatile.Write(ref snapshot, value);
            }
            manifest = nextManifest;
            loaded = true;
            CleanupUnreferencedEntries();
            return;
        }
        if (manifest is not null || HasManagedData())
        {
            throw new InvalidDataException("效果库已初始化但清单缺失，不能重新导入旧库。");
        }
        var styleBytes = legacyStyleFilePath is { } stylesPath
            ? await ReadOptionalAsync(stylesPath, SubtitleStylePresetStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false) : null;
        var effectBytes = legacyEffectFilePath is { } effectsPath
            ? await ReadOptionalAsync(effectsPath, EffectScriptPresetStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false) : null;
        var migrated = new EffectLibrarySnapshot
        {
            Styles = styleBytes is null ? new() : SubtitleStylePresetStore.Deserialize(styleBytes),
            Effects = effectBytes is null ? new() : EffectScriptPresetStore.Deserialize(effectBytes)
        };
        ValidateSnapshot(migrated);
        cancellationToken.ThrowIfCancellationRequested();
        if (styleBytes is null && effectBytes is null)
        {
            loaded = true;
            return;
        }
        var archives = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (styleBytes is not null)
        {
            archives.Add(ArchiveRelativePath(legacyStyleFilePath!, styleBytes), styleBytes);
        }
        if (effectBytes is not null)
        {
            var name = ArchiveRelativePath(legacyEffectFilePath!, effectBytes);
            if (archives.ContainsKey(name))
            {
                name = Path.Combine("legacy", Path.GetFileName(legacyEffectFilePath!) + "." + Digest(effectBytes) + ".bak");
            }
            archives.Add(name, effectBytes);
        }
        await CommitAsync(migrated, archives, null, cancellationToken).ConfigureAwait(false);
        loaded = true;
        RemoveArchivedSource(legacyStyleFilePath, styleBytes);
        RemoveArchivedSource(legacyEffectFilePath, effectBytes);
    }

    private async Task<byte[]> ReadEntryAsync(string kind, string name, string extension, int maximumBytes,
        CancellationToken cancellationToken)
    {
        _ = EffectLibraryManifestStore.EntryIdentity(name, extension);
        var path = Path.Combine(DirectoryPath, kind, name);
        byte[] bytes;
        try
        {
            RejectSymbolicLink(Path.Combine(DirectoryPath, kind));
            RejectSymbolicLink(path);
            bytes = await PresetFileReader.ReadAsync(path, maximumBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new InvalidDataException("效果库清单引用的条目不存在。", error);
        }
        if (!name.AsSpan(33, 64).SequenceEqual(Digest(bytes)))
        {
            throw new InvalidDataException("效果库条目内容与文件名中的 SHA-256 不一致。");
        }
        return bytes;
    }

    private async Task CommitAsync(EffectLibrarySnapshot value, Dictionary<string, byte[]>? archives,
        Action? beforeCommit, CancellationToken cancellationToken)
    {
        ValidateSnapshot(value);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var styleNames = ImmutableArray.CreateBuilder<string>();
        foreach (var preset in value.Styles.Presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = SubtitleStylePresetStore.Serialize(new() { Presets = [preset] });
            var name = $"{preset.Id:N}-{Digest(bytes)}.aegistyles";
            styleNames.Add(name);
            entries.Add(Path.Combine("styles", name), bytes);
        }
        var effectNames = ImmutableArray.CreateBuilder<string>();
        foreach (var preset in value.Effects.Presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = EffectScriptPresetStore.SerializePreset(preset);
            var name = $"{preset.Id:N}-{Digest(bytes)}.aegieffect";
            effectNames.Add(name);
            entries.Add(Path.Combine("effects", name), bytes);
        }
        var nextManifest = new EffectLibraryManifest
        {
            Revision = Guid.NewGuid(),
            Styles = styleNames.ToImmutable(),
            Effects = effectNames.ToImmutable()
        };
        if (archives is null && (manifest is not null && SameEntries(manifest, nextManifest) ||
            manifest is null && nextManifest.Styles.IsEmpty && nextManifest.Effects.IsEmpty))
        {
            return;
        }
        if (archives is not null)
        {
            foreach (var archive in archives)
            {
                entries.Add(archive.Key, archive.Value);
            }
        }
        Directory.CreateDirectory(DirectoryPath);
        var staging = Path.Combine(DirectoryPath, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var published = new List<string>();
        var committed = false;
        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(DirectoryPath, entry.Key);
                if (File.Exists(destination))
                {
                    RejectSymbolicLink(destination);
                    var existing = await PresetFileReader.ReadAsync(destination, entry.Value.Length, cancellationToken).ConfigureAwait(false);
                    if (!existing.AsSpan().SequenceEqual(entry.Value))
                    {
                        throw new InvalidDataException("效果库已有不可变条目内容无效，未覆盖原文件。");
                    }
                    continue;
                }
                var temporary = Path.Combine(staging, entry.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(temporary)!);
                await WriteNewAsync(temporary, entry.Value, cancellationToken).ConfigureAwait(false);
            }
            var manifestTemporary = Path.Combine(staging, "manifest.json");
            await WriteNewAsync(manifestTemporary, EffectLibraryManifestStore.Serialize(nextManifest), cancellationToken).ConfigureAwait(false);
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var temporary = Path.Combine(staging, entry.Key);
                if (!File.Exists(temporary))
                {
                    continue;
                }
                var destination = Path.Combine(DirectoryPath, entry.Key);
                var parent = Path.GetDirectoryName(destination)!;
                Directory.CreateDirectory(parent);
                RejectSymbolicLink(parent);
                File.Move(temporary, destination);
                published.Add(destination);
            }
            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            File.Move(manifestTemporary, ManifestPath, true);
            committed = true;
            manifest = nextManifest;
            Volatile.Write(ref snapshot, value with { Revision = nextManifest.Revision });
            CleanupUnreferencedEntries();
        }
        finally
        {
            if (!committed)
            {
                foreach (var path in published)
                {
                    TryMaintenance(() => File.Delete(path));
                }
            }
            TryMaintenance(() =>
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            });
        }
    }

    private static void ValidateSnapshot(EffectLibrarySnapshot value)
    {
        _ = SubtitleStylePresetStore.Serialize(value.Styles);
        _ = EffectScriptPresetStore.Serialize(value.Effects);
    }

    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(true);
    }

    private static async Task<byte[]?> ReadOptionalAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            RejectSymbolicLink(path);
            return await PresetFileReader.ReadAsync(path, maximumBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private string ArchiveRelativePath(string source, byte[] bytes)
    {
        var name = Path.Combine("legacy", Path.GetFileName(source));
        var path = Path.Combine(DirectoryPath, name);
        if (File.Exists(path))
        {
            name = Path.Combine("legacy", Path.GetFileName(source) + "." + Digest(bytes) + ".bak");
        }
        return name;
    }

    private void RemoveArchivedSource(string? path, byte[]? archived)
    {
        if (path is null || archived is null)
        {
            return;
        }
        TryMaintenance(() =>
        {
            if (File.Exists(path) && new FileInfo(path).Length == archived.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(archived))
            {
                File.Delete(path);
            }
        });
    }

    private void CleanupUnreferencedEntries()
    {
        if (manifest is null)
        {
            return;
        }
        CleanupDirectory("styles", ".aegistyles", manifest.Styles);
        CleanupDirectory("effects", ".aegieffect", manifest.Effects);
        TryMaintenance(() =>
        {
            foreach (var directory in Directory.EnumerateDirectories(DirectoryPath, ".staging-*", SearchOption.TopDirectoryOnly))
            {
                Directory.Delete(directory, true);
            }
        });
    }

    private void CleanupDirectory(string kind, string extension, ImmutableArray<string> referenced)
    {
        var directory = Path.Combine(DirectoryPath, kind);
        if (!Directory.Exists(directory))
        {
            return;
        }
        var active = referenced.ToHashSet(StringComparer.Ordinal);
        TryMaintenance(() =>
        {
            RejectSymbolicLink(directory);
            foreach (var path in Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly))
            {
                if (!active.Contains(Path.GetFileName(path)))
                {
                    File.Delete(path);
                }
            }
        });
    }

    private bool HasManagedData()
    {
        return Directory.Exists(Path.Combine(DirectoryPath, "legacy")) && Directory.EnumerateFiles(Path.Combine(DirectoryPath, "legacy")).Any() ||
            Directory.Exists(Path.Combine(DirectoryPath, "styles")) && Directory.EnumerateFiles(Path.Combine(DirectoryPath, "styles"), "*.aegistyles").Any() ||
            Directory.Exists(Path.Combine(DirectoryPath, "effects")) && Directory.EnumerateFiles(Path.Combine(DirectoryPath, "effects"), "*.aegieffect").Any();
    }

    private void TryMaintenance(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            lock (lifetime)
            {
                maintenanceErrors = maintenanceErrors.Add(error);
            }
        }
    }

    private bool IsWithinDirectory(string path)
    {
        var root = EffectLibraryPath.ResolvePhysicalPath(DirectoryPath);
        var destination = EffectLibraryPath.ResolvePhysicalPath(path);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(destination, root, comparison) || destination.StartsWith(prefix, comparison);
    }

    private static bool PathsEqual(string path, string? other)
    {
        return other is not null && string.Equals(EffectLibraryPath.ResolvePhysicalPath(path), EffectLibraryPath.ResolvePhysicalPath(other),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool SameEntries(EffectLibraryManifest first, EffectLibraryManifest second)
    {
        return first.Styles.SequenceEqual(second.Styles) && first.Effects.SequenceEqual(second.Effects);
    }

    private static string AbsolutePath(string path, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameter);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("效果库路径必须为本机绝对路径。", parameter);
        }
        return Path.GetFullPath(path);
    }

    private static string Digest(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static void RejectSymbolicLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("效果库条目及清单不能使用符号链接。");
        }
    }
}
