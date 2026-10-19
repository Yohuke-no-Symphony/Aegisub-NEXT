using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests.Presets;

public sealed class EffectLibraryTransferTests
{
    [Fact]
    public async Task MixedPackageCompleteFilesAndRawScriptsImportInOneRevision()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var packaged = Style("Packaged");
        var independent = Style("Independent");
        var complete = Effect("complete", "展示名称");
        var raw = Effect("raw", "Ignored Name");
        var packagePath = Path.Combine(directory.Path, "all.aegilibrary");
        var stylePath = Path.Combine(directory.Path, "style.aegistyles");
        var effectPath = Path.Combine(directory.Path, "effect.aegieffect");
        var rawPath = Path.Combine(directory.Path, "raw.aegifx");
        await TestEffectLibraryPackageStore.SaveAsync(new() { Styles = new() { Presets = [packaged] } }, packagePath);
        await TestStylePackageStore.SaveAsync(new() { Presets = [independent] }, stylePath);
        await EffectScriptPresetStore.WritePresetAsync(complete, effectPath);
        await EffectScriptPresetStore.WriteScriptAsync(raw.Source, rawPath);
        var plan = await service.AnalyzeImportAsync([packagePath, stylePath, effectPath, rawPath]);
        Assert.Equal(4, plan.Rows.Length);
        Assert.All(plan.Rows, row =>
        {
            Assert.Equal(EffectLibraryImportStatus.NEW, row.Status);
            Assert.True(row.CanImport);
        });
        var changes = 0;
        repository.Changed += (_, _) => changes++;

        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());

        Assert.Equal(1, changes);
        Assert.Equal([packaged.Id, independent.Id], repository.Snapshot.Styles.Presets.Select(preset => preset.Id));
        Assert.Equal(complete, repository.Snapshot.Effects.Presets[0]);
        Assert.Equal("raw", repository.Snapshot.Effects.Presets[1].Name);
        Assert.Equal(raw.Source, repository.Snapshot.Effects.Presets[1].Source);
    }

    [Fact]
    public async Task IdenticalAliasIdentitySkipsWithoutPublishingAndStylesKeepTheirLocalIdentityWhenReplaced()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var existing = Style("Same");
        await repository.UpdateAsync(Guid.Empty, current => current with
        {
            Styles = new() { Presets = [existing] },
            FontFamilies = TestStylePackageStore.Package(new() { Presets = [existing] }).FontFamilies
        });
        var path = Path.Combine(directory.Path, "import.aegistyles");
        await TestStylePackageStore.SaveAsync(new() { Presets = [existing with { Id = Guid.NewGuid() }] }, path);
        var identical = await service.AnalyzeImportAsync([path]);
        Assert.Equal(EffectLibraryImportStatus.IDENTICAL, Assert.Single(identical.Rows).Status);
        var snapshot = repository.Snapshot;
        await service.CommitImportAsync(identical, new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Same(snapshot, repository.Snapshot);
        var imported = existing with { Id = Guid.NewGuid(), Style = existing.Style with { FontSize = 100 } };
        await TestStylePackageStore.SaveAsync(new() { Presets = [imported] }, path);
        var conflict = await service.AnalyzeImportAsync([path]);
        var row = Assert.Single(conflict.Rows);
        Assert.Equal(existing.Id, row.TargetId);
        Assert.True(row.CanReplace);

        await service.CommitImportAsync(conflict, new Dictionary<int, EffectLibraryImportDecision> { [row.Index] = EffectLibraryImportDecision.REPLACE });

        var replaced = Assert.Single(repository.Snapshot.Styles.Presets);
        Assert.Equal(existing.Id, replaced.Id);
        Assert.Equal(100, replaced.Style.FontSize);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EffectReplacementKeepsLocalIdentityAndOnlyCompleteFilesOverrideDisplayName(bool complete)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var existing = Effect("personal", "本地名称");
        await repository.UpdateEffectsAsync(current => current with { Presets = [existing] });
        var imported = existing with { Id = Guid.NewGuid(), Name = "输入名称", Source = existing.Source + "\n" };
        var path = Path.Combine(directory.Path, complete ? "effect.aegieffect" : "effect.aegifx");
        if (complete)
        {
            await EffectScriptPresetStore.WritePresetAsync(imported, path);
        }
        else
        {
            await EffectScriptPresetStore.WriteScriptAsync(imported.Source, path);
        }
        var plan = await service.AnalyzeImportAsync([path]);
        var row = Assert.Single(plan.Rows);
        Assert.Equal(existing.Id, row.TargetId);
        Assert.True(row.CanReplace);

        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision> { [row.Index] = EffectLibraryImportDecision.REPLACE });

        var replaced = Assert.Single(repository.Snapshot.Effects.Presets);
        Assert.Equal(existing.Id, replaced.Id);
        Assert.Equal(complete ? imported.Name : existing.Name, replaced.Name);
        Assert.Equal(imported.Source, replaced.Source);
    }

    [Fact]
    public async Task DslIdentityChangeIsReportedAndSourceIsNeverRewritten()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var existing = Effect("old-script", "Same Name");
        await repository.UpdateEffectsAsync(current => current with { Presets = [existing] });
        var imported = Effect("new-script", existing.Name);
        var path = Path.Combine(directory.Path, "effect.aegieffect");
        await EffectScriptPresetStore.WritePresetAsync(imported, path);
        var plan = await service.AnalyzeImportAsync([path]);
        var row = Assert.Single(plan.Rows);
        Assert.True(row.DslIdChanges);
        Assert.Equal("old-script", row.PreviousDslId);
        Assert.Equal("new-script", row.DslId);

        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision> { [row.Index] = EffectLibraryImportDecision.REPLACE });

        Assert.Equal(imported.Source, Assert.Single(repository.Snapshot.Effects.Presets).Source);
        Assert.Equal(existing.Id, repository.Snapshot.Effects.Presets[0].Id);
    }

    [Fact]
    public async Task KeysPointingToDifferentTargetsCannotMergeOrReplaceExistingItems()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var first = Style("First");
        var second = Style("Second");
        await repository.UpdateStylesAsync(current => current with { Presets = [first, second] });
        var path = Path.Combine(directory.Path, "crossed.aegistyles");
        await TestStylePackageStore.SaveAsync(new() { Presets = [first with { Name = second.Name }] }, path);
        var plan = await service.AnalyzeImportAsync([path]);
        var row = Assert.Single(plan.Rows);
        Assert.Equal(EffectLibraryImportStatus.CONFLICT, row.Status);
        Assert.False(row.CanImport);
        Assert.False(row.CanReplace);
        Assert.Null(row.TargetId);
        Assert.Equal(2, row.ConflictingNames.Length);
        var snapshot = repository.Snapshot;

        await Assert.ThrowsAsync<InvalidDataException>(() => service.CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision> { [row.Index] = EffectLibraryImportDecision.REPLACE }));
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());

        Assert.Same(snapshot, repository.Snapshot);
    }

    [Fact]
    public async Task EffectDslAndLibraryIdentityCrossingCannotReplaceEitherTarget()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var first = Effect("first", "First");
        var second = Effect("second", "Second");
        await repository.UpdateEffectsAsync(current => current with { Presets = [first, second] });
        var path = Path.Combine(directory.Path, "crossed.aegieffect");
        await EffectScriptPresetStore.WritePresetAsync(first with { Id = second.Id }, path);

        var plan = await service.AnalyzeImportAsync([path]);

        var row = Assert.Single(plan.Rows);
        Assert.Equal(EffectLibraryImportConflictReason.MULTIPLE_TARGETS, row.ConflictReason);
        Assert.False(row.CanReplace);
        Assert.False(row.CanImport);
        Assert.Null(row.TargetId);
    }

    [Fact]
    public async Task PreviewFreezesInputsEvenIfFilesChangeBeforeCommit()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var original = Effect("frozen", "Original");
        var path = Path.Combine(directory.Path, "effect.aegieffect");
        await EffectScriptPresetStore.WritePresetAsync(original, path);
        var plan = await service.AnalyzeImportAsync([path]);
        await File.WriteAllTextAsync(path, "invalid");

        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());

        Assert.Equal(original, Assert.Single(repository.Snapshot.Effects.Presets));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultipleInputVersionsRequireOneExplicitChoiceAndDoNotDependOnFileOrder(bool alreadyExists)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var first = Style("Same");
        var second = first with { Style = first.Style with { FontSize = 100 } };
        if (alreadyExists)
        {
            await repository.UpdateStylesAsync(current => current with { Presets = [first with { Style = first.Style with { FontSize = 90 } }] });
        }
        var firstPath = Path.Combine(directory.Path, "first.aegistyles");
        var secondPath = Path.Combine(directory.Path, "second.aegistyles");
        await TestStylePackageStore.SaveAsync(new() { Presets = [first] }, firstPath);
        await TestStylePackageStore.SaveAsync(new() { Presets = [second] }, secondPath);
        var plan = await service.AnalyzeImportAsync([firstPath, secondPath]);
        Assert.All(plan.Rows, row => Assert.Equal(EffectLibraryImportStatus.CONFLICT, row.Status));
        Assert.NotNull(plan.Rows[0].ConflictGroupId);
        Assert.Equal(plan.Rows[0].ConflictGroupId, plan.Rows[1].ConflictGroupId);
        var snapshot = repository.Snapshot;
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Same(snapshot, repository.Snapshot);
        var decision = alreadyExists ? EffectLibraryImportDecision.REPLACE : EffectLibraryImportDecision.IMPORT;
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision> { [0] = decision, [1] = decision }));

        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision> { [1] = decision });

        Assert.Equal(100, Assert.Single(repository.Snapshot.Styles.Presets).Style.FontSize);
    }

    [Fact]
    public async Task DuplicateRawAndCompleteInputsPreferCompleteMetadataAndRawMatchesKeepCustomNames()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var effect = Effect("personal", "完整名称");
        var rawPath = Path.Combine(directory.Path, "raw.aegifx");
        var completePath = Path.Combine(directory.Path, "complete.aegieffect");
        await EffectScriptPresetStore.WriteScriptAsync(effect.Source, rawPath);
        await EffectScriptPresetStore.WritePresetAsync(effect, completePath);
        var plan = await service.AnalyzeImportAsync([rawPath, completePath]);
        Assert.Equal(EffectLibraryImportStatus.IDENTICAL, plan.Rows[0].Status);
        Assert.Equal(EffectLibraryImportStatus.NEW, plan.Rows[1].Status);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Equal(effect, Assert.Single(repository.Snapshot.Effects.Presets));

        var rawOnly = await service.AnalyzeImportAsync([rawPath]);

        Assert.Equal(EffectLibraryImportStatus.IDENTICAL, Assert.Single(rawOnly.Rows).Status);
    }

    [Fact]
    public async Task BuiltinRawSourcesAreSkippedAndModifiedBuiltinIdentitiesCannotBeReplaced()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var path = Path.Combine(directory.Path, "builtin.aegifx");
        var source = BuiltinEffectScripts.Get("fade-in").Source;
        await EffectScriptPresetStore.WriteScriptAsync(source, path);
        var same = await service.AnalyzeImportAsync([path]);
        Assert.Equal(EffectLibraryImportStatus.IDENTICAL, Assert.Single(same.Rows).Status);
        await service.CommitImportAsync(same, new Dictionary<int, EffectLibraryImportDecision>());
        await EffectScriptPresetStore.WriteScriptAsync(source + "\n", path);
        var changed = await service.AnalyzeImportAsync([path]);
        var row = Assert.Single(changed.Rows);
        Assert.Equal(EffectLibraryImportStatus.CONFLICT, row.Status);
        Assert.False(row.CanImport);
        Assert.False(row.CanReplace);

        await service.CommitImportAsync(changed, new Dictionary<int, EffectLibraryImportDecision>());

        Assert.Empty(repository.Snapshot.Effects.Presets);
        Assert.False(File.Exists(repository.ManifestPath));
    }

    [Fact]
    public async Task StaleOrForeignPlansAndInvalidDecisionsNeverCommit()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var path = Path.Combine(directory.Path, "style.aegistyles");
        await TestStylePackageStore.SaveAsync(new() { Presets = [Style("Imported")] }, path);
        var plan = await service.AnalyzeImportAsync([path]);
        using var other = new EffectLibraryRepository(Path.Combine(directory.Path, "other"));
        await Assert.ThrowsAsync<ArgumentException>(() => new EffectLibraryTransferService(other, new TestFontFamilySource(), new TestFontFamilyValidator()).CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision>()));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision> { [100] = EffectLibraryImportDecision.IMPORT }));
        await repository.UpdateEffectsAsync(current => current with { Presets = [Effect("current", "Current")] });
        var snapshot = repository.Snapshot;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision>()));

        Assert.Same(snapshot, repository.Snapshot);
    }

    [Fact]
    public async Task LaterInvalidInputAndCanceledCommitDoNotPartiallyImport()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var path = Path.Combine(directory.Path, "style.aegistyles");
        var invalid = Path.Combine(directory.Path, "invalid.aegieffect");
        await TestStylePackageStore.SaveAsync(new() { Presets = [Style("Imported")] }, path);
        await File.WriteAllBytesAsync(invalid, [0xFF]);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.AnalyzeImportAsync([path, invalid]));
        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.False(File.Exists(repository.ManifestPath));
        var plan = await service.AnalyzeImportAsync([path]);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision>(), () =>
            {
                cancellation.Cancel();
                cancellation.Token.ThrowIfCancellationRequested();
            }, cancellation.Token));

        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.False(File.Exists(repository.ManifestPath));
    }

    [Fact]
    public async Task CompleteIndependentAndFrozenWholePackageExportsKeepContentAndProtectManagedAliases()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "library");
        using var repository = new EffectLibraryRepository(root);
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var style = Style("Selected");
        var effect = Effect("selected", "原名称");
        await repository.UpdateAsync(Guid.Empty, current => current with
        {
            Styles = new() { Presets = [style] },
            Effects = new() { Presets = [effect] }
        });
        var frozen = repository.Snapshot;
        await repository.UpdateEffectsAsync(current => current with { Presets = [effect with { Name = "Changed" }] });
        var output = Path.Combine(directory.Path, "export");
        var destinations = service.GetIndependentDestinations([style], [effect], output);
        await service.ExportIndependentAsync([style], [effect], output);
        Assert.Equal(style, Assert.Single((await TestStylePackageStore.LoadAsync(destinations[0])).Presets));
        Assert.Equal(effect, await EffectScriptPresetStore.ReadPresetAsync(destinations[1]));
        var packagePath = Path.Combine(directory.Path, "whole.aegilibrary");
        await service.ExportAllPackageAsync(frozen, packagePath);
        Assert.Equal(effect.Name, Assert.Single((await TestEffectLibraryPackageStore.LoadAsync(packagePath)).Effects.Presets).Name);
        var alias = Path.Combine(directory.Path, "alias");
        Directory.CreateSymbolicLink(alias, root);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExportPackageAsync([style], [effect], Path.Combine(alias, "overwritten.aegilibrary")));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExportIndependentAsync([style], [effect], alias));
        await Assert.ThrowsAsync<IOException>(() => service.ExportIndependentAsync([style], [effect], output));
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
