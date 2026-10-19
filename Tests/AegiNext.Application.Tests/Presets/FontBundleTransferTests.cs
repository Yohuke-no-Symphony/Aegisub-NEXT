using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Application.Presets;
using AegiNext.Application.Fonts;
using AegiNext.Core.Fonts;
using AegiNext.Core.Presets;

namespace AegiNext.Application.Tests.Presets;

public sealed class FontBundleTransferTests
{
    [Fact]
    public void ImportBudgetCountsMetadataAndDeduplicatesTheSameFileAcrossFamilies()
    {
        var family = Family("Shared", 1);
        var alias = new FontFamilyBundle("Alias", family.Files);
        var maximum = FontFamilyBundleValidator.MAXIMUM_TOTAL_FONT_BYTES;
        PrivateFontFiles.ValidateInputBudget([family, alias], maximum - 1);
        Assert.Throws<InvalidDataException>(() => PrivateFontFiles.ValidateInputBudget([family, alias], maximum));
        Assert.Throws<InvalidDataException>(() => PrivateFontFiles.ValidateInputBudget([], maximum + 1));
    }

    [Fact]
    public async Task SeparatePackagesShareTtcBytesAcrossFamiliesAndKeepEachFilesMetadata()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var inputs = new List<string>();
        foreach (var name in new[] { "One", "Two" })
        {
            var family = Family(name, 1);
            family = family with { Files = [family.Files[0] with { FileName = name + ".ttc" }] };
            var path = Path.Combine(directory.Path, name + ".aegistyles");
            await SubtitleStylePresetStore.SaveAsync(new()
            {
                Styles = new() { Presets = [new(Guid.NewGuid(), name, new() { FontFamily = name })] }, FontFamilies = [family]
            }, path);
            inputs.Add(path);
        }
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var plan = await service.AnalyzeImportAsync(inputs);
        Assert.True(plan.Entries[0].FontFamilies[0].Files[0].Data.Equals(plan.Entries[1].FontFamilies[0].Files[0].Data));
        Assert.Equal("One", plan.Entries[0].FontFamilies[0].FamilyName);
        Assert.Equal("Two", plan.Entries[1].FontFamilies[0].FamilyName);
        Assert.Equal("One.ttc", plan.Entries[0].FontFamilies[0].Files[0].FileName);
        Assert.Equal("Two.ttc", plan.Entries[1].FontFamilies[0].Files[0].FileName);
        using var library = new SubtitleStylePresetLibrary(repository);
        await library.ImportAsync(inputs);
        var installed = repository.Snapshot.FontFamilies;
        Assert.True(installed[0].Files[0].Data.Equals(installed[1].Files[0].Data));
        Assert.Equal("One", installed[0].FamilyName);
        Assert.Equal("Two", installed[1].FamilyName);
        Assert.Equal("One.ttc", installed[0].Files[0].FileName);
        Assert.Equal("Two.ttc", installed[1].Files[0].FileName);
    }

    [Fact]
    public async Task ImportPublishesStylesAndWholeFamiliesInOneRevisionAndKeepsAnalysisReadOnly()
    {
        using var directory = new TemporaryProjectDirectory();
        var fonts = Path.Combine(directory.Path, "fonts");
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"), fonts);
        var family = Family("Shared", 1, 2);
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Shared style", new() { FontFamily = family.FamilyName });
        var path = Path.Combine(directory.Path, "incoming.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Styles = new() { Presets = [style] }, FontFamilies = [family] }, path);
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource([family]), new TestFontFamilyValidator());

        var plan = await service.AnalyzeImportAsync([path]);

        Assert.False(Directory.Exists(fonts));
        Assert.Empty(repository.Snapshot.FontFamilies);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Equal(style, Assert.Single(repository.Snapshot.Styles.Presets));
        Assert.Equal(2, Assert.Single(repository.Snapshot.FontFamilies).Files.Length);
        Assert.Equal(2, Directory.GetFiles(fonts, "*.font").Length);
        var loaded = await EffectLibraryRepository.LoadFontFamiliesAsync(repository.DirectoryPath, fonts);
        Assert.Equal(family.Files.Select(file => file.Sha256), Assert.Single(loaded).Files.Select(file => file.Sha256));
    }

    [Fact]
    public async Task FontOnlyReplacementCommitsAndRejectedCommitPreservesOldFontFilesAndSnapshot()
    {
        using var directory = new TemporaryProjectDirectory();
        var fonts = Path.Combine(directory.Path, "fonts");
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"), fonts);
        using var library = new SubtitleStylePresetLibrary(repository);
        var first = Family("Shared", 1, 2);
        var next = Family("Shared", 3);
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Shared style", new() { FontFamily = "Shared" });
        await library.UpsertAsync(new CapturedSubtitleStyle(style, [first]));
        var before = repository.Snapshot;
        var manifest = await File.ReadAllBytesAsync(repository.ManifestPath);
        var path = Path.Combine(directory.Path, "incoming.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Styles = new() { Presets = [style] }, FontFamilies = [next] }, path);
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource([next]), new TestFontFamilyValidator());
        var plan = await service.AnalyzeImportAsync([path]);
        var row = Assert.Single(plan.Rows);
        Assert.Equal(EffectLibraryImportStatus.CONFLICT, row.Status);
        var decisions = new Dictionary<int, EffectLibraryImportDecision> { [row.Index] = EffectLibraryImportDecision.REPLACE };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CommitImportAsync(plan, decisions,
            () => throw new OperationCanceledException()));

        Assert.Same(before, repository.Snapshot);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(repository.ManifestPath));
        Assert.Equal(2, Directory.GetFiles(fonts, "*.font").Length);
        await service.CommitImportAsync(plan, decisions);
        Assert.NotEqual(before.Revision, repository.Snapshot.Revision);
        Assert.Single(Assert.Single(repository.Snapshot.FontFamilies).Files);
        Assert.Single(Directory.GetFiles(fonts, "*.font"));
    }

    [Fact]
    public void StyleAndLibraryArchivesShareOneRawCopyOfAFontAndRejectPreviousJson()
    {
        var family = Family("Shared", 1);
        var otherFamily = new FontFamilyBundle("Another", family.Files);
        var styles = new SubtitleStylePresetCollection
        {
            Presets = [new(Guid.NewGuid(), "One", new() { FontFamily = "Shared" }), new(Guid.NewGuid(), "Two", new() { FontFamily = "Another" })]
        };
        var bytes = EffectLibraryPackageStore.Serialize(new() { Styles = styles, FontFamilies = [family, otherFamily] });
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("fonts/", StringComparison.Ordinal));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".aegistyles", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => SubtitleStylePresetStore.Deserialize("{\"version\":7,\"presets\":[]}"u8));
    }

    [Fact]
    public async Task MultipleInputsWithDifferentVersionsOfOneFamilyFailWithoutInstallingEither()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var inputs = new List<string>();
        foreach (var version in new byte[] { 1, 2 })
        {
            var family = Family("Shared", version);
            var path = Path.Combine(directory.Path, $"input-{version}.aegistyles");
            await SubtitleStylePresetStore.SaveAsync(new()
            {
                Styles = new() { Presets = [new(Guid.NewGuid(), $"Style {version}", new() { FontFamily = "Shared" })] },
                FontFamilies = [family]
            }, path);
            inputs.Add(path);
        }
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        await Assert.ThrowsAsync<InvalidDataException>(() => service.AnalyzeImportAsync(inputs));
        Assert.Empty(repository.Snapshot.FontFamilies);
        Assert.False(Directory.Exists(repository.FontDirectoryPath));
        Assert.False(File.Exists(repository.ManifestPath));
    }

    [Fact]
    public async Task ExportsNormalizeAliasesAndKeepFrozenFamilyBytesAfterLiveReplacement()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var original = Family("Canonical", 1, 2);
        var changed = Family("Canonical", 3);
        var style = new SubtitleStylePreset(Guid.NewGuid(), "Alias", new() { FontFamily = "sans-serif" });
        var source = new TestFontFamilySource([original], new Dictionary<string, string> { ["sans-serif"] = "Canonical" });
        var frozen = new EffectLibraryTransferService(repository, source, new TestFontFamilyValidator(), [original]);
        await repository.UpdateAsync(Guid.Empty, current => current with { FontFamilies = [changed] });
        var path = Path.Combine(directory.Path, "alias.aegistyles");
        await frozen.ExportStyleAsync(style, path);
        var exported = await SubtitleStylePresetStore.LoadAsync(path);
        Assert.Equal("Canonical", Assert.Single(exported.Styles.Presets).Style.FontFamily);
        Assert.Equal(original.Files.Select(file => file.Sha256), Assert.Single(exported.FontFamilies).Files.Select(file => file.Sha256));
        Assert.Equal("sans-serif", style.Style.FontFamily);
        Assert.Equal(changed, Assert.Single(repository.Snapshot.FontFamilies));
        var emptyFrozen = new EffectLibraryTransferService(repository, source, new TestFontFamilyValidator(), []);
        var direct = style with { Style = style.Style with { FontFamily = "Canonical" } };
        await emptyFrozen.ExportStyleAsync(direct, path);
        var directExport = await SubtitleStylePresetStore.LoadAsync(path);
        Assert.Equal(original.Files.Select(file => file.Sha256), Assert.Single(directExport.FontFamilies).Files.Select(file => file.Sha256));
    }

    [Fact]
    public async Task SkippedStyleDoesNotInstallItsFontFamilyOrCreateARevision()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = new EffectLibraryRepository(Path.Combine(directory.Path, "library"));
        var family = Family("Shared", 1);
        var path = Path.Combine(directory.Path, "skip.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new()
        {
            Styles = new() { Presets = [new(Guid.NewGuid(), "Skip", new() { FontFamily = "Shared" })] }, FontFamilies = [family]
        }, path);
        var service = new EffectLibraryTransferService(repository, new TestFontFamilySource(), new TestFontFamilyValidator());
        var plan = await service.AnalyzeImportAsync([path]);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision> { [0] = EffectLibraryImportDecision.SKIP });
        Assert.Empty(repository.Snapshot.FontFamilies);
        Assert.Equal(Guid.Empty, repository.Snapshot.Revision);
        Assert.False(Directory.Exists(repository.FontDirectoryPath));
        Assert.False(File.Exists(repository.ManifestPath));
    }

    [Fact]
    public async Task ReadOnlyFontLoadIgnoresLegacyManifestAndRejectsMissingCurrentFontsWithoutChangingFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "library");
        Directory.CreateDirectory(root);
        var manifest = Path.Combine(root, "manifest.json");
        var old = "{\"version\":1,\"revision\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\",\"styles\":[],\"effects\":[]}";
        await File.WriteAllTextAsync(manifest, old);
        Assert.Empty(await EffectLibraryRepository.LoadFontFamiliesAsync(root, Path.Combine(directory.Path, "fonts")));
        Assert.Equal(old, await File.ReadAllTextAsync(manifest));
        Assert.Single(Directory.GetDirectories(directory.Path));
        File.Delete(manifest);
        using var current = new EffectLibraryRepository(root);
        var family = Family("Shared", 1);
        await current.UpdateAsync(Guid.Empty, snapshot => snapshot with { FontFamilies = [family] });
        var before = await File.ReadAllBytesAsync(manifest);
        File.Delete(Path.Combine(current.FontDirectoryPath, family.Files[0].Sha256 + ".font"));
        await Assert.ThrowsAsync<InvalidDataException>(() => EffectLibraryRepository.LoadFontFamiliesAsync(root, current.FontDirectoryPath));
        Assert.Equal(before, await File.ReadAllBytesAsync(manifest));
    }

    private static FontFamilyBundle Family(string name, params byte[] values)
    {
        return new(name, values.Select(value =>
        {
            var data = ImmutableArray.Create(value);
            return new EmbeddedSubtitleFont($"font-{value}.ttf", Convert.ToHexStringLower(SHA256.HashData(data.AsSpan())), data);
        }).ToImmutableArray());
    }
}
