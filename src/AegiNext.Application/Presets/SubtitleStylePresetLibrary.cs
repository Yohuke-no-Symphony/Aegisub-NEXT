using System.Collections.Immutable;
using AegiNext.Core.Presets;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Presets;

/// <summary>串行持久化的应用样式库；磁盘提交成功后发布新的不可变快照。</summary>
public sealed class SubtitleStylePresetLibrary : IDisposable
{
    private readonly string filePath;
    private readonly EffectLibraryRepository? repository;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private SubtitleStylePresetCollection snapshot = new();
    private bool disposed;
    private bool loaded;
    private int operations;

    /// <summary>使用宿主注入的本机绝对库文件路径，不假定工作目录。</summary>
    public SubtitleStylePresetLibrary(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        if (!Path.IsPathFullyQualified(absoluteFilePath))
        {
            throw new ArgumentException("样式库路径必须为本机绝对路径。", nameof(absoluteFilePath));
        }

        filePath = Path.GetFullPath(absoluteFilePath);
    }

    /// <summary>连接宿主拥有的共享多文件效果库；释放门面不释放仓库。</summary>
    public SubtitleStylePresetLibrary(EffectLibraryRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        this.repository = repository;
        filePath = repository.ManifestPath;
    }

    /// <summary>最后一次成功加载或提交的完整库。</summary>
    public SubtitleStylePresetCollection Snapshot => repository?.Snapshot.Styles ?? Volatile.Read(ref snapshot);

