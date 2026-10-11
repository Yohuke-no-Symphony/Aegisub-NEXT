using AegiNext.Export;
using AegiNext.Export.Presets;

namespace AegiNext.Export.Tests.Presets;

/// <summary>个人压制预设库的完整提交、冲突保护和生命周期验证。</summary>
public sealed class VideoExportPresetLibraryTests
{
    /// <summary>本地库缺失时只发布空集合，不创建用户文件。</summary>
    [Fact]
    public async Task MissingLibraryLoadsEmptyWithoutCreatingAFile()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "nested", "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);

        await library.LoadAsync();

        Assert.Empty(library.Snapshot.Presets);
        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.Throws<ArgumentException>(() => new VideoExportPresetLibrary("relative.aegiexports"));
    }

    /// <summary>并发提交不丢失项目，修改、子集导出和删除可从磁盘读回。</summary>
    [Fact]
    public async Task ConcurrentUpsertsEditExportAndRemovePreserveWholeLibrary()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var first = Preset("First");
        var second = Preset("Second");

        await Task.WhenAll(library.UpsertAsync(first), library.UpsertAsync(second));

        Assert.Equal(2, library.Snapshot.Presets.Length);
        var changed = first with
        {
            Name = "Renamed",
            Settings = first.Settings with { RateControlMode = VideoRateControlMode.VBR, VideoBitrate = 12500000 }
        };
        await library.UpsertAsync(changed);
        Assert.Equal(changed, library.Snapshot.Presets.Single(item => item.Id == first.Id));
        var destination = Path.Combine(directory.Path, "selected.aegiexports");
        await library.ExportAsync(destination, [first.Id]);
        Assert.Equal(changed, Assert.Single((await VideoExportPresetStore.LoadAsync(destination)).Presets));
        await library.RemoveAsync(second.Id);
        Assert.Equal(changed, Assert.Single(library.Snapshot.Presets));
        using var readBack = new VideoExportPresetLibrary(path);
        await readBack.LoadAsync();
        Assert.Equal(changed, Assert.Single(readBack.Snapshot.Presets));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>第一次操作先加载已有库；损坏本地文件不能由修改操作覆盖。</summary>
    [Fact]
    public async Task FirstMutationLoadsExistingLibraryAndRefusesCorruptFiles()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        var existing = Preset("Existing");
        await VideoExportPresetStore.SaveAsync(new() { Presets = [existing] }, path);
        using (var library = new VideoExportPresetLibrary(path))
        {
            await library.UpsertAsync(Preset("Added"));
            Assert.Equal(2, library.Snapshot.Presets.Length);
            Assert.Contains(library.Snapshot.Presets, item => item.Id == existing.Id);
        }

        await File.WriteAllTextAsync(path, "invalid JSON");
        using var corrupted = new VideoExportPresetLibrary(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => corrupted.UpsertAsync(Preset("Cannot overwrite")));
        Assert.Empty(corrupted.Snapshot.Presets);
        Assert.Equal("invalid JSON", await File.ReadAllTextAsync(path));
    }

    /// <summary>全部输入文件验证通过后一次提交，并保持既有预设和导入顺序。</summary>
    [Fact]
    public async Task MultipleFilesImportAllPresetsAndReadBackInOrder()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var retained = Preset("Existing");
        var first = Preset("First");
        var second = Preset("Second");
        var third = Preset("Third");
        await library.UpsertAsync(retained);
        var firstPath = Path.Combine(directory.Path, "first.aegiexports");
        var secondPath = Path.Combine(directory.Path, "second.aegiexports");
        await VideoExportPresetStore.SaveAsync(new() { Presets = [first, second] }, firstPath);
        await VideoExportPresetStore.SaveAsync(new() { Presets = [third] }, secondPath);

        await library.ImportAsync([firstPath, secondPath]);

        Assert.Equal(new[] { retained, first, second, third }, library.Snapshot.Presets.ToArray());
        Assert.Equal(library.Snapshot.Presets.ToArray(),
            (await VideoExportPresetStore.LoadAsync(path)).Presets.ToArray());
    }

    /// <summary>后续损坏文件不会使已读取的文件部分入库。</summary>
    [Fact]
    public async Task LaterInvalidFilePreservesSnapshotAndDisk()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        await library.UpsertAsync(Preset("Existing"));
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        var firstPath = Path.Combine(directory.Path, "valid.aegiexports");
        var secondPath = Path.Combine(directory.Path, "invalid.aegiexports");
        await VideoExportPresetStore.SaveAsync(new() { Presets = [Preset("Imported")] }, firstPath);
        await File.WriteAllTextAsync(secondPath, "invalid JSON");

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync([firstPath, secondPath]));

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>跨文件或与现有库的身份、名称冲突整体拒绝，不静默替换。</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ImportConflictsPreserveSnapshotAndDisk(bool duplicateId, bool conflictsWithExisting)
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var existing = Preset("Existing");
        await library.UpsertAsync(existing);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        var first = conflictsWithExisting ? existing : Preset("Imported");
        var second = duplicateId ? first with { Name = "Other" } : Preset(first.Name.ToUpperInvariant());
        var firstPath = Path.Combine(directory.Path, "first.aegiexports");
        var secondPath = Path.Combine(directory.Path, "second.aegiexports");
        await VideoExportPresetStore.SaveAsync(new() { Presets = [first] }, firstPath);
        await VideoExportPresetStore.SaveAsync(new() { Presets = [second] }, secondPath);

        var inputs = conflictsWithExisting ? new[] { secondPath } : new[] { firstPath, secondPath };
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(inputs));

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    /// <summary>取消、空批次、非法修改和未知删除均保留已经提交的库。</summary>
    [Fact]
    public async Task CancellationEmptyBatchAndInvalidMutationsPreserveSnapshotAndDisk()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var existing = Preset("Existing");
        await library.UpsertAsync(existing);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.UpsertAsync(Preset("Cancelled"), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ImportAsync([path], cancellation.Token));
        await library.ImportAsync(Array.Empty<string>());
        await Assert.ThrowsAsync<InvalidDataException>(() => library.UpsertAsync(Preset("EXISTING")));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            library.UpsertAsync(existing with { Settings = existing.Settings with { Quality = 52 } }));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.RemoveAsync(Guid.NewGuid()));

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>文件选择期间库变化时，单条导出仍使用先前捕获的不可变内容。</summary>
    [Fact]
    public async Task SnapshotExportUsesCapturedContentAndDoesNotModifyLibrary()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var captured = Preset("Captured");
        await library.UpsertAsync(captured);
        var changed = captured with { Name = "Changed", Settings = captured.Settings with { Quality = 30 } };
        await library.UpsertAsync(changed);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        var destination = Path.Combine(directory.Path, "selected.aegiexports");

        await library.ExportPresetAsync(captured, destination);

        Assert.Equal(captured, Assert.Single((await VideoExportPresetStore.LoadAsync(destination)).Presets));
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    /// <summary>多条导出固定捕获集合，后续库修改和输入集合修改不影响同一文件中的预设。</summary>
    [Fact]
    public async Task BatchSnapshotExportCapturesSelectionAndDoesNotReadOrModifyLibrary()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var first = Preset("Captured first");
        var second = Preset("Captured second");
        await library.UpsertAsync(first);
        await library.UpsertAsync(second);
        var captured = new List<VideoExportPreset> { first, second };
        await library.UpsertAsync(first with { Name = "Updated first", Settings = first.Settings with { Quality = 30 } });
        await library.RemoveAsync(second.Id);
        var snapshot = library.Snapshot;
        await File.WriteAllTextAsync(path, "Unavailable current library");
        var destination = Path.Combine(directory.Path, "selection.aegiexports");

        var export = library.ExportPresetsAsync(captured, destination);
        captured.Clear();
        await export;

        Assert.Equal(new[] { first, second }, (await VideoExportPresetStore.LoadAsync(destination)).Presets.ToArray());
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal("Unavailable current library", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>非法批量中的身份冲突、名称冲突或参数错误均不能替换已有导出目标。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InvalidBatchSnapshotExportPreservesTargetAndLibrary(int invalidContent)
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var existing = Preset("Existing");
        await library.UpsertAsync(existing);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        var first = Preset("Captured");
        var second = invalidContent switch
        {
            0 => first with { Name = "Other name" },
            1 => Preset("CAPTURED"),
            _ => Preset("Invalid settings") with { Settings = new() { Quality = 52 } }
        };
        var destination = Path.Combine(directory.Path, "retained.aegiexports");
        await File.WriteAllTextAsync(destination, "Retained target");

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportPresetsAsync([first, second], destination));

        Assert.Equal("Retained target", await File.ReadAllTextAsync(destination));
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>真实父目录符号链接别名不能绕过绝对库路径保护；Windows 运行环境必须允许创建符号链接。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentDirectorySymbolicLinkCannotOverwriteLibrary(bool libraryUsesAlias)
    {
        using var directory = new ExportPresetTestDirectory();
        var physicalDirectory = Directory.CreateDirectory(Path.Combine(directory.Path, "physical")).FullName;
        var aliasDirectory = Path.Combine(directory.Path, "alias");
        Directory.CreateSymbolicLink(aliasDirectory, physicalDirectory);
        try
        {
            var physicalPath = Path.Combine(physicalDirectory, "exports.aegiexports");
            var aliasPath = Path.Combine(aliasDirectory, "exports.aegiexports");
            var libraryPath = libraryUsesAlias ? aliasPath : physicalPath;
            var destination = libraryUsesAlias ? physicalPath : aliasPath;
            Assert.True(Path.IsPathFullyQualified(libraryPath));
            Assert.True(Path.IsPathFullyQualified(destination));
            using var library = new VideoExportPresetLibrary(libraryPath);
            var first = Preset("Retained first");
            var second = Preset("Retained second");
            await library.UpsertAsync(first);
            await library.UpsertAsync(second);
            var snapshot = library.Snapshot;
            var bytes = await File.ReadAllBytesAsync(physicalPath);

            await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(destination, [first.Id]));
            await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportPresetAsync(first, destination));
            await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportPresetsAsync([first], destination));

            Assert.Same(snapshot, library.Snapshot);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(physicalPath));
            Assert.Equal(new[] { first, second }, (await VideoExportPresetStore.LoadAsync(physicalPath)).Presets.ToArray());
            Assert.Empty(Directory.GetFiles(physicalDirectory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(aliasDirectory);
        }
    }

    /// <summary>子集或快照不能覆盖当前库，非法导出不能替换现有目标。</summary>
    [Fact]
    public async Task ExportCannotOverwriteLibraryOrReplaceTargetWithInvalidContent()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var existing = Preset("Existing");
        await library.UpsertAsync(existing);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        var destination = Path.Combine(directory.Path, "retained.aegiexports");
        await File.WriteAllTextAsync(destination, "Retained target");

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(path, []));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportPresetAsync(existing, path));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            library.ExportPresetAsync(existing with { Id = Guid.Empty }, destination));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(destination, [Guid.NewGuid()]));

        Assert.Equal("Retained target", await File.ReadAllTextAsync(destination));
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>磁盘提交失败不能提前发布待写入快照。</summary>
    [Fact]
    public async Task CommitFailureRetainsPublishedSnapshotAndCleansTemporaryFiles()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        await library.UpsertAsync(Preset("Existing"));
        var snapshot = library.Snapshot;
        File.Delete(path);
        Directory.CreateDirectory(path);

        await Assert.ThrowsAsync<IOException>(() => library.UpsertAsync(Preset("Failed")));

        Assert.Same(snapshot, library.Snapshot);
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>加载损坏文件保留最后成功发布的内存快照及原损坏文件。</summary>
    [Fact]
    public async Task FailedReloadKeepsLastSuccessfulSnapshotAndPreservesCorruptFile()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        await library.UpsertAsync(Preset("Existing"));
        var snapshot = library.Snapshot;
        await File.WriteAllTextAsync(path, "invalid JSON");

        await Assert.ThrowsAsync<InvalidDataException>(() => library.LoadAsync());

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal("invalid JSON", await File.ReadAllTextAsync(path));
    }

    /// <summary>操作持有库时拒绝释放，失败释放不损伤当前操作和后续提交。</summary>
    [Fact]
    public async Task ActiveExportRejectsDisposeAndLibraryRemainsUsableAfterCompletion()
    {
        using var directory = new ExportPresetTestDirectory();
        var path = Path.Combine(directory.Path, "exports.aegiexports");
        using var library = new VideoExportPresetLibrary(path);
        var preset = Preset("Exported");
        await library.UpsertAsync(preset);
        var destination = Path.Combine(directory.Path, "selected.aegiexports");

        await library.ExportAsync(destination, EnumerateSelectionWhileActive(library, preset.Id));

        Assert.Equal(preset, Assert.Single((await VideoExportPresetStore.LoadAsync(destination)).Presets));
        await library.UpsertAsync(Preset("After completion"));
        Assert.Equal(2, library.Snapshot.Presets.Length);
    }

    /// <summary>释放后的库拒绝新操作，重复释放安全。</summary>
    [Fact]
    public async Task DisposedLibraryRejectsFurtherOperations()
    {
        using var directory = new ExportPresetTestDirectory();
        var library = new VideoExportPresetLibrary(Path.Combine(directory.Path, "exports.aegiexports"));
        library.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => library.LoadAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => library.UpsertAsync(Preset("Disposed")));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => library.ImportAsync(Array.Empty<string>()));
        library.Dispose();
    }

    private static VideoExportPreset Preset(string name)
    {
        return new(Guid.NewGuid(), name, new());
    }

    private static IEnumerable<Guid> EnumerateSelectionWhileActive(VideoExportPresetLibrary library, Guid id)
    {
        Assert.Throws<InvalidOperationException>(library.Dispose);
        yield return id;
    }
}
