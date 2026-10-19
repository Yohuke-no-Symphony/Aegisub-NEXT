using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests.Presets;

public sealed class EffectLibraryRepositoryTests
{
    [Fact]
    public async Task LegacySingleFileLibrariesAreIgnoredAndNewStylesCanBeSavedWithoutChangingOriginals()
    {
        using var directory = new TemporaryProjectDirectory();
        var legacyStyles = Path.Combine(directory.Path, "subtitle-styles.aegistyles");
        var legacyEffects = Path.Combine(directory.Path, "effect-scripts.json");
        await File.WriteAllTextAsync(legacyStyles, "{\"version\":7,\"presets\":[]}");
        await File.WriteAllTextAsync(legacyEffects, "legacy effects");
        var original = await File.ReadAllBytesAsync(legacyStyles);
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "EffectLibrary"));
        await repository.LoadAsync();
        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.Equal(original, await File.ReadAllBytesAsync(legacyStyles));
        Assert.Equal("legacy effects", await File.ReadAllTextAsync(legacyEffects));
        Assert.False(Directory.Exists(repository.DirectoryPath));
        var current = Style("Current");

        await repository.UpdateStylesAsync(styles => styles with { Presets = [current] });

        Assert.Equal(current, Assert.Single(repository.Snapshot.Styles.Presets));
        Assert.Equal(original, await File.ReadAllBytesAsync(legacyStyles));
        Assert.Equal("legacy effects", await File.ReadAllTextAsync(legacyEffects));
        using var reopened = new EffectLibraryRepository(repository.DirectoryPath);
        await reopened.LoadAsync();
        Assert.Equal(current, Assert.Single(reopened.Snapshot.Styles.Presets));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyManifestIsArchivedIntactAndDoesNotBlockCreatingTheCurrentLibrary(bool trailingSeparator)
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        var originals = new Dictionary<string, byte[]>
        {
            ["manifest.json"] = "{\"version\":1,\"revision\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"styles\":[\"unreadable.aegistyles\"],\"effects\":[]}"u8.ToArray(),
            ["styles/unreadable.aegistyles"] = "old style bytes"u8.ToArray(),
            ["effects/unreadable.aegieffect"] = "old effect bytes"u8.ToArray()
        };
        foreach (var (name, bytes) in originals)
        {
            var path = Path.Combine(root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
        }
        using var repository = new EffectLibraryRepository(trailingSeparator ? root + Path.DirectorySeparatorChar : root);
        Assert.Equal(Path.Combine(directory.Path, "fonts"), repository.FontDirectoryPath);

        await repository.LoadAsync();

        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.Empty(repository.Snapshot.Effects.Presets);
        Assert.False(Directory.Exists(root));
        var archive = Assert.Single(Directory.GetDirectories(directory.Path, "EffectLibrary.legacy-*"));
        var current = Style("Current");
        await repository.UpdateStylesAsync(styles => styles with { Presets = [current] });
        using var reopened = new EffectLibraryRepository(root);
        await reopened.LoadAsync();
        Assert.Equal(current, Assert.Single(reopened.Snapshot.Styles.Presets));
        Assert.Single(Directory.GetDirectories(directory.Path, "EffectLibrary.legacy-*"));
        foreach (var (name, bytes) in originals)
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(archive, name)));
        }
    }

    [Fact]
    public async Task SharedFacadesCommitConcurrentUpdatesWithoutLosingEitherLibrary()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        using var repository = new EffectLibraryRepository(root);
        using var styles = new SubtitleStylePresetLibrary(repository);
        using var effects = new EffectScriptPresetLibrary(repository);
        var changes = new ConcurrentQueue<EffectLibraryChangedEventArgs>();
        repository.Changed += (_, change) => changes.Enqueue(change);
        var updates = Enumerable.Range(0, 8).SelectMany(index => new[]
        {
            styles.UpsertAsync(Style($"Style {index}")),
            effects.UpsertAsync(Effect($"personal-{index}", $"Effect {index}"))
        });

        await Task.WhenAll(updates);

        Assert.Equal(8, repository.Snapshot.Styles.Presets.Length);
        Assert.Equal(8, repository.Snapshot.Effects.Presets.Length);
        Assert.Same(repository.Snapshot.Styles, styles.Snapshot);
        Assert.Same(repository.Snapshot.Effects, effects.Snapshot);
        Assert.Equal(16, changes.Count);
        Assert.All(changes, change => Assert.NotEqual(change.Previous.Revision, change.Current.Revision));
        using var reopened = new EffectLibraryRepository(root);
        await reopened.LoadAsync();
        Assert.Equal(8, reopened.Snapshot.Styles.Presets.Length);
        Assert.Equal(8, reopened.Snapshot.Effects.Presets.Length);
    }

    [Fact]
    public async Task EntryNamesContainIdentityAndDigestAndUnchangedEntriesAreReused()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        using var repository = new EffectLibraryRepository(root);
        var first = Style("First");
        var second = Style("Second");
        await repository.UpdateStylesAsync(current => current with { Presets = [first, second] });
        var original = Directory.GetFiles(Path.Combine(root, "styles"), "*.json");
        foreach (var path in original)
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var preset = Assert.Single(SubtitleStylePresetMetadataStore.Deserialize(bytes).Presets);
            Assert.Equal($"{preset.Id:N}-{Convert.ToHexStringLower(SHA256.HashData(bytes))}.json", Path.GetFileName(path));
        }
        var unchanged = Assert.Single(original, path => Path.GetFileName(path).StartsWith($"{second.Id:N}-", StringComparison.Ordinal));
        var revision = repository.Snapshot.Revision;

        await repository.UpdateStylesAsync(current => current with { Presets = current.Presets.SetItem(0, first with { Name = "Renamed" }) });

        Assert.NotEqual(revision, repository.Snapshot.Revision);
        Assert.True(File.Exists(unchanged));
        Assert.Equal(second, Assert.Single((await SubtitleStylePresetMetadataStore.LoadAsync(unchanged)).Presets));
        Assert.Equal("Renamed", repository.Snapshot.Styles.Presets[0].Name);
    }

    [Fact]
    public async Task FailedOrCanceledCommitPreservesManifestSnapshotAndExistingFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        using var repository = new EffectLibraryRepository(root);
        await repository.UpdateStylesAsync(current => current with { Presets = [Style("Retained")] });
        var snapshot = repository.Snapshot;
        var manifest = await File.ReadAllBytesAsync(repository.ManifestPath);
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order().ToArray();

        await Assert.ThrowsAsync<IOException>(() => repository.UpdateEffectsAsync(current =>
            current with { Presets = [Effect("personal-failed", "Failed")] }, () => throw new IOException("Rejected at commit")));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.UpdateStylesAsync(current =>
            current with { Presets = current.Presets.Add(Style("Canceled")) }, () =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }, cancellation.Token));

        Assert.Same(snapshot, repository.Snapshot);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(repository.ManifestPath));
        Assert.Equal(files, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order());
        Assert.Empty(Directory.GetDirectories(root, ".staging-*"));
    }

    [Fact]
    public async Task RevisionGuardRejectsStaleImportBeforeEvaluatingItsMutation()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "EffectLibrary"));
        await repository.LoadAsync();
        var expected = repository.Snapshot.Revision;
        await repository.UpdateEffectsAsync(current => current with { Presets = [Effect("personal-current", "Current")] });
        var snapshot = repository.Snapshot;
        var evaluated = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(expected, current =>
        {
            evaluated = true;
            return current with { Styles = current.Styles with { Presets = [Style("Stale")] } };
        }));

        Assert.False(evaluated);
        Assert.Same(snapshot, repository.Snapshot);
    }

    [Fact]
    public async Task CrossLibraryMutationPublishesOneRevisionAndSemanticNoOpDoesNotCommit()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "EffectLibrary"));
        await repository.LoadAsync();
        var changes = 0;
        repository.Changed += (_, _) => changes++;
        var style = Style("Style");
        var effect = Effect("personal-cross", "Effect");
        await repository.UpdateAsync(repository.Snapshot.Revision, current => current with
        {
            Styles = current.Styles with { Presets = [style] },
            Effects = current.Effects with { Presets = [effect] }
        });
        var snapshot = repository.Snapshot;
        var manifest = await File.ReadAllBytesAsync(repository.ManifestPath);
        var enteredCommit = false;

        await repository.UpdateStylesAsync(current => current with { Presets = [style with { }] }, () => enteredCommit = true);

        Assert.Equal(1, changes);
        Assert.False(enteredCommit);
        Assert.Same(snapshot, repository.Snapshot);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(repository.ManifestPath));
        Assert.Empty(repository.MaintenanceErrors);
    }

    [Fact]
    public async Task ExistingManifestNeverFallsBackToLegacyFilesAfterCorruption()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        var legacy = Path.Combine(directory.Path, "subtitle-styles.aegistyles");
        using var repository = new EffectLibraryRepository(root);
        await repository.UpdateStylesAsync(current => current with { Presets = [Style("Current")] });
        await TestStylePackageStore.SaveAsync(new() { Presets = [Style("Legacy")] }, legacy);
        var snapshot = repository.Snapshot;
        await File.WriteAllTextAsync(repository.ManifestPath, "{\"version\":99}");

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync());
        Assert.Same(snapshot, repository.Snapshot);
        using var reopened = new EffectLibraryRepository(root);
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.LoadAsync());
        Assert.Empty(reopened.Snapshot.Styles.Presets);
    }

    [Fact]
    public async Task MissingManifestAfterInitializationCannotResurrectArchivedLegacyData()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        var legacy = Path.Combine(directory.Path, "subtitle-styles.aegistyles");
        using var repository = new EffectLibraryRepository(root);
        await repository.UpdateStylesAsync(current => current with { Presets = [Style("Current")] });
        await TestStylePackageStore.SaveAsync(new() { Presets = [Style("Legacy")] }, legacy);
        var snapshot = repository.Snapshot;
        File.Delete(repository.ManifestPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync());
        Assert.Same(snapshot, repository.Snapshot);
        using var reopened = new EffectLibraryRepository(root);
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.LoadAsync());
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("path")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("version")]
    [InlineData("futureVersion")]
    public async Task TamperedManifestOrItemIsRejectedWithoutPublishingPartialData(string mutation)
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        using var repository = new EffectLibraryRepository(root);
        await repository.UpdateStylesAsync(current => current with { Presets = [Style("Retained")] });
        var snapshot = repository.Snapshot;
        var manifest = JsonNode.Parse(await File.ReadAllBytesAsync(repository.ManifestPath))!.AsObject();
        var entries = manifest["styles"]!.AsArray();
        var path = Assert.Single(Directory.GetFiles(Path.Combine(root, "styles"), "*.json"));
        if (mutation == "hash")
        {
            await File.AppendAllTextAsync(path, " ");
        }
        else if (mutation == "path")
        {
            entries[0] = "../outside.aegistyles";
        }
        else if (mutation == "duplicate")
        {
            entries.Add(entries[0]!.DeepClone());
        }
        else if (mutation is "version" or "futureVersion")
        {
            manifest["version"] = mutation == "version" ? 1 : 3;
        }
        else
        {
            File.Delete(path);
        }
        await File.WriteAllTextAsync(repository.ManifestPath, manifest.ToJsonString());

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync());
        Assert.Same(snapshot, repository.Snapshot);
        Assert.Empty(Directory.GetDirectories(directory.Path, "EffectLibrary.legacy-*"));
    }

    [Fact]
    public async Task InvalidLegacyFilesAreIgnoredWithoutPublishingOrArchivingEitherCollection()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        var legacyStyles = Path.Combine(directory.Path, "subtitle-styles.aegistyles");
        var legacyEffects = Path.Combine(directory.Path, "effect-scripts.json");
        await TestStylePackageStore.SaveAsync(new() { Presets = [Style("Valid")] }, legacyStyles);
        await File.WriteAllTextAsync(legacyEffects, "invalid");
        using var repository = new EffectLibraryRepository(root);

        await repository.LoadAsync();

        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.Empty(repository.Snapshot.Effects.Presets);
        Assert.False(File.Exists(repository.ManifestPath));
        Assert.False(Directory.Exists(Path.Combine(root, "legacy")));
        Assert.True(File.Exists(legacyStyles));
        Assert.True(File.Exists(legacyEffects));
    }

    [Fact]
    public async Task InterruptedFirstMigrationCannotPublishOrReimportUncommittedEntries()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        var legacy = Path.Combine(directory.Path, "subtitle-styles.aegistyles");
        var original = Style("Legacy");
        await TestStylePackageStore.SaveAsync(new() { Presets = [original] }, legacy);
        var before = await File.ReadAllBytesAsync(legacy);
        var uncommitted = TestStylePackageStore.Serialize(new() { Presets = [Style("Uncommitted")] });
        Directory.CreateDirectory(Path.Combine(root, "styles"));
        await File.WriteAllBytesAsync(Path.Combine(root, "styles", "orphan.aegistyles"), uncommitted);
        using var repository = new EffectLibraryRepository(root);

        await Assert.ThrowsAsync<InvalidDataException>(() => repository.LoadAsync());

        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.False(File.Exists(repository.ManifestPath));
        Assert.Equal(before, await File.ReadAllBytesAsync(legacy));
    }

    [Fact]
    public async Task ManagedPathProtectionIncludesDirectoryAliasesAndLinkCyclesAreBounded()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        Directory.CreateDirectory(root);
        var alias = Path.Combine(directory.Path, "alias");
        Directory.CreateSymbolicLink(alias, root);
        using var repository = new EffectLibraryRepository(root);
        using var styles = new SubtitleStylePresetLibrary(repository);
        var preset = Style("Retained");
        await styles.UpsertAsync(preset);

        Assert.True(repository.IsManagedPath(Path.Combine(alias, "new", "export.aegistyles")));
        await Assert.ThrowsAsync<InvalidDataException>(() => styles.ExportAsync(preset, Path.Combine(alias, "export.aegistyles")));
        var cycle = Path.Combine(directory.Path, "cycle");
        File.CreateSymbolicLink(cycle, cycle);
        Assert.Throws<InvalidDataException>(() => repository.IsManagedPath(cycle));
    }

    [Fact]
    public async Task FacadeDisposalDoesNotDisposeSharedRepositoryAndManagedExportsAreRejected()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "EffectLibrary");
        using var repository = new EffectLibraryRepository(root);
        var styles = new SubtitleStylePresetLibrary(repository);
        var preset = Style("Retained");
        await styles.UpsertAsync(preset);
        await Assert.ThrowsAsync<InvalidDataException>(() => styles.ExportAsync(preset, Path.Combine(root, "export.aegistyles")));
        styles.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => styles.RemoveAsync(preset.Id));
        await repository.UpdateEffectsAsync(current => current with { Presets = [Effect("personal-alive", "Alive")] });
        Assert.Single(repository.Snapshot.Styles.Presets);
        Assert.Single(repository.Snapshot.Effects.Presets);
        repository.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => repository.LoadAsync());
    }

    private static SubtitleStylePreset Style(string name)
    {
        return new(Guid.NewGuid(), name, new SubtitleStyle());
    }

    private static EffectScriptPreset Effect(string scriptId, string name)
    {
        return new(Guid.NewGuid(), name, BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"{scriptId}\"", StringComparison.Ordinal));
    }
}
