using System.Collections.Immutable;
using AegiNext.Application.Presets;
using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Settings.Presets;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Settings.Presets;

[Collection("Workspace session")]
public sealed class SettingsPresetTaskTests
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
        var styleTask = new SettingsStyleImportTask(owner, [stylePath]);
        var effectTask = new SettingsEffectImportTask(owner, [scriptPath]);

        Assert.Contains(AegiTaskResource.DeferredStoragePath(stylePath), styleTask.Resources);
        Assert.Contains(owner.GetLibraryResource(PersonalLibraryKind.STYLE), styleTask.Resources);
        Assert.Contains(AegiTaskResource.DeferredStoragePath(scriptPath), effectTask.Resources);
        var styles = owner.Tasks.Submit(styleTask);
        var effects = owner.Tasks.Submit(effectTask);
        await Task.WhenAll(styles.Completion, effects.Completion);

        AssertStyleEqual(bundle.Styles.Presets.Single(), owner.StyleLibrary.Snapshot.Presets.Single());
        Assert.Equal(bundle.Effects.Presets.Single().Source, owner.EffectScriptLibrary.Snapshot.Presets.Single().Source);
        Assert.Equal(AegiTaskState.Succeeded, styles.Snapshot.State);
        Assert.Equal(AegiTaskState.Succeeded, effects.Snapshot.State);
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
        var styles = owner.Tasks.Submit(new SettingsStyleExportTask(owner, bundle.Styles.Presets, stylePath, false));
        var effects = owner.Tasks.Submit(new SettingsEffectExportTask(owner, bundle.Effects.Presets, scriptPath, false));
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
        [new(Guid.NewGuid(), "Same name", new()), new(Guid.NewGuid(), "Same name", new())];
        var paths = PresetBatchExporter.GetStyleDestinations(selection, outputDirectory.Path);
        var task = new SettingsStyleExportTask(owner, selection, outputDirectory.Path, true);
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
            ? new SettingsEffectExportTask(owner, bundle.Effects.Presets, destination, false)
            : new SettingsStyleExportTask(owner, bundle.Styles.Presets, destination, false);
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
        var task = new SettingsStyleExportTask(owner, [new(Guid.NewGuid(), "Selected", new())], destination, false);

        await Assert.ThrowsAsync<InvalidDataException>(() => owner.Tasks.Submit(task).Completion);

        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task BatchCommitRejectionCleansAllStagingBeforePublishingAnyOutput()
    {
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        var outputPath = outputDirectory.Path;
        ImmutableArray<SubtitleStylePreset> selection =
        [new(Guid.NewGuid(), "First", new()), new(Guid.NewGuid(), "Second", new())];
        var paths = PresetBatchExporter.GetStyleDestinations(selection, outputPath);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PresetBatchExporter.ExportStylesAsync(selection,
            outputPath, () =>
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
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        using var cancellation = new CancellationTokenSource();
        ImmutableArray<SubtitleStylePreset> selection =
        [new(Guid.NewGuid(), "First", new()), new(Guid.NewGuid(), "Second", new())];
        var paths = PresetBatchExporter.GetStyleDestinations(selection, outputDirectory.Path);

        await PresetBatchExporter.ExportStylesAsync(selection, outputDirectory.Path, cancellation.Cancel, cancellation.Token);

        Assert.All(paths, path => Assert.True(File.Exists(path)));
        Assert.Empty(Directory.EnumerateDirectories(outputDirectory.Path));
    }

    [Fact]
    public async Task RejectedImportCommitPreservesTheDiskFileAndPublishedLibrarySnapshot()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.aegistyles");
        using var library = new SubtitleStylePresetLibrary(libraryPath);
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Original", new());
        await library.UpsertAsync(original);
        var before = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(libraryPath);
        var incomingPath = Path.Combine(directory.Path, "incoming.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [new(Guid.NewGuid(), "Incoming", new())] }, incomingPath);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ImportAsync([incomingPath],
            () => throw new OperationCanceledException()));

        Assert.Same(before, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(libraryPath));
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
