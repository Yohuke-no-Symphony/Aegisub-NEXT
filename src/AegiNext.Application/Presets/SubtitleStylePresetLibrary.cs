using System.Collections.Immutable;
using AegiNext.Application.Fonts;
using AegiNext.Core.Presets;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Presets;

/// <summary>共享效果库的样式门面，全部修改经同一清单事务发布。</summary>
public sealed class SubtitleStylePresetLibrary : IDisposable
{
    private readonly EffectLibraryRepository repository;
    private readonly bool ownsRepository;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock lifetime = new();
    private bool disposed;
    private int operations;

    /// <summary>创建独立的当前版本目录库；不读取旧单文件样式库。</summary>
    public SubtitleStylePresetLibrary(string absoluteDirectoryPath) : this(new EffectLibraryRepository(absoluteDirectoryPath))
    {
        ownsRepository = true;
    }

    /// <summary>连接宿主拥有的共享效果库，释放门面不会释放共享仓库。</summary>
    public SubtitleStylePresetLibrary(EffectLibraryRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        this.repository = repository;
    }

    public SubtitleStylePresetCollection Snapshot => repository.Snapshot.Styles;

    /// <summary>读取并完整验证当前版本目录库。</summary>
    public Task LoadAsync(CancellationToken cancellationToken = default) => ExecuteAsync(() => repository.LoadAsync(cancellationToken), cancellationToken);

    /// <summary>更新样式元数据，保留当前私有字体和同期个人特效。</summary>
    public Task UpsertAsync(SubtitleStylePreset preset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ExecuteAsync(() => repository.UpdateStylesAsync(current => Upsert(current, preset), cancellationToken: cancellationToken), cancellationToken);
    }

    /// <summary>保存捕获的样式；空字体集合仅更新参数，携带工程字体族时将它们与样式在同一修订保存。</summary>
    public Task UpsertAsync(CapturedSubtitleStyle captured, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if (captured.FontFamilies.IsDefault)
        {
            throw new InvalidDataException("捕获的字体族集合不能为默认值。");
        }
        if (captured.FontFamilies.IsEmpty)
        {
            return UpsertAsync(captured.Preset, cancellationToken);
        }
        var styles = new SubtitleStylePresetCollection { Presets = [captured.Preset] };
        PrivateFontFiles.ValidateCoverage(styles, captured.FontFamilies);
        return ExecuteAsync(() => repository.UpdateContentAsync(current => current with
        {
            Styles = Upsert(current.Styles, captured.Preset),
            FontFamilies = PrivateFontFiles.Merge(current.FontFamilies, captured.FontFamilies)
        }, cancellationToken: cancellationToken), cancellationToken);
    }

    /// <summary>仅覆盖已保存样式的参数，保留提交时的身份、名称和时间后续处理器，并原子保存携带的工程字体族。</summary>
    public Task OverwriteAsync(CapturedSubtitleStyle captured, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if (captured.FontFamilies.IsDefault)
        {
            throw new InvalidDataException("捕获的字体族集合不能为默认值。");
        }
        if (!captured.FontFamilies.IsEmpty)
        {
            var styles = new SubtitleStylePresetCollection { Presets = [captured.Preset] };
            PrivateFontFiles.ValidateCoverage(styles, captured.FontFamilies);
        }
        return ExecuteAsync(() => repository.UpdateContentAsync(current =>
        {
            var target = current.Styles.Presets.FirstOrDefault(preset => preset.Id == captured.Preset.Id)
                ?? throw new InvalidDataException("待覆盖的样式预设不存在。");
            var index = current.Styles.Presets.IndexOf(target);
            return current with
            {
                Styles = current.Styles with
                {
                    Presets = current.Styles.Presets.SetItem(index, target with { Style = captured.Preset.Style })
                },
                FontFamilies = captured.FontFamilies.IsEmpty
                    ? current.FontFamilies
                    : PrivateFontFiles.Merge(current.FontFamilies, captured.FontFamilies)
            };
        }, cancellationToken: cancellationToken), cancellationToken);
    }

    /// <summary>一次设置或解除全部所选样式的时间后续处理器关联。</summary>
    public Task SetTimingPostProcessorAsync(IEnumerable<Guid> presetIds, TimingPostProcessorOptions? options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(presetIds);
        var ids = presetIds.ToHashSet();
        if (ids.Count == 0)
        {
            return ExecuteAsync(() => Task.CompletedTask, cancellationToken);
        }
        return ExecuteAsync(() => repository.UpdateStylesAsync(current =>
        {
            RequireSelection(current, ids);
            try
            {
                options?.Validate();
            }
            catch (ArgumentOutOfRangeException error)
            {
                throw new InvalidDataException("字幕样式关联的时间后续处理器参数无效。", error);
            }
            return current with
            {
                Presets = current.Presets.Select(preset => ids.Contains(preset.Id) ? preset with { TimingPostProcessor = options } : preset).ToImmutableArray()
            };
        }, cancellationToken: cancellationToken), cancellationToken);
    }

