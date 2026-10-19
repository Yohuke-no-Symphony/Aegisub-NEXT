using AegiNext.Application.Presets;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Tests.Presets;

/// <summary>样式交换文件整批导入的提交边界。</summary>
public sealed class SubtitleStyleBatchImportTests
{
    /// <summary>多个文件的全部样式在一次提交后可从磁盘读回。</summary>
    [Fact]
    public async Task MultipleFilesCommitAllPresetsAndCanBeReadBack()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.aegistyles");
        using var library = new SubtitleStylePresetLibrary(libraryPath);
        var retained = Preset("Existing");
        var first = Preset("First");
        var second = Preset("Second");
        var third = Preset("Third");
        await library.UpsertAsync(retained);
        var firstPath = Path.Combine(directory.Path, "first.aegistyles");
        var secondPath = Path.Combine(directory.Path, "second.aegistyles");
        await TestStylePackageStore.SaveAsync(new() { Presets = [first, second] }, firstPath);
        await TestStylePackageStore.SaveAsync(new() { Presets = [third] }, secondPath);

        await library.ImportAsync([firstPath, secondPath]);

        Assert.Equal(new[] { retained.Id, first.Id, second.Id, third.Id }, library.Snapshot.Presets.Select(item => item.Id));
        var readBack = await TestStyleLibraryStorage.LoadAsync(libraryPath);
        Assert.Equal(library.Snapshot.Presets.Select(item => item.Id), readBack.Presets.Select(item => item.Id));
    }

    /// <summary>后续损坏文件不得使之前已读取的文件部分入库。</summary>
    [Fact]
    public async Task LaterInvalidFilePreservesSnapshotAndDisk()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.aegistyles");
        using var library = new SubtitleStylePresetLibrary(libraryPath);
        await library.UpsertAsync(Preset("Existing"));
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath));
        var firstPath = Path.Combine(directory.Path, "first.aegistyles");
        var secondPath = Path.Combine(directory.Path, "invalid.aegistyles");
        await TestStylePackageStore.SaveAsync(new() { Presets = [Preset("Valid")] }, firstPath);
        await File.WriteAllTextAsync(secondPath, "invalid JSON");

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync([firstPath, secondPath]));

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath)));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>跨文件名称或身份重复时整批拒绝。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CrossFileConflictsPreserveSnapshotAndDisk(bool duplicateId)
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.aegistyles");
        using var library = new SubtitleStylePresetLibrary(libraryPath);
        await library.UpsertAsync(Preset("Existing"));
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath));
        var first = Preset("Imported");
        var second = duplicateId ? first with { Name = "Other" } : Preset("IMPORTED");
        var firstPath = Path.Combine(directory.Path, "first.aegistyles");
        var secondPath = Path.Combine(directory.Path, "second.aegistyles");
        await TestStylePackageStore.SaveAsync(new() { Presets = [first] }, firstPath);
        await TestStylePackageStore.SaveAsync(new() { Presets = [second] }, secondPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync([firstPath, secondPath]));

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath)));
    }

    /// <summary>取消与空输入都不改变已有提交。</summary>
    [Fact]
    public async Task CancellationAndEmptyBatchPreserveSnapshotAndDisk()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.aegistyles");
        using var library = new SubtitleStylePresetLibrary(libraryPath);
        await library.UpsertAsync(Preset("Existing"));
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath));
        var firstPath = Path.Combine(directory.Path, "first.aegistyles");
        await TestStylePackageStore.SaveAsync(new() { Presets = [Preset("Imported")] }, firstPath);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ImportAsync([firstPath], cancellation.Token));
        await library.ImportAsync(Array.Empty<string>());

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath)));
    }

    /// <summary>文件选择期间库发生修改时，导出仍使用先前捕获的样式快照。</summary>
    [Fact]
    public async Task SnapshotExportPreservesCapturedContentAfterLibraryChanges()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.aegistyles");
        using var library = new SubtitleStylePresetLibrary(libraryPath);
        var captured = Preset("Captured");
        await library.UpsertAsync(TestStyleLibraryStorage.Captured(captured));
        var changed = captured with { Name = "Changed", Style = captured.Style with { FontSize = 99 } };
        await library.UpsertAsync(changed);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath));
        var destination = Path.Combine(directory.Path, "selected.aegistyles");

        await library.ExportAsync(captured, destination);

        var exported = Assert.Single((await TestStylePackageStore.LoadAsync(destination)).Presets);
        Assert.Equal(captured, exported);
        Assert.Equal(changed, Assert.Single(library.Snapshot.Presets));
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath)));
    }

    /// <summary>单条快照导出禁止写回当前库，非法内容不替换既有目标。</summary>
    [Fact]
    public async Task SnapshotExportCannotOverwriteLibraryAndInvalidContentPreservesDestination()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.aegistyles");
        using var library = new SubtitleStylePresetLibrary(libraryPath);
        var preset = Preset("Existing");
        await library.UpsertAsync(preset);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath));
        var destination = Path.Combine(directory.Path, "existing.aegistyles");
        await File.WriteAllTextAsync(destination, "Retain existing target");

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(preset, libraryPath));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(preset with { Id = Guid.Empty }, destination));

        Assert.Equal("Retain existing target", await File.ReadAllTextAsync(destination));
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(TestStyleLibraryStorage.ManifestPath(libraryPath)));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    private static SubtitleStylePreset Preset(string name)
    {
        return new(Guid.NewGuid(), name, new());
    }
}
