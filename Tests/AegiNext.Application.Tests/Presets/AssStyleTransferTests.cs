using System.Text;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests.Presets;

public sealed class AssStyleTransferTests
{
    [Fact]
    public async Task AnalysisFreezesAllStylesWithoutWritingAndOnlySelectedStylesAreCommitted()
    {
        using var directory = new TemporaryProjectDirectory();
        var root = Path.Combine(directory.Path, "library");
        using var repository = new EffectLibraryRepository(root);
        await repository.LoadAsync();
        var snapshot = repository.Snapshot;
        var service = Service(repository);
        var path = await WriteAsync(directory.Path, Source(Row("Selected"), Row("Unused", 40)), "styles.ASS");

        var plan = await service.AnalyzeImportAsync([path]);

        Assert.Same(snapshot, repository.Snapshot);
        Assert.False(File.Exists(repository.ManifestPath));
        Assert.Equal(new[] { "Selected", "Unused" }, plan.Rows.Select(row => row.Name));
        Assert.All(plan.Rows, row =>
        {
            Assert.Equal(EffectLibraryItemKind.STYLE, row.Kind);
            Assert.Equal(EffectLibraryImportStatus.NEW, row.Status);
            Assert.Equal("styles.ASS", row.SourceName);
            Assert.True(row.CanImport);
            Assert.False(row.IncludesFonts);
        });
        Assert.All(plan.Entries, entry => Assert.Null(entry.ExternalId));
        Assert.Equal(2, plan.Entries.Select(entry => entry.Style!.Id).Distinct().Count());
        var commitCount = 0;
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>
        {
            [1] = EffectLibraryImportDecision.SKIP
        }, () => commitCount++);

