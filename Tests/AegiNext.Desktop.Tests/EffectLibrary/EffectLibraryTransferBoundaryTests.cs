using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Desktop.EffectLibrary.Transfer;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.EffectLibrary;

[Collection("Workspace session")]
public sealed class EffectLibraryTransferBoundaryTests
{
    [Fact]
    public async Task StyleAndEffectImportsDeclareTheirInputPathsAndCommitThroughTheSharedService()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var inputDirectory = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(settingsDirectory.Path);
        await owner.Initialization;
        var bundle = CreatePresets();
        var stylePath = Path.Combine(inputDirectory.Path, "style.aegistyles");
        var scriptPath = Path.Combine(inputDirectory.Path, "effect.aegifx");
        await SubtitleStylePresetStore.SaveAsync(new() { Styles = bundle.Styles, FontFamilies = bundle.FontFamilies }, stylePath);
        await EffectScriptPresetStore.WriteScriptAsync(bundle.Effects.Presets.Single().Source, scriptPath);
        var task = new EffectLibraryImportAnalysisTask(owner, [stylePath, scriptPath]);

        Assert.Contains(AegiTaskResource.DeferredStoragePath(stylePath), task.Resources);
        Assert.Contains(owner.GetLibraryResource(PersonalLibraryKind.EFFECT_LIBRARY), task.Resources);
        Assert.Contains(AegiTaskResource.DeferredStoragePath(scriptPath), task.Resources);
        var analysis = owner.Tasks.Submit(task);
        var plan = await analysis.Completion;
        Assert.Empty(owner.StyleLibrary.Snapshot.Presets);
        Assert.Empty(owner.EffectScriptLibrary.Snapshot.Presets);
        Assert.Empty(owner.EffectLibrary.Snapshot.FontFamilies);
        Assert.False(Directory.Exists(owner.LibraryPaths.FontsDirectory));
        var commitTask = new EffectLibraryImportCommitTask(owner, plan,
            new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Contains(owner.GetLibraryResource(PersonalLibraryKind.EFFECT_LIBRARY), commitTask.Resources);
        var commit = owner.Tasks.Submit(commitTask);
        await commit.Completion;

        AssertStyleEqual(bundle.Styles.Presets.Single(), owner.StyleLibrary.Snapshot.Presets.Single());
        Assert.Equal(bundle.Effects.Presets.Single().Source, owner.EffectScriptLibrary.Snapshot.Presets.Single().Source);
        Assert.Equal(bundle.FontFamilies[0].Files[0].Sha256,
            Assert.Single(Assert.Single(owner.EffectLibrary.Snapshot.FontFamilies).Files).Sha256);
        Assert.Equal(AegiTaskState.Succeeded, analysis.Snapshot.State);
        Assert.Equal(AegiTaskState.Succeeded, commit.Snapshot.State);
    }

    [Fact]
    public async Task FrozenStyleAndEffectExportsDoNotRequireAProjectOrModifyTheLiveLibrary()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(settingsDirectory.Path);
        await owner.Initialization;
        var bundle = CreatePresets();
        var stylePath = Path.Combine(outputDirectory.Path, "style.aegistyles");
        var scriptPath = Path.Combine(outputDirectory.Path, "effect.aegifx");
        var styles = owner.Tasks.Submit(new EffectLibraryExportTask(owner, bundle.Styles.Presets, [], stylePath,
            EffectLibraryExportMode.SINGLE_FILE));
        var effects = owner.Tasks.Submit(new EffectLibraryExportTask(owner, [], bundle.Effects.Presets, scriptPath,
            EffectLibraryExportMode.SCRIPT_SOURCE));
        await Task.WhenAll(styles.Completion, effects.Completion);

