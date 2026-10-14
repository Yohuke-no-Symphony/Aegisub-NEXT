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
        await using var owner = new DesktopApplicationContext(new(settingsDirectory.Path), new());
        await owner.Initialization;
        var bundle = CreatePresets();
        var stylePath = Path.Combine(inputDirectory.Path, "style.aegistyles");
        var scriptPath = Path.Combine(inputDirectory.Path, "effect.aegifx");
        await SubtitleStylePresetStore.SaveAsync(bundle.Styles, stylePath);
        await EffectScriptPresetStore.WriteScriptAsync(bundle.Effects.Presets.Single().Source, scriptPath);
        var task = new EffectLibraryImportAnalysisTask(owner, [stylePath, scriptPath]);

        Assert.Contains(AegiTaskResource.DeferredStoragePath(stylePath), task.Resources);
        Assert.Contains(owner.GetLibraryResource(PersonalLibraryKind.EFFECT_LIBRARY), task.Resources);
        Assert.Contains(AegiTaskResource.DeferredStoragePath(scriptPath), task.Resources);
        var analysis = owner.Tasks.Submit(task);
        var plan = await analysis.Completion;
        Assert.Empty(owner.StyleLibrary.Snapshot.Presets);
        Assert.Empty(owner.EffectScriptLibrary.Snapshot.Presets);
        var commitTask = new EffectLibraryImportCommitTask(owner, plan,
            new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Contains(owner.GetLibraryResource(PersonalLibraryKind.EFFECT_LIBRARY), commitTask.Resources);
        var commit = owner.Tasks.Submit(commitTask);
        await commit.Completion;

        AssertStyleEqual(bundle.Styles.Presets.Single(), owner.StyleLibrary.Snapshot.Presets.Single());
        Assert.Equal(bundle.Effects.Presets.Single().Source, owner.EffectScriptLibrary.Snapshot.Presets.Single().Source);
        Assert.Equal(AegiTaskState.Succeeded, analysis.Snapshot.State);
        Assert.Equal(AegiTaskState.Succeeded, commit.Snapshot.State);
    }

    [Fact]
    public async Task FrozenStyleAndEffectExportsDoNotRequireAProjectOrModifyTheLiveLibrary()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        await using var owner = new DesktopApplicationContext(new(settingsDirectory.Path), new());
        await owner.Initialization;
        var bundle = CreatePresets();
        var stylePath = Path.Combine(outputDirectory.Path, "style.aegistyles");
        var scriptPath = Path.Combine(outputDirectory.Path, "effect.aegifx");
        var styles = owner.Tasks.Submit(new EffectLibraryExportTask(owner, bundle.Styles.Presets, [], stylePath,
            EffectLibraryExportMode.SINGLE_FILE));
        var effects = owner.Tasks.Submit(new EffectLibraryExportTask(owner, [], bundle.Effects.Presets, scriptPath,
            EffectLibraryExportMode.SCRIPT_SOURCE));
        await Task.WhenAll(styles.Completion, effects.Completion);

        AssertStyleEqual(bundle.Styles.Presets.Single(), (await SubtitleStylePresetStore.LoadAsync(stylePath)).Presets.Single());
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
        await using var owner = new DesktopApplicationContext(new(settingsDirectory.Path), new());
        await owner.Initialization;
        ImmutableArray<SubtitleStylePreset> selection =
        [new(Guid.NewGuid(), "Same/name", new()), new(Guid.NewGuid(), "Same\\name", new())];
        var paths = owner.EffectLibraryTransfer.GetIndependentDestinations(selection, [], outputDirectory.Path);
        var task = new EffectLibraryExportTask(owner, selection, [], outputDirectory.Path,
            EffectLibraryExportMode.INDEPENDENT_FILES);
        Assert.Equal(2, paths.Count);
        Assert.All(paths, path => Assert.Contains(AegiTaskResource.DeferredStoragePath(path), task.Resources));

        await owner.Tasks.Submit(task).Completion;

        Assert.All(paths, path => Assert.True(File.Exists(path)));
        Assert.Empty(Directory.EnumerateDirectories(outputDirectory.Path));
        Assert.Equal(2, Directory.EnumerateFiles(outputDirectory.Path).Count());
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
        await using var owner = new DesktopApplicationContext(new(settingsDirectory.Path), new());
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
            await SubtitleStylePresetStore.SaveAsync(bundle.Styles, libraryPath);
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
        await using var owner = new DesktopApplicationContext(new(settingsDirectory.Path), new());
        await owner.Initialization;
        var root = owner.LibraryPaths.EffectLibraryDirectory;
        if (symbolicLink)
        {
            var alias = Path.Combine(outputDirectory.Path, "alias");
            Directory.CreateSymbolicLink(alias, root);
            root = alias;
        }
        var destination = Path.Combine(root, "exported.aegistyles");
        var task = new EffectLibraryExportTask(owner, [new(Guid.NewGuid(), "Selected", new())], [], destination,
            EffectLibraryExportMode.SINGLE_FILE);

        await Assert.ThrowsAsync<InvalidDataException>(() => owner.Tasks.Submit(task).Completion);

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task BatchCommitRejectionCleansAllStagingBeforePublishingAnyOutput()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        using var repository = new EffectLibraryRepository(settingsDirectory.Path);
        var transfer = new EffectLibraryTransferService(repository);
        var outputPath = outputDirectory.Path;
        ImmutableArray<SubtitleStylePreset> selection =
        [new(Guid.NewGuid(), "First", new()), new(Guid.NewGuid(), "Second", new())];
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
        using var repository = new EffectLibraryRepository(settingsDirectory.Path);
        var transfer = new EffectLibraryTransferService(repository);
        using var cancellation = new CancellationTokenSource();
        ImmutableArray<SubtitleStylePreset> selection =
        [new(Guid.NewGuid(), "First", new()), new(Guid.NewGuid(), "Second", new())];
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
        using var repository = new EffectLibraryRepository(directory.Path);
        var transfer = new EffectLibraryTransferService(repository);
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Original", new());
        await repository.UpdateStylesAsync(collection => collection with { Presets = [original] });
        var before = repository.Snapshot;
        var bytes = await File.ReadAllBytesAsync(repository.ManifestPath);
        var incomingPath = Path.Combine(inputDirectory.Path, "incoming.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [new(Guid.NewGuid(), "Incoming", new())] }, incomingPath);
        var plan = await transfer.AnalyzeImportAsync([incomingPath]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision>(),
            () => throw new OperationCanceledException()));

        Assert.Same(before, repository.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(repository.ManifestPath));
        Assert.Empty(Directory.EnumerateDirectories(directory.Path, ".staging-*"));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, ".*.tmp"));
    }

    private static EffectLibrarySnapshot CreatePresets()
    {
        var bytes = ImmutableArray.Create<byte>(1, 2, 3, 4);
        var font = new EmbeddedSubtitleFont("subtitle.ttf",
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes.AsSpan())), bytes);
        return new()
        {
            Styles = new() { Presets = [new(Guid.NewGuid(), "Personal style", new() { FontSize = 62 }, font)] },
            Effects = new()
            {
                Presets = [new(Guid.NewGuid(), "Personal effect", "effect \"transfer-user\" version 1\nshort-clip compress\nsegment stay flex 1\n    at 0 opacity base\n    at 1 opacity base\nend\n")]
            }
        };
    }

    private static void AssertStyleEqual(SubtitleStylePreset expected, SubtitleStylePreset actual)
    {
        Assert.Equal(expected with { Font = null }, actual with { Font = null });
        var expectedFont = Assert.IsType<EmbeddedSubtitleFont>(expected.Font);
        var actualFont = Assert.IsType<EmbeddedSubtitleFont>(actual.Font);
        Assert.Equal(expectedFont.FileName, actualFont.FileName);
        Assert.Equal(expectedFont.Sha256, actualFont.Sha256);
        Assert.True(expectedFont.Data.AsSpan().SequenceEqual(actualFont.Data.AsSpan()));
    }
}
