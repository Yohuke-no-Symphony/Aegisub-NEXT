using System.Collections.Immutable;

namespace AegiNext.Application.Presets;

/// <summary>串行提交个人脚本库；仅在原子写入成功后发布不可变快照。</summary>
public sealed class EffectScriptPresetLibrary : IDisposable
{
    private readonly string filePath;
    private readonly EffectLibraryRepository? repository;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private EffectScriptPresetDocument snapshot = new();
    private bool loaded;
    private bool disposed;
    private int operations;

    /// <summary>由组合根提供个人数据目录内的绝对文件路径。</summary>
    public EffectScriptPresetLibrary(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        if (!Path.IsPathFullyQualified(absoluteFilePath))
        {
            throw new ArgumentException("特效脚本库路径必须为绝对路径。", nameof(absoluteFilePath));
        }

        filePath = Path.GetFullPath(absoluteFilePath);
    }

    /// <summary>连接宿主拥有的共享多文件效果库；门面仅管理自身操作生命周期。</summary>
    public EffectScriptPresetLibrary(EffectLibraryRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        this.repository = repository;
        filePath = repository.ManifestPath;
    }

    public EffectScriptPresetDocument Snapshot => repository?.Snapshot.Effects ?? Volatile.Read(ref snapshot);

    /// <summary>加载并验证完整库；不存在时使用空库，损坏时保留最后成功快照。</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            if (repository is not null)
            {
                await repository.LoadAsync(cancellationToken).ConfigureAwait(false);
                loaded = true;
            }
            else
            {
                await ReadAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);
    }

    /// <summary>新增或修改个人模板，所有冲突在落盘之前拒绝。</summary>
    public Task UpsertAsync(EffectScriptPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ExecuteAsync(async () =>
        {
            if (repository is not null)
            {
                await repository.UpdateEffectsAsync(current =>
                {
                    var index = FindIndex(current, preset.Id);
                    var items = index < 0 ? current.Presets.Add(preset) : current.Presets.SetItem(index, preset);
                    return current with { Presets = items };
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var index = FindIndex(preset.Id);
            var items = index < 0 ? snapshot.Presets.Add(preset) : snapshot.Presets.SetItem(index, preset);
            await CommitAsync(snapshot with { Presets = items }, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>删除个人模板，失败时保留快照和磁盘内容。</summary>
    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return RemoveAsync([id], cancellationToken);
    }

    /// <summary>固定并去重全部身份；完整验证后一次提交，失败或取消不部分删除。</summary>
    public Task RemoveAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var selection = ids.ToHashSet();
        return ExecuteAsync(async () =>
        {
            if (selection.Count == 0)
            {
                return;
            }

            if (repository is not null)
            {
                await repository.UpdateEffectsAsync(current =>
                {
                    if (current.Presets.Count(preset => selection.Contains(preset.Id)) != selection.Count)
                    {
                        throw new InvalidDataException("特效模板不存在。");
                    }
                    return current with { Presets = current.Presets.Where(preset => !selection.Contains(preset.Id)).ToImmutableArray() };
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.Presets.Count(preset => selection.Contains(preset.Id)) != selection.Count)
            {
                throw new InvalidDataException("特效模板不存在。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var items = snapshot.Presets.Where(preset => !selection.Contains(preset.Id)).ToImmutableArray();
            await CommitAsync(snapshot with { Presets = items }, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>原子导入单个 .aegifx；名称采用脚本标识，冲突明确失败。</summary>
    public Task ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        return ImportAsync([path], cancellationToken);
    }

    /// <summary>读取全部脚本并验证合并集合后一次提交；失败、冲突或取消均不部分入库。</summary>
    public Task ImportAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        return ImportAsync(paths, null, cancellationToken);
    }

    /// <summary>Reads and validates all scripts before invoking the commit boundary for the library replacement.</summary>
    public Task ImportAsync(IEnumerable<string> paths, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var inputs = paths.ToArray();
        return ExecuteAsync(async () =>
        {
            if (inputs.Length == 0)
            {
                return;
            }

            if (repository is not null)
            {
                var imported = ImmutableArray.CreateBuilder<EffectScriptPreset>();
                foreach (var path in inputs)
                {
                    if (string.Equals(Path.GetExtension(path), ".aegieffect", StringComparison.OrdinalIgnoreCase))
                    {
                        imported.Add(await EffectScriptPresetStore.ReadPresetAsync(path, cancellationToken).ConfigureAwait(false));
                    }
                    else
                    {
                        var template = await EffectScriptPresetStore.ReadScriptAsync(path, cancellationToken).ConfigureAwait(false);
                        imported.Add(new(Guid.NewGuid(), template.Script.Id, template.Source));
                    }
                }
                await repository.UpdateEffectsAsync(current => current with { Presets = current.Presets.AddRange(imported) },
                    beforeCommit, cancellationToken).ConfigureAwait(false);
                return;
            }

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var items = snapshot.Presets;
            foreach (var path in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var template = await EffectScriptPresetStore.ReadScriptAsync(path, cancellationToken).ConfigureAwait(false);
                items = items.Add(new(Guid.NewGuid(), template.Script.Id, template.Source));
            }

            var combined = snapshot with { Presets = items };
            EffectScriptPresetService.Validate(combined);
            cancellationToken.ThrowIfCancellationRequested();
            await CommitAsync(combined, beforeCommit, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>按个人模板身份导出源文件，不允许覆盖当前个人库。</summary>
    public Task ExportAsync(Guid id, string path, CancellationToken cancellationToken = default)
    {
        if (repository?.IsManagedPath(path) == true || string.Equals(Path.GetFullPath(path), filePath,
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException("导出位置不能是当前特效脚本库。");
        }

        return ExecuteAsync(async () =>
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var preset = Snapshot.Presets.FirstOrDefault(item => item.Id == id) ?? throw new InvalidDataException("特效模板不存在。");
            await EffectScriptPresetStore.WriteScriptAsync(preset.Source, path, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>导出所选内置或个人模板快照的原文；不改变库，也不允许覆盖当前库文件。</summary>
    public Task ExportAsync(EffectScriptPreset preset, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var destination = Path.GetFullPath(path);
        if (repository?.IsManagedPath(destination) == true || string.Equals(destination, filePath,
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException("导出位置不能是当前特效脚本库。");
        }

        return ExecuteAsync(() => EffectScriptPresetStore.WriteScriptAsync(preset.Source, destination, cancellationToken), cancellationToken);
    }

    /// <summary>宿主需先等待已有操作结束；释放后拒绝继续使用。</summary>
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
                throw new InvalidOperationException("请先等待特效脚本库异步操作完成。");
            }

            disposed = true;
            gate.Dispose();
        }
    }

    private async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            operations++;
        }

        var acquired = false;
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            await action().ConfigureAwait(false);
        }
        finally
        {
            if (acquired)
            {
                gate.Release();
            }

            lock (lifetime)
            {
                operations--;
            }
        }
    }

    private Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        return loaded ? Task.CompletedTask : ReadAsync(cancellationToken);
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        if (repository is not null)
        {
            await repository.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            loaded = true;
            return;
        }
        EffectScriptPresetDocument value;
        try
        {
            value = await EffectScriptPresetStore.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            value = new();
        }
        catch (DirectoryNotFoundException)
        {
            value = new();
        }

        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref snapshot, value);
        loaded = true;
    }

    private Task CommitAsync(EffectScriptPresetDocument value, CancellationToken cancellationToken)
    {
        return CommitAsync(value, null, cancellationToken);
    }

    private async Task CommitAsync(EffectScriptPresetDocument value, Action? beforeCommit, CancellationToken cancellationToken)
    {
        await EffectScriptPresetStore.SaveAsync(value, filePath, beforeCommit, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref snapshot, value);
    }

    private int FindIndex(Guid id)
    {
        return FindIndex(snapshot, id);
    }

    private static int FindIndex(EffectScriptPresetDocument collection, Guid id)
    {
        for (var index = 0; index < collection.Presets.Length; index++)
        {
            if (collection.Presets[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }
}