    /// <summary>删除一个样式，保留已经安装的私有字体族。</summary>
    public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) => RemoveAsync([id], cancellationToken);

    /// <summary>完整验证选择后一次删除，失败或取消时不发布部分内容。</summary>
    public Task RemoveAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var selection = ids.ToHashSet();
        if (selection.Count == 0)
        {
            return ExecuteAsync(() => Task.CompletedTask, cancellationToken);
        }
        return ExecuteAsync(() => repository.UpdateStylesAsync(current =>
        {
            RequireSelection(current, selection);
            return current with { Presets = current.Presets.Where(preset => !selection.Contains(preset.Id)).ToImmutableArray() };
        }, cancellationToken: cancellationToken), cancellationToken);
    }

    /// <summary>导入一个当前样式包；不自动解决样式身份或名称冲突。</summary>
    public Task ImportAsync(string path, CancellationToken cancellationToken = default) => ImportAsync([path], cancellationToken);

    /// <summary>完整读取全部当前样式包后一次提交。</summary>
    public Task ImportAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default) => ImportAsync(paths, null, cancellationToken);

    /// <summary>在样式和字体都暂存完成后调用宿主提交边界。</summary>
    public Task ImportAsync(IEnumerable<string> paths, Action? beforeCommit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var inputs = paths.ToArray();
        if (inputs.Length == 0)
        {
            return ExecuteAsync(() => Task.CompletedTask, cancellationToken);
        }
        return ExecuteAsync(async () =>
        {
            var presets = ImmutableArray.CreateBuilder<SubtitleStylePreset>();
            var families = new Dictionary<string, AegiNext.Core.Fonts.FontFamilyBundle>(StringComparer.OrdinalIgnoreCase);
            var fontData = new Dictionary<string, ImmutableArray<byte>>(StringComparer.Ordinal);
            long inputBytes = 0;
            foreach (var path in inputs)
            {
                var package = await SubtitleStylePresetStore.LoadAsync(path, cancellationToken).ConfigureAwait(false);
                inputBytes = checked(inputBytes + package.Styles.Presets.Sum(preset =>
                    (long)SubtitleStylePresetMetadataStore.Serialize(new() { Presets = [preset] }).Length));
                presets.AddRange(package.Styles.Presets);
                foreach (var family in package.FontFamilies)
                {
                    if (families.TryGetValue(family.FamilyName, out var previous) && !PrivateFontFiles.SameFamily(previous, family))
                    {
                        throw new InvalidDataException("导入输入包含同一字体族的不同版本。");
                    }
                    families[family.FamilyName] = PrivateFontFiles.ShareFontData(family, fontData);
                }
                PrivateFontFiles.ValidateInputBudget(families.Values, inputBytes);
            }
            await repository.UpdateContentAsync(current => current with
            {
                Styles = current.Styles with { Presets = current.Styles.Presets.AddRange(presets) },
                FontFamilies = PrivateFontFiles.Merge(current.FontFamilies, families.Values)
            }, beforeCommit, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>导出指定或全部样式，字体必须已安装在当前库。</summary>
    public Task ExportAsync(string path, IEnumerable<Guid>? ids = null, CancellationToken cancellationToken = default)
    {
        var selected = ids?.ToHashSet();
        return ExecuteAsync(async () =>
        {
            await repository.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            var captured = repository.Snapshot;
            if (selected is not null)
            {
                RequireSelection(captured.Styles, selected);
            }
            var styles = captured.Styles with
            {
                Presets = selected is null ? captured.Styles.Presets : captured.Styles.Presets.Where(preset => selected.Contains(preset.Id)).ToImmutableArray()
            };
            await ExportCoreAsync(styles, captured, path, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>导出冻结的单个样式及其已经安装的完整字体族。</summary>
    public Task ExportAsync(SubtitleStylePreset preset, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ExecuteAsync(() => ExportCoreAsync(new() { Presets = [preset] }, repository.Snapshot, path, cancellationToken), cancellationToken);
    }

    /// <summary>操作尚未结束时拒绝释放，确保宿主显式等待异步工作。</summary>
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
            if (ownsRepository)
            {
                repository.Dispose();
            }
        }
    }

    private Task ExportCoreAsync(SubtitleStylePresetCollection styles, EffectLibrarySnapshot snapshot, string path, CancellationToken cancellationToken)
    {
        if (repository.IsManagedPath(path))
        {
            throw new InvalidDataException("导出位置不能覆盖正在使用的效果库或私有字体。");
        }
        var names = styles.Presets.Select(preset => preset.Style.FontFamily).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return SubtitleStylePresetStore.SaveAsync(new()
        {
            Styles = styles, FontFamilies = snapshot.FontFamilies.Where(family => names.Contains(family.FamilyName)).ToImmutableArray()
        }, path, cancellationToken);
    }

    private static SubtitleStylePresetCollection Upsert(SubtitleStylePresetCollection current, SubtitleStylePreset preset)
    {
        var index = current.Presets.IndexOf(current.Presets.FirstOrDefault(item => item.Id == preset.Id)!);
        return current with { Presets = index < 0 ? current.Presets.Add(preset) : current.Presets.SetItem(index, preset) };
    }

    private static void RequireSelection(SubtitleStylePresetCollection current, HashSet<Guid> ids)
    {
        if (current.Presets.Count(preset => ids.Contains(preset.Id)) != ids.Count)
        {
            throw new InvalidDataException("所选样式预设不存在。");
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
}