    /// <summary>完整加载并验证本地库；不存在时发布空库，损坏文件保持原快照。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
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
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>按标识新增或修改；与其他预设重名时整体拒绝。</summary>
    public async Task UpsertAsync(SubtitleStylePreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (repository is not null)
            {
                await repository.UpdateStylesAsync(current =>
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
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>整体设置或解除指定样式的时间处理器关联；任一标识或参数无效均拒绝，内容相同时不写入。</summary>
    public async Task SetTimingPostProcessorAsync(IEnumerable<Guid> presetIds, TimingPostProcessorOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presetIds);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (repository is not null)
            {
                var ids = presetIds.ToHashSet();
                await repository.UpdateStylesAsync(current => WithTimingPostProcessor(current, ids, options, cancellationToken),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var selection = presetIds.ToHashSet();
            if (snapshot.Presets.Count(preset => selection.Contains(preset.Id)) != selection.Count)
            {
                throw new InvalidDataException("待关联时间后续处理器的样式预设不存在。");
            }

            try
            {
                options?.Validate();
            }
            catch (ArgumentOutOfRangeException error)
            {
                throw new InvalidDataException("字幕样式关联的时间后续处理器参数无效。", error);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var items = snapshot.Presets;
            for (var index = 0; index < items.Length; index++)
            {
                var preset = items[index];
                if (selection.Contains(preset.Id) && preset.TimingPostProcessor != options)
                {
                    items = items.SetItem(index, preset with { TimingPostProcessor = options });
                }
            }

            if (items != snapshot.Presets)
            {
                await CommitAsync(snapshot with { Presets = items }, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>删除已存在预设，提交失败时仍保留原库。</summary>
    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return RemoveAsync([id], cancellationToken);
    }

    /// <summary>固定并去重全部标识；完整验证后一次提交，失败或取消不部分删除。</summary>
    public async Task RemoveAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var selection = ids.ToHashSet();
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (selection.Count == 0)
            {
                return;
            }

            if (repository is not null)
            {
                await repository.UpdateStylesAsync(current =>
                {
                    if (current.Presets.Count(preset => selection.Contains(preset.Id)) != selection.Count)
                    {
                        throw new InvalidDataException("待删除的样式预设不存在。");
                    }
                    return current with { Presets = current.Presets.Where(preset => !selection.Contains(preset.Id)).ToImmutableArray() };
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.Presets.Count(preset => selection.Contains(preset.Id)) != selection.Count)
            {
                throw new InvalidDataException("待删除的样式预设不存在。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var items = snapshot.Presets.Where(preset => !selection.Contains(preset.Id)).ToImmutableArray();
            await CommitAsync(snapshot with { Presets = items }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>整体导入自包含文件；任一标识、名称或载荷冲突均保留现有库。</summary>
    public Task ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        return ImportAsync([path], cancellationToken);
    }

    /// <summary>读取并验证全部交换文件后一次提交；任一文件失败、冲突或取消均不部分入库。</summary>
    public Task ImportAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        return ImportAsync(paths, null, cancellationToken);
    }

    /// <summary>Reads and validates all inputs, invoking the commit boundary immediately before replacing the library.</summary>
    public async Task ImportAsync(IEnumerable<string> paths, Action? beforeCommit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var inputs = paths.ToArray();
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (inputs.Length == 0)
            {
                return;
            }

            if (repository is not null)
            {
                var imported = ImmutableArray.CreateBuilder<SubtitleStylePreset>();
                foreach (var path in inputs)
                {
                    var collection = await SubtitleStylePresetStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                    imported.AddRange(collection.Presets);
                }
                if (imported.Count != 0)
                {
                    await repository.UpdateStylesAsync(current => current with { Presets = current.Presets.AddRange(imported) },
                        beforeCommit, cancellationToken).ConfigureAwait(false);
                }
                return;
            }

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var items = snapshot.Presets;
            foreach (var path in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imported = await SubtitleStylePresetStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                items = items.AddRange(imported.Presets);
            }

            if (items == snapshot.Presets)
            {
                return;
            }

            var combined = snapshot with { Presets = items };
            SubtitleStylePresetValidator.Validate(combined);
            cancellationToken.ThrowIfCancellationRequested();
            await CommitAsync(combined, beforeCommit, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>导出全部或指定预设；不能将子集写回正在使用的库文件。</summary>
    public async Task ExportAsync(string path, IEnumerable<Guid>? ids = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var destination = Path.GetFullPath(path);
        if (repository?.IsManagedPath(destination) == true || string.Equals(destination, filePath, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException("导出位置不能是正在使用的样式库文件。");
        }

        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var exported = Snapshot;
            if (ids is not null)
            {
                var selection = ids.ToHashSet();
                var selected = exported.Presets.Where(preset => selection.Contains(preset.Id)).ToImmutableArray();
                if (selected.Length != selection.Count)
                {
                    throw new InvalidDataException("待导出的样式预设不存在。");
                }

                exported = exported with { Presets = selected };
            }

            await SubtitleStylePresetStore.SaveAsync(exported, destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>将所选样式快照导出为单条自包含集合；不改变库，也不允许覆盖正在使用的库文件。</summary>
    public async Task ExportAsync(SubtitleStylePreset preset, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var destination = Path.GetFullPath(path);
        if (repository?.IsManagedPath(destination) == true || string.Equals(destination, filePath, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidDataException("导出位置不能是正在使用的样式库文件。");
        }

        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SubtitleStylePresetStore.SaveAsync(new() { Presets = [preset] }, destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>宿主应先等待所有异步操作完成；操作尚未结束时拒绝释放以保留确定生命周期。</summary>
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
                throw new InvalidOperationException("请先等待样式库异步操作完成，再释放库。");
            }

            disposed = true;
            gate.Dispose();
        }
    }

    private Task CommitAsync(SubtitleStylePresetCollection updated, CancellationToken cancellationToken)
    {
        return CommitAsync(updated, null, cancellationToken);
    }

    private async Task CommitAsync(SubtitleStylePresetCollection updated, Action? beforeCommit, CancellationToken cancellationToken)
    {
        await SubtitleStylePresetStore.SaveAsync(updated, filePath, beforeCommit, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref snapshot, updated);
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
        SubtitleStylePresetCollection result;
        try
        {
            result = await SubtitleStylePresetStore.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            result = new();
        }
        catch (DirectoryNotFoundException)
        {
            result = new();
        }

        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref snapshot, result);
        loaded = true;
    }

    private int FindIndex(Guid id)
    {
        return FindIndex(snapshot, id);
    }

    private static int FindIndex(SubtitleStylePresetCollection collection, Guid id)
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

    private static SubtitleStylePresetCollection WithTimingPostProcessor(SubtitleStylePresetCollection current, HashSet<Guid> ids,
        TimingPostProcessorOptions? options, CancellationToken cancellationToken)
    {
        if (current.Presets.Count(preset => ids.Contains(preset.Id)) != ids.Count)
        {
            throw new InvalidDataException("待关联时间后续处理器的样式预设不存在。");
        }
        try
        {
            options?.Validate();
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new InvalidDataException("字幕样式关联的时间后续处理器参数无效。", error);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var items = current.Presets;
        for (var index = 0; index < items.Length; index++)
        {
            var preset = items[index];
            if (ids.Contains(preset.Id) && preset.TimingPostProcessor != options)
            {
                items = items.SetItem(index, preset with { TimingPostProcessor = options });
            }
        }
        return items == current.Presets ? current : current with { Presets = items };
    }

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        lock (lifetime)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            operations++;
        }

        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (lifetime)
            {
                operations--;
            }

            throw;
        }
    }

    private void Exit()
    {
        gate.Release();
        lock (lifetime)
        {
            operations--;
        }
    }
}