        var exported = await SubtitleStylePresetStore.LoadAsync(stylePath);
        AssertStyleEqual(bundle.Styles.Presets.Single(), exported.Styles.Presets.Single());
        Assert.Equal(bundle.FontFamilies[0].Files[0].Sha256, Assert.Single(Assert.Single(exported.FontFamilies).Files).Sha256);
        Assert.Equal(bundle.Effects.Presets.Single().Source, (await EffectScriptPresetStore.ReadScriptAsync(scriptPath)).Source);
        Assert.Empty(owner.StyleLibrary.Snapshot.Presets);
        Assert.Empty(owner.EffectScriptLibrary.Snapshot.Presets);
        Assert.Null(styles.Snapshot.ScopeId);
        Assert.Null(effects.Snapshot.ScopeId);
    }

    [Fact]
    public async Task BatchExportDeclaresEveryDestinationAndUsesTheSameNameResolutionForPublication()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(settingsDirectory.Path);
        await owner.Initialization;
        ImmutableArray<SubtitleStylePreset> selection =
        [new(Guid.NewGuid(), "Same/name", new() { FontFamily = "Noto Sans" }),
            new(Guid.NewGuid(), "Same\\name", new() { FontFamily = "Noto Sans" })];
        var paths = owner.EffectLibraryTransfer.GetIndependentDestinations(selection, [], outputDirectory.Path);
        var task = new EffectLibraryExportTask(owner, selection, [], outputDirectory.Path,
            EffectLibraryExportMode.INDEPENDENT_FILES);
        Assert.Equal(2, paths.Count);
        Assert.All(paths, path => Assert.Contains(AegiTaskResource.DeferredStoragePath(path), task.Resources));

        await owner.Tasks.Submit(task).Completion;

        Assert.All(paths, path => Assert.True(File.Exists(path)));
        Assert.Empty(Directory.EnumerateDirectories(outputDirectory.Path));
        Assert.Equal(2, Directory.EnumerateFiles(outputDirectory.Path).Count());
        foreach (var path in paths)
        {
            Assert.Equal(EffectLibraryTestFonts.Family.Files[0].Sha256,
                Assert.Single(Assert.Single((await SubtitleStylePresetStore.LoadAsync(path)).FontFamilies).Files).Sha256);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExportCannotOverwriteTheLiveLibraryThroughItsPathOrSymbolicLink(bool effects, bool symbolicLink)
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(settingsDirectory.Path);
        await owner.Initialization;
        var bundle = CreatePresets();
        var libraryPath = Path.Combine(settingsDirectory.Path,
            effects ? "effect-scripts.json" : "subtitle-styles.aegistyles");
        if (effects)
        {
            await EffectScriptPresetStore.SaveAsync(bundle.Effects, libraryPath);
        }
        else
        {
            await SubtitleStylePresetStore.SaveAsync(new() { Styles = bundle.Styles, FontFamilies = bundle.FontFamilies }, libraryPath);
        }

        var original = await File.ReadAllBytesAsync(libraryPath);
        var destination = libraryPath;
        if (symbolicLink)
        {
            destination = Path.Combine(outputDirectory.Path, effects ? "alias.aegifx" : "alias.aegistyles");
            File.CreateSymbolicLink(destination, libraryPath);
        }

        AegiTask task = effects
            ? new EffectLibraryExportTask(owner, [], bundle.Effects.Presets, destination, EffectLibraryExportMode.SCRIPT_SOURCE)
            : new EffectLibraryExportTask(owner, bundle.Styles.Presets, [], destination, EffectLibraryExportMode.SINGLE_FILE);
        var handle = owner.Tasks.Submit(task);

        await Assert.ThrowsAsync<InvalidDataException>(() => handle.Completion);

        Assert.Equal(original, await File.ReadAllBytesAsync(libraryPath));
        Assert.Equal(AegiTaskState.Failed, handle.Snapshot.State);
        Assert.Empty(owner.StyleLibrary.Snapshot.Presets);
        Assert.Empty(owner.EffectScriptLibrary.Snapshot.Presets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportRejectsAnyFileInsideTheManagedLibraryIncludingDirectoryAliases(bool symbolicLink)
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        await using var owner = EffectLibraryTestFonts.CreateOwner(settingsDirectory.Path);
        await owner.Initialization;
        var root = owner.LibraryPaths.EffectLibraryDirectory;
        if (symbolicLink)
        {
            var alias = Path.Combine(outputDirectory.Path, "alias");
            Directory.CreateSymbolicLink(alias, root);
            root = alias;
        }
        var destination = Path.Combine(root, "exported.aegistyles");
        var task = new EffectLibraryExportTask(owner, [new(Guid.NewGuid(), "Selected", new() { FontFamily = "Noto Sans" })], [], destination,
            EffectLibraryExportMode.SINGLE_FILE);

        await Assert.ThrowsAsync<InvalidDataException>(() => owner.Tasks.Submit(task).Completion);

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task BatchCommitRejectionCleansAllStagingBeforePublishingAnyOutput()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        using var repository = new EffectLibraryRepository(settingsDirectory.Path, Path.Combine(settingsDirectory.Path, "fonts"));
        var transfer = EffectLibraryTestFonts.CreateTransfer(repository);
        var outputPath = outputDirectory.Path;
        ImmutableArray<SubtitleStylePreset> selection =
        [new(Guid.NewGuid(), "First", new() { FontFamily = "Noto Sans" }),
            new(Guid.NewGuid(), "Second", new() { FontFamily = "Noto Sans" })];
        var paths = transfer.GetIndependentDestinations(selection, [], outputPath);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.ExportIndependentAsync(selection,
            [], outputPath, () =>
            {
                Assert.All(paths, path => Assert.False(File.Exists(path)));
                Assert.Single(Directory.EnumerateDirectories(outputPath));
                throw new OperationCanceledException();
            }));

        Assert.Empty(Directory.EnumerateFileSystemEntries(outputDirectory.Path));
    }

    [Fact]
    public async Task BatchPublicationFinishesAfterTheCommitBoundaryEvenIfAnExternalTokenIsCancelled()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        using var repository = new EffectLibraryRepository(settingsDirectory.Path, Path.Combine(settingsDirectory.Path, "fonts"));
        var transfer = EffectLibraryTestFonts.CreateTransfer(repository);
        using var cancellation = new CancellationTokenSource();
        ImmutableArray<SubtitleStylePreset> selection =
        [new(Guid.NewGuid(), "First", new() { FontFamily = "Noto Sans" }),
            new(Guid.NewGuid(), "Second", new() { FontFamily = "Noto Sans" })];
        var paths = transfer.GetIndependentDestinations(selection, [], outputDirectory.Path);

        await transfer.ExportIndependentAsync(selection, [], outputDirectory.Path, cancellation.Cancel, cancellation.Token);

        Assert.All(paths, path => Assert.True(File.Exists(path)));
        Assert.Empty(Directory.EnumerateDirectories(outputDirectory.Path));
    }

    [Fact]
    public async Task RejectedImportCommitPreservesTheDiskFileAndPublishedLibrarySnapshot()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var inputDirectory = new TemporaryWorkbenchDirectory();
        using var repository = new EffectLibraryRepository(directory.Path, Path.Combine(directory.Path, "fonts"));
        var transfer = EffectLibraryTestFonts.CreateTransfer(repository);
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Original", new() { FontFamily = "Noto Sans" });
        await repository.UpdateStylesAsync(collection => collection with { Presets = [original] });
        var before = repository.Snapshot;
        var bytes = await File.ReadAllBytesAsync(repository.ManifestPath);
        var incomingPath = Path.Combine(inputDirectory.Path, "incoming.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new()
        {
            Styles = new() { Presets = [new(Guid.NewGuid(), "Incoming", new() { FontFamily = "Noto Sans" })] },
            FontFamilies = [EffectLibraryTestFonts.Family]
        }, incomingPath);
        var plan = await transfer.AnalyzeImportAsync([incomingPath]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision>(),
            () => throw new OperationCanceledException()));

        Assert.Same(before, repository.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(repository.ManifestPath));
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".staging-*"));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, ".*.tmp"));
        Assert.Empty(repository.Snapshot.FontFamilies);
    }

    private static EffectLibrarySnapshot CreatePresets()
    {
        return new()
        {
            Styles = new() { Presets = [new(Guid.NewGuid(), "Personal style", new() { FontSize = 62, FontFamily = "Noto Sans" })] },
            FontFamilies = [EffectLibraryTestFonts.Family],
            Effects = new()
            {
                Presets = [new(Guid.NewGuid(), "Personal effect", "effect \"transfer-user\" version 3\nshort-clip compress\nscope clip current\nsegment stay flex 1\n    at 0 opacity base\n    at 1 opacity base\nend\nend\n")]
            }
        };
    }

    private static void AssertStyleEqual(SubtitleStylePreset expected, SubtitleStylePreset actual)
    {
        Assert.Equal(expected, actual);
    }
}
