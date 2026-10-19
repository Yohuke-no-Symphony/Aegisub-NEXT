using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Fonts;
using AegiNext.Application.Fonts;

namespace AegiNext.Application.Presets;

/// <summary>以不可变逐项文件和原子清单维护共享效果库；只在提交成功后发布完整快照。</summary>
public sealed class EffectLibraryRepository : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private EffectLibrarySnapshot snapshot = new();
    private EffectLibraryManifest? manifest;
    private ImmutableArray<Exception> maintenanceErrors = [];
    private bool loaded;
    private bool disposed;
    private int operations;

    /// <summary>使用宿主提供的效果库和私有字体绝对目录；旧版目录原样归档，不转换其中内容。</summary>
    public EffectLibraryRepository(string absoluteDirectoryPath, string? absoluteFontDirectoryPath = null)
    {
        DirectoryPath = Path.TrimEndingDirectorySeparator(AbsolutePath(absoluteDirectoryPath, nameof(absoluteDirectoryPath)));
        FontDirectoryPath = AbsolutePath(absoluteFontDirectoryPath ?? Path.Combine(Path.GetDirectoryName(DirectoryPath)!, "fonts"),
            nameof(absoluteFontDirectoryPath));
        ManifestPath = Path.Combine(DirectoryPath, "manifest.json");
    }

    public string FontDirectoryPath { get; }
    public string DirectoryPath { get; }
    public string ManifestPath { get; }
    public EffectLibrarySnapshot Snapshot => Volatile.Read(ref snapshot);
    public event EventHandler<EffectLibraryChangedEventArgs>? Changed;

    /// <summary>已完成提交后的旧条目或字体文件清理错误；不会把成功提交降级为失败。</summary>
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

    /// <summary>完整校验当前库；首次加载旧版目录时原样归档，并从空库开始。</summary>
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

    /// <summary>判断导出位置是否会覆盖应用管理的效果库和私有字体文件。</summary>
    public bool IsManagedPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return IsWithinDirectory(fullPath) || IsWithinFontDirectory(fullPath);
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

    internal Task UpdateContentAsync(Func<EffectLibrarySnapshot, EffectLibrarySnapshot> update,
        Action? beforeCommit = null, CancellationToken cancellationToken = default)
    {
        return UpdateCoreAsync(null, update, beforeCommit, cancellationToken);
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
            await CommitAsync(updated, beforeCommit, cancellationToken).ConfigureAwait(false);
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
            var nextManifest = EffectLibraryManifestStore.DeserializeCurrent(bytes);
            if (nextManifest is null)
            {
                if (manifest is not null)
                {
                    throw new InvalidDataException("当前效果库清单不能替换为旧版格式。");
                }
                cancellationToken.ThrowIfCancellationRequested();
                RejectSymbolicLink(DirectoryPath);
                var archive = Path.TrimEndingDirectorySeparator(DirectoryPath) + $".legacy-{Guid.NewGuid():N}";
                Directory.Move(DirectoryPath, archive);
                loaded = true;
                return;
            }
            var styles = ImmutableArray.CreateBuilder<SubtitleStylePreset>();
            foreach (var name in nextManifest.Styles)
            {
                var content = await ReadEntryAsync("styles", name, ".json", SubtitleStylePresetMetadataStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
                var collection = SubtitleStylePresetMetadataStore.Deserialize(content);
                if (collection.Presets.Length != 1 || collection.Presets[0].Id != EffectLibraryManifestStore.EntryIdentity(name, ".json"))
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
                Effects = new() { Presets = effects.ToImmutable() },
                FontFamilies = await ReadFontFamiliesAsync(nextManifest.FontFamilies, FontDirectoryPath, cancellationToken).ConfigureAwait(false)
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
        if (manifest is not null || HasManagedData(DirectoryPath))
        {
            throw new InvalidDataException("效果库已初始化但清单缺失，不能重新导入旧库。");
        }
        loaded = true;
    }

    /// <summary>只读当前有效清单及字体字节；不初始化、迁移、清理或写入任何文件。</summary>
    public static async Task<ImmutableArray<FontFamilyBundle>> LoadFontFamiliesAsync(string absoluteDirectoryPath,
        string absoluteFontDirectoryPath, CancellationToken cancellationToken = default)
    {
        var directory = AbsolutePath(absoluteDirectoryPath, nameof(absoluteDirectoryPath));
        var fonts = AbsolutePath(absoluteFontDirectoryPath, nameof(absoluteFontDirectoryPath));
        var manifestPath = Path.Combine(directory, "manifest.json");
        for (var attempt = 0; ; attempt++)
        {
            var bytes = await ReadOptionalAsync(manifestPath, EffectLibraryManifestStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                if (HasManagedData(directory))
                {
                    throw new InvalidDataException("效果库已有条目但有效清单缺失；原文件已保留。");
                }
                return [];
            }
            var current = EffectLibraryManifestStore.DeserializeCurrent(bytes);
            if (current is null)
            {
                return [];
            }
            try
            {
                return await ReadFontFamiliesAsync(current.FontFamilies, fonts, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException) when (attempt < 2)
            {
                var latest = await ReadOptionalAsync(manifestPath, EffectLibraryManifestStore.MAXIMUM_FILE_BYTES, cancellationToken).ConfigureAwait(false);
                if (latest is null || bytes.AsSpan().SequenceEqual(latest))
                {
                    throw;
                }
            }
        }
    }

    private static async Task<ImmutableArray<FontFamilyBundle>> ReadFontFamiliesAsync(ImmutableArray<FontFamilyReference> references,
        string directory, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        var families = ImmutableArray.CreateBuilder<FontFamilyBundle>();
        foreach (var family in references)
        {
            var files = ImmutableArray.CreateBuilder<EmbeddedSubtitleFont>();
            foreach (var file in family.Files)
            {
                if (!data.TryGetValue(file.Sha256, out var bytes))
                {
                    var path = Path.Combine(directory, PrivateFontFiles.Name(file.Sha256));
                    byte[] raw;
                    try
                    {
                        RejectSymbolicLink(directory);
                        RejectSymbolicLink(path);
                        raw = await PresetFileReader.ReadAsync(path, FontFamilyBundleValidator.MAXIMUM_FONT_BYTES, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
                    {
                        throw new InvalidDataException("私有字体清单引用的文件不存在。", error);
                    }
                    bytes = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsImmutableArray(raw);
                    data.Add(file.Sha256, bytes);
                }
                files.Add(new(file.FileName, file.Sha256, bytes));
            }
            families.Add(new(family.FamilyName, files.ToImmutable()));
        }
        var result = families.ToImmutable();
        FontFamilyBundleValidator.ValidateLibrary(result);
        return result;
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

    private async Task CommitAsync(EffectLibrarySnapshot value, Action? beforeCommit, CancellationToken cancellationToken)
    {
        ValidateSnapshot(value);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var styleNames = ImmutableArray.CreateBuilder<string>();
        foreach (var preset in value.Styles.Presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [preset] });
            var name = $"{preset.Id:N}-{Digest(bytes)}.json";
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
            Effects = effectNames.ToImmutable(),
            FontFamilies = PrivateFontFiles.References(value.FontFamilies)
        };
        if (manifest is not null && SameEntries(manifest, nextManifest) ||
            manifest is null && nextManifest.Styles.IsEmpty && nextManifest.Effects.IsEmpty && nextManifest.FontFamilies.IsEmpty)
        {
            return;
        }
        var fontEntries = value.FontFamilies.SelectMany(family => family.Files)
            .DistinctBy(file => file.Sha256).ToDictionary(file => PrivateFontFiles.Name(file.Sha256), file => file.Data.AsMemory(), StringComparer.Ordinal);
        if (fontEntries.Count > 0)
        {
            Directory.CreateDirectory(FontDirectoryPath);
            RejectSymbolicLink(FontDirectoryPath);
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
            foreach (var font in fontEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(FontDirectoryPath, font.Key);
                if (File.Exists(destination))
                {
                    RejectSymbolicLink(destination);
                    var existing = await PresetFileReader.ReadAsync(destination, font.Value.Length, cancellationToken).ConfigureAwait(false);
                    if (!existing.AsSpan().SequenceEqual(font.Value.Span))
                    {
                        throw new InvalidDataException("私有字体文件内容与 SHA-256 不一致，未覆盖原文件。");
                    }
                    continue;
                }
                var temporary = Path.Combine(FontDirectoryPath, $".{font.Key}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await WriteNewAsync(temporary, font.Value, cancellationToken).ConfigureAwait(false);
                    File.Move(temporary, destination);
                    published.Add(destination);
                }
                finally
                {
                    File.Delete(temporary);
                }
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
        _ = SubtitleStylePresetMetadataStore.Serialize(value.Styles);
        _ = EffectScriptPresetStore.Serialize(value.Effects);
        FontFamilyBundleValidator.ValidateLibrary(value.FontFamilies);
    }

    private static async Task WriteNewAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
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

    private void CleanupUnreferencedEntries()
    {
        if (manifest is null)
        {
            return;
        }
        CleanupDirectory("styles", ".json", manifest.Styles);
        var activeFonts = manifest.FontFamilies.SelectMany(family => family.Files).Select(file => PrivateFontFiles.Name(file.Sha256))
            .ToHashSet(StringComparer.Ordinal);
        if (Directory.Exists(FontDirectoryPath))
        {
            TryMaintenance(() =>
            {
                RejectSymbolicLink(FontDirectoryPath);
                foreach (var path in Directory.EnumerateFiles(FontDirectoryPath, "*.font"))
                {
                    if (!activeFonts.Contains(Path.GetFileName(path)))
                    {
                        TryMaintenance(() => File.Delete(path));
                    }
                }
            });
        }
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

    private static bool HasManagedData(string directory)
    {
        return Directory.Exists(Path.Combine(directory, "legacy")) && Directory.EnumerateFiles(Path.Combine(directory, "legacy")).Any() ||
            Directory.Exists(Path.Combine(directory, "styles")) && Directory.EnumerateFiles(Path.Combine(directory, "styles")).Any() ||
            Directory.Exists(Path.Combine(directory, "effects")) && Directory.EnumerateFiles(Path.Combine(directory, "effects"), "*.aegieffect").Any();
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

    private bool IsWithinFontDirectory(string path)
    {
        var root = EffectLibraryPath.ResolvePhysicalPath(FontDirectoryPath);
        var destination = EffectLibraryPath.ResolvePhysicalPath(path);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(destination, root, comparison) || destination.StartsWith(prefix, comparison);
    }

    private static bool SameEntries(EffectLibraryManifest first, EffectLibraryManifest second)
    {
        return first.Styles.SequenceEqual(second.Styles) && first.Effects.SequenceEqual(second.Effects) &&
            first.FontFamilies.Select(family => family.FamilyName + ":" + string.Join(",", family.Files.Select(file => file.Sha256)))
                .SequenceEqual(second.FontFamilies.Select(family => family.FamilyName + ":" + string.Join(",", family.Files.Select(file => file.Sha256))), StringComparer.Ordinal);
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
