using System.Collections.Immutable;

namespace AegiNext.Export.Presets;

/// <summary>串行持久化的个人压制预设库；磁盘提交成功后发布新的不可变快照。</summary>
public sealed class VideoExportPresetLibrary : IDisposable
{
    private readonly string filePath;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private VideoExportPresetCollection snapshot = new();
    private bool disposed;
    private bool loaded;
    private int operations;

    /// <summary>使用宿主注入的本机绝对库文件路径，不依赖当前工作目录。</summary>
    public VideoExportPresetLibrary(string absoluteFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);
        if (!Path.IsPathFullyQualified(absoluteFilePath))
        {
            throw new ArgumentException("压制预设库路径必须为本机绝对路径。", nameof(absoluteFilePath));
        }

        filePath = Path.GetFullPath(absoluteFilePath);
    }

    public VideoExportPresetCollection Snapshot => Volatile.Read(ref snapshot);

    /// <summary>完整加载本地库；不存在时发布空库，损坏文件保持最后成功的快照。</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>按身份新增或修改预设；非法参数或与其他预设重名时整体拒绝。</summary>
    public async Task UpsertAsync(VideoExportPreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
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

    /// <summary>删除已存在的预设；提交失败或身份不存在时保留当前库。</summary>
    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return RemoveAsync([id], cancellationToken);
    }

    /// <summary>固定并去重全部身份；完整验证后一次提交，失败或取消不部分删除。</summary>
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

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.Presets.Count(preset => selection.Contains(preset.Id)) != selection.Count)
            {
                throw new InvalidDataException("待删除的压制预设不存在。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var items = snapshot.Presets.Where(preset => !selection.Contains(preset.Id)).ToImmutableArray();
            await CommitAsync(snapshot with { Presets = items }, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>整体导入一个交换文档；身份、名称或内容冲突时保留现有库。</summary>
    public Task ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        return ImportAsync([path], cancellationToken);
    }

    /// <summary>读取并验证全部输入后一次提交；任一文件失败、冲突或取消都不部分入库。</summary>
    public async Task ImportAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
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

            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var items = snapshot.Presets;
            foreach (var path in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imported = await VideoExportPresetStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                items = items.AddRange(imported.Presets);
                VideoExportPresetValidator.Validate(snapshot with { Presets = items });
            }

            if (items == snapshot.Presets)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            await CommitAsync(snapshot with { Presets = items }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>导出全部或显式选中的预设；不能将导出内容写回当前使用的库文件。</summary>
    public async Task ExportAsync(string path, IEnumerable<Guid>? ids = null,
        CancellationToken cancellationToken = default)
    {
        var destination = GetExportDestination(path);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var exported = snapshot;
            if (ids is not null)
            {
                var selection = ids.ToHashSet();
                var selected = snapshot.Presets.Where(preset => selection.Contains(preset.Id)).ToImmutableArray();
                if (selected.Length != selection.Count)
                {
                    throw new InvalidDataException("待导出的压制预设不存在。");
                }

                exported = snapshot with { Presets = selected };
            }

            await VideoExportPresetStore.SaveAsync(exported, destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>将先前捕获的单条预设导出为独立文档，不修改当前库，也不允许覆盖库文件。</summary>
    public Task ExportPresetAsync(VideoExportPreset preset, string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ExportPresetsAsync([preset], path, cancellationToken);
    }

    /// <summary>将先前捕获的预设集合导出为单个交换文档；立即固定输入，不读取或修改个人库。</summary>
    public async Task ExportPresetsAsync(IEnumerable<VideoExportPreset> presets, string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var exported = new VideoExportPresetCollection { Presets = presets.ToImmutableArray() };
        var destination = GetExportDestination(path);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await VideoExportPresetStore.SaveAsync(exported, destination, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>宿主必须先等待全部异步操作完成；操作尚未排空时拒绝释放以保留确定生命周期。</summary>
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
                throw new InvalidOperationException("请先等待压制预设库异步操作完成，再释放库。");
            }

            disposed = true;
            gate.Dispose();
        }
    }

    private string GetExportDestination(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var destination = Path.GetFullPath(path);
        var resolvedDestination = ResolveParentDirectories(destination);
        var resolvedLibrary = ResolveParentDirectories(filePath);
        if (string.Equals(resolvedDestination, resolvedLibrary, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
        {
            throw new InvalidDataException("导出位置不能是正在使用的压制预设库文件。");
        }

        return destination;
    }

    private static string ResolveParentDirectories(string path)
    {
        var directory = ResolveDirectory(new(Path.GetDirectoryName(path)!));
        return Path.Combine(directory, Path.GetFileName(path));
    }

    private static string ResolveDirectory(DirectoryInfo directory)
    {
        if (directory.Parent is not { } parent)
        {
            return directory.FullName;
        }

        var resolved = new DirectoryInfo(Path.Combine(ResolveDirectory(parent), directory.Name));
        if (resolved.Exists && resolved.ResolveLinkTarget(true) is { } target)
        {
            return ResolveDirectory(new(target.FullName));
        }

        return resolved.FullName;
    }

    private async Task CommitAsync(VideoExportPresetCollection updated, CancellationToken cancellationToken)
    {
        await VideoExportPresetStore.SaveAsync(updated, filePath, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref snapshot, updated);
    }

    private Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        return loaded ? Task.CompletedTask : ReadAsync(cancellationToken);
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        VideoExportPresetCollection result;
        try
        {
            result = await VideoExportPresetStore.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
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
        for (var index = 0; index < snapshot.Presets.Length; index++)
        {
            if (snapshot.Presets[index].Id == id)
            {
                return index;
            }
        }

        return -1;
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