        Assert.Equal(1, commitCount);
        var preset = Assert.Single(repository.Snapshot.Styles.Presets);
        Assert.Equal("Selected", preset.Name);
        Assert.Equal(28, preset.Style.FontSize);
        Assert.Equal("Missing Test Font", preset.Style.FontFamily);
        Assert.Null(preset.Style.FontAssetId);
        Assert.Equal((640, 360), (preset.Style.Scaling.ReferenceWidth, preset.Style.Scaling.ReferenceHeight));
        using var reopened = new EffectLibraryRepository(root);
        await reopened.LoadAsync();
        Assert.Equal(preset, Assert.Single(reopened.Snapshot.Styles.Presets));
    }

    [Fact]
    public async Task ReimportOfIdenticalStylesMatchesByNameAndSkipsNewlyGeneratedIdentities()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var path = await WriteAsync(directory.Path, Source(Row("Repeated")));
        var first = await service.AnalyzeImportAsync([path]);
        await service.CommitImportAsync(first, new Dictionary<int, EffectLibraryImportDecision>());
        var snapshot = repository.Snapshot;
        var existing = Assert.Single(snapshot.Styles.Presets);
        var bytes = await File.ReadAllBytesAsync(repository.ManifestPath);

        var repeated = await service.AnalyzeImportAsync([path]);

        var row = Assert.Single(repeated.Rows);
        Assert.Equal(EffectLibraryImportStatus.IDENTICAL, row.Status);
        Assert.Equal(existing.Id, row.TargetId);
        Assert.NotEqual(existing.Id, repeated.Entries[0].Style!.Id);
        Assert.Null(repeated.Entries[0].ExternalId);
        await service.CommitImportAsync(repeated, new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Same(snapshot, repository.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(repository.ManifestPath));
    }

    [Fact]
    public async Task DifferentStyleContentRequiresExplicitReplacementAndPreservesLocalIdentity()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var path = await WriteAsync(directory.Path, Source(Row("Existing")));
        await service.CommitImportAsync(await service.AnalyzeImportAsync([path]), new Dictionary<int, EffectLibraryImportDecision>());
        var snapshot = repository.Snapshot;
        var existing = Assert.Single(snapshot.Styles.Presets);
        await File.WriteAllTextAsync(path, Source(Row(existing.Name, 42)));

        var plan = await service.AnalyzeImportAsync([path]);

        var row = Assert.Single(plan.Rows);
        Assert.Equal(EffectLibraryImportStatus.CONFLICT, row.Status);
        Assert.True(row.CanReplace);
        Assert.False(row.CanImport);
        Assert.Equal(existing.Id, row.TargetId);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Same(snapshot, repository.Snapshot);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>
        {
            [row.Index] = EffectLibraryImportDecision.REPLACE
        });
        var replaced = Assert.Single(repository.Snapshot.Styles.Presets);
        Assert.Equal(existing.Id, replaced.Id);
        Assert.Equal(42, replaced.Style.FontSize);
    }

    [Fact]
    public async Task AssAndNativeFilesShareOneAtomicImportRevision()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var ass = await WriteAsync(directory.Path, Source(Row("ASS")));
        var native = new SubtitleStylePreset(Guid.NewGuid(), "Native", new());
        var nativePath = Path.Combine(directory.Path, "native.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Styles = new() { Presets = [native] }, IncludesFonts = false }, nativePath);
        var effect = new EffectScriptPreset(Guid.NewGuid(), "Imported effect",
            BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", "\"ass-mixed\"", StringComparison.Ordinal));
        var effectPath = Path.Combine(directory.Path, "effect.aegieffect");
        await EffectScriptPresetStore.WritePresetAsync(effect, effectPath);
        var plan = await service.AnalyzeImportAsync([ass, nativePath, effectPath]);
        var changed = 0;
        repository.Changed += (_, _) => changed++;
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());

        Assert.Equal(1, changed);
        Assert.Equal(new[] { "ASS", "Native" }, repository.Snapshot.Styles.Presets.Select(preset => preset.Name));
        Assert.Equal(effect, Assert.Single(repository.Snapshot.Effects.Presets));
        Assert.Empty(repository.Snapshot.FontFamilies);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AssAndNativeVersionsOfTheSameNameRequireOneExplicitChoice(bool assFirst)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var ass = await WriteAsync(directory.Path, Source(Row("Same")));
        var native = new SubtitleStylePreset(Guid.NewGuid(), "Same", new() { FontSize = 50 });
        var nativePath = Path.Combine(directory.Path, "native.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new() { Styles = new() { Presets = [native] }, IncludesFonts = false }, nativePath);

        var plan = await service.AnalyzeImportAsync(assFirst ? [ass, nativePath] : [nativePath, ass]);

        Assert.All(plan.Rows, row => Assert.Equal(EffectLibraryImportConflictReason.MULTIPLE_INPUT_VERSIONS, row.ConflictReason));
        Assert.NotNull(plan.Rows[0].ConflictGroupId);
        Assert.Equal(plan.Rows[0].ConflictGroupId, plan.Rows[1].ConflictGroupId);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Empty(repository.Snapshot.Styles.Presets);
        var selected = plan.Rows.Single(row => row.SourceName == Path.GetFileName(ass));
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>
        {
            [selected.Index] = EffectLibraryImportDecision.IMPORT
        });
        Assert.Equal(28, Assert.Single(repository.Snapshot.Styles.Presets).Style.FontSize);
    }

    [Fact]
    public async Task NamesDifferingOnlyByCaseUseTheExistingLibraryConflictGroup()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var path = await WriteAsync(directory.Path, Source(Row("Default"), Row("default")));

        var plan = await service.AnalyzeImportAsync([path]);

        Assert.All(plan.Rows, row => Assert.Equal(EffectLibraryImportStatus.CONFLICT, row.Status));
        Assert.Equal(plan.Rows[0].ConflictGroupId, plan.Rows[1].ConflictGroupId);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision> { [1] = EffectLibraryImportDecision.IMPORT });
        Assert.Equal("default", Assert.Single(repository.Snapshot.Styles.Presets).Name);
    }

    [Fact]
    public async Task IdenticalAssInputsAreDeduplicatedWithoutUsingExternalGuids()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var first = await WriteAsync(directory.Path, Source(Row("Same")), "first.ass");
        var second = await WriteAsync(directory.Path, Source(Row("Same")), "second.ass");

        var plan = await service.AnalyzeImportAsync([first, second]);

        Assert.Equal(EffectLibraryImportStatus.NEW, plan.Rows[0].Status);
        Assert.Equal(EffectLibraryImportStatus.IDENTICAL, plan.Rows[1].Status);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());
        Assert.Single(repository.Snapshot.Styles.Presets);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FontNamesRemainMetadataAndInstalledFontFilesArePreserved(bool withInstalledFonts)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        if (withInstalledFonts)
        {
            using var library = new SubtitleStylePresetLibrary(repository);
            await library.UpsertAsync(new CapturedSubtitleStyle(new(Guid.NewGuid(), "Installed", new() { FontFamily = "Installed Font" }),
                [TestStylePackageStore.Family("Installed Font")]));
        }
        var source = new UnavailableFontFamilySource();
        var validator = new RejectingFontFamilyValidator();
        var service = new EffectLibraryTransferService(repository, source, validator);
        var families = repository.Snapshot.FontFamilies;
        var fontFiles = withInstalledFonts ? Directory.GetFiles(repository.FontDirectoryPath) : [];
        var path = await WriteAsync(directory.Path, Source(Row("No fonts needed")) + "\n[Fonts]\ninvalid embedded font");

        var plan = await service.AnalyzeImportAsync([path]);
        Assert.False(Assert.Single(plan.Rows).IncludesFonts);
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());

        Assert.Equal(0, source.CaptureCount);
        Assert.Equal(0, validator.ValidateCount);
        Assert.True(families.Equals(repository.Snapshot.FontFamilies));
        if (withInstalledFonts)
        {
            Assert.Equal(fontFiles, Directory.GetFiles(repository.FontDirectoryPath));
        }
        else
        {
            Assert.False(Directory.Exists(repository.FontDirectoryPath));
        }
    }

    [Fact]
    public async Task DiagnosticsAreMergedPerPreviewRowAndNeverPersistedInTheLibrary()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var source = """
            [V4+ Styles]
            Format: Name, Fontname, Fontsize, PrimaryColour, Angle
            Style: Rotated,Missing Test Font,28,&H00FFFFFF,20
            Style: Plain,Missing Test Font,28,&H00FFFFFF,0
            """;
        var path = await WriteAsync(directory.Path, source);

        var plan = await service.AnalyzeImportAsync([path]);

        Assert.All(plan.Rows, row =>
        {
            Assert.Contains(row.Diagnostics, diagnostic => diagnostic.Code == "Ass.PlayRes");
            Assert.Contains(row.Diagnostics, diagnostic => diagnostic.Code == "Ass.WrapModeApproximation");
        });
        var rotation = Assert.Single(plan.Rows[0].Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleRotation");
        Assert.StartsWith("Style: Rotated", source.Substring(rotation.SourceStart, rotation.SourceLength));
        Assert.DoesNotContain(plan.Rows[1].Diagnostics, diagnostic => diagnostic.Code == "Ass.StyleRotation");
        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());
        var metadata = string.Join('\n', await Task.WhenAll(Directory.GetFiles(repository.DirectoryPath, "*.json", SearchOption.AllDirectories)
            .Select(path => File.ReadAllTextAsync(path))));
        Assert.DoesNotContain("Ass.StyleRotation", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("diagnostics", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SubtitleStylePresetCollection.CURRENT_VERSION, repository.Snapshot.Styles.Version);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("normalization")]
    public async Task InvalidLibraryNamesFailWithFileAndStyleIdentityInsteadOfRenaming(string invalidity)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var name = invalidity == "length" ? new string('长', 129) : "Cafe\u0301";
        var path = await WriteAsync(directory.Path, Source(Row("Valid"), Row(name)), "invalid-name.ass");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.AnalyzeImportAsync([path]));

        Assert.Contains("invalid-name.ass", error.Message, StringComparison.Ordinal);
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.False(File.Exists(repository.ManifestPath));
    }

    [Theory]
    [InlineData("[V4+ Styles]\nStyle: Broken,Missing Test Font,28,&H00FFFFFF")]
    [InlineData("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour\nStyle: Broken,Missing Test Font,invalid,&H00FFFFFF")]
    [InlineData("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour")]
    public async Task ASubsequentMalformedAssFileLeavesSnapshotAndDiskUnchanged(string invalidSource)
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        await repository.UpdateStylesAsync(current => current with { Presets = [new(Guid.NewGuid(), "Existing", new())] });
        var snapshot = repository.Snapshot;
        var bytes = await File.ReadAllBytesAsync(repository.ManifestPath);
        var service = Service(repository);
        var valid = await WriteAsync(directory.Path, Source(Row("Valid")), "valid.ass");
        var invalid = await WriteAsync(directory.Path, invalidSource, "invalid.ass");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.AnalyzeImportAsync([valid, invalid]));

        Assert.Contains("invalid.ass", error.Message, StringComparison.Ordinal);
        Assert.Same(snapshot, repository.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(repository.ManifestPath));
    }

    [Fact]
    public async Task InvalidUtf8IsRejectedAndUtf8BomIsAccepted()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var path = Path.Combine(directory.Path, "encoding.ass");
        await File.WriteAllBytesAsync(path, [0xFF, 0xFE, 0x20, 0x00]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.AnalyzeImportAsync([path]));

        Assert.Contains("encoding.ass", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.ManifestPath));
        await File.WriteAllTextAsync(path, Source(Row("中文样式")), new UTF8Encoding(true, true));
        var plan = await service.AnalyzeImportAsync([path]);
        Assert.Equal("中文样式", Assert.Single(plan.Rows).Name);
    }

    [Fact]
    public async Task CommitUsesFrozenAssContentAfterTheSourceFileIsRemoved()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var path = await WriteAsync(directory.Path, Source(Row("Frozen", 39)));
        var plan = await service.AnalyzeImportAsync([path]);
        File.Delete(path);

        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>());

        var preset = Assert.Single(repository.Snapshot.Styles.Presets);
        Assert.Equal("Frozen", preset.Name);
        Assert.Equal(39, preset.Style.FontSize);
    }

    [Fact]
    public async Task StaleAssPreviewsCannotOverwriteConcurrentLibraryChanges()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var path = await WriteAsync(directory.Path, Source(Row("Incoming")));
        var plan = await service.AnalyzeImportAsync([path]);
        await repository.UpdateStylesAsync(current => current with { Presets = [new(Guid.NewGuid(), "Concurrent", new())] });
        var snapshot = repository.Snapshot;
        var bytes = await File.ReadAllBytesAsync(repository.ManifestPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>()));

        Assert.Same(snapshot, repository.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(repository.ManifestPath));
    }

    [Fact]
    public async Task CancellationDuringAnalysisOrAtCommitDoesNotPublishAnyStyles()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var path = await WriteAsync(directory.Path, Source(Row("Canceled")));
        using var analysisCancellation = new CancellationTokenSource();
        analysisCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AnalyzeImportAsync([path], analysisCancellation.Token));
        var plan = await service.AnalyzeImportAsync([path]);
        var snapshot = repository.Snapshot;
        using var commitCancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CommitImportAsync(plan,
            new Dictionary<int, EffectLibraryImportDecision>(), () =>
            {
                commitCancellation.Cancel();
                commitCancellation.Token.ThrowIfCancellationRequested();
            }, commitCancellation.Token));

        Assert.Same(snapshot, repository.Snapshot);
        Assert.False(File.Exists(repository.ManifestPath));
    }

    [Fact]
    public async Task AllStylesCanBePreviewedAndASubsetSelectedWithinTheExistingLibraryCapacity()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var service = Service(repository);
        var path = await WriteAsync(directory.Path, Source(Enumerable.Range(0, 257).Select(index => Row("Style " + index)).ToArray()));
        var plan = await service.AnalyzeImportAsync([path]);
        Assert.Equal(257, plan.Rows.Length);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision>()));
        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.False(File.Exists(repository.ManifestPath));

        await service.CommitImportAsync(plan, plan.Rows.ToDictionary(row => row.Index,
            row => row.Index == 0 ? EffectLibraryImportDecision.IMPORT : EffectLibraryImportDecision.SKIP));

        Assert.Equal("Style 0", Assert.Single(repository.Snapshot.Styles.Presets).Name);
    }

    [Fact]
    public async Task OversizedAssTextIsRejectedBeforeDecodingOrParsing()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var path = Path.Combine(directory.Path, "oversized.ass");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(SubtitleStylePresetMetadataStore.MAXIMUM_FILE_BYTES + 1L);
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Service(repository).AnalyzeImportAsync([path]));

        Assert.Contains("oversized.ass", error.Message, StringComparison.Ordinal);
        Assert.Contains("容量预算", error.Message, StringComparison.Ordinal);
        Assert.Empty(repository.Snapshot.Styles.Presets);
        Assert.False(File.Exists(repository.ManifestPath));
    }

    [Fact]
    public async Task AssMetadataDoesNotInstallFontsFromASkippedNativeInput()
    {
        using var directory = new TemporaryProjectDirectory();
        using var repository = Repository(directory.Path);
        var ass = await WriteAsync(directory.Path, Source(Row("ASS")));
        var complete = new SubtitleStylePreset(Guid.NewGuid(), "Complete", new() { FontFamily = "Missing Test Font" });
        var nativePath = Path.Combine(directory.Path, "complete.aegistyles");
        await SubtitleStylePresetStore.SaveAsync(new()
        {
            Styles = new() { Presets = [complete] },
            FontFamilies = [TestStylePackageStore.Family(complete.Style.FontFamily)]
        }, nativePath);
        var source = new UnavailableFontFamilySource();
        var service = new EffectLibraryTransferService(repository, source, new TestFontFamilyValidator());
        var plan = await service.AnalyzeImportAsync([ass, nativePath]);
        Assert.False(plan.Rows[0].IncludesFonts);
        Assert.True(plan.Rows[1].IncludesFonts);

        await service.CommitImportAsync(plan, new Dictionary<int, EffectLibraryImportDecision> { [1] = EffectLibraryImportDecision.SKIP });

        Assert.Equal("ASS", Assert.Single(repository.Snapshot.Styles.Presets).Name);
        Assert.Empty(repository.Snapshot.FontFamilies);
        Assert.False(Directory.Exists(repository.FontDirectoryPath));
        Assert.Equal(0, source.CaptureCount);
    }

    private static EffectLibraryRepository Repository(string directory) => new(Path.Combine(directory, "library"));

    private static EffectLibraryTransferService Service(EffectLibraryRepository repository) =>
        new(repository, new UnavailableFontFamilySource(), new RejectingFontFamilyValidator());

    private static async Task<string> WriteAsync(string directory, string source, string name = "styles.ass")
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, source);
        return path;
    }

    private static string Source(params string[] rows) => $"""
        [Script Info]
        ScriptType: v4.00+
        PlayResX: 640
        PlayResY: 360
        WrapStyle: 1
        [V4+ Styles]
        Format: Name, Fontname, Fontsize, PrimaryColour
        {string.Join('\n', rows)}
        """;

    private static string Row(string name, int fontSize = 28) => $"Style: {name},Missing Test Font,{fontSize},&H800000FF";
}
